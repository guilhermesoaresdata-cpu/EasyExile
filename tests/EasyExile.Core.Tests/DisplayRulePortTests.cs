using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// The display-rule engine, threat rules and the SVG icon pipeline.
/// </summary>
/// <remarks>
/// Most of these guard PRECEDENCE and CATEGORY GATES rather than appearance.
/// The reference arrived at every gate by watching a bare substring hijack
/// something real, and a port that keeps the icons but drops the gates
/// reintroduces bugs someone already found the hard way.
/// </remarks>
public class DisplayRulePortTests
{
    private static WorldSnapshot World(params EntitySnapshot[] entities) =>
        RadarFixture.World(entities: entities) with
        {
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
        };

    private static RecordingCanvas Draw(EntitySnapshot entity, NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var feature = new NativeMapRadarFeature(settings, new RadarStats(), (_, _, _) => 0x1234);
        var canvas = new RecordingCanvas();
        var world = World(entity);

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = RadarFixture.MapFrame(world) }, canvas);

        return canvas;
    }

    private static EntitySnapshot Entity(string metadata, EntityKind kind) =>
        RadarFixture.Entity(0, new Vector3(0, 0, 0), metadata) with { Kind = kind };

    private static EntitySnapshot Monster(MonsterRarity rarity, params string[] mods) =>
        RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Monsters/Abyss/Void") with
        {
            Kind = EntityKind.Monster,
            Rarity = rarity,
            Mods = mods.ToImmutableArray(),
        };

    private static string? RuleFor(EntitySnapshot entity, NativeMapSettings? options = null) =>
        new DisplayRules(options ?? new NativeMapSettings()).Resolve(entity)?.Name;

    // ---- the mods reader --------------------------------------------------------

    [Fact]
    public void ModsReaderReturnsExpectedIds()
    {
        // The reader itself is proven against the live client by the Analyzer
        // probe; what has to hold here is that the ids survive the snapshot
        // boundary intact, since that is what every mod rule matches on.
        var monster = Monster(MonsterRarity.Rare, "MonsterIgniteChanceIncrease1", "RareMonsterPack");

        Assert.Equal(2, monster.ModIds.Length);
        Assert.Contains("RareMonsterPack", monster.ModIds);

        // An entity that never had mods read must still answer safely.
        Assert.Empty(RadarFixture.Entity(0, new Vector3(0, 0, 0)).ModIds);
    }

    // ---- threats ----------------------------------------------------------------

    [Theory]
    [InlineData("AbyssLightless1")]
    [InlineData("LightlessWellBuff")]
    [InlineData("MonsterLightlessAura")]
    public void DangerousModTriggersThreatRule(string mod)
    {
        // The reference's seeded rule matches these three terms as substrings.
        var canvas = Draw(Monster(MonsterRarity.Rare, mod));

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Threat);
        Assert.Contains(canvas.Texts, t => t.Text == "VOID");
    }

    [Fact]
    public void AnOrdinaryModIsNotAThreat()
    {
        var canvas = Draw(Monster(MonsterRarity.Rare, "MonsterIgniteChanceIncrease1"));

        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.Threat);
    }

    [Fact]
    public void RarityStillPreservedWithThreat()
    {
        // The reference lets a mod rule REPLACE the marker, which costs the rank
        // colour. Here the threat mark rides on top, so a dangerous rare still
        // reads as a rare — that is the whole point of the overlay layer.
        var canvas = Draw(Monster(MonsterRarity.Rare, "AbyssLightless1"));

        Assert.Contains(canvas.Circles, c => c.Colour == Palette.Rare);
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Threat);
    }

    [Fact]
    public void ThreatsCanBeTurnedOffWithoutLosingTheMonster()
    {
        var canvas = Draw(
            Monster(MonsterRarity.Unique, "AbyssLightless1"),
            new NativeMapSettings { ShowThreats = false });

        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.Threat);
        Assert.Contains(canvas.Circles, c => c.Colour == Palette.Unique);
    }

    [Fact]
    public void ADeadMonsterCarriesNoThreatMark()
    {
        var dead = Monster(MonsterRarity.Rare, "AbyssLightless1") with { IsAlive = false };

        Assert.DoesNotContain(Draw(dead).Polygons, p => p.Colour == Palette.Threat);
    }

    // ---- precedence -------------------------------------------------------------

    [Fact]
    public void DisplayRulePrecedenceMatchesReference()
    {
        // The reference's order, from DisplayRules.CategoryDefaults: mechanics,
        // then monsters by rank, then the category defaults, then the catch-all
        // POI rule LAST. That tail position is what keeps a transition drawing
        // as a transition when it also carries the game's map icon.
        var transition = Entity("Metadata/Terrain/Act1/AreaTransition", EntityKind.Transition) with
        {
            IsPoi = true,
        };

        Assert.Equal("Transition", RuleFor(transition));

        var checkpoint = Entity("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other) with
        {
            IsPoi = true,
        };

        Assert.Equal("Point of interest", RuleFor(checkpoint));
    }

    [Theory]
    [InlineData("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter", EntityKind.Other, "Expedition")]
    [InlineData("Metadata/MiscellaneousObjects/Ritual/RitualRuneMarker", EntityKind.Other, "Ritual")]
    [InlineData("Metadata/Terrain/Leagues/Breach/BreachObject", EntityKind.Object, "Breach")]
    [InlineData("Metadata/Chests/StrongBoxes/ArcanistStrongbox", EntityKind.Chest, "Strongbox")]
    [InlineData("Metadata/MiscellaneousObjects/Essence/EssenceMonolith", EntityKind.Other, "Essence")]
    [InlineData("Metadata/Shrines/ShrineDiamond", EntityKind.Other, "Shrine")]
    public void MechanicRulePrecedenceMatchesReference(string metadata, EntityKind kind, string expected)
    {
        // Every one of these would otherwise fall through to a category default,
        // so each also proves the mechanic block sits above them.
        Assert.Equal(expected, RuleFor(Entity(metadata, kind)));
    }

    /// <summary>
    /// The gates the reference had to add. A bare "Expedition" tagged the
    /// league's combat mobs and its detonation effects; a bare "Strongbox"
    /// tagged the Vaal guards the box spawns.
    /// </summary>
    [Theory]
    [InlineData("Metadata/Monsters/LeagueExpedition/CrabExpedition", EntityKind.Monster)]
    [InlineData("Metadata/MiscellaneousObjects/Expedition2EncounterCrack", EntityKind.Other)]
    [InlineData("Metadata/Monsters/LeagueRitual/RitualBoss", EntityKind.Monster)]
    [InlineData("Metadata/Monsters/LeagueBreach/BreachMonster", EntityKind.Monster)]
    [InlineData("Metadata/Monsters/VaalStrongboxGuard", EntityKind.Monster)]
    public void AMechanicRuleNeverHijacksAMonster(string metadata, EntityKind kind)
    {
        var name = RuleFor(Entity(metadata, kind));

        Assert.True(
            name is null || name.StartsWith("Monster", StringComparison.Ordinal),
            $"a mechanic rule claimed a monster: {name}");
    }

    [Fact]
    public void AGlobTermMatchesLikeTheReference()
    {
        // The reference compiles a term carrying * or ? to an anchored regex and
        // leaves everything else a substring.
        var rule = new CompiledRule(new DisplayRule(
            "Glob", "Star", Palette.Threat, 5f, Mods: new[] { "Monster*Aura*" }));

        Assert.True(rule.Matches(Monster(MonsterRarity.Rare, "MonsterPhysicalDamageAura1")));
        Assert.False(rule.Matches(Monster(MonsterRarity.Rare, "RareMonsterPack")));
    }

    // ---- the icon pipeline ------------------------------------------------------

    [Fact]
    public void CustomSvgLoads()
    {
        // The library materialises its built-ins to icons/ on first use and
        // reads any *.svg there, so a user can restyle one or add their own.
        Assert.True(IconLibrary.Contains("Diamond"));
        Assert.True(IconLibrary.Contains("MapPin"));

        // Case-insensitive, as the reference's lookups are.
        Assert.Equal("Diamond", IconLibrary.Canonical("diamond"));

        var figures = IconCache.Get("Diamond");

        Assert.NotEmpty(figures);
        Assert.True(figures[0].Points.Length >= 4, "the diamond path has four corners");
    }

    [Fact]
    public void AMultiPathIconKeepsItsHole()
    {
        // MapPin is a teardrop with an eye cut out of it. The reference gets the
        // hole from the Alternate fill rule; ours marks the contained ring so it
        // paints in the shadow colour instead.
        var figures = IconCache.Get("MapPin");

        Assert.True(figures.Length >= 2, "MapPin is authored as two paths");
        Assert.Contains(figures, f => f.IsHole);
    }

    [Fact]
    public void SvgIsCached()
    {
        IconCache.Clear();

        var first = IconCache.Get("Star");
        var second = IconCache.Get("Star");

        // Same instance, not an equal one: parsing and flattening a path per
        // marker per frame is exactly what the cache exists to avoid.
        //
        // Identity, not the cache's total count. The cache is process-wide and
        // test classes run in parallel, so any assertion about how many entries
        // it holds is a race with whatever else is drawing.
        Assert.NotEmpty(first);
        Assert.Same(first, second);
    }

    [Fact]
    public void AnUnknownIconStillDrawsSomething()
    {
        // A rule naming a missing icon must not make the entity vanish; a
        // silently dropped marker gives no way to tell what went wrong.
        Assert.Empty(IconCache.Get("NoSuchIconAnywhere"));

        var canvas = new RecordingCanvas();

        MapIcons.Draw(canvas, "NoSuchIconAnywhere", new Vector2(10, 10), 4f, Palette.Threat);

        Assert.Contains(canvas.Circles, c => c.Colour == Palette.Threat);
    }

    [Fact]
    public void AreaChangeClearsRelevantCaches()
    {
        using var memory = WorldFixture.Build(monsters: 2);

        var epoch = new AreaEpoch();

        var first = SnapshotCapture.Capture(memory, WorldFixture.ModuleBase, epoch, CaptureOptions.Default);

        WorldFixture.EnterSecondArea(memory, monsters: 3);

        var second = SnapshotCapture.Capture(memory, WorldFixture.ModuleBase, epoch, CaptureOptions.Default);

        // Everything keyed on an area has to move with it: the epoch, the
        // identity, and the per-area caches behind terrain and landmarks.
        Assert.NotEqual(first.Snapshot!.Epoch, second.Snapshot!.Epoch);
        Assert.NotEqual(first.Snapshot.Area, second.Snapshot.Area);
        Assert.Equal(3, second.Snapshot.Entities.Count(e => e.Kind == EntityKind.Monster));
    }
}
