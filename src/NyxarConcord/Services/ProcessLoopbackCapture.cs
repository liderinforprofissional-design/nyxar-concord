using System.Runtime.InteropServices;
using NAudio.Wave;

namespace NyxarConcord.Services;

/// <summary>
/// Captura o áudio que sai do computador EXCLUINDO um processo (e a sua árvore),
/// via a API WASAPI de "process loopback" (Windows 10 2004 / build 19041+).
///
/// Uso no Nyxar: transmitir o áudio do PC (jogo/vídeo) sem incluir o próprio app.
/// Como a voz dos outros participantes sai pelas caixas e o loopback comum a
/// recapturaria, ela voltava pra eles como eco ("se ouvir no áudio do outro").
/// Excluindo o processo do Nyxar, só o áudio "de fora" é capturado — fim do eco,
/// e sem precisar abaixar o volume do jogo.
///
/// Se a ativação nativa falhar (SO antigo, driver, etc.), <see cref="Failed"/>
/// fica verdadeiro e o chamador deve usar o loopback comum como fallback.
/// </summary>
internal sealed class ProcessLoopbackCapture : IDisposable
{
    /// <summary>Formato entregue ao consumidor: PCM 48 kHz, 16-bit, estéreo.</summary>
    public WaveFormat WaveFormat { get; } = new WaveFormat(48000, 16, 2);

    /// <summary>Um bloco de áudio capturado (buffer, nº de bytes válidos).</summary>
    public event Action<byte[], int>? DataAvailable;

    /// <summary>Verdadeiro se a captura nativa não conseguiu iniciar.</summary>
    public bool Failed { get; private set; }

    private readonly ManualResetEventSlim _ready = new(false);
    private volatile bool _running;
    private Thread? _thread;

    private IAudioClient? _audioClient;
    private IAudioCaptureClient? _capture;
    private AutoResetEvent? _bufferEvent;
    private IntPtr _pFormat = IntPtr.Zero;

    // --- Constantes WASAPI ---
    private const string VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK = "VAD\\Process_Loopback";
    private const int AUDCLNT_SHAREMODE_SHARED = 0;
    private const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
    private const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
    private const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
    private const int AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK = 1;
    private const int PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE = 1;
    private const short VT_BLOB = 65;

    private static readonly Guid IID_IAudioClient =
        new("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2");
    private static readonly Guid IID_IAudioCaptureClient =
        new("C8ADBD64-E71E-48A0-A4DE-185C395CD317");

    /// <summary>Inicia a captura numa thread própria (MTA).</summary>
    public void Start()
    {
        _running = true;
        _thread = new Thread(CaptureLoop) { IsBackground = true, Name = "NyxarProcLoopback" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Espera a inicialização terminar (com sucesso ou falha). </summary>
    public bool WaitStarted(int ms) => _ready.Wait(ms);

    private void CaptureLoop()
    {
        IntPtr pParams = IntPtr.Zero;
        IntPtr pProp = IntPtr.Zero;
        try
        {
            // 1) Monta os parâmetros de ativação (excluir a árvore do processo atual).
            var acp = new AudioClientActivationParams
            {
                ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK,
                TargetProcessId = (uint)Environment.ProcessId,
                ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE
            };
            pParams = Marshal.AllocHGlobal(Marshal.SizeOf<AudioClientActivationParams>());
            Marshal.StructureToPtr(acp, pParams, false);

            // 2) Embrulha num PROPVARIANT do tipo BLOB (layout válido em x86 e x64).
            int ptrOffset = 8 + (IntPtr.Size == 8 ? 8 : 4);
            int propSize = ptrOffset + IntPtr.Size;
            pProp = Marshal.AllocHGlobal(propSize);
            for (int i = 0; i < propSize; i++) Marshal.WriteByte(pProp, i, 0);
            Marshal.WriteInt16(pProp, 0, VT_BLOB);
            Marshal.WriteInt32(pProp, 8, Marshal.SizeOf<AudioClientActivationParams>()); // blob.cbSize
            Marshal.WriteIntPtr(pProp, ptrOffset, pParams);                              // blob.pBlobData

            // 3) Ativa a interface de áudio de forma assíncrona e espera o resultado.
            var handler = new ActivationHandler();
            ActivateAudioInterfaceAsync(
                VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, IID_IAudioClient, pProp, handler,
                out IActivateAudioInterfaceAsyncOperation op);
            if (!handler.Wait(3000)) throw new TimeoutException("process-loopback activation timeout");

            int hr = op.GetActivateResult(out int activateHr, out object clientObj);
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(activateHr);
            _audioClient = (IAudioClient)clientObj;

            // 4) Inicializa em modo compartilhado, loopback + event callback.
            _pFormat = WaveFormat.MarshalToPtr(WaveFormat); // WAVEFORMATEX PCM 48k/16/2
            const long bufferHns = 200 * 10000; // 200 ms
            hr = _audioClient.Initialize(
                AUDCLNT_SHAREMODE_SHARED,
                AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK,
                bufferHns, 0, _pFormat, IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);

            _bufferEvent = new AutoResetEvent(false);
            Marshal.ThrowExceptionForHR(
                _audioClient.SetEventHandle(_bufferEvent.SafeWaitHandle.DangerousGetHandle()));

            Marshal.ThrowExceptionForHR(
                _audioClient.GetService(IID_IAudioCaptureClient, out object capObj));
            _capture = (IAudioCaptureClient)capObj;

            Marshal.ThrowExceptionForHR(_audioClient.Start());

            // Inicialização OK — libera o chamador.
            Failed = false;
            _ready.Set();

            int frameSize = WaveFormat.Channels * WaveFormat.BitsPerSample / 8; // 4 bytes
            while (_running)
            {
                if (!_bufferEvent.WaitOne(200)) continue;
                while (_running)
                {
                    if (_capture.GetNextPacketSize(out uint packet) != 0 || packet == 0) break;
                    if (_capture.GetBuffer(out IntPtr data, out uint frames, out uint flags, out _, out _) != 0)
                        break;
                    int bytes = (int)frames * frameSize;
                    if (frames > 0)
                    {
                        var arr = new byte[bytes];
                        if ((flags & AUDCLNT_BUFFERFLAGS_SILENT) == 0 && data != IntPtr.Zero)
                            Marshal.Copy(data, arr, 0, bytes);
                        DataAvailable?.Invoke(arr, bytes);
                    }
                    _capture.ReleaseBuffer(frames);
                }
            }

            try { _audioClient.Stop(); } catch { }
        }
        catch
        {
            // Falha: sinaliza pro chamador cair no fallback (loopback comum).
            Failed = true;
            _ready.Set();
        }
        finally
        {
            if (pParams != IntPtr.Zero) Marshal.FreeHGlobal(pParams);
            if (pProp != IntPtr.Zero) Marshal.FreeHGlobal(pProp);
        }
    }

    public void Dispose()
    {
        _running = false;
        try { _bufferEvent?.Set(); } catch { }
        try { _thread?.Join(500); } catch { }
        try { if (_capture is not null) Marshal.ReleaseComObject(_capture); } catch { }
        try { if (_audioClient is not null) Marshal.ReleaseComObject(_audioClient); } catch { }
        _capture = null; _audioClient = null;
        try { _bufferEvent?.Dispose(); } catch { }
        _bufferEvent = null;
        if (_pFormat != IntPtr.Zero) { try { Marshal.FreeHGlobal(_pFormat); } catch { } _pFormat = IntPtr.Zero; }
        _ready.Dispose();
    }

    // ================= Interop =================

    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = false)]
    private static extern void ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        IntPtr activationParams,
        IActivateAudioInterfaceCompletionHandler completionHandler,
        out IActivateAudioInterfaceAsyncOperation operation);

    [StructLayout(LayoutKind.Sequential)]
    private struct AudioClientActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    [ComImport, Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult,
            [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandler
    {
        private readonly ManualResetEventSlim _done = new(false);
        public int ActivateCompleted(IActivateAudioInterfaceAsyncOperation op) { _done.Set(); return 0; }
        public bool Wait(int ms) => _done.Wait(ms);
    }

    [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioClient
    {
        [PreserveSig] int Initialize(int shareMode, uint streamFlags, long hnsBufferDuration,
            long hnsPeriodicity, IntPtr pFormat, IntPtr audioSessionGuid);
        [PreserveSig] int GetBufferSize(out uint numBufferFrames);
        [PreserveSig] int GetStreamLatency(out long latency);
        [PreserveSig] int GetCurrentPadding(out uint numPaddingFrames);
        [PreserveSig] int IsFormatSupported(int shareMode, IntPtr pFormat, out IntPtr closestMatch);
        [PreserveSig] int GetMixFormat(out IntPtr deviceFormat);
        [PreserveSig] int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);
        [PreserveSig] int Start();
        [PreserveSig] int Stop();
        [PreserveSig] int Reset();
        [PreserveSig] int SetEventHandle(IntPtr eventHandle);
        [PreserveSig] int GetService([MarshalAs(UnmanagedType.LPStruct)] Guid interfaceId,
            [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioCaptureClient
    {
        [PreserveSig] int GetBuffer(out IntPtr data, out uint numFramesToRead,
            out uint flags, out long devicePosition, out long qpcPosition);
        [PreserveSig] int ReleaseBuffer(uint numFramesRead);
        [PreserveSig] int GetNextPacketSize(out uint numFramesInNextPacket);
    }
}
