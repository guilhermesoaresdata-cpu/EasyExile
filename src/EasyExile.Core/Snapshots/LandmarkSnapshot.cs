using EasyExile.Core.Spatial;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// A notable terrain feature and where it sits on the grid.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Poe2Live.Landmark</c>. Static: it comes from
/// the area's tile layout, not from entities, so a boss arena is on the map
/// before anything in it has loaded.
/// </remarks>
/// <param name="Name">The curated label, or the asset's own file name.</param>
/// <param name="Path">The tile's <c>.tdt</c> asset path.</param>
/// <param name="Centre">Grid centroid of this cluster of tiles.</param>
/// <param name="TileCount">How many tiles formed it.</param>
/// <param name="IsWayOut">
/// A transition or entrance tile. Worth its own marker: it is an exit, and it is
/// known from the moment the area loads rather than when you walk up to it.
/// </param>
public sealed record LandmarkSnapshot(
    string Name, string Path, Vector2 Centre, int TileCount, bool IsWayOut = false)
{
    /// <summary>
    /// Identity per CLUSTER, not per path. One tile path can yield several
    /// landmarks — each stair section of a multi-level dungeon is its own — so
    /// the path alone is ambiguous.
    /// </summary>
    public string Key => $"{Path}@{(int)Centre.X},{(int)Centre.Y}";
}
