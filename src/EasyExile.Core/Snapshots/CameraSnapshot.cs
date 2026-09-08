using System.Collections.Immutable;
using EasyExile.Core.Spatial;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The camera as it stood when the snapshot was taken: just enough to project,
/// and nothing that could read the client again.
/// </summary>
/// <remarks>
/// This is what makes a render loop faster than the capture loop possible. A
/// renderer projects against a captured matrix as many times as it likes, at
/// whatever frame rate it runs, without a single further read.
/// </remarks>
public sealed record CameraSnapshot(ImmutableArray<float> ViewProjection, int Width, int Height)
{
    /// <summary>
    /// Pure. Same input, same output, no memory access, no session — call it
    /// from a Draw() as often as you want.
    /// </summary>
    public ScreenPoint Project(Vector3 world) =>
        Projection.Project(ViewProjection.AsSpan(), world, Width, Height);
}
