using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// Which cells of this area the player has actually been near.
/// </summary>
/// <remarks>
/// Ours, not the client's. The game keeps its own fog of war and we have no
/// offset for it, so inventing one would mean inventing data — this is a record
/// of where the player has stood, which is a different claim and an honest one.
/// It answers the question that was actually asked: have I been over there.
///
/// Per area, and thrown away when the area changes. A memory of the last zone's
/// exploration painted onto this one would be worse than none.
/// </remarks>
public sealed class ExplorationMap
{
    private bool[] _seen = [];
    private long _epoch = -1;
    private int _width;
    private int _height;

    /// <summary>Bumped whenever a new cell is seen, so a texture knows to rebuild.</summary>
    public int Version { get; private set; }

    public int Seen { get; private set; }

    public int Cells => _width * _height;

    /// <summary>
    /// Records the ground around the player as seen.
    /// </summary>
    public void Observe(long epoch, TerrainSnapshot terrain, Vector2 playerGrid, int radius)
    {
        if (terrain.IsEmpty || radius <= 0) return;

        if (epoch != _epoch || _width != terrain.Width || _height != terrain.Height)
        {
            _epoch = epoch;
            _width = terrain.Width;
            _height = terrain.Height;
            _seen = new bool[_width * _height];
            Seen = 0;
            Version++;
        }

        var px = (int)playerGrid.X;
        var py = (int)playerGrid.Y;

        var fromX = Math.Max(0, px - radius);
        var toX = Math.Min(_width - 1, px + radius);
        var fromY = Math.Max(0, py - radius);
        var toY = Math.Min(_height - 1, py + radius);

        var squared = radius * radius;
        var added = 0;

        for (var y = fromY; y <= toY; y++)
        {
            var dy = y - py;

            for (var x = fromX; x <= toX; x++)
            {
                var dx = x - px;

                // Round, not square. A square reveal leaves visible corners as
                // you walk, which reads as a rendering artefact rather than as
                // something the tool learned.
                if ((dx * dx) + (dy * dy) > squared) continue;

                var i = (y * _width) + x;

                if (_seen[i]) continue;

                _seen[i] = true;
                added++;
            }
        }

        if (added == 0) return;

        Seen += added;
        Version++;
    }

    public bool IsSeen(int x, int y)
    {
        if (_seen.Length == 0) return false;
        if ((uint)x >= (uint)_width || (uint)y >= (uint)_height) return false;

        return _seen[(y * _width) + x];
    }
}
