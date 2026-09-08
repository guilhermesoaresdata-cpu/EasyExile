using EasyExile.Core.Snapshots;
using EasyExile.Radar.Features.AutoPotion;
using EasyExile.Radar.Input;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.AutoPotion;

namespace EasyExile.Core.Tests;

/// <summary>
/// The only feature that sends anything to the game.
/// </summary>
/// <remarks>
/// Most of these are about what it must NOT do. A macro that presses a key on
/// bad information is worse than one that never presses at all, so every gate
/// gets its own test and every one of them fails closed.
/// </remarks>
public class AutoPotionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RadarSettings Armed(Func<AutoPotionSettings, AutoPotionSettings>? tweak = null)
    {
        var options = new AutoPotionSettings
        {
            Enabled = true,
            LifeThresholdPercent = 65f,
            ManaThresholdPercent = 30f,
            LifeCooldownMs = 2500,
            ManaCooldownMs = 2000,
        };

        return new RadarSettings { AutoPotion = tweak is null ? options : tweak(options) };
    }

    /// <summary>A fast frame with the pools at the given percentages.</summary>
    private static MapFrameSnapshot Frame(int healthPercent, int manaPercent, int shieldPercent = 100, int maxShield = 0)
    {
        var world = RadarFixture.World();

        return RadarFixture.MapFrame(world) with
        {
            Vitals = new VitalsSnapshot(healthPercent, 100, manaPercent, 100, shieldPercent, maxShield),
        };
    }

    // ---- thresholds -------------------------------------------------------------

    [Fact]
    public void LifeBelowThresholdTriggersOnce()
    {
        var potion = new AutoPotion(Armed());

        var action = potion.Tick(Frame(healthPercent: 40, manaPercent: 100), foreground: true, T0);

        Assert.Equal(AutoPotionAction.Life, action);
        Assert.Equal(1, potion.LifePresses);
    }

    [Fact]
    public void LifeAboveThresholdDoesNotTrigger()
    {
        var potion = new AutoPotion(Armed());

        Assert.Equal(
            AutoPotionAction.None,
            potion.Tick(Frame(healthPercent: 80, manaPercent: 100), foreground: true, T0));

        Assert.Equal(0, potion.LifePresses);
    }

    [Fact]
    public void ManaBelowThresholdTriggersOnce()
    {
        var potion = new AutoPotion(Armed());

        var action = potion.Tick(Frame(healthPercent: 100, manaPercent: 10), foreground: true, T0);

        Assert.Equal(AutoPotionAction.Mana, action);
        Assert.Equal(1, potion.ManaPresses);
    }

    [Fact]
    public void TheShieldOnlyCountsWhenTheBuildHasOne()
    {
        // "Either" on a build with no shield must behave exactly like "Health",
        // or every life-only character would fire on a pool it does not have.
        var potion = new AutoPotion(Armed(o => o with { LifeMode = LifeFlaskMode.Either }));

        Assert.Equal(
            AutoPotionAction.None,
            potion.Tick(Frame(healthPercent: 100, manaPercent: 100, shieldPercent: 0), foreground: true, T0));

        // With a real shield pool, the same mode does trip on it.
        var shielded = new AutoPotion(Armed(o => o with { LifeMode = LifeFlaskMode.Either }));

        var action = shielded.Tick(
            Frame(healthPercent: 100, manaPercent: 100, shieldPercent: 10, maxShield: 100),
            foreground: true, T0);

        Assert.Equal(AutoPotionAction.Life, action);
    }

    // ---- cooldowns --------------------------------------------------------------

    [Fact]
    public void CooldownPreventsSpam()
    {
        var potion = new AutoPotion(Armed());

        var low = Frame(healthPercent: 40, manaPercent: 100);

        // Health stays low across a hundred frames. That is one press, not a
        // hundred: the threshold says WHETHER, the cooldown says HOW OFTEN.
        for (var i = 0; i < 100; i++) potion.Tick(low, foreground: true, T0.AddMilliseconds(i * 7));

        Assert.Equal(1, potion.LifePresses);
    }

    [Fact]
    public void CooldownAllowsLaterPress()
    {
        var potion = new AutoPotion(Armed());

        var low = Frame(healthPercent: 40, manaPercent: 100);

        potion.Tick(low, foreground: true, T0);
        potion.Tick(low, foreground: true, T0.AddMilliseconds(2499));

        Assert.Equal(1, potion.LifePresses);

        potion.Tick(low, foreground: true, T0.AddMilliseconds(2500));

        Assert.Equal(2, potion.LifePresses);
    }

    [Fact]
    public void LifeAndManaCooldownIndependent()
    {
        var potion = new AutoPotion(Armed());

        var low = Frame(healthPercent: 40, manaPercent: 10);

        Assert.Equal(AutoPotionAction.Life | AutoPotionAction.Mana, potion.Tick(low, foreground: true, T0));

        // Mana's cooldown is the shorter of the two, so it comes back first.
        var action = potion.Tick(low, foreground: true, T0.AddMilliseconds(2000));

        Assert.Equal(AutoPotionAction.Mana, action);
        Assert.Equal(1, potion.LifePresses);
        Assert.Equal(2, potion.ManaPresses);
    }

    // ---- the gates, every one of which fails closed -----------------------------

    [Fact]
    public void AutoFlaskDisabledNeverPresses()
    {
        var potion = new AutoPotion(new RadarSettings());

        Assert.False(new RadarSettings().AutoPotion.Enabled, "it has to ship disarmed");

        Assert.Equal(
            AutoPotionAction.None,
            potion.Tick(Frame(healthPercent: 1, manaPercent: 1), foreground: true, T0));
    }

    [Fact]
    public void F8KillSwitchDisablesInput()
    {
        var settings = Armed();
        var potion = new AutoPotion(settings);

        var low = Frame(healthPercent: 40, manaPercent: 100);

        Assert.Equal(AutoPotionAction.Life, potion.Tick(low, foreground: true, T0));

        // F8 down, then released and pressed again would toggle twice; one press
        // is one toggle.
        potion.PollKillSwitch(_ => true, T0.AddSeconds(3));

        Assert.False(settings.AutoPotion.Enabled);

        Assert.Equal(AutoPotionAction.None, potion.Tick(low, foreground: true, T0.AddSeconds(10)));
        Assert.Equal("OFF (F8)", potion.Status);
    }

    [Fact]
    public void TheKillSwitchTogglesBackOn()
    {
        var settings = Armed();
        var potion = new AutoPotion(settings);

        potion.PollKillSwitch(_ => true, T0);
        Assert.False(settings.AutoPotion.Enabled);

        // Held down is still one toggle; it takes a release first.
        potion.PollKillSwitch(_ => true, T0.AddSeconds(1));
        Assert.False(settings.AutoPotion.Enabled);

        potion.PollKillSwitch(_ => false, T0.AddSeconds(2));
        potion.PollKillSwitch(_ => true, T0.AddSeconds(3));

        Assert.True(settings.AutoPotion.Enabled);
    }

    [Fact]
    public void GameNotForegroundNeverPresses()
    {
        var potion = new AutoPotion(Armed());

        // Alt-tabbed, the keystroke lands in whatever the player is typing into.
        Assert.Equal(
            AutoPotionAction.None,
            potion.Tick(Frame(healthPercent: 1, manaPercent: 1), foreground: false, T0));

        Assert.Contains("foco", potion.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void NoAreaNeverPresses()
    {
        var potion = new AutoPotion(Armed());

        Assert.Equal(AutoPotionAction.None, potion.Tick(null, foreground: true, T0));
    }

    [Theory]
    // No maximum health: not read yet, or the offsets drifted.
    [InlineData(50, 0, 50, 100)]
    // Negative anything.
    [InlineData(-1, 100, 50, 100)]
    [InlineData(50, 100, -5, 100)]
    public void InvalidVitalsNeverPress(int health, int maxHealth, int mana, int maxMana)
    {
        var potion = new AutoPotion(Armed());

        var world = RadarFixture.World();

        var frame = RadarFixture.MapFrame(world) with
        {
            Vitals = new VitalsSnapshot(health, maxHealth, mana, maxMana, 0, 0),
        };

        Assert.Equal(AutoPotionAction.None, potion.Tick(frame, foreground: true, T0));
        Assert.Contains("ilegiveis", potion.Status, StringComparison.Ordinal);
    }

    // ---- dry run ----------------------------------------------------------------

    [Fact]
    public void DryRunNeverCallsSendInput()
    {
        var pressed = new List<ushort>();

        SendInputNative.Intercept = pressed.Add;

        try
        {
            var potion = new AutoPotion(Armed(o => o with { DryRun = true }));

            var action = potion.Tick(Frame(healthPercent: 10, manaPercent: 10), foreground: true, T0);

            // The whole decision runs — it says what it WOULD do — and nothing
            // reaches the keyboard.
            Assert.Equal(AutoPotionAction.Life | AutoPotionAction.Mana, action);
            Assert.Empty(pressed);
            Assert.Contains("WOULD_PRESS", potion.Status, StringComparison.Ordinal);
        }
        finally
        {
            SendInputNative.Intercept = null;
        }
    }

    [Fact]
    public void ARealPressSendsTheConfiguredKey()
    {
        var pressed = new List<ushort>();

        SendInputNative.Intercept = pressed.Add;

        try
        {
            var potion = new AutoPotion(Armed(o => o with { LifeKey = 0x35, ManaKey = 0x36 }));

            potion.Tick(Frame(healthPercent: 10, manaPercent: 10), foreground: true, T0);

            // The keys are configurable and the configured ones are what get
            // sent - not a hardcoded '1' and '2'.
            Assert.Equal(new ushort[] { 0x35, 0x36 }, pressed);
        }
        finally
        {
            SendInputNative.Intercept = null;
        }
    }

    // ---- persistence ------------------------------------------------------------

    [Fact]
    public void SettingsSurviveRestart()
    {
        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            var settings = new RadarSettings
            {
                AutoPotion = new AutoPotionSettings
                {
                    Enabled = true,
                    LifeThresholdPercent = 42f,
                    ManaThresholdPercent = 21f,
                    LifeKey = 0x35,
                    LifeMode = LifeFlaskMode.Either,
                    DryRun = true,
                },
            };

            SettingsStore.Save(settings, path);

            var reloaded = new RadarSettings();

            SettingsStore.Load(reloaded, path);

            Assert.True(reloaded.AutoPotion.Enabled);
            Assert.Equal(42f, reloaded.AutoPotion.LifeThresholdPercent, 1);
            Assert.Equal(21f, reloaded.AutoPotion.ManaThresholdPercent, 1);
            Assert.Equal(0x35, reloaded.AutoPotion.LifeKey);
            Assert.Equal(LifeFlaskMode.Either, reloaded.AutoPotion.LifeMode);

            // Arming a macro is not something a stale config file should do
            // quietly, so the simulation flag deliberately does not persist.
            Assert.False(reloaded.AutoPotion.DryRun);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempSettings() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"easyexile-autopotion-{Guid.NewGuid():N}.txt");
}
