using EasyExile.Core.Snapshots;
using EasyExile.Radar.Input;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.AutoPotion;

namespace EasyExile.Radar.Features.AutoPotion;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// Presses the flask key when a pool drops below its threshold.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>RadarApp.TickAutoFlask</c> and its F8 handling.
/// The mode selection, the thresholds, the per-flask cooldowns and the gate
/// order are the reference's.
///
/// Everything here fails CLOSED. Vitals that do not read, a character not in an
/// area, the game not in front, the switch off — every one of those returns
/// without pressing. The reason is the same in each case: a macro that acts on
/// what it does not know either fires forever or never fires, and both are worse
/// than doing nothing.
/// </remarks>
public sealed class AutoPotion
{
    private readonly RadarSettings _settings;

    private DateTimeOffset _lifeFiredAt = DateTimeOffset.MinValue;
    private DateTimeOffset _manaFiredAt = DateTimeOffset.MinValue;

    private bool _killSwitchWasDown;
    private DateTimeOffset _nextToggleAt = DateTimeOffset.MinValue;

    public AutoPotion(RadarSettings settings) => _settings = settings;

    /// <summary>Why nothing fired, in the words the panel prints.</summary>
    public string Status { get; private set; } = "desligado";

    public int LifePresses { get; private set; }

    public int ManaPresses { get; private set; }

    public DateTimeOffset? LastLife => _lifeFiredAt == DateTimeOffset.MinValue ? null : _lifeFiredAt;

    public DateTimeOffset? LastMana => _manaFiredAt == DateTimeOffset.MinValue ? null : _manaFiredAt;

    /// <summary>Time left on each cooldown, for the panel.</summary>
    public TimeSpan LifeCooldownLeft => Remaining(_lifeFiredAt, _settings.AutoPotion.LifeCooldownMs);

    public TimeSpan ManaCooldownLeft => Remaining(_manaFiredAt, _settings.AutoPotion.ManaCooldownMs);

    /// <summary>
    /// The F8 toggle, debounced. Separate from <see cref="Tick"/> because it has
    /// to keep working while everything else is gated off — otherwise the switch
    /// that turns automation OFF would only work while automation is armed.
    /// </summary>
    public void PollKillSwitch(Func<int, bool> isKeyDown, DateTimeOffset now)
    {
        var options = _settings.AutoPotion;
        var down = isKeyDown(options.KillSwitchKey);

        if (down && !_killSwitchWasDown && now >= _nextToggleAt)
        {
            _nextToggleAt = now.AddMilliseconds(300);
            _settings.AutoPotion = options with { Enabled = !options.Enabled };
        }

        _killSwitchWasDown = down;
    }

    /// <summary>
    /// One decision. Returns what it did, so a caller can count and a test can
    /// assert without watching a keyboard.
    /// </summary>
    /// <param name="foreground">Whether the game is the window in front.</param>
    public AutoPotionAction Tick(MapFrameSnapshot? frame, bool foreground, DateTimeOffset now)
    {
        var options = _settings.AutoPotion;

        if (!options.Enabled)
        {
            Status = "OFF (F8)";
            return AutoPotionAction.None;
        }

        // No frame means no area: loading, in a menu, or mid-transition.
        if (frame is null)
        {
            Status = T("pausado (fora de area)");
            return AutoPotionAction.None;
        }

        var vitals = frame.Vitals;

        // Unreadable vitals are the dangerous case, and the reference surfaces
        // them rather than sitting silently armed: a post-patch drift should look
        // broken, not idle.
        if (!vitals.IsPlausible)
        {
            Status = T("pausado (vitais ilegiveis)");
            return AutoPotionAction.None;
        }

        // Last gate before anything is sent. Alt-tabbed, the keystroke would land
        // in whatever the player is actually typing into.
        if (!foreground)
        {
            Status = T("pausado (PoE2 sem foco)");
            return AutoPotionAction.None;
        }

        Status = options.DryRun ? "armado (simulacao)" : "armado";

        var action = AutoPotionAction.None;

        if (options.LifeEnabled && LifeTriggered(options, vitals) &&
            now - _lifeFiredAt >= TimeSpan.FromMilliseconds(options.LifeCooldownMs))
        {
            Press(options.LifeKey, options.DryRun);

            _lifeFiredAt = now;
            LifePresses++;

            Status = options.DryRun ? "WOULD_PRESS_LIFE" : $"vida @ {vitals.HealthPercent:F0}%";
            action |= AutoPotionAction.Life;
        }

        if (options.ManaEnabled && vitals.ManaPercent < options.ManaThresholdPercent &&
            now - _manaFiredAt >= TimeSpan.FromMilliseconds(options.ManaCooldownMs))
        {
            Press(options.ManaKey, options.DryRun);

            _manaFiredAt = now;
            ManaPresses++;

            Status = options.DryRun ? "WOULD_PRESS_MANA" : $"mana @ {vitals.ManaPercent:F0}%";
            action |= AutoPotionAction.Mana;
        }

        return action;
    }

    /// <summary>
    /// Which pool the life key watches. The shield only ever participates when
    /// the build HAS one, so "Either" is safe on a pure-life character.
    /// </summary>
    private static bool LifeTriggered(AutoPotionSettings options, VitalsSnapshot vitals)
    {
        var healthLow = vitals.HealthPercent < options.LifeThresholdPercent;
        var shieldLow = vitals.HasEnergyShield &&
                        vitals.EnergyShieldPercent < options.EnergyShieldThresholdPercent;

        return options.LifeMode switch
        {
            LifeFlaskMode.EnergyShield => shieldLow,
            LifeFlaskMode.Either => healthLow || shieldLow,
            _ => healthLow,
        };
    }

    private static void Press(int key, bool dryRun)
    {
        if (dryRun) return;

        SendInputNative.Tap((ushort)key);
    }

    private static TimeSpan Remaining(DateTimeOffset firedAt, int cooldownMs)
    {
        if (firedAt == DateTimeOffset.MinValue) return TimeSpan.Zero;

        var left = TimeSpan.FromMilliseconds(cooldownMs) - (DateTimeOffset.UtcNow - firedAt);

        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}

/// <summary>What one tick did. Flags, because both can fire in the same tick.</summary>
[Flags]
public enum AutoPotionAction
{
    None = 0,
    Life = 1,
    Mana = 2,
}
