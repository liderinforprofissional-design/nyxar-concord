using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NyxarConcord.Services;

/// <summary>
/// Sons bonitos e discretos para as ações do app, sintetizados em memória
/// (sem arquivos .wav). Usa decaimento exponencial + harmônicos (timbre de
/// sino/marimba) e um "pingo d'água" com glide de frequência. Cada som toca
/// numa saída própria e é liberado ao terminar.
/// </summary>
public sealed class SoundService
{
    private const int Rate = 44100;

    /// <summary>Liga/desliga global (preferência do usuário).</summary>
    public bool Enabled { get; set; } = true;

    // --- Eventos do app ---
    public void MessageReceived() => Play(Drop(560, 1150, 210));                  // pingo d'água
    public void MessageSent()     => Play(Bell(784, 150, 0.20));                  // toque curto e limpo
    public void Mention()         => Play(Mix(Drop(700, 1500, 180), Delay(Drop(700, 1500, 180), 70)));
    public void JoinCall()        => Play(Arp(new[] { 523.25, 659.25, 783.99 }, 300, 95));  // dó-mi-sol sobe
    public void LeaveCall()       => Play(Arp(new[] { 783.99, 587.33, 392.00 }, 300, 95));  // desce
    public void UserJoined()      => Play(Arp(new[] { 659.25, 987.77 }, 280, 90));
    public void ScreenShare()     => Play(Mix(                                   // início: sobe, com corpo
                                          Arp(new[] { 523.25, 659.25, 783.99, 1046.50 }, 560, 115, 0.20),
                                          Bell(261.63, 680, 0.14)));             // C3 grave = corpo
    public void ScreenShareStop() => Play(Mix(                                   // fim: desce, com corpo
                                          Arp(new[] { 783.99, 587.33, 392.00 }, 520, 130, 0.20),
                                          Bell(196.00, 620, 0.13)));             // G3 grave = corpo
    public void WatcherJoined()   => Play(Arp(new[] { 659.25, 987.77, 1318.51 }, 300, 90)); // alguém começou a assistir você
    public void WatcherLeft()     => Play(Arp(new[] { 987.77, 659.25 }, 280, 105));          // alguém saiu da sua transmissão
    public void FileSent()        => Play(Drop(900, 1500, 130, 0.22));            // "whoosh" curto pra cima
    public void FileReceived()    => Play(Mix(Bell(659, 260, 0.18), Delay(Bell(987.77, 260, 0.18), 60)));
    public void MuteOn()          => Play(Drop(620, 330, 110, 0.26));             // desce (mutou)
    public void MuteOff()         => Play(Drop(330, 620, 110, 0.26));             // sobe (voltou)
    public void Success()         => Play(Arp(new[] { 523.25, 783.99, 1046.50 }, 320, 80)); // dó-sol-dó
    public void Error()           => Play(Arp(new[] { 415.30, 311.13 }, 320, 110));

    // ================= síntese =================

    /// <summary>Nota com timbre de sino/marimba: fundamental + harmônicos que
    /// decaem mais rápido, tudo com envelope exponencial.</summary>
    private static float[] Bell(double hz, int ms, double gain = 0.26)
    {
        int n = Rate * ms / 1000;
        var buf = new float[n];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double p = (double)i / n;
            double body = Math.Exp(-5.0 * p);
            double s = Math.Sin(2 * Math.PI * hz * t)
                     + 0.5 * Math.Exp(-8.0 * p) * Math.Sin(2 * Math.PI * hz * 2 * t)
                     + 0.22 * Math.Exp(-11.0 * p) * Math.Sin(2 * Math.PI * hz * 3.01 * t);
            buf[i] = (float)(s * body * gain);
        }
        return SoftAttack(buf);
    }

    /// <summary>"Pingo d'água": a frequência sobe rápido (glide) enquanto a
    /// amplitude decai — dá aquele "plim" arredondado e agradável.</summary>
    private static float[] Drop(double startHz, double endHz, int ms, double gain = 0.34)
    {
        int n = Rate * ms / 1000;
        var buf = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double p = (double)i / n;
            double f = startHz + (endHz - startHz) * (1 - Math.Pow(1 - p, 3)); // rápido no início
            phase += 2 * Math.PI * f / Rate;
            double amp = Math.Exp(-4.2 * p);
            double s = Math.Sin(phase) + 0.16 * Math.Sin(2 * phase);
            buf[i] = (float)(s * amp * gain);
        }
        return SoftAttack(buf);
    }

    /// <summary>Arpejo: toca as notas em sequência com sobreposição, então elas
    /// ressoam juntas (soa como um sininho de verdade).</summary>
    private static float[] Arp(double[] freqs, int noteMs, int stepMs, double gain = 0.24)
    {
        var notes = freqs.Select(f => Bell(f, noteMs, gain)).ToArray();
        int step = Rate * stepMs / 1000;
        int total = step * (notes.Length - 1) + notes.Max(a => a.Length);
        var buf = new float[total];
        for (int k = 0; k < notes.Length; k++)
        {
            int off = k * step;
            var nt = notes[k];
            for (int i = 0; i < nt.Length && off + i < total; i++) buf[off + i] += nt[i];
        }
        return Clamp(buf);
    }

    // ---- utilidades ----

    /// <summary>Soma dois buffers (mistura), no comprimento do maior.</summary>
    private static float[] Mix(float[] a, float[] b)
    {
        var buf = new float[Math.Max(a.Length, b.Length)];
        for (int i = 0; i < a.Length; i++) buf[i] += a[i];
        for (int i = 0; i < b.Length; i++) buf[i] += b[i];
        return Clamp(buf);
    }

    /// <summary>Copia um som deslocado no tempo (para eco/segunda batida).</summary>
    private static float[] Delay(float[] src, int ms)
    {
        int d = Rate * ms / 1000;
        var buf = new float[src.Length + d];
        for (int i = 0; i < src.Length; i++) buf[i + d] = src[i] * 0.7f;
        return buf;
    }

    /// <summary>Ataque de ~2 ms para não estalar no início.</summary>
    private static float[] SoftAttack(float[] buf)
    {
        int a = Rate * 2 / 1000;
        for (int i = 0; i < a && i < buf.Length; i++) buf[i] *= (float)i / a;
        return buf;
    }

    private static float[] Clamp(float[] buf)
    {
        for (int i = 0; i < buf.Length; i++) buf[i] = Math.Clamp(buf[i], -1f, 1f);
        return buf;
    }

    private void Play(float[] samples)
    {
        if (!Enabled) return;
        try
        {
            var wave = new WaveOutEvent { DesiredLatency = 100 };
            wave.Init(new SampleToWaveProvider16(new OneShotProvider(samples)));
            wave.PlaybackStopped += (_, _) => { try { wave.Dispose(); } catch { } };
            wave.Play();
        }
        catch { /* sem dispositivo de áudio — ignora */ }
    }

    /// <summary>Toca um buffer de floats uma única vez (mono).</summary>
    private sealed class OneShotProvider : ISampleProvider
    {
        private readonly float[] _data;
        private int _pos;

        public OneShotProvider(float[] data) => _data = data;

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, _data.Length - _pos);
            if (n <= 0) return 0;
            Array.Copy(_data, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
    }
}
