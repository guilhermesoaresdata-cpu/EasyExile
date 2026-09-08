namespace EasyExile.Core.Snapshots;

/// <summary>
/// The character's three pools, as percentages.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Poe2Live.Vitals</c>. It rides on the fast
/// frame rather than the world snapshot for the same reason the player position
/// does: anything deciding on health has to be looking at health as it is now,
/// not as it was at the last entity walk.
///
/// A pool with no maximum reads as full rather than as empty. That is the
/// fail-closed direction: a build with no energy shield must never look like a
/// build whose shield just broke.
/// </remarks>
public readonly record struct VitalsSnapshot(
    int Health, int MaxHealth, int Mana, int MaxMana, int EnergyShield, int MaxEnergyShield)
{
    public float HealthPercent => Percent(Health, MaxHealth);

    public float ManaPercent => Percent(Mana, MaxMana);

    public float EnergyShieldPercent => Percent(EnergyShield, MaxEnergyShield);

    /// <summary>True when the build actually has a shield to watch.</summary>
    public bool HasEnergyShield => MaxEnergyShield > 0;

    /// <summary>
    /// Whether these numbers can be acted on at all.
    /// </summary>
    /// <remarks>
    /// The gate everything automatic hangs off. A character with no maximum
    /// health has not been read — it is loading, or the offsets drifted — and
    /// acting on that is how a macro either fires forever or never fires at all.
    /// </remarks>
    public bool IsPlausible =>
        MaxHealth > 0 && Health >= 0 && Health <= MaxHealth * 2 &&
        MaxMana >= 0 && Mana >= 0 &&
        MaxEnergyShield >= 0 && EnergyShield >= 0;

    private static float Percent(int current, int max) =>
        max > 0 ? Math.Clamp(100f * current / max, 0f, 100f) : 100f;
}
