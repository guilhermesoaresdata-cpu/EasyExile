namespace EasyExile.Core.Spatial;

public enum ScreenStatus
{
    OnScreen,
    OffScreen,
    BehindCamera,
    Invalid,
}

public readonly record struct ScreenPoint(ScreenStatus Status, Vector2 Ndc, Vector2 Screen, float Depth);

/// <summary>
/// World to screen. Pure maths over a matrix and a viewport, with no reader and
/// no session anywhere in the signature — which is what lets a renderer project
/// the same captured camera as many times as it likes without touching the
/// client. The contract supplies the matrix layout; the projection is an
/// algorithm and was never an offset.
/// </summary>
public static class Projection
{
    /// <summary>
    /// Column-major, as the contract records. Nothing is clamped: a point behind
    /// the camera folded onto the screen would look like a real position.
    /// </summary>
    public static ScreenPoint Project(ReadOnlySpan<float> matrix, Vector3 world, float width, float height)
    {
        if (matrix.Length < 16) return new ScreenPoint(ScreenStatus.Invalid, default, default, 0);

        Span<float> v = stackalloc float[4] { world.X, world.Y, world.Z, 1f };
        Span<float> clip = stackalloc float[4];

        for (int i = 0; i < 4; i++)
        {
            float sum = 0f;
            for (int j = 0; j < 4; j++) sum += matrix[(j * 4) + i] * v[j];
            clip[i] = sum;
        }

        foreach (var component in clip)
        {
            if (!float.IsFinite(component)) return new ScreenPoint(ScreenStatus.Invalid, default, default, 0);
        }

        var w = clip[3];
        if (w <= 0.0001f) return new ScreenPoint(ScreenStatus.BehindCamera, default, default, w);

        var ndc = new Vector2(clip[0] / w, clip[1] / w);
        var depth = clip[2] / w;

        var screen = new Vector2(
            (ndc.X + 1f) * 0.5f * width,
            (1f - ndc.Y) * 0.5f * height);

        var status = MathF.Abs(ndc.X) <= 1f && MathF.Abs(ndc.Y) <= 1f
            ? ScreenStatus.OnScreen
            : ScreenStatus.OffScreen;

        return new ScreenPoint(status, ndc, screen, depth);
    }

    /// <summary>
    /// Grid cells from world units. Derived, not read: the offset that used to
    /// supply the grid position was withdrawn from the contract because it read
    /// (0,0) while the world position was valid.
    /// </summary>
    public static Vector2? ToGrid(Vector3? world, float worldUnitsPerCell)
    {
        if (world is not { } position || worldUnitsPerCell <= 0f) return null;

        return new Vector2(position.X / worldUnitsPerCell, position.Y / worldUnitsPerCell);
    }
}
