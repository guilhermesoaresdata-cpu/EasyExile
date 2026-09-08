using EasyExile.Core.Snapshots;

namespace EasyExile.Core.Navigation;

/// <summary>
/// Draw-only path planning: terrain plus a start and a goal, out come waypoints.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Pathfinding/PathPlanner.cs</c>.
///
/// The path is DRAW-ONLY. It is a line on a map and never drives input — there
/// is no step-follower here and there will not be one.
///
/// Owns one <see cref="AStar"/> sized to the grid, rebuilt only when the grid's
/// dimensions change. Not thread-safe: exactly one thread may call
/// <see cref="Plan"/>, which is what makes the reused buffers safe.
/// </remarks>
public sealed class PathPlanner
{
    private AStar? _astar;
    private int _width;
    private int _height;

    public IReadOnlyList<(int X, int Y)> Plan(
        TerrainSnapshot terrain, (int X, int Y) start, (int X, int Y) goal, int maxNodes = 1_000_000)
    {
        if (terrain.Width <= 0 || terrain.Height <= 0) return Array.Empty<(int, int)>();

        if (_astar is null || _width != terrain.Width || _height != terrain.Height)
        {
            _astar = new AStar(terrain.Width, terrain.Height);
            _width = terrain.Width;
            _height = terrain.Height;
        }

        var reader = new TerrainCellReader(terrain);

        var path = _astar.FindPath(
            reader, new PathCell(start.X, start.Y), new PathCell(goal.X, goal.Y), maxNodes, flatCost: true);

        if (!path.Found || path.Cells.Count == 0) return Array.Empty<(int, int)>();

        var smoothed = PathSmoother.Smooth(reader, path.Cells, minWalkable: 1);

        var result = new (int X, int Y)[smoothed.Count];

        for (var i = 0; i < smoothed.Count; i++) result[i] = (smoothed[i].X, smoothed[i].Y);

        return result;
    }
}
