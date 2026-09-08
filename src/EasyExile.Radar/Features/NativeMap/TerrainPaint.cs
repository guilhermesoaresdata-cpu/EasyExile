namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// How to colour the terrain: what has been walked, what has not, and the record
/// of which is which.
/// </summary>
/// <remarks>
/// A record rather than three parameters because the three only make sense
/// together — a colour for "unvisited" means nothing without the map that says
/// which cells those are, and passing them separately invites a caller to supply
/// one and forget the other.
/// </remarks>
public readonly record struct TerrainPaint(int Visited, int Unvisited, ExplorationMap? Explored);
