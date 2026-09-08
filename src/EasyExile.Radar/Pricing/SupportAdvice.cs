using System.Collections.Immutable;
using System.Text.Json;

namespace EasyExile.Radar.Pricing;

/// <summary>
/// Which supports go in a skill, best first.
/// </summary>
/// <remarks>
/// Built from poe2db's "Recommended Support Gems", which is a curated list in
/// five tiers. It is worth being clear that this is NOT what a player means by
/// "the most used supports": nobody counted real builds to produce it. Usage
/// numbers like that come from build aggregators and are a different dataset
/// with a different shape and a different refresh rate.
///
/// What the tiers do give is a ranking, and the file preserves it — the first
/// entries are the ones poe2db puts at tier one — so "the first five" really is
/// "the five most recommended" even though it is not "the five most common".
///
/// The table is also what decides whether a caption on screen is a skill at all.
/// The skill panel hangs no entity off its entries, unlike a grid cell, so there
/// is no structural way to know; a label whose text is a key here is a skill,
/// and one that is not is not interesting anyway.
/// </remarks>
public sealed class SupportAdvice
{
    private readonly Dictionary<string, ImmutableArray<string>> _bySkill =
        new(StringComparer.OrdinalIgnoreCase);

    private SupportAdvice()
    {
    }

    public static SupportAdvice Empty { get; } = new();

    public int Skills => _bySkill.Count;

    public bool IsLoaded => _bySkill.Count > 0;

    /// <summary>Reads the file, which is skill name to an ordered list of supports.</summary>
    public static SupportAdvice Load(string path)
    {
        var advice = new SupportAdvice();

        if (!File.Exists(path)) return advice;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var skill in document.RootElement.EnumerateObject())
            {
                if (skill.Value.ValueKind != JsonValueKind.Array) continue;

                var supports = ImmutableArray.CreateBuilder<string>();

                foreach (var support in skill.Value.EnumerateArray())
                {
                    if (support.GetString() is { Length: > 0 } name) supports.Add(name);
                }

                if (supports.Count > 0) advice._bySkill[skill.Name] = supports.ToImmutable();
            }
        }
        catch (JsonException)
        {
            // A table nobody can parse is an empty table. The feature goes quiet
            // rather than the overlay going down.
            return new SupportAdvice();
        }
        catch (IOException)
        {
            return new SupportAdvice();
        }

        return advice;
    }

    /// <summary>
    /// The supports for this skill, best first, or nothing.
    /// </summary>
    /// <remarks>
    /// The client draws some entries with a trailing space and some prefixed by
    /// the mechanic that grants them — "Command: Gas Arrow" is Gas Arrow put on
    /// a mercenary. Both are the same skill as far as advice goes, so both find
    /// it.
    /// </remarks>
    public ImmutableArray<string> Find(string? skill)
    {
        if (skill is not { Length: > 0 }) return ImmutableArray<string>.Empty;

        var name = skill.Trim();

        if (_bySkill.TryGetValue(name, out var direct)) return direct;

        var colon = name.IndexOf(':');

        if (colon >= 0 && colon + 1 < name.Length &&
            _bySkill.TryGetValue(name[(colon + 1)..].Trim(), out var granted))
            return granted;

        return ImmutableArray<string>.Empty;
    }
}
