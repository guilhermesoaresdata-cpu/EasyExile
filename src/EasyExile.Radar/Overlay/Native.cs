using System.Runtime.InteropServices;

namespace EasyExile.Radar.Overlay;

/// <summary>A rectangle in screen coordinates.</summary>
public readonly record struct ScreenRect(int X, int Y, int Width, int Height)
{
    public static readonly ScreenRect Empty = new(0, 0, 0, 0);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public int Right => X + Width;
    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height} @ {X},{Y}";
}

/// <summary>
/// The Win32 surface the overlay needs, and nothing more.
/// </summary>
/// <remarks>
/// Everything here observes or positions our own window. Nothing writes to the
/// game, sends it input, or hooks it — the same rule the memory layer follows.
/// </remarks>
internal static class Native
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X, Y;
    }

    /// <summary>
    /// The client area in screen coordinates: the drawable part of the window,
    /// with the border and the title bar excluded.
    /// </summary>
    /// <remarks>
    /// GetWindowRect would include the frame, which in a windowed client puts
    /// every marker off by the border width and the title bar height. The game
    /// renders into the client area and the projection matrix describes that
    /// area, so that is what the overlay has to match.
    /// </remarks>
    public static ScreenRect? ClientBounds(nint window)
    {
        if (window == 0 || !IsWindow(window)) return null;
        if (!GetClientRect(window, out var client)) return null;

        var origin = new Point { X = client.Left, Y = client.Top };
        if (!ClientToScreen(window, ref origin)) return null;

        return new ScreenRect(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
    }

    public static bool IsAlive(nint window) => window != 0 && IsWindow(window);

    public static bool IsMinimised(nint window) => window != 0 && IsIconic(window);

    public static bool IsVisible(nint window) => window != 0 && IsWindowVisible(window);

    public static bool IsForeground(nint window) => window != 0 && GetForegroundWindow() == window;

    public static nint Foreground() => GetForegroundWindow();

    /// <summary>Dots per inch of the monitor the window is on. 96 is unscaled.</summary>
    public static int DpiFor(nint window)
    {
        if (window == 0) return 96;

        try
        {
            var dpi = GetDpiForWindow(window);
            return dpi == 0 ? 96 : (int)dpi;
        }
        catch (EntryPointNotFoundException)
        {
            // Pre-1607 Windows. The overlay still works, it just cannot ask.
            return 96;
        }
    }

    /// <summary>
    /// Opts the process into per-monitor DPI awareness before any window exists.
    /// </summary>
    /// <remarks>
    /// Without this, Windows lies to a scaled process about coordinates and sizes:
    /// GetClientRect returns virtualised numbers and the overlay lands in the
    /// wrong place on any display above 100%, in a way that looks like an
    /// arithmetic bug and is not one. It must run before the first window is
    /// created, which is why it is the first thing the program does.
    /// </remarks>
    public static string EnableDpiAwareness()
    {
        try
        {
            if (SetProcessDpiAwarenessContext(PerMonitorAwareV2)) return "per-monitor v2";
        }
        catch (EntryPointNotFoundException)
        {
            // Falls through to the older API below.
        }

        try
        {
            return SetProcessDPIAware() ? "system" : "none";
        }
        catch (EntryPointNotFoundException)
        {
            return "none";
        }
    }

    /// <summary>
    /// True while the key is down. A poll, deliberately: a keyboard hook would
    /// see every keystroke the player makes, including the ones typed into the
    /// game, and this needs to know about exactly one key.
    /// </summary>
    public static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    // ---- overlay window styling ---------------------------------------------

    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    private static readonly nint PerMonitorAwareV2 = -4;

    /// <summary>
    /// Keeps the overlay out of the taskbar and out of the activation order, so
    /// clicking it never pulls focus away from the game.
    /// </summary>
    public static void MakeToolWindow(nint window)
    {
        if (window == 0) return;

        var style = GetWindowLongPtr(window, GwlExStyle);
        SetWindowLongPtr(window, GwlExStyle, style | WsExToolWindow | WsExNoActivate);
    }

    /// <summary>Whether mouse input falls through the overlay to the game.</summary>
    public static void SetClickThrough(nint window, bool clickThrough)
    {
        if (window == 0) return;

        var style = GetWindowLongPtr(window, GwlExStyle);

        var updated = clickThrough
            ? style | WsExTransparent
            : style & ~WsExTransparent;

        if (updated != style) SetWindowLongPtr(window, GwlExStyle, updated);
    }

    /// <summary>The cursor in screen pixels, or null when Windows will not say.</summary>
    public static (int X, int Y)? CursorPosition() =>
        GetCursorPos(out var point) ? (point.X, point.Y) : null;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    public static bool IsClickThrough(nint window) =>
        window != 0 && (GetWindowLongPtr(window, GwlExStyle) & WsExTransparent) != 0;

    private const int SwHide = 0;
    private const int SwShowNoActivate = 4;

    /// <summary>
    /// Shows or hides the overlay without ever activating it. Activating would
    /// take focus from the game, and the game pauses its simulation when it
    /// loses focus — an overlay that stops the world to draw itself is worse
    /// than no overlay.
    /// </summary>
    public static void SetVisible(nint window, bool visible)
    {
        if (window == 0) return;

        ShowWindow(window, visible ? SwShowNoActivate : SwHide);
    }

    private static nint GetWindowLongPtr(nint window, int index) =>
        nint.Size == 8 ? GetWindowLongPtr64(window, index) : GetWindowLong32(window, index);

    private static nint SetWindowLongPtr(nint window, int index, nint value) =>
        nint.Size == 8 ? SetWindowLongPtr64(window, index, value) : SetWindowLong32(window, index, value);

    // ---- imports -------------------------------------------------------------

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessDPIAware();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern nint GetWindowLong32(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr64(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern nint SetWindowLong32(nint window, int index, nint value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr64(nint window, int index, nint value);
}
