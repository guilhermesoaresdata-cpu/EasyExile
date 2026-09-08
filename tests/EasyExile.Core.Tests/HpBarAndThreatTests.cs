using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Features.HpBars;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.HpBars;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// Monster threats and the health bars over them.
/// </summary>
public class HpBarAndThreatTests
{
    // ---- threats ----------------------------------------------------------------

    private static EntitySnapshot Monster(
        MonsterRarity rarity = MonsterRarity.Rare, int life = 100, int maxLife = 100, params string[] mods) =>
        RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Monsters/Abyss/Void") with
        {
            Kind = EntityKind.Monster,
            Rarity = rarity,
            Mods = mods.ToImmutableArray(),
            Vitals = new[] { new Vital("Health", 239, life, maxLife) }.ToImmutableArray(),
            IsAlive = life > 0,
        };

    [Theory]
    [InlineData("AbyssLightless1")]
    [InlineData("LightlessWellBuff")]
    [InlineData("MonsterLightlessAura")]
    public void KnownThreatModMatches(string mod)
    {
        var rules = new DisplayRules(new NativeMapSettings());

        Assert.NotNull(rules.ResolveOverlay(Monster(mods: mod)));
    }

    [Theory]
    [InlineData("MonsterIgniteChanceIncrease1")]
    [InlineData("RareMonsterPack")]
    [InlineData("MonsterNoDropsOrExperience")]
    public void UnknownModDoesNotMatch(string mod)
    {
        var rules = new DisplayRules(new NativeMapSettings());

        Assert.Null(rules.ResolveOverlay(Monster(mods: mod)));
    }

    [Fact]
    public void ThreatRuleFriendlyName()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        var threat = rules.ResolveOverlay(Monster(mods: "AbyssLightless1"));

        // The reference's own seeded rule, name and label included.
        Assert.Equal("Abyss Lightless (Void)", threat!.Name);
        Assert.Equal("VOID", threat.Label);
    }

    [Fact]
    public void InvalidModsFailClosed()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        // No mods read yet, and mods that are not this rule's. Neither is a
        // threat, and neither may be guessed into one.
        Assert.Null(rules.ResolveOverlay(RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Monster,
        }));

        Assert.Null(rules.ResolveOverlay(Monster(mods: string.Empty)));
    }

    [Fact]
    public void MechanicPrecedenceStillWorks()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        var device = RadarFixture.Entity(
            0, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter")
            with { Kind = EntityKind.Other };

        Assert.Equal("Expedition", rules.Resolve(device)?.Name);
    }

    [Fact]
    public void RarityStillPreservedWithThreat()
    {
        var rules = new DisplayRules(new NativeMapSettings());

        var dangerous = Monster(mods: "AbyssLightless1");

        // The base rule is still the rank's, in the rank's colour. The threat is
        // an overlay ON it, not a replacement FOR it.
        Assert.Equal(Palette.Rare, rules.Resolve(dangerous)!.Colour);
        Assert.NotNull(rules.ResolveOverlay(dangerous));
    }

    // ---- health bars ------------------------------------------------------------

    private static WorldSnapshot World(params EntitySnapshot[] entities) =>
        RadarFixture.World(entities: entities);

    private static RecordingCanvas Draw(
        EntitySnapshot entity, HpBarSettings? options = null, CameraSnapshot? camera = null)
    {
        var settings = new RadarSettings { HpBars = options ?? new HpBarSettings() };
        var feature = new MonsterHpBars(settings, new RadarStats());
        var canvas = new RecordingCanvas();

        var world = World(entity) with { Camera = camera ?? RadarFixture.Camera() };

        feature.Draw(RadarFixture.Frame(world), canvas);

        return canvas;
    }

    /// <summary>A bar is a background rectangle plus a fill, so two at minimum.</summary>
    private static int Bars(RecordingCanvas canvas) => canvas.Rects;

    [Fact]
    public void MonsterWithLifeProducesBar()
    {
        Assert.True(Bars(Draw(Monster(life: 50))) >= 2);
    }

    [Fact]
    public void DeadMonsterNoBar()
    {
        // A bar over a corpse is a lie about where the fight is.
        Assert.Equal(0, Bars(Draw(Monster(life: 0))));
    }

    [Theory]
    // No maximum: not read, or the offsets drifted. There is no fraction to draw.
    [InlineData(50, 0)]
    [InlineData(0, 100)]
    public void InvalidLifeNoBar(int life, int maxLife)
    {
        Assert.Equal(0, Bars(Draw(Monster(life: life, maxLife: maxLife))));
    }

    [Fact]
    public void BehindCameraNoBar()
    {
        Assert.Equal(0, Bars(Draw(Monster(), camera: RadarFixture.CameraFacingAway())));
    }

    [Fact]
    public void OutsideDistanceNoBar()
    {
        // The reference's only cull is the screen — it has no radius, so neither
        // does this. A monster far enough away projects off the client area.
        var distant = Monster() with { WorldPosition = new Vector3(500_000, 0, 0) };

        Assert.Equal(0, Bars(Draw(distant)));
    }

    [Theory]
    [InlineData(EntityKind.Npc)]
    [InlineData(EntityKind.Chest)]
    [InlineData(EntityKind.Ally)]
    [InlineData(EntityKind.Transition)]
    public void OnlyMonstersGetBars(EntityKind kind)
    {
        var other = Monster() with { Kind = kind };

        Assert.Equal(0, Bars(Draw(other)));
    }

    [Fact]
    public void RareStyleApplied()
    {
        // Fill follows the dot colour, so a rare's bar and a rare's marker are
        // the same yellow.
        var canvas = Draw(Monster(MonsterRarity.Rare, life: 90));

        Assert.Contains(canvas.RectColours, c => c == Palette.Rare);
    }

    [Fact]
    public void UniqueStyleApplied()
    {
        var canvas = Draw(Monster(MonsterRarity.Unique, life: 90));

        Assert.Contains(canvas.RectColours, c => c == Palette.Unique);

        // Rank drives width too: a unique's bar is wider than a magic's.
        var options = new HpBarSettings();

        Assert.True(options.WidthFor(MonsterRarity.Unique) > options.WidthFor(MonsterRarity.Magic));
        Assert.True(options.BorderFor(MonsterRarity.Rare) > options.BorderFor(MonsterRarity.Normal));
    }

    [Fact]
    public void LowHealthTurnsTheBarRed()
    {
        // The reference's rule: below 30% the fill goes red whatever the rank is,
        // because "nearly dead" is what you need at a glance.
        var healthy = Draw(Monster(MonsterRarity.Unique, life: 90));
        var dying = Draw(Monster(MonsterRarity.Unique, life: 10));

        Assert.DoesNotContain(healthy.RectColours, c => c == Palette.Monster);
        Assert.Contains(dying.RectColours, c => c == Palette.Monster);

        // The border stays the rank's, so the bar never stops saying what it is.
        Assert.Contains(dying.RectColours, c => c == Palette.Unique);
    }

    [Fact]
    public void ARankCanBeTurnedOff()
    {
        var canvas = Draw(Monster(MonsterRarity.Rare), new HpBarSettings { ShowRare = false });

        Assert.Equal(0, Bars(canvas));

        // Normal monsters are off by default, and the others are not.
        Assert.False(new HpBarSettings().ShowNormal);
        Assert.True(new HpBarSettings().ShowRare);
    }

    [Fact]
    public void ThreatIndicatorDoesNotReplaceRarity()
    {
        var canvas = Draw(Monster(MonsterRarity.Rare, life: 90, maxLife: 100, mods: "AbyssLightless1"));

        // Both facts survive: the bar is still the rank's colour, and the threat
        // mark sits beside it.
        Assert.Contains(canvas.RectColours, c => c == Palette.Rare);
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Threat);
    }
}
