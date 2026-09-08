using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Core.World;

/// <summary>
/// Static tile landmarks: boss arenas, waypoint chambers, mechanic rooms.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>Poe2Live.ScanLandmarks</c> and
/// <c>ClusterTiles</c>. This is the "X is over there" layer, and it comes from
/// the terrain rather than from entities — which is why it works before you have
/// been anywhere near the thing.
///
/// The clustering is the part worth keeping intact. A reusable tile recurs in
/// several disjoint places; averaging every instance into one centroid drops a
/// marker in the empty space between them, pointing at nothing.
/// </remarks>
internal static class LandmarkReader
{
    /// <summary>
    /// Chebyshev distance in tiles at which two cells join a cluster. The
    /// reference's default: 2 bridges a one-tile hole inside a feature while
    /// keeping well-separated copies apart.
    /// </summary>
    private const int ClusterGap = 2;

    public static IReadOnlyList<LandmarkSnapshot> Read(IMemoryReader memory, nint areaInstance, string areaCode)
    {
        var empty = Array.Empty<LandmarkSnapshot>();

        // Terrain is INLINE on the area: the offset is added, never followed.
        var terrain = areaInstance + GameLayout.Terrain.Root;

        if (!memory.TryRead<long>(terrain + GameLayout.Terrain.TotalTiles, out var tilesX) || tilesX <= 0)
            return empty;

        if (!memory.TryReadPointer(terrain + GameLayout.Terrain.TileDetails, out var first) || first == 0)
            return empty;

        if (!memory.TryReadPointer(terrain + GameLayout.Terrain.TileDetails + 8, out var last))
            return empty;

        var stride = GameLayout.Terrain.TileStride;
        var count = ((long)last - first) / stride;

        if (count is <= 0 or > 1_000_000) return empty;

        // One string read per distinct tile TYPE — dozens, not one per tile.
        var pathOf = new Dictionary<nint, string?>();
        var cellsByPath = new Dictionary<string, List<(int X, int Y)>>(StringComparer.Ordinal);

        for (long i = 0; i < count; i++)
        {
            var tile = first + (nint)(i * stride);

            if (!memory.TryReadPointer(tile + GameLayout.Terrain.TgtFile, out var asset) || asset == 0) continue;

            if (!pathOf.TryGetValue(asset, out var path))
            {
                var read = memory.TryReadWideString(
                    asset + GameLayout.Terrain.TgtPath,
                    GameLayout.Native.StringBuffer,
                    GameLayout.Native.StringSize,
                    GameLayout.Native.StringCapacity);

                // Curated first, then anything that reads as a way OUT. The
                // reference dropped its generic keyword sweep because it
                // surfaced decorative terrain as noise, and that judgement
                // stands — this is not a sweep. It is one category, and it is
                // the category you most want named before you have walked to
                // it: the exits are in the tile grid from the moment the area
                // loads, while the entity standing on one does not exist until
                // you are near enough for the client to load it.
                path = read is not null && (CuratedLandmarks.Match(areaCode, read) is not null || IsWayOut(read))
                    ? read
                    : null;

                pathOf[asset] = path;
            }

            if (path is null) continue;

            if (!cellsByPath.TryGetValue(path, out var cells)) cellsByPath[path] = cells = new List<(int, int)>();

            cells.Add(((int)(i % tilesX), (int)(i / tilesX)));
        }

        if (cellsByPath.Count == 0) return empty;

        var cell = GameLayout.Terrain.CellsPerTile;
        var found = new List<LandmarkSnapshot>();

        foreach (var (path, cells) in cellsByPath)
        {
            var label = CuratedLandmarks.Match(areaCode, path);

            foreach (var cluster in Cluster(cells, ClusterGap))
            {
                double sx = 0, sy = 0;

                foreach (var (x, y) in cluster) { sx += x; sy += y; }

                var centre = new Vector2(
                    (float)(sx / cluster.Count * cell),
                    (float)(sy / cluster.Count * cell));

                found.Add(new LandmarkSnapshot(label ?? NameOf(path), path, centre, cluster.Count, IsWayOut(path)));
            }
        }

        return found;
    }

    /// <summary>
    /// Groups same-path cells into spatially disjoint clusters. Plain BFS over a
    /// cell set, so a tile type recurring across the map yields one cluster per
    /// place rather than one meaningless average.
    /// </summary>
    private static List<List<(int X, int Y)>> Cluster(List<(int X, int Y)> cells, int gap)
    {
        var set = new HashSet<(int, int)>(cells);
        var visited = new HashSet<(int, int)>();
        var clusters = new List<List<(int X, int Y)>>();
        var queue = new Queue<(int, int)>();

        foreach (var start in cells)
        {
            if (!visited.Add(start)) continue;

            var cluster = new List<(int X, int Y)>();

            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var (cx, cy) = queue.Dequeue();

                cluster.Add((cx, cy));

                for (var dx = -gap; dx <= gap; dx++)
                {
                    for (var dy = -gap; dy <= gap; dy++)
                    {
                        var neighbour = (cx + dx, cy + dy);

                        if (set.Contains(neighbour) && visited.Add(neighbour)) queue.Enqueue(neighbour);
                    }
                }
            }

            clusters.Add(cluster);
        }

        return clusters;
    }

    /// <summary>
    /// A tile that is a way out of the area.
    /// </summary>
    /// <remarks>
    /// "Transition" and nothing else. This started out also matching "Entrance",
    /// and that was the reference's warning coming true within one area: it
    /// surfaced Cityroof_Entrance, Citywall_2_Entrance and
    /// Keth_Stairs_RuinEntrance — a roof, a wall and a staircase, none of them a
    /// way anywhere. Each got a marker reading "Entrance" sitting a few tiles
    /// off the real furniture, which reads as a misaligned exit rather than as
    /// architecture that should never have been marked.
    ///
    /// A tile whose path says Transition is a place the area actually ends. That
    /// is the whole category, and it is the one worth naming before you have
    /// walked to it.
    /// </remarks>
    private static bool IsWayOut(string path) =>
        path.Contains("Transition", StringComparison.OrdinalIgnoreCase);

    /// <summary>The same rule, reachable from a test. Nothing else calls it.</summary>
    internal static bool IsWayOutForTests(string path) => IsWayOut(path);

    /// <summary>The asset's file name, minus the extension. The fallback label.</summary>
    private static string NameOf(string path)
    {
        var slash = path.LastIndexOf('/');
        var name = slash >= 0 ? path[(slash + 1)..] : path;

        if (name.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase)) name = name[..^4];

        // Tile assets are named like AreaTransition_BadlandsToPits_01. Strip the
        // trailing copy number and the generic prefix, then space the words.
        while (name.Length > 0 && char.IsDigit(name[^1])) name = name[..^1];
        if (name.EndsWith("_", StringComparison.Ordinal)) name = name[..^1];

        foreach (var prefix in new[] { "AreaTransition_", "AreaTransition", "Transition_" })
        {
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

            name = name[prefix.Length..];
            break;
        }

        return Spaced(name.Replace('_', ' ').Trim());
    }

    /// <summary>Splits CamelCase into words, the way the entity labels do.</summary>
    private static string Spaced(string name)
    {
        var words = new System.Text.StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];

            if (i > 0 && char.IsUpper(c) && char.IsLower(name[i - 1]) && words[^1] != ' ') words.Append(' ');

            words.Append(c);
        }

        return words.ToString().Trim();
    }
}
