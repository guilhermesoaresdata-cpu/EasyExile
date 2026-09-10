using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Settings.Levelling;

/// <summary>
/// The levelling guide.
/// </summary>
public sealed record LevellingSettings
{
    /// <summary>
    /// Off by default. A fresh install has not chosen a build to level, and the
    /// guide is still marked BETA in its own tab - opting in is a decision the
    /// player makes, not one a new install makes for them.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Pick the exit to the next step automatically and route to it.
    /// </summary>
    /// <remarks>
    /// Still draw-only. This chooses a destination and draws the path to it; it
    /// does not move the character, click, or send a key. The user walks.
    /// </remarks>
    public bool AutoRoute { get; init; } = true;

    /// <summary>
    /// Append every area entered to the recorded route.
    /// </summary>
    /// <remarks>
    /// On by default because there is no published table of PoE2 area codes, so
    /// the only honest way to get a route is to walk the campaign once. After
    /// that the file is a route, and this can go off.
    /// </remarks>
    public bool Recording { get; init; } = true;

    /// <summary>
    /// Draw the steps on screen instead of only inside the settings window.
    /// </summary>
    /// <remarks>
    /// "Eu nao vou jogar com o painel aberto" — and that is the whole point of
    /// a guide. A walkthrough you have to stop and open is a website, and there
    /// are already good websites.
    /// </remarks>
    public bool ShowSteps { get; init; } = true;

    /// <summary>Which corner of the client the panel sits in.</summary>
    public ChipCorner Corner { get; init; } = ChipCorner.TopLeft;

    /// <summary>How many steps to show at once, current one first.</summary>
    public int VisibleSteps { get; init; } = 4;

    /// <summary>Show the steps the walkthrough marks as skippable.</summary>
    public bool ShowOptional { get; init; } = true;

    public float TextScale { get; init; } = 1.1f;

    /// <summary>How solid the plate behind the text is.</summary>
    public float Opacity { get; init; } = 0.72f;

    /// <summary>
    /// Write down what each zone actually contained, for correcting the guide.
    /// </summary>
    /// <remarks>
    /// Off by default: it is a file that grows for as long as it is on, and
    /// nobody should discover that by finding it.
    /// </remarks>
    public bool Journal { get; init; }
}
