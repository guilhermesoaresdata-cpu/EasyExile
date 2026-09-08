using EasyExile.Core.Spatial;
using EasyExile.Radar.Rendering;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// The few markers that are worth more than a dot.
/// </summary>
/// <remarks>
/// Drawn from primitives rather than loaded as images: at map scale these are a
/// dozen pixels across, an atlas would be one more thing to ship and invalidate,
/// and the shapes have to stay legible against whatever the game is drawing
/// underneath. Every one gets a dark outline first for that reason.
/// </remarks>
public static class MapIcons
{
    /// <summary>
    /// Stamps a library icon: the cached unit polygons scaled to the marker.
    /// </summary>
    /// <remarks>
    /// Port of POE2Radar's <c>OverlayRenderer.DrawIcon</c>, which sets a scale
    /// transform and fills the cached geometry. Same shape, same size, same
    /// naming — an icon file authored for the reference works here unchanged.
    ///
    /// An unknown name falls back to a dot rather than drawing nothing. A rule
    /// naming a missing icon should still put the entity on the map; silently
    /// dropping it is how a marker goes missing with no way to tell why.
    /// </remarks>
    public static void Draw(IOverlayCanvas canvas, string? icon, Vector2 at, float size, uint colour)
    {
        var figures = IconCache.Get(icon);

        if (figures.Length == 0)
        {
            canvas.Circle(at, size + 1f, Palette.Shadow);
            canvas.Circle(at, size, colour);

            return;
        }

        Span<Vector2> scratch = stackalloc Vector2[64];

        // The dark pass first, whole: outlining each ring separately would draw
        // a halo through the middle of the icon.
        foreach (var figure in figures)
        {
            if (figure.IsHole) continue;

            Stamp(canvas, figure.Points, at, size * 1.18f, Palette.Shadow, scratch);
        }

        foreach (var figure in figures)
            Stamp(canvas, figure.Points, at, size, figure.IsHole ? Palette.Shadow : colour, scratch);
    }

    private static void Stamp(
        IOverlayCanvas canvas, Vector2[] points, Vector2 at, float size, uint colour, Span<Vector2> scratch)
    {
        // No allocation per marker: the scratch buffer is the frame's, and a
        // figure with more points than it holds falls back to a heap array.
        var buffer = points.Length <= scratch.Length ? scratch[..points.Length] : new Vector2[points.Length];

        for (var i = 0; i < points.Length; i++)
            buffer[i] = new Vector2(at.X + (points[i].X * size), at.Y + (points[i].Y * size));

        canvas.Polygon(buffer, colour);
    }

}
