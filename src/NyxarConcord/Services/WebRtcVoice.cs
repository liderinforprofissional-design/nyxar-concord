using System.Net.Http;
using System.Text.Json;
using System.Collections.Concurrent;
using SIPSorcery.Net;
using SIPSorcery.Media;
using SIPSorceryMedia.Abstractions;
using SIPSorceryMedia.Encoders;
using NyxarConcord.Models;
using NyxarConcord.Networking;

namespace NyxarConcord.Services;

/// <summary>
/// Mídia por WebRTC em malha (mesh): cada participante conecta direto com os outros.
/// - A "apresentação" (SDP/ICE) passa pelo relay do Cloudflare (o mesmo /ws).
/// - As credenciais TURN vêm de /turn (permite atravessar NAT fechado).
/// - Vídeo (tela): VP8 (libvpx, embutido no pacote — sem DLLs à mão).
/// - Áudio: PCMU 8 kHz — SÓ quando <see cref="VideoOnly"/> é falso. No Nyxar hoje a
///   voz continua indo pelo relay; aqui usamos WebRTC só para o VÍDEO da transmissão
///   (com fallback JPEG para quem não conectar), por isso VideoOnly = true.
/// </summary>
public sealed class WebRtcVoice : IDisposable
{
    private readonly string _selfId;
    private readonly VoiceService _voice;
    private readonly WorkerRelay _relay;

    private readonly AudioEncoder _encoder = new();
    private readonly AudioFormat _pcmu;
    private readonly VideoFormat _vp8 = new(VideoCodecsEnum.VP8, 96, 90000);

    private readonly ConcurrentDictionary<string, RTCPeerConnection> _pcs = new();
    private readonly ConcurrentDictionary<string, bool> _ready = new();
    private readonly ConcurrentDictionary<string, VpxVideoEncoder> _video = new();

    private List<RTCIceServer> _iceServers = new();
    private string? _roomId;
    private bool _turnLoaded;
    private long _lastVideoTick;

    /// <summary>Quando verdadeiro, negocia SÓ vídeo (a voz vai por outro caminho/relay).</summary>
    public bool VideoOnly { get; set; }

    public bool IsActive => _roomId is not null;

    /// <summary>Este par está conectado (pronto para receber mídia)?</summary>
    public bool IsPeerReady(string peerId) => _ready.TryGetValue(peerId, out var ok) && ok;

    /// <summary>Pares conectados por WebRTC agora (para decidir quem recebe JPEG de fallback).</summary>
    public IReadOnlyCollection<string> ReadyPeers()
        => _ready.Where(kv => kv.Value).Select(kv => kv.Key).ToList();

    /// <summary>Há pelo menos um par recebendo vídeo por WebRTC?</summary>
    public bool AnyVideoReady => _ready.Any(kv => kv.Value);

    /// <summary>Quadro de vídeo (tela) decodificado: (peerId, BGR, largura, altura, stride).</summary>
    public event Action<string, byte[], int, int, int>? VideoFrameDecoded;

    public WebRtcVoice(string selfId, VoiceService voice, WorkerRelay relay)
    {
        _selfId = selfId;
        _voice = voice;
        _relay = relay;
        _pcmu = _encoder.SupportedFormats.First(f => f.Codec == AudioCodecsEnum.PCMU);
    }

    // ---------------- Ciclo de vida da chamada ----------------

    public async Task StartAsync(string roomId, IEnumerable<string> peerIds)
    {
        _roomId = roomId;
        await EnsureTurnAsync();
        foreach (var id in peerIds.Distinct())
        {
            if (string.IsNullOrEmpty(id) || id == _selfId) continue;
            if (string.CompareOrdinal(_selfId, id) < 0)
                await OfferToAsync(id);
        }
    }

    public async Task PeerJoinedAsync(string peerId)
    {
        if (!IsActive || peerId == _selfId) return;
        if (_pcs.ContainsKey(peerId)) return;
        if (string.CompareOrdinal(_selfId, peerId) < 0)
            await OfferToAsync(peerId);
    }

    public void PeerLeft(string peerId) => ClosePeer(peerId);

    public void Stop()
    {
        _roomId = null;
        foreach (var id in _pcs.Keys.ToList()) ClosePeer(id);
    }

    // ---------------- Envio de áudio ----------------

    public void SendFrame(byte[] pcm16k)
    {
        if (VideoOnly) return;                 // voz vai por outro caminho (relay)
        if (!IsActive || _pcs.IsEmpty) return;
        try
        {
            short[] s16 = BytesToShorts(pcm16k);
            short[] s8 = PcmResampler.Resample(s16, 16000, 8000);
            byte[] encoded = _encoder.EncodeAudio(s8, _pcmu);
            uint duration = (uint)s8.Length;

            foreach (var kv in _pcs)
                if (_ready.TryGetValue(kv.Key, out var ok) && ok)
                    try { kv.Value.SendAudio(duration, encoded); } catch { }
        }
        catch { }
    }

    // ---------------- Envio de vídeo (tela) — VP8 ----------------

    /// <summary>Envia um quadro de tela (BGR 24-bit) para todos os pares conectados.</summary>
    public void SendVideoFrame(byte[] bgr, int width, int height)
    {
        if (!IsActive) return;

        long now = Environment.TickCount64;
        long deltaMs = _lastVideoTick == 0 ? 66 : Math.Clamp(now - _lastVideoTick, 20, 1000);
        _lastVideoTick = now;
        uint durationRtp = (uint)(deltaMs * 90); // relógio de vídeo = 90 kHz

        foreach (var kv in _pcs)
        {
            if (!_ready.TryGetValue(kv.Key, out var ok) || !ok) continue;
            if (!_video.TryGetValue(kv.Key, out var enc)) continue;
            try
            {
                byte[]? encoded = enc.EncodeVideo(width, height, bgr, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8);
                if (encoded is { Length: > 0 })
                    kv.Value.SendVideo(durationRtp, encoded);
            }
            catch { }
        }
    }

    // ---------------- Sinalização recebida ----------------

    public async Task HandleSignalAsync(string fromId, ChatMessage msg)
    {
        try
        {
            switch (msg.Signal)
            {
                case SignalType.RtcOffer: await OnOfferAsync(fromId, msg.Text); break;
                case SignalType.RtcAnswer: OnAnswer(fromId, msg.Text); break;
                case SignalType.RtcIce: OnIce(fromId, msg.Text); break;
            }
        }
        catch { }
    }

    // ---------------- Núcleo WebRTC ----------------

    private RTCPeerConnection CreatePeer(string peerId)
    {
        var pc = new RTCPeerConnection(new RTCConfiguration { iceServers = _iceServers });

        // Faixa de áudio (PCMU) — só quando NÃO é vídeo-apenas (senão a voz duplicaria
        // com o relay). No Nyxar de hoje, VideoOnly = true, então isto é pulado.
        if (!VideoOnly)
        {
            var audioTrack = new MediaStreamTrack(new List<AudioFormat> { _pcmu }, MediaStreamStatusEnum.SendRecv);
            pc.addTrack(audioTrack);
        }

        // Faixa de vídeo (VP8) — negociada já aqui para não renegociar ao compartilhar tela.
        try
        {
            var enc = new VpxVideoEncoder();
            _video[peerId] = enc;

            var videoTrack = new MediaStreamTrack(new List<VideoFormat> { _vp8 }, MediaStreamStatusEnum.SendRecv);
            pc.addTrack(videoTrack);

            pc.OnVideoFrameReceived += (rep, ts, frame, fmt) =>
            {
                try
                {
                    foreach (var img in enc.DecodeVideo(frame, VideoPixelFormatsEnum.Bgr, VideoCodecsEnum.VP8))
                    {
                        int w = (int)img.Width, h = (int)img.Height;
                        VideoFrameDecoded?.Invoke(peerId, img.Sample, w, h, w * 3);
                    }
                }
                catch { }
            };
        }
        catch
        {
            // Sem codec de vídeo disponível: segue (o app cai no fallback JPEG).
            _video.TryRemove(peerId, out _);
        }

        pc.onicecandidate += (cand) =>
        {
            if (cand is null) return;
            Send(peerId, SignalType.RtcIce, cand.toJSON());
        };

        // Recebimento de áudio (só quando há faixa de áudio negociada).
        if (!VideoOnly)
        {
            pc.OnRtpPacketReceived += (rep, mediaType, rtpPkt) =>
            {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPkt?.Payload is null) return;
                try
                {
                    short[] pcm8 = _encoder.DecodeAudio(rtpPkt.Payload, _pcmu);
                    short[] pcm16 = PcmResampler.Resample(pcm8, 8000, 16000);
                    _voice.PlayFrom(peerId, ShortsToBytes(pcm16));
                }
                catch { }
            };
        }

        pc.onconnectionstatechange += (state) =>
        {
            _ready[peerId] = state == RTCPeerConnectionState.connected;
            if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed
                     or RTCPeerConnectionState.disconnected)
                ClosePeer(peerId);
        };

        _pcs[peerId] = pc;
        _ready[peerId] = false;
        return pc;
    }

    private async Task OfferToAsync(string peerId)
    {
        if (_pcs.ContainsKey(peerId)) return;
        var pc = CreatePeer(peerId);
        var offer = pc.createOffer(null);
        await pc.setLocalDescription(offer);
        Send(peerId, SignalType.RtcOffer, offer.sdp);
    }

    private async Task OnOfferAsync(string fromId, string sdp)
    {
        if (_pcs.ContainsKey(fromId)) ClosePeer(fromId);
        var pc = CreatePeer(fromId);

        pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.offer, sdp = sdp });
        var answer = pc.createAnswer(null);
        await pc.setLocalDescription(answer);
        Send(fromId, SignalType.RtcAnswer, answer.sdp);
    }

    private void OnAnswer(string fromId, string sdp)
    {
        if (_pcs.TryGetValue(fromId, out var pc))
            pc.setRemoteDescription(new RTCSessionDescriptionInit { type = RTCSdpType.answer, sdp = sdp });
    }

    private void OnIce(string fromId, string json)
    {
        if (_pcs.TryGetValue(fromId, out var pc) &&
            RTCIceCandidateInit.TryParse(json, out var init))
        {
            pc.addIceCandidate(init);
        }
    }

    private void ClosePeer(string peerId)
    {
        _ready.TryRemove(peerId, out _);
        if (_video.TryRemove(peerId, out var enc))
            try { enc.Dispose(); } catch { }
        if (_pcs.TryRemove(peerId, out var pc))
            try { pc.close(); } catch { }
    }

    // ---------------- TURN (Cloudflare) ----------------

    private async Task EnsureTurnAsync()
    {
        if (_turnLoaded) return;
        _turnLoaded = true;

        var servers = new List<RTCIceServer>
        {
            new RTCIceServer { urls = "stun:stun.cloudflare.com:3478" },
        };

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            string body = await http.GetStringAsync(WorkerRelay.TurnUrl);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("iceServers", out var ice))
            {
                if (ice.ValueKind == JsonValueKind.Object)
                    AddIceObject(servers, ice);
                else if (ice.ValueKind == JsonValueKind.Array)
                    foreach (var el in ice.EnumerateArray()) AddIceObject(servers, el);
            }
        }
        catch { /* sem TURN: fica só o STUN */ }

        _iceServers = servers;
    }

    private static void AddIceObject(List<RTCIceServer> list, JsonElement el)
    {
        string? user = el.TryGetProperty("username", out var u) ? u.GetString() : null;
        string? cred = el.TryGetProperty("credential", out var c) ? c.GetString() : null;

        if (el.TryGetProperty("urls", out var urls))
        {
            if (urls.ValueKind == JsonValueKind.Array)
                foreach (var one in urls.EnumerateArray()) Add(list, one.GetString(), user, cred);
            else
                Add(list, urls.GetString(), user, cred);
        }
    }

    private static void Add(List<RTCIceServer> list, string? url, string? user, string? cred)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        list.Add(new RTCIceServer { urls = url, username = user ?? "", credential = cred ?? "" });
    }

    // ---------------- Utilidades ----------------

    private void Send(string peerId, SignalType type, string text)
    {
        _ = _relay.SendToPeerAsync(peerId, new ChatMessage
        {
            Kind = MessageKind.Signal,
            Signal = type,
            RoomId = _roomId,
            Text = text,
        });
    }

    private static short[] BytesToShorts(byte[] bytes)
    {
        int n = bytes.Length / 2;
        var s = new short[n];
        for (int i = 0; i < n; i++)
            s[i] = (short)(bytes[i * 2] | (bytes[i * 2 + 1] << 8));
        return s;
    }

    private static byte[] ShortsToBytes(short[] samples)
    {
        var b = new byte[samples.Length * 2];
        for (int i = 0; i < samples.Length; i++)
        {
            b[i * 2] = (byte)(samples[i] & 0xFF);
            b[i * 2 + 1] = (byte)((samples[i] >> 8) & 0xFF);
        }
        return b;
    }

    public void Dispose() => Stop();
}
