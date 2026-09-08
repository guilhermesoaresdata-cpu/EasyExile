using System.Reflection;
using System.Text.Json;

namespace EasyExile.Core.World;

/// <summary>
/// Curated landmark labels: area code, then terrain tile path, then a friendly
/// name.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>CustomLandmarkData</c>, including its data
/// file. The data is the feature: a tile path like
/// <c>.../Arena_Boss_01.tdt</c> is not a label anyone wants to read, and there
/// is no way to derive "Trial of the Sekhemas" from the asset name.
///
/// Only curated tiles surface. The reference used to sweep for keywords and
/// dropped it: it turned every decorative vault door into a marker. A tile is a
/// landmark because someone said so.
/// </remarks>
public static class CuratedLandmarks
{
    private static Dictionary<string, Dictionary<string, string>>? _data;

    /// <summary>Area code, then tile-path substring, then label.</summary>
    public static IReadOnlyDictionary<string, Dictionary<string, string>> Load()
    {
        if (_data is not null) return _data;

        _data = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        var assembly = Assembly.GetExecutingAssembly();
        var name = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.Contains("CustomLandmarks", StringComparison.Ordinal));

        if (name is null) return _data;

        using var stream = assembly.GetManifestResourceStream(name);
        if (stream is null) return _data;

        var document = JsonDocument.Parse(stream);

        foreach (var area in document.RootElement.EnumerateObject())
        {
            var tiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tile in area.Value.EnumerateObject())
            {
                var path = tile.Name;

                // Keys carry an optional ":suffix" qualifier, and the data uses
                // both .tdtx and .tdt for the same asset.
                var colon = path.IndexOf(':');

                if (colon >= 0) path = path[..colon];

                path = path.Replace(".tdtx", ".tdt", StringComparison.OrdinalIgnoreCase);

                tiles[path] = tile.Value.GetString() ?? tile.Name;
            }

            _data[area.Name] = tiles;
        }

        return _data;
    }

    /// <summary>The label for a tile path, area-specific first, then global.</summary>
    public static string? Match(string areaCode, string tilePath)
    {
        var data = Load();

        if (data.TryGetValue(areaCode, out var area) && Find(area, tilePath) is { } local) return local;

        return data.TryGetValue("*", out var global) ? Find(global, tilePath) : null;
    }

    private static string? Find(Dictionary<string, string> patterns, string tilePath)
    {
        foreach (var (pattern, label) in patterns)
        {
            if (tilePath.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return label;
        }

        return null;
    }
}
