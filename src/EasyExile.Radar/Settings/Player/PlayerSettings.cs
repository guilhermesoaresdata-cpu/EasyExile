namespace EasyExile.Radar.Settings.Player;

/// <summary>Where the player information is drawn.</summary>
public enum PlayerAnchor
{
    /// <summary>Next to the projected position of the character.</summary>
    AtCharacter,

    /// <summary>In a fixed corner, independent of where the character is.</summary>
    FixedHud,
}

/// <summary>Settings for the player feature, and nothing else.</summary>
public sealed record PlayerSettings
{
    public bool Enabled { get; init; } = true;

    public bool ShowMarker { get; init; } = true;
    public bool ShowName { get; init; } = true;
    public bool ShowLevel { get; init; } = true;
    public bool ShowVitals { get; init; } = true;

    /// <summary>
    /// A fixed corner by default. Text pinned to the character follows it around
    /// the screen and sits on top of whatever is being fought; in a corner it
    /// stays where the eye already knows to look.
    /// </summary>
    public PlayerAnchor Anchor { get; init; } = PlayerAnchor.FixedHud;

    public float MarkerRadius { get; init; } = 6f;

    /// <summary>
    /// Your marker in the world, and the lines that go with it.
    /// </summary>
    /// <remarks>
    /// Separate from the dot on the game's map, which belongs to the map's
    /// settings, because they are two drawings in two places over two very
    /// different backgrounds - a tileset here, the map's own art there. One
    /// colour that has to work on both is a colour that works well on neither.
    /// </remarks>
    public int MarkerColour { get; init; } = unchecked((int)0xFFFFC85A);
}
