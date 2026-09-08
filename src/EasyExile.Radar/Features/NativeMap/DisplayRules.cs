using EasyExile.Core.Snapshots;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// The ordered ruleset that decides every marker on the map.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>DisplayRules.CategoryDefaults</c>. Order IS
/// precedence — the first matching rule wins — and the reference's order is kept
/// exactly: threat rules, then mechanics, then monsters by rank, then the
/// remaining category defaults, then the catch-all point-of-interest rule last.
///
/// That tail position matters: a transition or a chest that also carries the
/// game's map icon still draws as what it is, because its category rule was
/// reached first.
/// </remarks>
public sealed class DisplayRules
{
    private readonly CompiledRule[] _base;
    private readonly CompiledRule[] _overlay;

    public DisplayRules(NativeMapSettings options)
    {
        var rules = Build(options);

        _base = rules.Where(r => !r.Overlay).Select(r => new CompiledRule(r)).ToArray();
        _overlay = rules.Where(r => r.Overlay).Select(r => new CompiledRule(r)).ToArray();

        Signature = SignatureFor(options);
    }

    /// <summary>What the ruleset was built from, so it can be rebuilt when that changes.</summary>
    public int Signature { get; }

    /// <summary>The first base rule claiming this entity, or null for undrawn.</summary>
    public DisplayRule? Resolve(in EntitySnapshot entity)
    {
        foreach (var rule in _base)
        {
            if (rule.Matches(entity)) return rule.Rule.Hide ? null : rule.Rule;
        }

        return null;
    }

    /// <summary>
    /// True when this entity's rule opted into auto-routing.
    /// </summary>
    /// <remarks>
    /// The reference's per-rule "Auto-path" flag: on a first visit to an area it
    /// auto-selects every target whose rule carries it, so you arrive with the
    /// route already drawn instead of having to ask for it.
    /// </remarks>
    public bool IsNavigable(in EntitySnapshot entity) => Resolve(entity) is { Navigable: true };

    /// <summary>The first overlay rule claiming it — a threat mark on top, or null.</summary>
    public DisplayRule? ResolveOverlay(in EntitySnapshot entity)
    {
        foreach (var rule in _overlay)
        {
            if (rule.Matches(entity)) return rule.Rule;
        }

        return null;
    }

    /// <summary>
    /// A plain filled circle. Not a library icon: at three pixels a stamped
    /// polygon is a worse circle than a circle.
    /// </summary>
    internal const string Dot = "";

    private static readonly EntityKind[] MonsterOnly = { EntityKind.Monster };
    private static readonly EntityKind[] Marker = { EntityKind.Object, EntityKind.Other };
    private static readonly EntityKind[] OtherOnly = { EntityKind.Other };
    private static readonly EntityKind[] ChestOnly = { EntityKind.Chest };

    private static List<DisplayRule> Build(NativeMapSettings o)
    {
        var rules = new List<DisplayRule>(20);

        // 1) Threat overlays. The reference seeds exactly one — the Abyss
        //    Lightless rule — and inserts it ABOVE the monster rules so a void
        //    monster is never just another dot. Ours rides on top of the rank
        //    marker instead of replacing it, so the rank colour survives.
        if (o.ShowThreats)
        {
            rules.Add(new DisplayRule(
                "Abyss Lightless (Void)", "Exclamation", Palette.Threat, 6f,
                Categories: MonsterOnly,
                Mods: new[] { "AbyssLightless", "LightlessWell", "Lightless" },
                Alive: true,
                Label: "VOID",
                Overlay: true));
        }

        // 2) Mechanic overrides, gated by category exactly as the reference
        //    gates them. Above the category defaults so an expedition device
        //    draws as a flag and not as a generic dot.
        if (o.ShowMechanics)
        {
            // The league mechanic, and the default navigation destination: it
            // is what the area is entered for.
            rules.Add(new DisplayRule("Expedition", "Flag", Palette.Rgba(38, 230, 217), 7f,
                Categories: OtherOnly, Match: new[] { "Expedition2/Expedition2Encounter" },
                Navigable: o.AutoRouteLeagueMechanic));

            rules.Add(new DisplayRule("Ritual", "Star", Palette.Rgba(255, 51, 85), 7f,
                Categories: Marker, Match: new[] { "Ritual" }));

            rules.Add(new DisplayRule("Breach", "Portal", Palette.Rgba(166, 77, 255), 7f,
                Categories: Marker, Match: new[] { "Breach" }));

            rules.Add(new DisplayRule("Strongbox", "Chest", Palette.Rgba(255, 179, 0), 6f,
                Categories: ChestOnly, Match: new[] { "StrongBoxes" }));

            rules.Add(new DisplayRule("Essence", "Flask", Palette.Rgba(51, 224, 255), 7f,
                Categories: Marker, Match: new[] { "Essence" }));

            rules.Add(new DisplayRule("Shrine", "Star", Palette.Rgba(125, 255, 125), 6f,
                Match: new[] { "Metadata/Shrines/" }));
        }

        // 3) Monsters by rank, rarest first. The colours are ours rather than
        //    the reference's: rank IS the marker here, because at map scale a
        //    ring around a three-pixel dot is mud.
        void Monster(string name, MonsterRarity rarity, uint colour, float size)
        {
            rules.Add(new DisplayRule(
                "Monster - " + name, Dot, colour, size,
                Categories: MonsterOnly, Rarity: rarity,
                Alive: o.ShowDeadMonsters ? null : true,
                Enabled: o.ShowMonsters && o.ShowsRank(rarity)));
        }

        Monster("Unique", MonsterRarity.Unique, unchecked((uint)o.MonsterUniqueColour), 4.6f);
        Monster("Rare", MonsterRarity.Rare, unchecked((uint)o.MonsterRareColour), 3.8f);
        Monster("Magic", MonsterRarity.Magic, unchecked((uint)o.MonsterMagicColour), 3.2f);
        Monster("Normal", MonsterRarity.Normal, unchecked((uint)o.MonsterNormalColour), 3f);

        // Unknown rank reads as normal rather than being promoted to one the
        // client never claimed.
        Monster("Unranked", MonsterRarity.Unknown, unchecked((uint)o.MonsterNormalColour), 3f);

        // 4) Category defaults.
        rules.Add(new DisplayRule("Player", Dot, unchecked((uint)o.PlayerColour), 3.5f,
            Categories: new[] { EntityKind.Player }, Hide: true));

        rules.Add(new DisplayRule("Ally", Dot, unchecked((uint)o.AllyColour), 2.5f,
            Categories: new[] { EntityKind.Ally }, Alive: true, Enabled: o.ShowAllies));

        rules.Add(new DisplayRule("NPC", Dot, Palette.Npc, 3f,
            Categories: new[] { EntityKind.Npc }, Enabled: o.ShowNpcs));

        rules.Add(new DisplayRule("Chest", "Chest", Palette.Chest, 4f,
            Categories: ChestOnly, Enabled: o.ShowChests));

        // Named on the map. Which exit is which is the whole question you ask of
        // one, and a row of identical green staircases does not answer it.
        rules.Add(new DisplayRule("Transition", "Stairs", Palette.TransitionGreen, 5f,
            Categories: new[] { EntityKind.Transition },
            LabelFromMetadata: o.ShowTransitionNames,
            Navigable: o.AutoRouteExits,
            Enabled: o.ShowTransitions));

        rules.Add(new DisplayRule("Other player", Dot, Palette.OtherPlayer, 3.5f,
            Categories: new[] { EntityKind.OtherPlayer }, Enabled: o.ShowOtherPlayers));

        // 5) Anything the client itself marks, LAST — so a category rule always
        //    wins over it.
        rules.Add(new DisplayRule("Point of interest", "MapPin", Palette.PoiBlue, 3.6f,
            Categories: Marker, Poi: true, Enabled: o.ShowPois));

        // 6) Debug: every remaining entity as a plain dot.
        if (o.ShowRawEntities) rules.Add(new DisplayRule("Raw", Dot, Palette.Raw, 2f));

        return rules;
    }

    /// <summary>Everything the ruleset is built from, folded into one value.</summary>
    public static int SignatureFor(NativeMapSettings o)
    {
        var hash = new HashCode();

        hash.Add(o.ShowMonsters); hash.Add(o.ShowNormalMonsters); hash.Add(o.ShowMagicMonsters);
        hash.Add(o.ShowRareMonsters); hash.Add(o.ShowUniqueMonsters); hash.Add(o.ShowDeadMonsters);
        hash.Add(o.ShowAllies); hash.Add(o.ShowNpcs); hash.Add(o.ShowChests); hash.Add(o.ShowTransitions);
        hash.Add(o.ShowOtherPlayers); hash.Add(o.ShowPois); hash.Add(o.ShowMechanics);
        hash.Add(o.ShowThreats); hash.Add(o.ShowRawEntities);
        hash.Add(o.AutoRouteExits); hash.Add(o.AutoRouteLeagueMechanic);
        hash.Add(o.ShowTransitionNames);

        // The colours belong here too. The ruleset is compiled once and rebuilt
        // only when this value changes, so a colour left out of it would be a
        // setting the HUD accepts and the map ignores until something else
        // happens to change.
        hash.Add(o.MonsterNormalColour); hash.Add(o.MonsterMagicColour);
        hash.Add(o.MonsterRareColour); hash.Add(o.MonsterUniqueColour);
        hash.Add(o.AllyColour); hash.Add(o.PlayerColour);

        return hash.ToHashCode();
    }
}
