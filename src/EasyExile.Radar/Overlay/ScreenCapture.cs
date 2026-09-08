using System.Runtime.InteropServices;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyExile.Radar.Overlay;

/// <summary>
/// A picture of the whole screen — the game and this overlay on top of it.
/// </summary>
/// <remarks>
/// Neither of the obvious ways to take one works here. The client binds Print
/// Screen and writes its own file from the game's back buffer, so the overlay is
/// absent from it; Windows 11 binds the key again for the Snipping Tool, which
/// cannot draw over a fullscreen game and so appears to do nothing at all. What
/// is actually wanted is the composited desktop, which is neither of those.
///
/// GDI rather than System.Drawing.Common, to avoid adding a package for sixty
/// lines: BitBlt the screen into a DIB, then hand the pixels to ImageSharp,
/// which is already here for the terrain texture.
/// </remarks>
public static class ScreenCapture
{
    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    private const int SRCCOPY = 0x00CC0020;

    /// <summary>Include layered windows, which is what this overlay is.</summary>
    private const int CAPTUREBLT = 0x40000000;

    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;

    /// <summary>
    /// Writes a PNG of the screen and returns its path, or null if it failed.
    /// </summary>
    public static string? Save(string directory)
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var width = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var height = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (width <= 0 || height <= 0) return null;

        var screen = GetDC(nint.Zero);

        if (screen == nint.Zero) return null;

        var memory = nint.Zero;
        var bitmap = nint.Zero;

        try
        {
            memory = CreateCompatibleDC(screen);
            bitmap = CreateCompatibleBitmap(screen, width, height);

            if (memory == nint.Zero || bitmap == nint.Zero) return null;

            var previous = SelectObject(memory, bitmap);

            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT)) return null;

            SelectObject(memory, previous);

            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,

                // Negative height asks for a top-down rows, which is the order
                // ImageSharp expects. Bottom-up would arrive upside down.
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB,
            };

            var pixels = new byte[width * height * 4];

            if (GetDIBits(memory, bitmap, 0, (uint)height, pixels, ref header, DIB_RGB_COLORS) == 0)
                return null;

            Directory.CreateDirectory(directory);

            var path = Path.Combine(
                directory, $"easyexile-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");

            using var image = Image.LoadPixelData<Bgra32>(pixels, width, height);

            image.SaveAsPng(path);

            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ExternalException)
        {
            // A screenshot that cannot be written is not worth taking the
            // overlay down for.
            return null;
        }
        finally
        {
            if (bitmap != nint.Zero) DeleteObject(bitmap);
            if (memory != nint.Zero) DeleteDC(memory);

            ReleaseDC(nint.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint window, nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);

    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint handle);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(
        nint destination, int dx, int dy, int width, int height,
        nint source, int sx, int sy, int operation);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(
        nint dc, nint bitmap, uint start, uint lines, byte[] pixels,
        ref BITMAPINFOHEADER header, int usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);
}
