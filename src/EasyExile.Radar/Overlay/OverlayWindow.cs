using System.Drawing;
using ClickableTransparentOverlay;

namespace EasyExile.Radar.Overlay;

/// <summary>
/// The transparent, borderless, always-on-top window the radar draws into.
/// </summary>
/// <remarks>
/// An ordinary window of our own, outside the game's process. Nothing is
/// injected into Path of Exile and its Direct3D is never hooked — the client is
/// read and never touched, and the same restraint applies to how it is drawn on.
///
/// The base class owns the D3D11 device, the swap chain and the ImGui backends;
/// this type owns where the window sits and whether the mouse goes through it.
/// </remarks>
internal sealed class OverlayWindow : ClickableTransparentOverlay.Overlay
{
    private readonly Action<OverlayWindow> _drawFrame;
    private readonly Func<Task> _afterStart;

    private ScreenRect _appliedBounds = ScreenRect.Empty;
    private bool _appliedVisible = true;
    private bool _styled;

    public OverlayWindow(Action<OverlayWindow> drawFrame, Func<Task> afterStart, int fps = 60)
        : base("EasyExile", DPIAware: true)
    {
        _drawFrame = drawFrame;
        _afterStart = afterStart;

        // High. Dropping it to sixty was measured and saved no CPU at all —
        // the core this overlay spends is the world walk, not the drawing — so
        // capping it only cost smoothness. The render rate floats free of the
        // capture rate, which is the whole point of having two.
        FPSLimit = fps > 0 ? fps : 144;
        VSync = false;
    }

    public nint Handle => window?.Handle ?? 0;

    /// <summary>Whether the settings panel is up and the overlay takes input.</summary>
    public bool Interactive { get; set; }

    /// <summary>
    /// True only while the cursor is over something of ours worth clicking.
    /// </summary>
    /// <remarks>
    /// Interactive mode used to take the mouse for the WHOLE screen, so opening
    /// the panel meant the game could not be clicked at all until it was closed
    /// again. The overlay covers the client area; owning every pixel of it
    /// because a panel is open in one corner is the wrong trade.
    /// </remarks>
    public bool CursorOverPanel { get; set; }

    /// <summary>Matches the overlay to the game's client area, if it moved.</summary>
    public void Place(OverlayPlacement placement)
    {
        EnsureStyled();

        if (!placement.Visible)
        {
            if (_appliedVisible)
            {
                Native.SetVisible(Handle, false);
                _appliedVisible = false;
            }

            return;
        }

        var bounds = placement.Bounds;

        // SetWindowPos on every frame would be a syscall per frame for a window
        // that moves once in a while.
        if (bounds != _appliedBounds)
        {
            Position = new Point(bounds.X, bounds.Y);
            Size = new Size(bounds.Width, bounds.Height);
            _appliedBounds = bounds;
        }

        if (!_appliedVisible)
        {
            Native.SetVisible(Handle, true);
            _appliedVisible = true;
        }
    }

    /// <summary>
    /// In HUD mode every click belongs to the game; in interactive mode the
    /// overlay takes them.
    /// </summary>
    /// <remarks>
    /// Asserted every frame rather than only when the mode changes. The base
    /// library decides click-through for itself from whether ImGui wants the
    /// mouse, and it re-applies that decision on its own schedule, so a
    /// once-per-toggle write gets silently reverted.
    ///
    /// We cannot use ImGui's own "is the panel hovered" answer either: while the
    /// window is click-through the cursor never reaches the panel, so it is
    /// never hovered, so the window stays click-through. The way out is to test
    /// the cursor against the panel's rectangle ourselves — geometry, not
    /// hover state, and it has no such circularity.
    /// </remarks>
    public void ApplyInputMode()
    {
        Native.SetClickThrough(Handle, !(Interactive && CursorOverPanel));
    }

    public bool IsClickThrough => Native.IsClickThrough(Handle);

    protected override Task PostInitialized() => _afterStart();

    protected override void Render() => _drawFrame(this);

    /// <summary>
    /// Keeps the overlay off the taskbar and out of the activation order. Done
    /// once the handle exists, which is after the base class has built it.
    /// </summary>
    private void EnsureStyled()
    {
        if (_styled || Handle == 0) return;

        Native.MakeToolWindow(Handle);
        _styled = true;
    }
}
