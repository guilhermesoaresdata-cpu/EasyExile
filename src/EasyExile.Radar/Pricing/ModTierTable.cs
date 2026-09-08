using System.Text.Json;

namespace EasyExile.Radar.Pricing;

/// <summary>
/// How many tiers each mod family has, so a tier number can become "T1".
/// </summary>
/// <remarks>
/// The game names its mods Strength1..Strength8, so the trailing number IS the
/// tier and the letters before it are the family. That much can be read straight
/// off the item and needs no table at all. What a table adds is the CEILING —
/// eight of how many — which is the whole difference between "tier 8" and "this
/// is the best roll that exists".
///
/// The ceiling is per item class: IncreasedLife has 8 tiers on a ring, 10 on a
/// belt and 13 on a body armour. So the table is indexed both ways. Asked with
/// the class it answers exactly; asked without, it answers with the range across
/// every class that has the family, and says whether that range is a single
/// number.
///
/// Where the range is wide the answer deliberately leans on the HIGHEST ceiling,
/// which under-claims rather than over-claims: a mod that is top-tier on a ring
/// reads as "8 of 13" instead of announcing a T1 that might not be one. A missed
/// alert costs a second look; a false one costs trust.
/// </remarks>
public sealed class ModTierTable
{
    private readonly Dictionary<string, Dictionary<string, Entry>> _byClass =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Entry> _anyClass = new(StringComparer.OrdinalIgnoreCase);

    private ModTierTable()
    {
    }

    public static ModTierTable Empty { get; } = new();

    /// <summary>Distinct mod names the table knows.</summary>
    public int Mods => _anyClass.Count;

    public int Classes => _byClass.Count;

    public bool IsLoaded => _anyClass.Count > 0;

    /// <summary>
    /// Reads the table, which is shaped
    /// class → subtype → Prefix|Suffix → family → { mod name: required item level }.
    /// </summary>
    public static ModTierTable Load(string path)
    {
        var table = new ModTierTable();

        if (!File.Exists(path)) return table;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));

            foreach (var itemClass in document.RootElement.EnumerateObject())
            {
                foreach (var subtype in itemClass.Value.EnumerateObject())
                {
                    foreach (var affix in subtype.Value.EnumerateObject())
                    {
                        foreach (var family in affix.Value.EnumerateObject())
                        {
                            table.Add(itemClass.Name, affix.Name, family);
                        }
                    }
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A table that cannot be read means no ceiling, which the caller
            // already handles: the tier still reads off the mod's own name.
            return new ModTierTable();
        }

        return table;
    }

    private void Add(string itemClass, string affix, JsonProperty family)
    {
        var count = 0;
        var top = 0;

        foreach (var mod in family.Value.EnumerateObject())
        {
            count++;
            top = Math.Max(top, TierOf(mod.Name));
        }

        if (count == 0) return;

        // The ceiling is the highest tier NUMBER present, not the number of
        // entries. They usually agree, but a family missing a middle tier would
        // otherwise report a top that no mod can reach.
        var ceiling = Math.Max(count, top);

        if (!_byClass.TryGetValue(itemClass, out var mods))
            _byClass[itemClass] = mods = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in family.Value.EnumerateObject())
        {
            var entry = new Entry(family.Name, affix, TierOf(mod.Name), ceiling, ceiling);

            mods[Bare(mod.Name)] = entry;

            // Across classes the same mod can have different ceilings, so the
            // class-less index keeps the whole range rather than whichever class
            // happened to be read last.
            if (_anyClass.TryGetValue(Bare(mod.Name), out var seen))
            {
                _anyClass[Bare(mod.Name)] = seen with
                {
                    Lowest = Math.Min(seen.Lowest, ceiling),
                    Highest = Math.Max(seen.Highest, ceiling),
                };
            }
            else
            {
                _anyClass[Bare(mod.Name)] = entry;
            }
        }
    }

    /// <summary>
    /// What tier a mod id is, and out of how many.
    /// </summary>
    /// <param name="itemClass">
    /// The item's class, when known. Without it the answer covers every class
    /// that has the family, and <see cref="ModTier.IsExact"/> says whether they
    /// agree.
    /// </param>
    public ModTier? Find(string? modId, string? itemClass = null)
    {
        if (modId is not { Length: > 1 }) return null;

        var name = Bare(modId);

        if (itemClass is { Length: > 0 } &&
            _byClass.TryGetValue(itemClass, out var mods) &&
            mods.TryGetValue(name, out var exact))
            return new ModTier(exact.Family, exact.Affix, exact.Tier, exact.Highest, IsExact: true);

        if (!_anyClass.TryGetValue(name, out var any)) return null;

        return new ModTier(
            any.Family, any.Affix, any.Tier, any.Highest, IsExact: any.Lowest == any.Highest);
    }

    /// <summary>
    /// The tier a mod carries in its own name, with no table involved.
    /// </summary>
    /// <remarks>
    /// Useful on its own: it answers "tier 3" for a mod the table has never
    /// heard of, which is most of what a new league adds.
    /// </remarks>
    public static int TierOf(string? modId)
    {
        if (modId is not { Length: > 0 }) return 0;

        var end = modId.TrimEnd('_');
        var digits = 0;

        while (digits < end.Length && char.IsAsciiDigit(end[^(digits + 1)])) digits++;

        return digits > 0 && int.TryParse(end[^digits..], out var tier) ? tier : 0;
    }

    /// <summary>The family: the name with its tier number and padding removed.</summary>
    public static string FamilyOf(string modId)
    {
        var end = modId.TrimEnd('_');
        var digits = 0;

        while (digits < end.Length && char.IsAsciiDigit(end[^(digits + 1)])) digits++;

        return digits > 0 ? end[..^digits] : end;
    }

    /// <summary>Names are padded with underscores to keep them unique; the pad is not the name.</summary>
    private static string Bare(string modId) => modId.TrimEnd('_');

    private readonly record struct Entry(
        string Family, string Affix, int Tier, int Lowest, int Highest);
}

/// <summary>One mod's standing within its family.</summary>
/// <param name="Top">The highest tier the family reaches.</param>
/// <param name="IsExact">
/// False when the ceiling had to be taken across item classes that disagree, so
/// the mod may be better than it reads — never worse.
/// </param>
public readonly record struct ModTier(
    string Family, string Affix, int Tier, int Top, bool IsExact)
{
    public bool IsBest => Tier > 0 && Tier >= Top;

    public bool IsPrefix => string.Equals(Affix, "Prefix", StringComparison.OrdinalIgnoreCase);

    /// <summary>"T1 (8/8)" in the direction players read: T1 is best.</summary>
    public string Label => Top > 0 ? $"T{Top - Tier + 1} ({Tier}/{Top})" : $"tier {Tier}";
}
