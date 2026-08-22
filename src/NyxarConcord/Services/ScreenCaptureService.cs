using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace NyxarConcord.Services;

/// <summary>
/// Captura um monitor ou janela do Windows e devolve os bytes em JPEG.
/// Usa GDI (BitBlt) + imaging do WPF — sem dependências externas.
/// </summary>
public sealed class ScreenCaptureService
{
    /// <summary>Captura a fonte e devolve JPEG. maxHeight limita a resolução (720/480/360).</summary>
    public byte[]? CaptureJpeg(ScreenSource source, int maxHeight = 720, int quality = 50)
    {
        int x, y, w, h;

        if (source.Kind == ScreenSourceKind.Window && source.Handle != IntPtr.Zero)
        {
            if (!GetWindowRect(source.Handle, out RECT r)) return null;
            x = r.left; y = r.top; w = r.right - r.left; h = r.bottom - r.top;
        }
        else
        {
            x = source.X; y = source.Y; w = source.Width; h = source.Height;
        }

        if (w <= 0 || h <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, hBitmap);

        try
        {
            BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY);

            var source2 = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            // Escala pela ALTURA para atingir a resolução escolhida (ex.: 720p).
            BitmapSource frame = source2;
            if (h > maxHeight)
            {
                double scale = (double)maxHeight / h;
                frame = new TransformedBitmap(source2, new System.Windows.Media.ScaleTransform(scale, scale));
            }
            frame.Freeze();

            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(frame));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Captura a fonte em BGR 24-bit cru (para o codec de vídeo WebRTC/VP8).</summary>
    public byte[]? CaptureBgr(ScreenSource source, int maxHeight, out int outWidth, out int outHeight)
    {
        outWidth = 0; outHeight = 0;
        int x, y, w, h;

        if (source.Kind == ScreenSourceKind.Window && source.Handle != IntPtr.Zero)
        {
            if (!GetWindowRect(source.Handle, out RECT r)) return null;
            x = r.left; y = r.top; w = r.right - r.left; h = r.bottom - r.top;
        }
        else
        {
            x = source.X; y = source.Y; w = source.Width; h = source.Height;
        }
        if (w <= 0 || h <= 0) return null;

        IntPtr screenDc = GetDC(IntPtr.Zero);
        IntPtr memDc = CreateCompatibleDC(screenDc);
        IntPtr hBitmap = CreateCompatibleBitmap(screenDc, w, h);
        IntPtr old = SelectObject(memDc, hBitmap);
        try
        {
            BitBlt(memDc, 0, 0, w, h, screenDc, x, y, SRCCOPY);
            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            BitmapSource frame = src;
            if (h > maxHeight)
            {
                double scale = (double)maxHeight / h;
                frame = new TransformedBitmap(src, new System.Windows.Media.ScaleTransform(scale, scale));
            }

            var bgr = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgr24, null, 0);
            int fw = bgr.PixelWidth, fh = bgr.PixelHeight;
            fw -= fw % 2; fh -= fh % 2;                // dimensões pares (exigência do VP8)
            if (fw <= 0 || fh <= 0) return null;

            int stride = fw * 3;
            var buf = new byte[stride * fh];
            bgr.CopyPixels(new Int32Rect(0, 0, fw, fh), buf, stride, 0);
            outWidth = fw; outHeight = fh;
            return buf;
        }
        catch { return null; }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(hBitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // --- P/Invoke GDI ---
    private const int SRCCOPY = 0x00CC0020;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int left, top, right, bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dest, int xd, int yd, int w, int h,
                                                               IntPtr src, int xs, int ys, int rop);
}
