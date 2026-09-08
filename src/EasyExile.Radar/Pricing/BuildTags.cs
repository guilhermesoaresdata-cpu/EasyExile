using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Pricing;

/// <summary>
/// Which kind of build a rolled mod helps.
/// </summary>
/// <remarks>
/// Read off the mod's own family name, which the game writes in full:
/// MinionLife, DamageWithBowSkills, ColdDamagePercentage. No table is needed and
/// none is shipped, so a mod added next league is classified the day it appears
/// rather than the day somebody updates a file.
///
/// The one deliberate exception is resistances. ColdResistance contains "Cold"
/// and helps no cold build; it belongs to the other question - what is missing
/// from a cap - and answering that one here would put a mark on every ring in
/// the game.
/// </remarks>
public static class BuildTags
{
    /// <summary>A tag, and the words in a family name that earn it.</summary>
    private static readonly (BuildTag Tag, string[] Words)[] Rules =
    [
        (BuildTag.Minion, ["minion", "allies"]),
        (BuildTag.Projectile, ["projectile", "bow", "arrow", "pierce"]),
        (BuildTag.Fire, ["fire", "burn", "ignite"]),
        (BuildTag.Cold, ["cold", "freeze", "chill"]),
        (BuildTag.Lightning, ["lightning", "shock"]),
        (BuildTag.Chaos, ["chaos", "poison"]),
        (BuildTag.Spell, ["spell", "cast"]),
        (BuildTag.Attack, ["attack", "melee", "weapon", "accuracy"]),
        (BuildTag.Life, ["life"]),
        (BuildTag.EnergyShield, ["energyshield"]),
        (BuildTag.Spirit, ["spirit"]),
        (BuildTag.Crit, ["critical"]),
    ];

    /// <summary>What this mod helps, if anything.</summary>
    public static BuildTag Of(string? modId)
    {
        if (modId is not { Length: > 0 }) return BuildTag.None;

        var family = ModTierTable.FamilyOf(modId);

        if (family.Length == 0) return BuildTag.None;

        // A resistance is not a damage type. It answers the other question.
        var resistance = family.Contains("resist", StringComparison.OrdinalIgnoreCase);

        var tags = BuildTag.None;

        foreach (var (tag, words) in Rules)
        {
            if (resistance && tag is not (BuildTag.Life or BuildTag.EnergyShield)) continue;

            foreach (var word in words)
            {
                if (!family.Contains(word, StringComparison.OrdinalIgnoreCase)) continue;

                tags |= tag;

                break;
            }
        }

        return tags;
    }

    /// <summary>Everything on this item that the chosen build cares about.</summary>
    public static BuildTag Of(IEnumerable<string> affixes, BuildTag wanted)
    {
        var found = BuildTag.None;

        foreach (var affix in affixes) found |= Of(affix) & wanted;

        return found;
    }

    /// <summary>
    /// A short mark for the corner of a slot. Two characters at most.
    /// </summary>
    /// <remarks>
    /// English, because the game is and so is everything written about it. The
    /// first version abbreviated the Portuguese - Fo for fogo, Fr for frio - and
    /// every glance cost a translation back into the words the item itself uses.
    /// </remarks>
    public static string Mark(BuildTag tags) => tags switch
    {
        BuildTag.None => string.Empty,
        BuildTag.Minion => "Mi",
        BuildTag.Projectile => "Pj",
        BuildTag.Fire => "Fi",
        BuildTag.Cold => "Co",
        BuildTag.Lightning => "Li",
        BuildTag.Chaos => "Ch",
        BuildTag.Spell => "Sp",
        BuildTag.Attack => "At",
        BuildTag.Life => "HP",
        BuildTag.EnergyShield => "ES",
        BuildTag.Spirit => "Sr",
        BuildTag.Crit => "Cr",

        // More than one kind of help on the same item, which is the item worth
        // looking at hardest.
        _ => "**",
    };
}
