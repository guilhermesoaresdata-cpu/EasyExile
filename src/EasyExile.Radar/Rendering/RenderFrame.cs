using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Overlay;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// One frame's worth of context, handed to every feature.
/// </summary>
/// <remarks>
/// The whole boundary in one type: a feature gets an immutable snapshot and the
/// geometry it is drawing into. There is no session here, no reader, and no way
/// to ask the client anything — a feature that wants more data has to wait for
/// the next capture like everything else.
/// </remarks>
public sealed record RenderFrame(
    WorldSnapshot? Snapshot,
    CaptureStatus Status,
    TimeSpan SnapshotAge,
    bool SnapshotIsStale,
    ScreenRect ClientBounds,
    bool Interactive,
    MapFrameSnapshot? MapFrame = null,
    UiSnapshot? Ui = null)
{
    /// <summary>The item UI, never null so a feature need not check.</summary>
    public UiSnapshot Panels => Ui ?? UiSnapshot.Empty;

    /// <summary>
    /// The game's ground tags and the camera they were measured against.
    /// </summary>
    /// <remarks>
    /// Prefers this frame's own reading. The sweep decides WHICH tags exist a
    /// few times a second; the fast lane asks WHERE they are every frame, and
    /// only the second question has to keep up with the camera — which is the
    /// whole difference between a chip that glides with the game's label and one
    /// that hops after it.
    ///
    /// The camera travels with the rectangles because whoever measured them is
    /// who they are consistent with. Falling back to the sweep's pair is not a
    /// degradation, only an older instant, and the caller corrects for the gap.
    ///
    /// The test is whether the frame READ the tags, not whether it found any. A
    /// frame that read them and found none has watched the loot be picked up,
    /// and falling back to the sweep there would leave a chip floating over
    /// ground that is now bare.
    /// </remarks>
    public (System.Collections.Immutable.ImmutableArray<LootLabelSnapshot> Tags, CameraSnapshot? Camera)
        GroundTags =>
        MapFrame is { Labels.IsDefault: false }
            ? (MapFrame.Labels, MapFrame.Camera)
            : (Panels.Labels, Panels.Camera);

    /// <summary>
    /// True when the fast frame and the world snapshot describe the same area.
    /// They are captured on different threads at different rates, so a portal
    /// taken between them would otherwise draw the old area's entities onto the
    /// new area's map.
    /// </summary>
    public bool AreasAgree =>
        MapFrame is not null && Snapshot is not null && MapFrame.Area == Snapshot.Area;

    /// <summary>
    /// Whether world-anchored drawing is meaningful this frame. False while the
    /// character is loading, and false once the last snapshot has aged out —
    /// markers left over from an area the player has left are worse than none.
    /// </summary>
    public bool HasWorld => Snapshot is not null && !SnapshotIsStale;

    public long Epoch => Snapshot?.Epoch ?? -1;

    /// <summary>
    /// Projects through the captured camera. Nothing is read from the client:
    /// the matrix was copied at capture time and this is arithmetic over it.
    /// </summary>
    public ScreenPoint? Project(Vector3 world) => Snapshot?.Camera?.Project(world);

    /// <summary>Top-left of the client area, which is the overlay's own origin.</summary>
    public Vector2 Origin => new(0, 0);
}
