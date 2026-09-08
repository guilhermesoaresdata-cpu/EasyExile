namespace EasyExile.Radar.Settings.Debug;

/// <summary>
/// Settings for the debug markers.
/// </summary>
/// <remarks>
/// Everything here exists to prove that a correct entity becomes a correct world
/// position becomes a correct projection becomes a correct pixel. It is not the
/// beginning of a monster radar, and the categories that will eventually drive
/// one do not belong in this file.
/// </remarks>
public sealed record DebugEntitySettings
{
    /// <summary>
    /// Off by default. The map radar is the main feature now, and a dot with a
    /// label on every one of several hundred entities buries the game underneath
    /// the thing that is supposed to help read it.
    /// </summary>
    public bool Enabled { get; init; }

    public bool ShowMarker { get; init; } = true;
    public bool ShowDistance { get; init; } = true;
    public bool ShowMetadata { get; init; }
    public bool ShowEntityId { get; init; }
    public bool ShowComponentNames { get; init; }

    /// <summary>Counters for off-screen, behind-camera and invalid projections.</summary>
    public bool ShowProjectionCounters { get; init; } = true;

    /// <summary>Performance and alignment diagnostics.</summary>
    public bool ShowDiagnostics { get; init; } = true;

    public float MarkerRadius { get; init; } = 4f;
}
