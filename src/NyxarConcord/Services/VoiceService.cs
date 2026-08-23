using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NyxarConcord.Services;

/// <summary>
/// Voz P2P: captura o microfone, aplica supressão de ruído e envia os quadros
/// PCM; reproduz o áudio recebido de vários participantes com mixagem.
/// PCM 16 kHz, 16-bit, mono. Sem codec (simples e sem dependências nativas).
///
/// Supressão de ruído: gate/expansor adaptativo com VAD (detecção de voz).
/// - Piso de ruído estimado continuamente (sobe devagar, desce rápido).
/// - Histerese: dois limiares (abrir/fechar) para não "tremer".
/// - Hangover: mantém aberto ~250 ms após a fala, sem cortar o fim das palavras.
/// - Recuperação de ataque: reenvia o quadro anterior ao abrir, sem cortar o começo.
/// - Fade suave ao fechar (release) + filtro passa-alta (remove zumbido/rumble).
/// </summary>
public sealed class VoiceService : IDisposable
{
    private readonly WaveFormat _format = new(16000, 16, 1);
    private readonly object _lock = new();
    private readonly Dictionary<string, BufferedWaveProvider> _inputs = new();
    // Pessoas que eu silenciei só para mim (não reproduz o áudio delas).
    private readonly HashSet<string> _mutedPeers = new();

    private WaveInEvent? _mic;
    private WaveOutEvent? _out;
    private MixingSampleProvider? _mixer;

    // --- Estado da supressão de ruído (VAD/gate adaptativo) ---
    private const double OpenRatio = 2.8;    // abre quando RMS > piso * 2.8 (~ +9 dB)
    private const double CloseRatio = 1.7;   // fecha quando RMS < piso * 1.7
    private const int HangoverFrames = 6;    // ~250 ms (quadros de 40 ms)
    private const double FloorMin = 60, FloorMax = 4000;
    private const double HpAlpha = 0.965;    // passa-alta ~90 Hz @ 16 kHz

    private double _noiseFloor = 300;
    private bool _gateOpen;
    private int _hangover;
    private double _gain = 1.0;
    private float _hpPrevIn, _hpPrevOut;
    private byte[]? _prevFrame;

    /// <summary>Ativa a supressão de ruído (gate/expansor adaptativo com VAD).</summary>
    public bool NoiseSuppression { get; set; } = true;

    /// <summary>Microfone silenciado (não envia áudio).</summary>
    public bool Muted { get; set; }

    public bool IsRunning { get; private set; }

    /// <summary>Um quadro de áudio capturado do microfone (PCM 16-bit).</summary>
    public event Action<byte[]>? FrameCaptured;

    public void Start(int inputDeviceNumber = -1)
    {
        Stop();
        // Reinicia o estado da supressão a cada sessão.
        _noiseFloor = 300; _gateOpen = false; _hangover = 0; _gain = 1.0;
        _hpPrevIn = _hpPrevOut = 0; _prevFrame = null;
        try
        {
            var mixFormat = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);
            _mixer = new MixingSampleProvider(mixFormat) { ReadFully = true };
            _out = new WaveOutEvent { DesiredLatency = 120 };
            _out.Init(new SampleToWaveProvider16(_mixer));
            _out.Play();

            _mic = new WaveInEvent
            {
                WaveFormat = _format,
                BufferMilliseconds = 40,
                DeviceNumber = inputDeviceNumber
            };
            _mic.DataAvailable += OnMicData;
            _mic.StartRecording();
            IsRunning = true;
        }
        catch
        {
            Stop(); // ambiente sem áudio
        }
    }

    private void OnMicData(object? sender, WaveInEventArgs e)
    {
        if (Muted) return;

        var buffer = new byte[e.BytesRecorded];
        Array.Copy(e.Buffer, buffer, e.BytesRecorded);

        // Sem supressão: envia direto.
        if (!NoiseSuppression) { FrameCaptured?.Invoke(buffer); return; }

        // 1) Passa-alta: remove rumble/zumbido de baixa frequência.
        HighPass(buffer);

        // 2) Energia do quadro e piso de ruído adaptativo.
        double rms = Rms(buffer);
        UpdateNoiseFloor(rms);
        double openTh = _noiseFloor * OpenRatio;
        double closeTh = _noiseFloor * CloseRatio;
        bool loud = rms > openTh;
        bool active = rms > closeTh;

        // Guarda o quadro "limpo" (ganho cheio) para recuperar o ataque na próxima abertura.
        var clean = (byte[])buffer.Clone();

        // 3) VAD com histerese + hangover.
        if (loud)
        {
            if (!_gateOpen && _prevFrame is not null)
                FrameCaptured?.Invoke(_prevFrame); // reenvia o anterior: não corta o início da fala
            _gateOpen = true;
            _hangover = HangoverFrames;
            _gain = 1.0;
        }
        else if (_gateOpen && active)
        {
            _hangover = HangoverFrames; // ainda há voz: mantém aberto
            _gain = 1.0;
        }

        if (_gateOpen)
        {
            if (!active)
            {
                // Em hangover: desce o ganho suavemente (release) até fechar.
                _hangover--;
                _gain *= 0.72;
                ApplyGain(buffer, _gain);
                if (_hangover <= 0) _gateOpen = false;
            }
            FrameCaptured?.Invoke(buffer);
        }

        _prevFrame = clean;
    }

    private static double Rms(byte[] pcm16)
    {
        int samples = pcm16.Length / 2;
        if (samples == 0) return 0;
        long sum = 0;
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            short v = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            sum += (long)v * v;
        }
        return Math.Sqrt((double)sum / samples);
    }

    private void UpdateNoiseFloor(double rms)
    {
        // Desce rápido (acompanha silêncio), sobe bem devagar (não "engole" a fala).
        if (rms < _noiseFloor) _noiseFloor = 0.9 * _noiseFloor + 0.1 * rms;
        else _noiseFloor = 0.9995 * _noiseFloor + 0.0005 * rms;
        _noiseFloor = Math.Clamp(_noiseFloor, FloorMin, FloorMax);
    }

    /// <summary>Filtro passa-alta de 1º ordem (in-place) para tirar rumble/hum.</summary>
    private void HighPass(byte[] pcm16)
    {
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            float x = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            float y = (float)(HpAlpha * (_hpPrevOut + x - _hpPrevIn));
            _hpPrevIn = x;
            _hpPrevOut = y;
            short s = (short)Math.Clamp(y, short.MinValue, short.MaxValue);
            pcm16[i] = (byte)(s & 0xFF);
            pcm16[i + 1] = (byte)((s >> 8) & 0xFF);
        }
    }

    private static void ApplyGain(byte[] pcm16, double gain)
    {
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            short v = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            int s = (int)Math.Round(v * gain);
            s = Math.Clamp(s, short.MinValue, short.MaxValue);
            pcm16[i] = (byte)(s & 0xFF);
            pcm16[i + 1] = (byte)((s >> 8) & 0xFF);
        }
    }

    /// <summary>Silencia (ou reativa) uma pessoa só para mim, localmente.</summary>
    public void SetPeerMuted(string peerId, bool muted)
    {
        lock (_lock)
        {
            if (muted)
            {
                _mutedPeers.Add(peerId);
                // Descarta o que já estava no buffer dessa pessoa.
                if (_inputs.TryGetValue(peerId, out var buf)) buf.ClearBuffer();
            }
            else _mutedPeers.Remove(peerId);
        }
    }

    public bool IsPeerMuted(string peerId)
    {
        lock (_lock) return _mutedPeers.Contains(peerId);
    }

    /// <summary>Reproduz um quadro recebido de um participante (mixado).</summary>
    public void PlayFrom(string peerId, byte[] pcm)
    {
        lock (_lock)
        {
            if (_mixer is null) return;
            if (_mutedPeers.Contains(peerId)) return; // silenciado só para mim
            if (!_inputs.TryGetValue(peerId, out var buf))
            {
                buf = new BufferedWaveProvider(_format)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(3)
                };
                _inputs[peerId] = buf;
                _mixer.AddMixerInput(buf.ToSampleProvider());
            }
            buf.AddSamples(pcm, 0, pcm.Length);
        }
    }

    public void Stop()
    {
        IsRunning = false;
        try { _mic?.StopRecording(); } catch { }
        _mic?.Dispose();
        _mic = null;
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        lock (_lock)
        {
            _inputs.Clear();
            _mixer = null;
        }
    }

    public void Dispose() => Stop();
}
