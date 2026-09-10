using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;
using EasyExile.Radar.Settings.Player;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.Settings.Levelling;

namespace EasyExile.Core.Tests;

/// <summary>
/// Area identity, settings persistence and the navigation default.
/// </summary>
public class SettingsAndAreaTests
{
    // ---- a fresh install's defaults ------------------------------------------

    [Fact]
    public void A_fresh_install_speaks_English()
    {
        // Portuguese is what this was written in, and English is what a wider
        // audience than one language reads. An install that already has a
        // settings.txt keeps whatever it had - this is only what a NEW one
        // starts as.
        Assert.Equal(Language.English, new GeneralSettings().Language);
    }

    [Fact]
    public void A_fresh_install_starts_with_levelling_off()
    {
        // The guide has not been told what to build yet, and it still says BETA
        // in its own tab. Opting in is the player's decision, not the default
        // a new install makes for them.
        Assert.False(new LevellingSettings().Enabled);
    }

    // ---- the area bug -----------------------------------------------------------

    [Fact]
    public void ReusingAnAreaAddressStillCountsAsANewArea()
    {
        // Reported live: walked to another zone and the map did not change. The
        // client had handed back the SAME AreaInstance allocation, so an epoch
        // watching only the address never moved and every per-area cache — the
        // terrain most visibly — kept answering for the zone that had been left.
        var epoch = new AreaEpoch();

        var first = epoch.Observe(0x1000, "G2_5_1");
        var same = epoch.Observe(0x1000, "G2_5_1");
        var elsewhere = epoch.Observe(0x1000, "G1_2");

        Assert.Equal(first, same);
        Assert.True(elsewhere > first, "a different zone at the same address must move the epoch");
    }

    [Fact]
    public void AMovedAddressStillCountsWithoutACode()
    {
        // The code is a second signal, not a replacement: when it cannot be read
        // the address decides alone, as it always did.
        var epoch = new AreaEpoch();

        var first = epoch.Observe(0x1000);
        var moved = epoch.Observe(0x2000);
        var again = epoch.Observe(0x2000, null);

        Assert.True(moved > first, "a different address must move the epoch");
        Assert.Equal(moved, again);
    }

    [Fact]
    public void ANewAreaGetsItsOwnTerrainTexture()
    {
        // The bug this guards cost two rounds of guessing. The capture was right
        // all along: the epoch moved, the terrain re-read, the dimensions
        // changed. What did not change was the TEXTURE — the overlay's table is
        // keyed by name, AddOrGetImagePointer returns whatever is already
        // registered under one, and the name was a constant. Every area after
        // the first drew the first one's map.
        var uploaded = new List<string>();

        var texture = new TerrainTexture((name, _, _) =>
        {
            uploaded.Add(name);

            return 0x1000 + uploaded.Count;
        });

        var first = new TerrainSnapshot(new byte[4 * 4], 4, 4);
        var second = new TerrainSnapshot(new byte[8 * 8], 8, 8);

        // No exploration map: the area epoch is the only thing that should
        // rebuild here, which is what this test is about.
        var paint = new TerrainPaint(0, 0, null);

        texture.Ensure(1, first, paint);
        texture.Ensure(1, first, paint);
        texture.Ensure(2, second, paint);

        Assert.Equal(2, texture.Builds);
        Assert.Equal(2, uploaded.Count);
        Assert.Equal(uploaded[0], uploaded.Distinct().First());
        Assert.NotEqual(uploaded[0], uploaded[1]);
    }

    // ---- settings persistence ---------------------------------------------------

    [Fact]
    public void SettingsSurviveARestart()
    {
        var saved = new RadarSettings
        {
            NativeMap = new NativeMapSettings
            {
                ShowMagicMonsters = false,
                ShowLandmarks = false,
                TerrainOpacity = 0.42f,
            },
        };

        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            SettingsStore.Save(saved, path);

            // A fresh tree, as a restart produces, with the file applied over it.
            var loaded = new RadarSettings();

            Assert.True(loaded.NativeMap.ShowMagicMonsters, "the default has to differ, or this proves nothing");

            SettingsStore.Load(loaded, path);

            Assert.False(loaded.NativeMap.ShowMagicMonsters);
            Assert.False(loaded.NativeMap.ShowLandmarks);
            Assert.Equal(0.42f, loaded.NativeMap.TerrainOpacity, 3);

            // And an option that was never touched keeps its default rather than
            // being zeroed by the round trip.
            Assert.True(loaded.NativeMap.ShowMonsters);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUnknownSettingIsSkippedRatherThanFatal()
    {
        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            // A file from an older build should cost you the options that moved,
            // not the ones that did not.
            File.WriteAllLines(path, new[]
            {
                "NativeMap.ShowChests=0",
                "NativeMap.SomethingThatMovedAway=1",
                "garbage without an equals sign",
                "Nonsense.Category=1",
            });

            var settings = new RadarSettings();

            SettingsStore.Load(settings, path);

            Assert.False(settings.NativeMap.ShowChests);
            Assert.True(settings.NativeMap.ShowTransitions);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SettingsAreWrittenWhenTheyChangeNotOnlyOnExit()
    {
        // Reported: settings kept resetting. They were only written on a clean
        // shutdown, and an overlay gets its console closed or its process killed
        // far more often than it is closed politely — so the file had never once
        // been created.
        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            var settings = new RadarSettings();

            SettingsStore.Load(settings, path);
            SettingsStore.SaveIfChanged(settings, path);

            Assert.True(File.Exists(path), "the first check must write the file");

            var written = File.GetLastWriteTimeUtc(path);

            // Nothing changed: nothing written. This runs every second, so it
            // has to be free when the answer is no.
            SettingsStore.SaveIfChanged(settings, path);

            Assert.Equal(written, File.GetLastWriteTimeUtc(path));

            settings.General = settings.General with { PanelX = 640f, PanelY = 128f };

            SettingsStore.SaveIfChanged(settings, path);

            var reloaded = new RadarSettings();

            SettingsStore.Load(reloaded, path);

            Assert.Equal(640f, reloaded.General.PanelX, 1);
            Assert.Equal(128f, reloaded.General.PanelY, 1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AChosenModeSurvivesARestart()
    {
        // The anchor is an enum, and enums were not in the supported set — so
        // this one choice reset every launch no matter what the autosave did.
        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            var settings = new RadarSettings
            {
                Player = new PlayerSettings { Anchor = PlayerAnchor.AtCharacter },
            };

            SettingsStore.Save(settings, path);

            var reloaded = new RadarSettings();

            Assert.Equal(PlayerAnchor.FixedHud, reloaded.Player.Anchor);

            SettingsStore.Load(reloaded, path);

            Assert.Equal(PlayerAnchor.AtCharacter, reloaded.Player.Anchor);

            // Stored by name, so reordering the enum cannot turn one saved
            // choice into another.
            Assert.Contains("Player.Anchor=AtCharacter", File.ReadAllText(path), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void TheFixedHudIsTheDefault()
    {
        Assert.Equal(PlayerAnchor.FixedHud, new RadarSettings().Player.Anchor);
    }

    // ---- the navigation default -------------------------------------------------

    [Fact]
    public void TheLeagueMechanicIsTheDefaultDestination()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        var monolith = RadarFixture.Entity(
            0, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter")
            with { Kind = EntityKind.Other, IsPoi = true };

        // It is what the area is entered for, so arriving with it already routed
        // is the useful default.
        Assert.True(rules.IsNavigable(monolith));
    }

    [Fact]
    public void ExitsAreNotAutoRoutedByDefault()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        var exit = RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Terrain/AreaTransition")
            with { Kind = EntityKind.Transition };

        Assert.False(rules.IsNavigable(exit));

        Assert.True(new DisplayRules(new NativeMapSettings { AutoRouteExits = true }).IsNavigable(exit));
    }

    private static string TempSettings() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"easyexile-settings-{Guid.NewGuid():N}.txt");
}
