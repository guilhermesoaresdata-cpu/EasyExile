namespace EasyExile.Radar.Settings.Loot;

/// <summary>
/// The kinds of build an item can help.
/// </summary>
/// <remarks>
/// Flags, because an item usually helps more than one and a levelling character
/// usually is more than one thing - a minion build still wants life and
/// resistances, and the mark exists to say "look at this" rather than to
/// classify.
/// </remarks>
[Flags]
public enum BuildTag
{
    None = 0,
    Minion = 1,
    Projectile = 1 << 1,
    Fire = 1 << 2,
    Cold = 1 << 3,
    Lightning = 1 << 4,
    Chaos = 1 << 5,
    Spell = 1 << 6,
    Attack = 1 << 7,
    Life = 1 << 8,
    EnergyShield = 1 << 9,
    Spirit = 1 << 10,
    Crit = 1 << 11,
}
