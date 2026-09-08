namespace EasyExile.Radar.Settings.AutoPotion;

/// <summary>Which pool the single life-flask key watches.</summary>
public enum LifeFlaskMode
{
    /// <summary>Health only. The reference's default.</summary>
    Health,

    /// <summary>Energy shield only, for CI and shield-stacking builds.</summary>
    EnergyShield,

    /// <summary>Whichever drops first. Safe on a build with no shield: it never trips.</summary>
    Either,
}

/// <summary>
/// Auto potion. The one feature that sends anything to the game.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>RadarSettings</c>: the modes, the thresholds,
/// the cooldowns and the key defaults are its numbers, not ones chosen here.
///
/// <see cref="Enabled"/> is the F8 kill-switch and it persists, exactly as the
/// reference persists it — a disabled state has to survive a restart, or turning
/// it off means nothing the moment the overlay is reopened. It ships OFF, which
/// is where the reference and this part company: the reference defaults ON, and
/// a feature that presses keys should not arm itself on a fresh install.
/// </remarks>
public sealed record AutoPotionSettings
{
    /// <summary>The master switch, toggled in game by F8.</summary>
    public bool Enabled { get; init; }

    public bool LifeEnabled { get; init; } = true;

    public LifeFlaskMode LifeMode { get; init; } = LifeFlaskMode.Health;

    /// <summary>Percent of maximum health below which the life key fires.</summary>
    public float LifeThresholdPercent { get; init; } = 65f;

    /// <summary>Percent of maximum shield, used by the two shield modes.</summary>
    public float EnergyShieldThresholdPercent { get; init; } = 50f;

    public int LifeCooldownMs { get; init; } = 2500;

    /// <summary>Win32 virtual key. 0x31 is '1'.</summary>
    public int LifeKey { get; init; } = 0x31;

    public bool ManaEnabled { get; init; } = true;

    public float ManaThresholdPercent { get; init; } = 30f;

    public int ManaCooldownMs { get; init; } = 2000;

    /// <summary>0x32 is '2'.</summary>
    public int ManaKey { get; init; } = 0x32;

    /// <summary>Toggles <see cref="Enabled"/> in game. 0x77 is F8.</summary>
    public int KillSwitchKey { get; init; } = 0x77;

    /// <summary>
    /// Runs the whole decision and presses nothing.
    /// </summary>
    /// <remarks>
    /// For checking that a threshold fires where you expect it to without
    /// spending a flask charge or sending a keystroke. Not persisted: arming
    /// automation should never be something a stale config file does quietly.
    /// </remarks>
    [Transient]
    public bool DryRun { get; init; }
}
