using System.Text.RegularExpressions;
using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// One rule deciding whether an entity is drawn and how.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Web/DisplayRules.cs</c>. Every condition here
/// exists in the reference; none was invented, and none of the reference's was
/// dropped.
///
/// The one addition is <see cref="Overlay"/>. The reference lets a mod rule
/// REPLACE a monster's marker, which costs the rank colour — the thing that
/// says whether it is a normal or a unique. Here a threat rule adds a mark on
/// top instead, so both facts survive.
/// </remarks>
/// <param name="Categories">Kinds this may apply to. Empty means any.</param>
/// <param name="Match">Metadata terms, ANY-of. Supports <c>*</c> and <c>?</c>.</param>
/// <param name="Mods">Affix-mod terms, ANY-of, against the entity's mod ids.</param>
/// <param name="Overlay">Drawn on top of the base marker rather than instead of it.</param>
/// <param name="LabelFromMetadata">
/// Names each entity from its own path instead of using a fixed
/// <paramref name="Label"/>. What an exit needs: one rule, a different name per
/// destination.
/// </param>
/// <param name="Navigable">Auto-routed on first arrival in an area.</param>
public sealed record DisplayRule(
    string Name,
    string Shape,
    uint Colour,
    float Size,
    EntityKind[]? Categories = null,
    string[]? Match = null,
    string[]? Mods = null,
    MonsterRarity? Rarity = null,
    bool? Alive = null,
    bool? Poi = null,
    bool? EncounterComplete = null,
    string? Label = null,
    bool Hide = false,
    bool LabelFromMetadata = false,
    bool Overlay = false,
    bool Navigable = false,
    bool Enabled = true);

/// <summary>
/// A rule with its terms precompiled, ready for the render path.
/// </summary>
/// <remarks>
/// The reference precompiles for a stated reason: matching by
/// <c>Category.ToString()</c> per entity per rule per frame was its dominant
/// source of GC pressure in combat. Same approach here — enum comparisons and
/// prepared terms, no allocation while matching.
/// </remarks>
public sealed class CompiledRule
{
    private readonly DisplayRule _rule;
    private readonly (string Term, Regex? Glob)[]? _match;
    private readonly (string Term, Regex? Glob)[]? _mods;

    public CompiledRule(DisplayRule rule)
    {
        _rule = rule;
        _match = Compile(rule.Match);
        _mods = Compile(rule.Mods);
    }

    public DisplayRule Rule => _rule;

    public bool Matches(in EntitySnapshot entity)
    {
        if (!_rule.Enabled) return false;

        if (_rule.Categories is { Length: > 0 } categories && !Contains(categories, entity.Kind)) return false;

        if (_match is not null && !AnyMatch(_match, entity.Metadata)) return false;
        if (_mods is not null && !AnyMod(entity)) return false;

        if (_rule.Rarity is { } rarity && entity.Rarity != rarity) return false;
        if (_rule.Alive is { } alive && entity.IsAlive != alive) return false;
        if (_rule.Poi is { } poi && entity.IsPoi != poi) return false;
        if (_rule.EncounterComplete is { } complete && entity.IconComplete != complete) return false;

        return true;
    }

    private static bool Contains(EntityKind[] categories, EntityKind kind)
    {
        foreach (var allowed in categories)
        {
            if (allowed == kind) return true;
        }

        return false;
    }

    private static bool AnyMatch((string Term, Regex? Glob)[] terms, string text)
    {
        foreach (var (term, glob) in terms)
        {
            if (glob is not null)
            {
                if (glob.IsMatch(text)) return true;
            }
            else if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Any of this rule's terms against any of the entity's mod ids.</summary>
    private bool AnyMod(in EntitySnapshot entity)
    {
        var mods = entity.ModIds;

        if (mods.Length == 0) return false;

        foreach (var (term, glob) in _mods!)
        {
            for (var i = 0; i < mods.Length; i++)
            {
                var mod = mods[i];

                if (glob is not null)
                {
                    if (glob.IsMatch(mod)) return true;
                }
                else if (mod.Contains(term, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A term carrying <c>*</c> or <c>?</c> becomes an anchored glob; anything
    /// else stays a case-insensitive substring.
    /// </summary>
    private static (string Term, Regex? Glob)[]? Compile(string[]? terms)
    {
        if (terms is not { Length: > 0 }) return null;

        var compiled = new (string, Regex?)[terms.Length];

        for (var i = 0; i < terms.Length; i++)
        {
            var term = terms[i];

            compiled[i] = term.IndexOf('*') < 0 && term.IndexOf('?') < 0
                ? (term, null)
                : (term, new Regex(
                    "^" + Regex.Escape(term).Replace(@"\*", ".*").Replace(@"\?", ".") + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        }

        return compiled;
    }
}
