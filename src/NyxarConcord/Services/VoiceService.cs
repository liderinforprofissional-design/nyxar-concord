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
    private readonly NoiseSuppressor _rnnoise = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, BufferedWaveProvider> _inputs = new();
    // Controle de volume por fluxo (voz e áudio de tela) e por pessoa.
    private readonly Dictionary<string, NAudio.Wave.SampleProviders.VolumeSampleProvider> _vol = new();
    private readonly Dictionary<string, float> _peerVol = new();
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

    /// <summary>Id do próprio usuário (para o indicador de "falando").</summary>
    public string SelfId { get; set; } = "";

    /// <summary>Um quadro de áudio capturado do microfone (PCM 16-bit).</summary>
    public event Action<byte[]>? FrameCaptured;

    /// <summary>Um quadro de áudio do computador (transmissão) — enviado separado da voz.</summary>
    public event Action<byte[]>? DesktopFrameCaptured;

    /// <summary>Alguém começou/parou de falar (id do participante, falando?).</summary>
    public event Action<string, bool>? SpeakingChanged;

    // --- Detecção de "falando" (indicador verde) ---
    private readonly object _spkLock = new();
    private readonly Dictionary<string, bool> _speaking = new();
    private readonly Dictionary<string, DateTime> _lastVoiceAt = new();
    private System.Timers.Timer? _spkSweep;
    private const double SpeakRms = 550;      // energia mínima para "falando"
    private const int SpeakHoldMs = 350;      // mantém aceso por um tempinho após a fala

    private void MarkVoice(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        bool raise = false;
        lock (_spkLock)
        {
            _lastVoiceAt[id] = DateTime.UtcNow;
            if (!_speaking.TryGetValue(id, out var sp) || !sp) { _speaking[id] = true; raise = true; }
        }
        if (raise) SpeakingChanged?.Invoke(id, true);
    }

    private void SweepSpeaking()
    {
        var stopped = new List<string>();
        lock (_spkLock)
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _speaking)
                if (kv.Value && _lastVoiceAt.TryGetValue(kv.Key, out var t)
                    && (now - t).TotalMilliseconds > SpeakHoldMs) stopped.Add(kv.Key);
            foreach (var id in stopped) _speaking[id] = false;
        }
        foreach (var id in stopped) SpeakingChanged?.Invoke(id, false);
    }

    /// <summary>
    /// True se algum participante REMOTO (não eu) falou há pouco. Usado para "abaixar"
    /// o áudio do PC transmitido nesse instante — assim a voz dos outros, que sai pelas
    /// minhas caixas e é capturada pelo loopback, não volta pra eles como eco.
    /// </summary>
    private bool RemoteSpeaking()
    {
        var now = DateTime.UtcNow;
        lock (_spkLock)
        {
            foreach (var kv in _lastVoiceAt)
            {
                if (kv.Key == SelfId) continue;
                if ((now - kv.Value).TotalMilliseconds <= SpeakHoldMs) return true;
            }
        }
        return false;
    }

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

            _spkSweep = new System.Timers.Timer(150) { AutoReset = true };
            _spkSweep.Elapsed += (_, _) => SweepSpeaking();
            _spkSweep.Start();
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

        // Indicador "falando": energia acima do piso conta como fala.
        if (Rms(buffer) > SpeakRms) MarkVoice(SelfId);

        // Sem supressão: envia direto.
        if (!NoiseSuppression) { FrameCaptured?.Invoke(buffer); return; }

        // Supressão por rede neural (RNNoise), se a DLL estiver disponível — bem
        // melhor que o gate. Se não, cai no gate adaptativo abaixo.
        if (_rnnoise.Process(buffer)) { FrameCaptured?.Invoke(buffer); return; }

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

    /// <summary>Reproduz um quadro recebido de um participante (mixado).
    /// bufferKey permite um fluxo separado (ex.: áudio da transmissão) sem estourar a voz;
    /// o mudo é sempre decidido pelo peer real (peerId).</summary>
    public void PlayFrom(string peerId, byte[] pcm, string? bufferKey = null, bool markSpeaking = true)
    {
        // Indicador "falando" do participante remoto (não para o áudio da transmissão).
        if (markSpeaking && Rms(pcm) > SpeakRms) MarkVoice(peerId);

        string key = bufferKey ?? peerId;
        lock (_lock)
        {
            if (_mixer is null) return;
            if (_mutedPeers.Contains(peerId)) return; // silenciado só para mim (voz + tela)
            if (!_inputs.TryGetValue(key, out var buf))
            {
                buf = new BufferedWaveProvider(_format)
                {
                    DiscardOnBufferOverflow = true,
                    BufferDuration = TimeSpan.FromSeconds(3)
                };
                _inputs[key] = buf;
                var vsp = new NAudio.Wave.SampleProviders.VolumeSampleProvider(buf.ToSampleProvider())
                {
                    Volume = _peerVol.TryGetValue(peerId, out var v) ? v : 1f
                };
                _vol[key] = vsp;
                _mixer.AddMixerInput(vsp);
            }
            buf.AddSamples(pcm, 0, pcm.Length);
        }
    }

    /// <summary>Define o volume de uma pessoa (0 = mudo, 1 = 100%, 2 = 200%). Vale para voz e tela.</summary>
    public void SetPeerVolume(string peerId, float volume)
    {
        volume = Math.Clamp(volume, 0f, 2f);
        lock (_lock)
        {
            _peerVol[peerId] = volume;
            if (_vol.TryGetValue(peerId, out var a)) a.Volume = volume;
            if (_vol.TryGetValue(peerId + "#scr", out var b)) b.Volume = volume;
        }
    }

    public float GetPeerVolume(string peerId)
    {
        lock (_lock) return _peerVol.TryGetValue(peerId, out var v) ? v : 1f;
    }

    public void Stop()
    {
        IsRunning = false;
        StopDesktopAudio();
        try { _spkSweep?.Stop(); _spkSweep?.Dispose(); } catch { }
        _spkSweep = null;
        lock (_spkLock) { _speaking.Clear(); _lastVoiceAt.Clear(); }
        try { _mic?.StopRecording(); } catch { }
        _mic?.Dispose();
        _mic = null;
        try { _out?.Stop(); } catch { }
        _out?.Dispose();
        _out = null;
        lock (_lock)
        {
            _inputs.Clear();
            _vol.Clear();
            _mixer = null;
        }
    }

    // ============================================================
    //  Áudio do computador (loopback) — para a transmissão de tela
    // ============================================================
    private WasapiLoopbackCapture? _loopback;
    private ProcessLoopbackCapture? _procLoop;
    // Verdadeiro quando a exclusão nativa do próprio app está ativa: aí NÃO precisa
    // do ducking (não abaixa o áudio do jogo), porque o eco já é eliminado na origem.
    private bool _excludeActive;
    private BufferedWaveProvider? _loopBuf;
    private IWaveProvider? _loop16;        // saída já em 16 kHz mono 16-bit

    /// <summary>Silencia o áudio do computador na transmissão (sem parar a captura).</summary>
    public bool DesktopAudioMuted { get; set; }
    private System.Timers.Timer? _loopTimer;
    private const int DesktopFrameBytes = 1280; // 40 ms @ 16 kHz mono 16-bit

    /// <summary>Captura o áudio que sai do computador (vídeos/jogo) e envia junto.
    /// Tenta a captura nativa que EXCLUI o próprio Nyxar (mata o eco na origem);
    /// se não der, cai no loopback comum + ducking (abaixa o áudio quando alguém fala).</summary>
    public void StartDesktopAudio()
    {
        StopDesktopAudio();
        if (TryStartExcludeCapture()) return; // caminho ideal (sem eco, sem abaixar o jogo)
        StartLoopbackFallback();              // fallback seguro
    }

    /// <summary>Captura nativa (WASAPI process-loopback) excluindo o processo do app.</summary>
    private bool TryStartExcludeCapture()
    {
        ProcessLoopbackCapture? cap = null;
        try
        {
            cap = new ProcessLoopbackCapture();
            _loopBuf = new BufferedWaveProvider(cap.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };
            cap.DataAvailable += (b, n) => _loopBuf?.AddSamples(b, 0, n);

            ISampleProvider sp = _loopBuf.ToSampleProvider();
            if (sp.WaveFormat.Channels != 1) sp = new DownmixToMonoSampleProvider(sp);
            sp = new WdlResamplingSampleProvider(sp, 16000);
            _loop16 = new SampleToWaveProvider16(sp);

            cap.Start();
            // Só confirma se a ativação nativa realmente vingou.
            if (!cap.WaitStarted(2000) || cap.Failed)
            {
                cap.Dispose();
                _loopBuf = null; _loop16 = null;
                return false;
            }

            _procLoop = cap;
            _excludeActive = true;
            _loopTimer = new System.Timers.Timer(40) { AutoReset = true };
            _loopTimer.Elapsed += (_, _) => PumpDesktopAudio();
            _loopTimer.Start();
            return true;
        }
        catch
        {
            try { cap?.Dispose(); } catch { }
            _loopBuf = null; _loop16 = null; _excludeActive = false;
            return false;
        }
    }

    /// <summary>Fallback: loopback comum do sistema (inclui tudo) + ducking anti-eco.</summary>
    private void StartLoopbackFallback()
    {
        try
        {
            _excludeActive = false;
            _loopback = new WasapiLoopbackCapture();
            _loopBuf = new BufferedWaveProvider(_loopback.WaveFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };
            _loopback.DataAvailable += (_, e) => _loopBuf?.AddSamples(e.Buffer, 0, e.BytesRecorded);

            // Cadeia 100% gerenciada: float (N canais) -> mono -> 16 kHz -> PCM16.
            ISampleProvider sp = _loopBuf.ToSampleProvider();
            if (sp.WaveFormat.Channels != 1) sp = new DownmixToMonoSampleProvider(sp);
            sp = new WdlResamplingSampleProvider(sp, 16000);
            _loop16 = new SampleToWaveProvider16(sp);

            _loopback.StartRecording();
            _loopTimer = new System.Timers.Timer(40) { AutoReset = true };
            _loopTimer.Elapsed += (_, _) => PumpDesktopAudio();
            _loopTimer.Start();
        }
        catch { StopDesktopAudio(); } // sem loopback disponível: segue sem o áudio do PC
    }

    private void PumpDesktopAudio()
    {
        var prov = _loop16;
        var buf = _loopBuf;
        if (prov is null || buf is null) return;
        try
        {
            // Envia no MÁXIMO 2 quadros por tique (evita "rajada" que atropela a voz
            // na fila do relay). Se acumulou muito, descarta o excedente.
            // Anti-eco (só no fallback): enquanto um participante remoto fala, abaixa bem
            // o áudio do PC. A voz dos outros sai pelas minhas caixas e o loopback comum a
            // captura; sem isso ela voltaria como eco ("se ouvir no áudio do outro").
            // Com a captura nativa que EXCLUI o app, o eco já não existe — sem ducking.
            bool duck = !_excludeActive && RemoteSpeaking();
            int sent = 0;
            while (buf.BufferedBytes > 0 && sent < 2)
            {
                var frame = new byte[DesktopFrameBytes];
                int got = prov.Read(frame, 0, frame.Length);
                if (got <= 0) break;
                if (DesktopAudioMuted) { sent++; continue; }
                if (got < frame.Length) Array.Clear(frame, got, frame.Length - got);
                if (duck) ApplyGain(frame, 0.12); // ~ -18 dB durante a fala remota
                DesktopFrameCaptured?.Invoke(frame); // fluxo SEPARADO da voz
                sent++;
            }
            // Se sobrou áudio atrasado demais no buffer, joga fora para não acumular latência.
            if (buf.BufferedBytes > buf.WaveFormat.AverageBytesPerSecond) buf.ClearBuffer();
        }
        catch { }
    }

    public void StopDesktopAudio()
    {
        try { _loopTimer?.Stop(); _loopTimer?.Dispose(); } catch { }
        _loopTimer = null;
        try { _procLoop?.Dispose(); } catch { }
        _procLoop = null;
        try { _loopback?.StopRecording(); } catch { }
        try { _loopback?.Dispose(); } catch { }
        _loopback = null;
        _loop16 = null;
        _loopBuf = null;
        _excludeActive = false;
    }

    /// <summary>Mistura todos os canais num só (média) — para qualquer nº de canais.</summary>
    private sealed class DownmixToMonoSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _src;
        private readonly int _ch;
        private float[] _buf = Array.Empty<float>();
        public DownmixToMonoSampleProvider(ISampleProvider src)
        {
            _src = src;
            _ch = src.WaveFormat.Channels;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 1);
        }
        public WaveFormat WaveFormat { get; }
        public int Read(float[] buffer, int offset, int count)
        {
            int need = count * _ch;
            if (_buf.Length < need) _buf = new float[need];
            int read = _src.Read(_buf, 0, need);
            int frames = read / _ch;
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int c = 0; c < _ch; c++) sum += _buf[i * _ch + c];
                buffer[offset + i] = sum / _ch;
            }
            return frames;
        }
    }

    public void Dispose() { Stop(); _rnnoise.Dispose(); }
}
