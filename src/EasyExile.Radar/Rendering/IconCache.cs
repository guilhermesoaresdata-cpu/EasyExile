using EasyExile.Core.Spatial;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// Icons flattened once into unit polygons, ready to stamp.
/// </summary>
/// <remarks>
/// The reference turns each <see cref="IconLibrary"/> icon into a Direct2D
/// geometry and caches that; our canvas fills polygons, so the same idea lands
/// here as a flattening pass. Either way the point is identical: an icon is
/// parsed and tessellated ONCE, not per marker per frame.
///
/// The normalisation is the reference's — its 24x24 box puts an edge point at
/// +/-1, aspect-preserving — so an icon authored against POE2Radar drops in at
/// the same size.
/// </remarks>
public static class IconCache
{
    /// <summary>
    /// One filled ring of an icon. <paramref name="IsHole"/> figures are painted
    /// in the shadow colour instead of the icon's, which is how the reference's
    /// Alternate fill rule reads at marker size — a donut, a pin's eye.
    /// </summary>
    public readonly record struct Figure(Vector2[] Points, bool IsHole);

    private static readonly Dictionary<string, Figure[]> Flattened = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object Gate = new();

    /// <summary>Segments a curve is chopped into. Eight is invisible at marker size.</summary>
    private const int CurveSteps = 8;

    /// <summary>The icon's unit polygons, or empty when no such icon exists.</summary>
    public static Figure[] Get(string? name)
    {
        if (string.IsNullOrEmpty(name)) return Array.Empty<Figure>();

        lock (Gate)
        {
            if (Flattened.TryGetValue(name, out var cached)) return cached;

            var built = IconLibrary.Map.TryGetValue(name, out var def)
                ? Flatten(def)
                : Array.Empty<Figure>();

            // Cached even when empty: a rule naming an icon nobody shipped must
            // not re-walk the library on every frame.
            Flattened[name] = built;

            return built;
        }
    }

    /// <summary>Drops the flattened icons so the next lookup re-reads the library.</summary>
    public static void Clear()
    {
        lock (Gate) Flattened.Clear();
    }

    public static int Count
    {
        get { lock (Gate) return Flattened.Count; }
    }

    private static Figure[] Flatten(IconDef def)
    {
        var rings = new List<Vector2[]>();

        foreach (var d in def.Paths)
        {
            foreach (var figure in SvgPath.Parse(d))
            {
                var points = Walk(figure, def);

                if (points.Length >= 3) rings.Add(points);
            }
        }

        if (rings.Count == 0) return Array.Empty<Figure>();

        var figures = new Figure[rings.Count];

        for (var i = 0; i < rings.Count; i++)
            figures[i] = new Figure(rings[i], i > 0 && Inside(rings[i], rings));

        return figures;
    }

    /// <summary>
    /// True when this ring sits entirely inside another. That is what makes it a
    /// hole rather than a second shape — the difference between a pin's eye and
    /// a flag's banner.
    /// </summary>
    private static bool Inside(Vector2[] ring, List<Vector2[]> all)
    {
        var (minX, minY, maxX, maxY) = Bounds(ring);

        foreach (var other in all)
        {
            if (ReferenceEquals(other, ring)) continue;

            var (oMinX, oMinY, oMaxX, oMaxY) = Bounds(other);

            if (minX > oMinX && maxX < oMaxX && minY > oMinY && maxY < oMaxY) return true;
        }

        return false;
    }

    private static (float MinX, float MinY, float MaxX, float MaxY) Bounds(Vector2[] ring)
    {
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;

        foreach (var p in ring)
        {
            if (p.X < minX) minX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.X > maxX) maxX = p.X;
            if (p.Y > maxY) maxY = p.Y;
        }

        return (minX, minY, maxX, maxY);
    }

    /// <summary>Walks one figure's segments, flattening every curve into lines.</summary>
    private static Vector2[] Walk(SvgPath.SvgFigure figure, IconDef def)
    {
        var points = new List<Vector2>(32);

        var current = figure.Start;

        points.Add(Normalise(current, def));

        foreach (var segment in figure.Segs)
        {
            switch (segment.Kind)
            {
                case SvgPath.SegKind.Cubic:
                    for (var s = 1; s <= CurveSteps; s++)
                        points.Add(Normalise(Cubic(current, segment.C1, segment.C2, segment.End,
                            s / (float)CurveSteps), def));
                    break;

                case SvgPath.SegKind.Quad:
                    for (var s = 1; s <= CurveSteps; s++)
                        points.Add(Normalise(Quad(current, segment.C1, segment.End, s / (float)CurveSteps), def));
                    break;

                default:
                    points.Add(Normalise(segment.End, def));
                    break;
            }

            current = segment.End;
        }

        return points.ToArray();
    }

    /// <summary>
    /// The reference's normalisation: centre the viewBox and scale by its larger
    /// side, so a square 24x24 icon puts an edge point at +/-1 and a non-square
    /// one keeps its aspect.
    /// </summary>
    private static Vector2 Normalise(System.Numerics.Vector2 p, IconDef def)
    {
        var half = MathF.Max(def.VbW, def.VbH) * 0.5f;

        if (half <= 0f) return new Vector2(0, 0);

        return new Vector2(
            (p.X - (def.VbX + (def.VbW * 0.5f))) / half,
            (p.Y - (def.VbY + (def.VbH * 0.5f))) / half);
    }

    private static System.Numerics.Vector2 Cubic(
        System.Numerics.Vector2 a, System.Numerics.Vector2 b,
        System.Numerics.Vector2 c, System.Numerics.Vector2 d, float t)
    {
        var u = 1f - t;

        return (u * u * u * a) + (3f * u * u * t * b) + (3f * u * t * t * c) + (t * t * t * d);
    }

    private static System.Numerics.Vector2 Quad(
        System.Numerics.Vector2 a, System.Numerics.Vector2 b, System.Numerics.Vector2 c, float t)
    {
        var u = 1f - t;

        return (u * u * a) + (2f * u * t * b) + (t * t * c);
    }
}
