namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// Turns a metadata path into something worth printing on a map.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>RadarApp.EntityLabel</c>: take the last path
/// segment, drop a trailing number, and split the CamelCase back into words.
/// The reference also consults a curated name table first; we have no such
/// table, so the derived name is the only answer here.
///
/// One addition, for the case this exists to serve: a trailing "Transition" is
/// dropped. Every exit's path ends in it, so keeping it would put the same
/// useless word on every label — "Lightless Passage Transition" says nothing
/// that "Lightless Passage" does not.
/// </remarks>
public static class EntityLabels
{
    /// <summary>Labels are per metadata path, and a path is per entity TYPE.</summary>
    private static readonly Dictionary<string, string> Cache = new(StringComparer.Ordinal);

    private const string TransitionSuffix = "Transition";

    /// <summary>Cheap after the first entity of a type: the answer is memoised.</summary>
    public static string Pretty(string metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return string.Empty;

        if (Cache.TryGetValue(metadata, out var cached)) return cached;

        var label = Derive(metadata);

        // Bounded by the number of distinct paths in play, which is dozens. It
        // never needs clearing on an area change: a path always means the same
        // thing, unlike the addresses everything else here is keyed on.
        Cache[metadata] = label;

        return label;
    }

    private static string Derive(string metadata)
    {
        var slash = metadata.LastIndexOf('/');
        var segment = slash >= 0 ? metadata[(slash + 1)..] : metadata;

        // A trailing "_NN" or digit run: "Encounter_03" becomes "Encounter",
        // while "Expedition2Encounter" keeps its interior digit.
        var end = segment.Length;

        while (end > 0 && char.IsDigit(segment[end - 1])) end--;
        if (end > 0 && segment[end - 1] == '_') end--;
        if (end > 0) segment = segment[..end];

        if (segment.Length > TransitionSuffix.Length &&
            segment.EndsWith(TransitionSuffix, StringComparison.Ordinal))
            segment = segment[..^TransitionSuffix.Length];

        var words = new System.Text.StringBuilder(segment.Length + 8);

        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];

            if (i > 0)
            {
                var previous = segment[i - 1];

                var boundary = (char.IsUpper(c) && (char.IsLower(previous) || char.IsDigit(previous))) ||
                               (char.IsDigit(c) && char.IsLetter(previous) && !char.IsDigit(previous));

                if (boundary && words.Length > 0 && words[^1] != ' ') words.Append(' ');
            }

            words.Append(c);
        }

        return words.ToString().Trim();
    }
}
