using System.Diagnostics;

namespace EasyExile.Radar.Overlay;

public enum GameWindowStatus
{
    /// <summary>The window exists, is visible, and has a usable client area.</summary>
    Tracking,

    /// <summary>Minimised to the taskbar. It still exists; it has no client area worth matching.</summary>
    Minimised,

    /// <summary>Alive but not currently showing a window.</summary>
    Hidden,

    /// <summary>The client is gone.</summary>
    Gone,
}

/// <summary>What the game window looked like the last time it was asked.</summary>
public readonly record struct GameWindowState(
    GameWindowStatus Status,
    ScreenRect ClientBounds,
    bool IsForeground,
    int Dpi)
{
    public static readonly GameWindowState Gone =
        new(GameWindowStatus.Gone, ScreenRect.Empty, false, 96);

    /// <summary>1.0 at 100% scaling, 1.5 at 150%, and so on.</summary>
    public float DpiScale => Dpi <= 0 ? 1f : Dpi / 96f;
}

/// <summary>Where the overlay should be, and whether it should be seen at all.</summary>
public readonly record struct OverlayPlacement(bool Visible, ScreenRect Bounds, string Reason);

/// <summary>
/// Decides where the overlay goes. Pure, so the rules can be tested without a
/// window, a display, or a running game.
/// </summary>
public static class OverlayPlacementPolicy
{
    public static OverlayPlacement Decide(GameWindowState game, bool showWhenUnfocused, bool interactive)
    {
        switch (game.Status)
        {
            case GameWindowStatus.Gone:
                return new OverlayPlacement(false, ScreenRect.Empty, "cliente encerrado");

            case GameWindowStatus.Minimised:
                return new OverlayPlacement(false, ScreenRect.Empty, "cliente minimizado");

            case GameWindowStatus.Hidden:
                return new OverlayPlacement(false, ScreenRect.Empty, "janela do cliente oculta");
        }

        if (game.ClientBounds.IsEmpty)
            return new OverlayPlacement(false, ScreenRect.Empty, "client area vazia");

        // Interactive mode keeps the overlay up even though the game has lost
        // focus, because clicking the settings panel is what took the focus
        // away. Hiding on the click that opened it would be unusable.
        if (!game.IsForeground && !showWhenUnfocused && !interactive)
            return new OverlayPlacement(false, game.ClientBounds, "cliente fora de foco");

        return new OverlayPlacement(true, game.ClientBounds, "ok");
    }
}

/// <summary>
/// Follows the window of the client the Core validated.
/// </summary>
/// <remarks>
/// It is handed one process and never looks for another. Path of Exile 1 and 2
/// share a process name, so searching by name from here could pick the game the
/// contract does not describe — the Core already resolved that question by
/// fingerprint, and this must not undo it.
/// </remarks>
internal sealed class GameWindowTracker
{
    private readonly Process _process;
    private nint _window;

    public GameWindowTracker(Process process)
    {
        _process = process;
        _window = process.MainWindowHandle;
    }

    public nint Window => _window;

    public GameWindowState Poll()
    {
        if (HasExited()) return GameWindowState.Gone;

        // The handle is cached and only re-read when it stops being a window.
        // MainWindowHandle enumerates top-level windows, which is not something
        // to do at render rate.
        if (!Native.IsAlive(_window))
        {
            _window = RefreshWindow();

            if (!Native.IsAlive(_window)) return GameWindowState.Gone;
        }

        var foreground = Native.IsForeground(_window);
        var dpi = Native.DpiFor(_window);

        if (Native.IsMinimised(_window))
            return new GameWindowState(GameWindowStatus.Minimised, ScreenRect.Empty, foreground, dpi);

        if (!Native.IsVisible(_window))
            return new GameWindowState(GameWindowStatus.Hidden, ScreenRect.Empty, foreground, dpi);

        var bounds = Native.ClientBounds(_window);

        return bounds is { } client
            ? new GameWindowState(GameWindowStatus.Tracking, client, foreground, dpi)
            : new GameWindowState(GameWindowStatus.Hidden, ScreenRect.Empty, foreground, dpi);
    }

    private bool HasExited()
    {
        try
        {
            return _process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private nint RefreshWindow()
    {
        try
        {
            _process.Refresh();
            return _process.MainWindowHandle;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }
}
