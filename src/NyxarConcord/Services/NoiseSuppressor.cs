using System.Runtime.InteropServices;

namespace NyxarConcord.Services;

/// <summary>
/// Supressor de ruído por rede neural (RNNoise) — grátis, leve e funciona em
/// qualquer placa. Se a DLL nativa (rnnoise.dll) não estiver presente, fica
/// indisponível e o app usa o gate simples como reserva.
///
/// RNNoise trabalha em 48 kHz, quadros de 480 amostras (10 ms), com as amostras
/// em float na FAIXA de int16 (-32768..32767). Nosso áudio é 16 kHz, então cada
/// quadro de 40 ms (640 amostras) é reamostrado para 48 kHz (1920 = 4×480),
/// processado e reamostrado de volta para 16 kHz.
/// </summary>
public sealed class NoiseSuppressor : IDisposable
{
    private IntPtr _state;
    private bool _tried;
    public bool Available { get; private set; }

    // Buffers reutilizados (evita alocar por quadro).
    private readonly float[] _up = new float[1920];
    private readonly float[] _in480 = new float[480];
    private readonly float[] _out480 = new float[480];
    private readonly float[] _up2 = new float[1920];

    private void EnsureInit()
    {
        if (_tried) return;
        _tried = true;
        try
        {
            _state = rnnoise_create(IntPtr.Zero);
            Available = _state != IntPtr.Zero;
        }
        catch { Available = false; } // DLL ausente / incompatível
    }

    /// <summary>
    /// Limpa o ruído do quadro (PCM 16-bit, 16 kHz, 640 amostras = 1280 bytes),
    /// no próprio buffer. Retorna false se não deu para processar (usar o reserva).
    /// </summary>
    public bool Process(byte[] pcm16)
    {
        EnsureInit();
        if (!Available || pcm16.Length != 1280) return false;
        try
        {
            int n = 640;
            // short -> float (mesma magnitude do int16)
            // e upsample x3 (linear) para 1920 amostras @48k.
            for (int k = 0; k < n; k++)
            {
                short s = (short)(pcm16[k * 2] | (pcm16[k * 2 + 1] << 8));
                short s2 = (k + 1 < n)
                    ? (short)(pcm16[(k + 1) * 2] | (pcm16[(k + 1) * 2 + 1] << 8))
                    : s;
                float a = s, b = s2;
                _up[k * 3] = a;
                _up[k * 3 + 1] = a + (b - a) / 3f;
                _up[k * 3 + 2] = a + 2f * (b - a) / 3f;
            }

            // 4 quadros de 480 pela RNNoise.
            for (int f = 0; f < 4; f++)
            {
                Array.Copy(_up, f * 480, _in480, 0, 480);
                rnnoise_process_frame(_state, _out480, _in480);
                Array.Copy(_out480, 0, _up2, f * 480, 480);
            }

            // downsample /3 (média) de volta para 640 @16k.
            for (int k = 0; k < n; k++)
            {
                float v = (_up2[k * 3] + _up2[k * 3 + 1] + _up2[k * 3 + 2]) / 3f;
                int iv = (int)Math.Round(v);
                if (iv > short.MaxValue) iv = short.MaxValue;
                else if (iv < short.MinValue) iv = short.MinValue;
                pcm16[k * 2] = (byte)(iv & 0xFF);
                pcm16[k * 2 + 1] = (byte)((iv >> 8) & 0xFF);
            }
            return true;
        }
        catch { Available = false; return false; }
    }

    public void Dispose()
    {
        try { if (_state != IntPtr.Zero) rnnoise_destroy(_state); } catch { }
        _state = IntPtr.Zero;
    }

    // --- P/Invoke (rnnoise.dll deve estar na pasta do app) ---
    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr rnnoise_create(IntPtr model);

    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rnnoise_destroy(IntPtr st);

    [DllImport("rnnoise", CallingConvention = CallingConvention.Cdecl)]
    private static extern float rnnoise_process_frame(IntPtr st, float[] outFrame, float[] inFrame);
}
