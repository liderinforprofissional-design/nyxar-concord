using System.Runtime.InteropServices;

namespace NyxarConcord.Services;

public sealed class AudioDevice
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public bool IsDefault { get; init; }
    public override string ToString() => Name;
}

/// <summary>
/// Enumera dispositivos de áudio de entrada (microfones) e saída
/// (alto-falantes/fones) usando a API nativa do Windows (WinMM) — sem dependências
/// externas. Os nomes ficam limitados a ~31 caracteres (limitação do WinMM);
/// para nomes completos, migre para WASAPI (NAudio) no futuro.
/// </summary>
public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioDevice> GetInputDevices()
    {
        var list = new List<AudioDevice>();
        try
        {
            uint count = waveInGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                var caps = new WAVEINCAPS();
                if (waveInGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<WAVEINCAPS>()) == 0)
                    list.Add(new AudioDevice { Id = i.ToString(), Name = caps.szPname, IsDefault = i == 0 });
            }
        }
        catch { /* sem áudio */ }
        return list;
    }

    public IReadOnlyList<AudioDevice> GetOutputDevices()
    {
        var list = new List<AudioDevice>();
        try
        {
            uint count = waveOutGetNumDevs();
            for (uint i = 0; i < count; i++)
            {
                var caps = new WAVEOUTCAPS();
                if (waveOutGetDevCaps(i, ref caps, (uint)Marshal.SizeOf<WAVEOUTCAPS>()) == 0)
                    list.Add(new AudioDevice { Id = i.ToString(), Name = caps.szPname, IsDefault = i == 0 });
            }
        }
        catch { /* sem áudio */ }
        return list;
    }

    // --- P/Invoke WinMM ---

    [DllImport("winmm.dll")]
    private static extern uint waveInGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveInGetDevCapsW")]
    private static extern uint waveInGetDevCaps(uint deviceId, ref WAVEINCAPS caps, uint size);

    [DllImport("winmm.dll")]
    private static extern uint waveOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "waveOutGetDevCapsW")]
    private static extern uint waveOutGetDevCaps(uint deviceId, ref WAVEOUTCAPS caps, uint size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEINCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WAVEOUTCAPS
    {
        public ushort wMid;
        public ushort wPid;
        public uint vDriverVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szPname;
        public uint dwFormats;
        public ushort wChannels;
        public ushort wReserved1;
        public uint dwSupport;
    }
}
