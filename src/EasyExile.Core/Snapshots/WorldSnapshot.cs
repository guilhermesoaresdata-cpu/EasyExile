using System.Collections.Immutable;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// Everything a visual module is allowed to know, frozen at one instant.
/// </summary>
/// <remarks>
/// Nothing reachable from here is a memory reader, a session, a component
/// resolver or an offset. That is the boundary the whole product rests on, and
/// EasyExile.Core.Tests asserts it by reflection over the reachable type graph
/// rather than trusting it as a convention.
/// </remarks>
public sealed record WorldSnapshot(
    DateTimeOffset Timestamp,
    long Epoch,
    AreaId Area,
    PlayerSnapshot Player,
    ImmutableArray<EntitySnapshot> Entities,
    CameraSnapshot? Camera,
    MapSnapshot Map,
    TerrainSnapshot? Terrain,
    ImmutableArray<LandmarkSnapshot> Landmarks = default,
    string? AreaCode = null,
    ImmutableArray<EntitySnapshot> Remembered = default,
    string? League = null,
    ImmutableArray<LootLabelSnapshot> LootLabels = default,
    ImmutableArray<ItemSlotSnapshot> ItemSlots = default,
    CameraSnapshot? UiCamera = null)
{
    /// <summary>The game's own loot tags, never default.</summary>
    public ImmutableArray<LootLabelSnapshot> Labels =>
        LootLabels.IsDefault ? ImmutableArray<LootLabelSnapshot>.Empty : LootLabels;

    /// <summary>Visible item slots, never default.</summary>
    public ImmutableArray<ItemSlotSnapshot> Slots =>
        ItemSlots.IsDefault ? ImmutableArray<ItemSlotSnapshot>.Empty : ItemSlots;

    /// <summary>
    /// The zone's own identifier, e.g. <c>G2_5_1</c>. Null when unreadable.
    /// </summary>
    /// <remarks>
    /// Carried because it is half of what "a different area" means here: the
    /// client is free to hand back the same AreaInstance allocation for the next
    /// zone, and it does.
    /// </remarks>
    public string Zone => AreaCode ?? "?";

    /// <summary>
    /// Exits and marked points seen earlier in this area but no longer loaded.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Entities"/> on purpose. These are remembered,
    /// not present, and a consumer that treats the two the same would report a
    /// monster count that includes things the client has unloaded.
    /// </remarks>
    public ImmutableArray<EntitySnapshot> Recalled =>
        Remembered.IsDefault ? ImmutableArray<EntitySnapshot>.Empty : Remembered;

    /// <summary>The area's tile landmarks, never default.</summary>
    public ImmutableArray<LandmarkSnapshot> Marks =>
        Landmarks.IsDefault ? ImmutableArray<LandmarkSnapshot>.Empty : Landmarks;

    /// <summary>True when the game's own map is open and usable to draw on.</summary>
    public bool CanDrawOnMap => Map.IsUsable && Terrain is { IsEmpty: false };

    /// <summary>
    /// True when the camera could not be resolved. Everything else in the
    /// snapshot is still usable; only projection is unavailable.
    /// </summary>
    public bool CanProject => Camera is not null;
}

public enum CaptureStatus
{
    /// <summary>A consistent snapshot of a single area.</summary>
    Captured,

    /// <summary>
    /// The chain did not resolve. The character is loading, in a menu, or not in
    /// an area at all. Not an error, and not something to retry harder.
    /// </summary>
    NoArea,

    /// <summary>
    /// The area changed while the capture was in progress, twice in a row. The
    /// partial reading is discarded rather than returned: half of one area mixed
    /// with half of another looks like valid data and is not.
    /// </summary>
    InvalidatedByTransition,
}

/// <summary>
/// The outcome of a capture attempt. <see cref="Snapshot"/> is non-null exactly
/// when <see cref="Status"/> is <see cref="CaptureStatus.Captured"/>.
/// </summary>
public sealed record CaptureResult(CaptureStatus Status, WorldSnapshot? Snapshot)
{
    public bool Success => Status == CaptureStatus.Captured && Snapshot is not null;

    internal static CaptureResult Failed(CaptureStatus status) => new(status, null);
}

/// <summary>How much of the world to capture.</summary>
/// <param name="MaxEntities">
/// Ceiling on entities described in one snapshot. Enumerating the containers is
/// cheap; reading metadata, components and vitals for each one is not, and a
/// busy map holds several hundred.
/// </param>
/// <param name="IncludeSleepingEntities">
/// Sleeping entities are the ones the client is not simulating — distant chests,
/// unloaded monsters. Useful for a map view, wasteful for a combat overlay.
/// </param>
public sealed record CaptureOptions(int MaxEntities = 512, bool IncludeSleepingEntities = true)
{
    public static readonly CaptureOptions Default = new();
}
