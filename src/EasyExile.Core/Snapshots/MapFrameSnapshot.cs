using EasyExile.Core.Spatial;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The cheap, fast-moving half of the world: where the player is and what the
/// game's map is doing. Captured per rendered frame.
/// </summary>
/// <remarks>
/// This is the split POE2Radar makes, and the reason its map moves smoothly.
/// Everything here is a handful of reads, so it can be taken at render rate; the
/// entity walk and the terrain stay on the slow lane. A marker drawn from a
/// 30 Hz entity list still tracks at full rate, because what moves under it —
/// the player and the map — is re-read every frame.
///
/// Carrying screen coordinates in the slow snapshot instead would put the whole
/// map on the slow lane, which is exactly the stepping this removes.
///
/// The camera rides here for the same reason. A route drawn on the ground is
/// projected through it, and the camera pans with the player — read at capture
/// rate it would drag the whole trail behind the character.
/// </remarks>
public sealed record MapFrameSnapshot(
    DateTimeOffset Timestamp,
    long Epoch,
    AreaId Area,
    Vector3 PlayerWorld,
    Vector2 PlayerGrid,
    MapSnapshot Map,
    CameraSnapshot? Camera = null,
    VitalsSnapshot Vitals = default,

    /// <summary>
    /// The game's ground tags, re-located this frame from the elements the UI
    /// sweep found. Empty when the sweep has not found any yet.
    /// </summary>
    System.Collections.Immutable.ImmutableArray<LootLabelSnapshot> Labels = default);
