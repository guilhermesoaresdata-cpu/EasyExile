using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Runtime;

/// <summary>
/// Whatever is publishing snapshots for the renderer to draw.
/// </summary>
/// <remarks>
/// The render loop needs three things from the capture side and nothing else.
/// Naming them makes the direction of the dependency explicit — rendering reads,
/// it never asks for a capture — and it means the whole render path can be run
/// against constructed snapshots, with no client attached and no session at all.
/// </remarks>
public interface ISnapshotSource
{
    /// <summary>The most recent consistent snapshot, or null before the first one.</summary>
    WorldSnapshot? LatestSnapshot { get; }

    /// <summary>Outcome of the last attempt, including the ones that produced nothing.</summary>
    CaptureStatus LastStatus { get; }

    /// <summary>Measured captures per second.</summary>
    double CaptureHz { get; }

    /// <summary>
    /// The cheap half, taken fresh. Called once per rendered frame, which is
    /// what keeps the map moving at render rate instead of at capture rate.
    /// </summary>
    MapFrameSnapshot? CaptureMapFrame();

    /// <summary>The item UI, throttled internally so a caller may ask every frame.</summary>
    UiSnapshot? CaptureUi();

    // Deliberately not a member here: writing the UI tree walks every element,
    // and the rule this contract exists for is that nothing reachable from the
    // render loop can start a walk. It arrives as a delegate instead, so the
    // expensive thing stays outside the shape that promises to be cheap.
}
