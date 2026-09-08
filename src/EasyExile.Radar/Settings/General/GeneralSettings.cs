namespace EasyExile.Radar.Settings.General;

/// <summary>
/// Settings that belong to the radar as a whole rather than to any one feature.
/// </summary>
public sealed record GeneralSettings
{
    /// <summary>Whether the update loop captures at all.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// The heavy walk: entities and terrain, per second. 30 Hz is the rate the
    /// reference runs it at. The map itself does not move at this rate â€” the
    /// player and the map state are re-read every rendered frame â€” so this only
    /// governs how often the entity list is refreshed.
    /// </summary>
    public int UpdateRateHz { get; init; } = 30;

    /// <summary>
    /// How often the overlay redraws itself.
    /// </summary>
    /// <remarks>
    /// High by default. Lowering it to sixty was measured and saved exactly
    /// nothing — the core this overlay spends is the world walk, not the
    /// drawing — so the only thing the cap changed was how smooth the markers
    /// looked, which is the one thing it must not cost.
    ///
    /// A setting for the machine that needs it, not a default for the machine
    /// that does not.
    /// </remarks>
    public int RenderFps { get; init; } = 144;

    /// <summary>
    /// Where the panel was left, in client pixels. Zero means never moved.
    /// </summary>
    /// <remarks>
    /// Kept here rather than left to ImGui's own <c>imgui.ini</c>: that file is
    /// written on a graceful shutdown, and an overlay is very often not closed
    /// gracefully. Ours is written whenever it changes.
    /// </remarks>
    public float PanelX { get; init; }

    public float PanelY { get; init; }

    /// <summary>Ceiling on entities per snapshot.</summary>
    public int MaxEntities { get; init; } = 512;

    /// <summary>Toggles between HUD mode and interactive mode. Virtual-key code.</summary>
    public int HudInteractiveHotkey { get; init; } = VirtualKey.F10;

    /// <summary>
    /// Saves a picture of the screen — game and overlay together — beside the
    /// executable.
    /// </summary>
    /// <remarks>
    /// Print Screen is taken twice over. The client binds it and writes its own
    /// file from the game's back buffer, which does not contain this overlay;
    /// Windows 11 binds it again for the Snipping Tool, which cannot draw over a
    /// fullscreen game and so appears to do nothing. Neither produces the one
    /// picture that is useful when reporting what the overlay did.
    /// </remarks>
    public int ScreenshotHotkey { get; init; } = VirtualKey.F9;

    /// <summary>
    /// Writes the whole UI tree to a file, at the moment it is pressed.
    /// </summary>
    /// <remarks>
    /// The answer to a question that cost most of a session: a probe only sees
    /// what is on screen while it runs, so every question about a panel needed
    /// that panel open at the same instant, and each miss looked exactly like a
    /// real negative result.
    ///
    /// This hands over the whole tree instead - every element, where it is,
    /// whether it is drawn, and what it says in both of the places a caption can
    /// live. Press it when something interesting is on screen and carry on
    /// playing; the file answers the questions afterwards, including the ones
    /// nobody thought to ask at the time.
    /// </remarks>
    public int UiTreeHotkey { get; init; } = VirtualKey.F11;

    /// <summary>
    /// Turns the element picker on and off.
    /// </summary>
    /// <remarks>
    /// While it is on, whatever the cursor is over is outlined and described:
    /// address, rectangle, flags, what it carries, and what it says in BOTH of
    /// the places a caption can live. Pointing at a thing and being told exactly
    /// what it is beats reading a fifty-thousand line dump looking for it.
    ///
    /// Off by default and polled only while on, because each pick walks the tree
    /// from the root.
    /// </remarks>
    public int InspectHotkey { get; init; } = VirtualKey.F8;

    /// <summary>
    /// Writes the drawn tree, as opposed to what the cursor is on.
    /// </summary>
    /// <remarks>
    /// Its own key rather than a modifier on the other one. Shift and control
    /// belong to the game - shift is held to read an item's prefixes - so a
    /// modifier here means doing two things at once, one of them unwanted.
    /// </remarks>
    public int FullTreeHotkey { get; init; } = VirtualKey.F12;

    /// <summary>
    /// Whether the HUD stays drawn while the game is not the foreground window.
    /// Off by default: an overlay floating over a browser is confusing and looks
    /// like a bug.
    /// </summary>
    public bool ShowWhenGameUnfocused { get; init; }

    /// <summary>
    /// How stale a snapshot may be before entity markers stop being drawn.
    /// Covers the gap between area transitions, where the honest answer is "the
    /// world you were looking at no longer exists".
    /// </summary>
    public TimeSpan StaleAfter { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Clamped, so a bad value slows the loop instead of pinning a core.</summary>
    public TimeSpan UpdateInterval =>
        TimeSpan.FromSeconds(1.0 / Math.Clamp(UpdateRateHz, 1, 240));

    /// <summary>
    /// Show, on the game screen, what a running analysis needs done.
    /// </summary>
    /// <remarks>
    /// On by default, because it exists for the moments when nobody is looking
    /// at the terminal - which is every moment the game is in the foreground. A
    /// whole session of probes was lost to requests that were only ever printed
    /// behind the window, and each empty result read like a finding about the
    /// client rather than about where the question had been asked.
    /// </remarks>
    public bool ShowProbePrompts { get; init; } = true;

    /// <summary>
    /// The language the interface speaks.
    /// </summary>
    /// <remarks>
    /// Portuguese by default because that is what it was written in and what
    /// its author reads. English exists because the game is in English and so
    /// is everything written about it, so the two sit side by side in one
    /// person's head and the interface should be able to match either.
    /// </remarks>
    public Language Language { get; init; } = Language.PtBr;
}

/// <summary>The handful of virtual-key codes the settings need to name.</summary>
public static class VirtualKey
{
    public const int F8 = 0x77;
    public const int F9 = 0x78;
    public const int F10 = 0x79;
    public const int F11 = 0x7A;
    public const int F12 = 0x7B;

    public static string Name(int code) => code switch
    {
        F8 => "F8",
        F9 => "F9",
        F10 => "F10",
        F11 => "F11",
        F12 => "F12",
        _ => $"0x{code:X2}",
    };
}

/// <summary>What the interface speaks.</summary>
public enum Language
{
    PtBr,
    English,
}
