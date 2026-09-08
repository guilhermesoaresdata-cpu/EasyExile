using EasyExile.Core.Snapshots;

namespace EasyExile.Core.Navigation;

/// <summary>
/// The walkable grid, seen as pathfinding cells.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Pathfinding/TerrainCellReader.cs</c>, over our
/// <see cref="TerrainSnapshot"/> instead of its <c>TerrainData</c>. The grid is
/// binary — every walkable cell is worth the same — so callers pass
/// <c>flatCost: true</c> to A* and <c>minWalkable: 1</c> to the smoother.
/// </remarks>
public sealed class TerrainCellReader : ICellReader
{
    private readonly byte[] _walkable;

    public TerrainCellReader(TerrainSnapshot terrain)
    {
        _walkable = terrain.Walkable;
        Width = terrain.Width;
        Height = terrain.Height;
    }

    public int Width { get; }

    public int Height { get; }

    public int Read(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;

        return _walkable[(y * Width) + x];
    }
}
