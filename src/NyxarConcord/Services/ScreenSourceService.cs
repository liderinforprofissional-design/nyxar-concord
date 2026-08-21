using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NyxarConcord.Services;

public enum ScreenSourceKind { Monitor, Window }

/// <summary>
/// Uma fonte que pode ser compartilhada: um monitor inteiro ou uma janela
/// específica (app/jogo em execução).
/// </summary>
public sealed class ScreenSource
{
    public ScreenSourceKind Kind { get; init; }
    public string Title { get; init; } = "";
    public IntPtr Handle { get; init; }          // HWND para janelas
    public int MonitorIndex { get; init; }        // índice para monitores

    // Coordenadas do monitor (usadas na captura). Para janelas usa-se GetWindowRect.
    public int X { get; init; }
    public int Y { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }

    /// <summary>Ícone do app (para janelas). Null para monitores.</summary>
    public ImageSource? Icon { get; init; }

    /// <summary>Seção para agrupar na UI.</summary>
    public string Category => Kind == ScreenSourceKind.Monitor ? "Telas" : "Janelas e aplicativos";

    public override string ToString() => Title;
}

/// <summary>
/// Lista as fontes de captura disponíveis no Windows: todos os monitores e todas
/// as janelas de aplicativos visíveis (incluindo jogos). Usa apenas P/Invoke da
/// user32 — sem dependência do WinForms (que conflita com o WPF).
/// </summary>
public sealed class ScreenSourceService
{
    public IReadOnlyList<ScreenSource> GetSources()
    {
        var sources = new List<ScreenSource>();
        int selfPid = Environment.ProcessId;

        // Monitores (EnumDisplayMonitors)
        int index = 0;
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr _, IntPtr _, ref RECT rect, IntPtr _) =>
        {
            int w = rect.right - rect.left;
            int h = rect.bottom - rect.top;
            bool primary = rect.left == 0 && rect.top == 0;
            sources.Add(new ScreenSource
            {
                Kind = ScreenSourceKind.Monitor,
                MonitorIndex = index,
                X = rect.left,
                Y = rect.top,
                Width = w,
                Height = h,
                Title = $"Tela {index + 1} ({w}x{h})" + (primary ? " • principal" : "")
            });
            index++;
            return true;
        }, IntPtr.Zero);

        // Janelas de apps/jogos (EnumWindows)
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;

            int len = GetWindowTextLength(hWnd);
            if (len == 0) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            string title = sb.ToString();
            if (string.IsNullOrWhiteSpace(title)) return true;

            long style = GetWindowLong(hWnd, GWL_STYLE);
            if ((style & WS_VISIBLE) == 0) return true;

            // Janelas do próprio Nyxar: usa o PNG do app (evita o alfa quebrado do WM_GETICON).
            GetWindowThreadProcessId(hWnd, out int pid);
            ImageSource? icon = pid == selfPid ? AppIcon() : GetWindowIcon(hWnd, pid);

            sources.Add(new ScreenSource
            {
                Kind = ScreenSourceKind.Window,
                Handle = hWnd,
                Title = title,
                Icon = icon
            });
            return true;
        }, IntPtr.Zero);

        return sources;
    }

    private const int WM_GETICON = 0x7F;

    private static ImageSource? _appIcon;

    /// <summary>Ícone oficial do app (PNG dos recursos), carregado uma vez.</summary>
    private static ImageSource? AppIcon()
    {
        if (_appIcon is not null) return _appIcon;
        try
        {
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri("pack://application:,,,/Assets/nyxar.png");
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.EndInit();
            img.Freeze();
            _appIcon = img;
        }
        catch { }
        return _appIcon;
    }

    private static ImageSource? GetWindowIcon(IntPtr hWnd, int pid)
    {
        // 1) Ícone da janela (WM_GETICON) — muitos apps respondem aqui.
        IntPtr hIcon = SendMessage(hWnd, WM_GETICON, new IntPtr(1), IntPtr.Zero);           // ICON_BIG
        if (hIcon == IntPtr.Zero) hIcon = SendMessage(hWnd, WM_GETICON, new IntPtr(2), IntPtr.Zero); // ICON_SMALL2
        if (hIcon == IntPtr.Zero) hIcon = SendMessage(hWnd, WM_GETICON, IntPtr.Zero, IntPtr.Zero);   // ICON_SMALL

        // 2) Ícone da CLASSE da janela — apps que não respondem WM_GETICON costumam ter aqui.
        if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hWnd, GCL_HICON);
        if (hIcon == IntPtr.Zero) hIcon = GetClassLongPtr(hWnd, GCL_HICONSM);

        if (hIcon != IntPtr.Zero)
        {
            var img = FromHIcon(hIcon);
            if (img is not null) return img;
        }

        // 3) Último recurso: extrai o ícone do próprio executável do processo.
        return IconFromExe(pid);
    }

    private static ImageSource? FromHIcon(IntPtr hIcon)
    {
        try
        {
            var img = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            img.Freeze();
            return img;
        }
        catch { return null; }
    }

    /// <summary>Extrai o ícone do executável do processo (para apps que não expõem HICON).</summary>
    private static ImageSource? IconFromExe(int pid)
    {
        string? path = GetProcessPath(pid);
        if (string.IsNullOrEmpty(path)) return null;

        var large = new IntPtr[1];
        var small = new IntPtr[1];
        try
        {
            if (ExtractIconEx(path, 0, large, small, 1) == 0) return null;
            IntPtr h = large[0] != IntPtr.Zero ? large[0] : small[0];
            if (h == IntPtr.Zero) return null;
            return FromHIcon(h);
        }
        catch { return null; }
        finally
        {
            if (large[0] != IntPtr.Zero) DestroyIcon(large[0]);
            if (small[0] != IntPtr.Zero) DestroyIcon(small[0]);
        }
    }

    private static string? GetProcessPath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            int cap = 1024;
            var sb = new StringBuilder(cap);
            return QueryFullProcessImageName(h, 0, sb, ref cap) ? sb.ToString() : null;
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    // --- P/Invoke ---
    private const int GWL_STYLE = -16;
    private const long WS_VISIBLE = 0x10000000L;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hWnd, out int processId);

    // --- Ícone da classe da janela ---
    private const int GCL_HICON = -14;
    private const int GCL_HICONSM = -34;

    // Em 64-bit usa-se GetClassLongPtr; em 32-bit o entry point cai em GetClassLongW.
    private static IntPtr GetClassLongPtr(IntPtr hWnd, int index) =>
        IntPtr.Size == 8 ? GetClassLongPtr64(hWnd, index) : new IntPtr(GetClassLong32(hWnd, index));

    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")]
    private static extern IntPtr GetClassLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetClassLongW")]
    private static extern uint GetClassLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // --- Extrair ícone do executável ---
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

    // --- Caminho do executável do processo ---
    private const int PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int flags, StringBuilder buffer, ref int size);
}
