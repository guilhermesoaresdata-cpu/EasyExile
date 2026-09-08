using EasyExile.Core.Snapshots;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// A small texture, stretched over the map, tinting the ground you have not
/// reached.
/// </summary>
/// <remarks>
/// The first attempt repainted the whole terrain texture whenever the explored
/// set grew. That can never be smooth: the terrain is eight megapixels and the
/// cost is the size of the MAP, not the size of the change — so it repainted in
/// lurches however the interval was tuned.
///
/// This separates the two. The terrain is built once per area, as it always
/// was. The unexplored tint is its own image at one texel per eight cells — a
/// sixty-fourth of the pixels — cheap enough to rebuild every frame, so the
/// ground fills in as you walk instead of catching up in steps.
///
/// A texel is coarse on purpose. Fog does not want a crisp edge, and eight
/// cells is roughly a third of a tile: fine enough to follow a corridor,
/// cheap enough to be free.
/// </remarks>
public sealed class ExplorationOverlay
{
    /// <summary>Cells per texel. Bigger is cheaper and blockier.</summary>
    private const int Block = 8;

    private readonly Func<string, Image<Rgba32>, bool, nint> _upload;

    private bool[] _walkable = [];
    private long _epoch = -1;
    private int _width;
    private int _height;
    private int _uploadedVersion = -1;
    private byte _uploadedAlpha;

    public ExplorationOverlay(Func<string, Image<Rgba32>, bool, nint> upload) => _upload = upload;

    public nint Handle { get; private set; }

    public int Builds { get; private set; }

    /// <summary>
    /// Rebuilds the tint if anything new has been seen. Cheap by construction.
    /// </summary>
    public bool Ensure(
        long epoch, TerrainSnapshot terrain, ExplorationMap explored, int colour, float strength)
    {
        if (terrain.IsEmpty) return false;

        if (epoch != _epoch)
        {
            _epoch = epoch;
            _width = Math.Max(1, (terrain.Width + Block - 1) / Block);
            _height = Math.Max(1, (terrain.Height + Block - 1) / Block);
            _uploadedVersion = -1;
            Handle = 0;

            // Which blocks contain any walkable ground. Fixed for the area, so
            // it is computed once: the per-frame work is then only the explored
            // test, which is what makes this affordable.
            _walkable = new bool[_width * _height];

            for (var y = 0; y < terrain.Height; y++)
            {
                for (var x = 0; x < terrain.Width; x++)
                {
                    if (!terrain.IsWalkable(x, y)) continue;

                    _walkable[((y / Block) * _width) + (x / Block)] = true;
                }
            }
        }

        var alpha = (byte)Math.Clamp(strength * 255f, 0f, 255f);

        // Strength is a setting, so it can change without anything new being
        // seen — and a version check alone would leave the old wash on screen
        // while the slider moved.
        if (Handle != 0 && explored.Version == _uploadedVersion && alpha == _uploadedAlpha) return true;

        var configuration = Configuration.Default.Clone();
        configuration.PreferContiguousImageBuffers = true;

        using var image = new Image<Rgba32>(configuration, _width, _height);

        var tint = new Rgba32(
            (byte)(colour & 0xFF),
            (byte)((colour >> 8) & 0xFF),
            (byte)((colour >> 16) & 0xFF),
            alpha);

        image.ProcessPixelRows(accessor =>
        {
            for (var by = 0; by < _height; by++)
            {
                var row = accessor.GetRowSpan(by);

                for (var bx = 0; bx < _width; bx++)
                {
                    if (!_walkable[(by * _width) + bx]) continue;

                    // A block counts as reached if any cell in it was, so the
                    // tint retreats a little ahead of the player rather than
                    // clinging to the exact cells walked.
                    if (Reached(explored, bx, by)) continue;

                    row[bx] = tint;
                }
            }
        });

        Handle = _upload($"easyexile_unexplored_{epoch}", image, false);
        _uploadedVersion = explored.Version;
        _uploadedAlpha = alpha;

        Builds++;

        return Handle != 0;
    }

    private static bool Reached(ExplorationMap explored, int bx, int by)
    {
        var fromX = bx * Block;
        var fromY = by * Block;

        for (var y = fromY; y < fromY + Block; y++)
        {
            for (var x = fromX; x < fromX + Block; x++)
            {
                if (explored.IsSeen(x, y)) return true;
            }
        }

        return false;
    }
}
