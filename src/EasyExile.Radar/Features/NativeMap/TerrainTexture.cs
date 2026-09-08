using EasyExile.Core.Snapshots;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// The walkable mask as a texture, built once per area.
/// </summary>
/// <remarks>
/// Port of <c>POE2Radar.Overlay/Overlay/TerrainBitmap.cs</c> (MIT). The pixel
/// rules are the reference's exactly:
/// <list type="bullet">
/// <item>an unwalkable cell stays fully transparent, so the game's own map shows through</item>
/// <item>a walkable cell next to an unwalkable one, or on the grid boundary, is an edge — brighter</item>
/// <item>every other walkable cell is a faint interior wash</item>
/// </list>
///
/// The backend differs and only the backend: the reference builds a Direct2D
/// bitmap, and this hands an ImageSharp image to the overlay, which is the
/// texture path our renderer already has. Cache invalidation follows the
/// reference's reasoning — two areas can share dimensions, so size alone is not
/// a key. Ours is the epoch, which is the identity the Core already tracks.
/// </remarks>
public sealed class TerrainTexture
{
    /// <summary>
    /// How long to wait between repaints, from how long the last one took.
    /// </summary>
    /// <remarks>
    /// A fixed second and a half made walking feel like the map was catching up
    /// in lurches. A fixed short interval would instead spend a visible slice of
    /// every second rebuilding an eight-megapixel image. Paying a fixed FRACTION
    /// of the time — about a sixth — keeps it responsive on a small map and
    /// honest on a large one, with a floor so a cheap build cannot spin.
    /// </remarks>
    private TimeSpan RebuildInterval => TimeSpan.FromMilliseconds(
        Math.Clamp(LastBuildMilliseconds * 6, 200, 2000));

    private readonly Func<string, Image<Rgba32>, bool, nint> _upload;

    private DateTime _builtAt = DateTime.MinValue;
    private int _builtForVersion = -1;

    /// <summary>
    /// Converts a packed setting colour, keeping the alpha the terrain wants.
    /// </summary>
    /// <remarks>
    /// The terrain is drawn faint on purpose — it sits under the client's own
    /// map — so the alpha belongs to the drawing, not to the chosen colour.
    /// </remarks>
    private static Rgba32 Rgba(int packed, byte alpha) => new(
        (byte)(packed & 0xFF),
        (byte)((packed >> 8) & 0xFF),
        (byte)((packed >> 16) & 0xFF),
        alpha);

    private long _builtForEpoch = -1;
    private int _width;
    private int _height;

    public TerrainTexture(Func<string, Image<Rgba32>, bool, nint> upload) => _upload = upload;

    public nint Handle { get; private set; }

    public int Width => _width;

    public int Height => _height;

    /// <summary>Number of rebuilds, which should equal the number of areas entered.</summary>
    public int Builds { get; private set; }

    public double LastBuildMilliseconds { get; private set; }

    /// <summary>Cheap when the epoch has not moved.</summary>
    public bool Ensure(long epoch, TerrainSnapshot terrain, TerrainPaint paint)
    {
        if (terrain.IsEmpty) return false;

        // Once per area. Exploration is drawn as a separate, tiny overlay
        // rather than repainted into this: the cost here is the size of the map,
        // so repainting it as you walk lurches however it is throttled.
        if (Handle != 0 && epoch == _builtForEpoch) return true;

        var clock = System.Diagnostics.Stopwatch.StartNew();

        using var image = Build(terrain, paint);

        // Named per area. The overlay's texture table is keyed by name and it
        // hands back whatever is already registered under one, so reusing a
        // constant name meant every area after the first drew the first one's
        // terrain.
        // Named per AREA and nothing else. Putting the exploration version in
        // the name registered a fresh texture on every repaint and never
        // released the old one — a leak every second and a half while walking.
        // UploadTexture already removes the entry under a name before adding,
        // which is exactly what a repaint needs.
        Handle = _upload($"easyexile_terrain_{epoch}", image, false);

        _builtAt = DateTime.UtcNow;
        _builtForVersion = paint.Explored?.Version ?? 0;
        _builtForEpoch = epoch;
        _width = terrain.Width;
        _height = terrain.Height;

        Builds++;
        LastBuildMilliseconds = clock.Elapsed.TotalMilliseconds;

        return Handle != 0;
    }

    private static Image<Rgba32> Build(TerrainSnapshot terrain, TerrainPaint paint)
    {
        var w = terrain.Width;
        var h = terrain.Height;

        // The overlay uploads the texture from a single pixel span, so the image
        // has to be one contiguous buffer. ImageSharp splits large images across
        // buffers by default, and the upload then fails with an allocator error.
        // GameHelper's GenerateMapTexture does the same thing for the same reason.
        var configuration = Configuration.Default.Clone();
        configuration.PreferContiguousImageBuffers = true;

        var image = new Image<Rgba32>(configuration, w, h);

        // Always the VISITED colour. Exploration is a separate overlay now, so
        // this is the ground underneath it — painting it in the unvisited colour
        // left the whole map red and the tint on top of that made it worse.
        var interior = Rgba(paint.Visited, 40);
        var edge = Rgba(paint.Visited, 170);

        var map = paint.Explored;

        image.ProcessPixelRows(accessor =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = accessor.GetRowSpan(y);

                for (var x = 0; x < w; x++)
                {
                    if (!terrain.IsWalkable(x, y)) continue;

                    row[x] = IsEdge(terrain, x, y) ? edge : interior;
                }
            }
        });

        return image;
    }

    /// <summary>
    /// A walkable cell touching an unwalkable one, or the boundary. The
    /// reference treats running off the grid as an edge too, which is what draws
    /// an outline around the whole map rather than leaving it open.
    /// </summary>
    private static bool IsEdge(TerrainSnapshot terrain, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
        {
            var ny = y + dy;
            if (ny < 0 || ny >= terrain.Height) return true;

            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                var nx = x + dx;
                if (nx < 0 || nx >= terrain.Width) return true;
                if (!terrain.IsWalkable(nx, ny)) return true;
            }
        }

        return false;
    }
}
