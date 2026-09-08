namespace EasyExile.Core.Snapshots;

/// <summary>
/// What the item-UI sweep should bother reading.
/// </summary>
/// <remarks>
/// A record rather than a flag on the session, for two reasons that happen to
/// agree. Nothing on the session may take a bool — that is the shape a "continue
/// past the build mismatch" override would have, and a test holds the entire
/// surface to it. And the world capture already takes its options this way, so
/// the two lanes read alike.
/// </remarks>
public readonly record struct UiSweepOptions(bool ReadLabels = false)
{
    public static readonly UiSweepOptions Default = new();
}
