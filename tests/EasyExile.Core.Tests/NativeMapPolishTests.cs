using System.Reflection;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// The two things this pass was for: the map moves at render rate, and it stops
/// drawing everything the game happens to have loaded.
/// </summary>
public class NativeMapPolishTests
{
    private static (NativeMapRadarFeature Feature, RadarStats Stats, RadarSettings Settings) Map(
        NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var stats = new RadarStats();

        // No device in a test, so the texture upload hands back a fake handle.
        // The terrain quad still goes through the canvas.
        return (new NativeMapRadarFeature(settings, stats, (_, _, _) => 0x1234), stats, settings);
    }

    private static WorldSnapshot World(params EntitySnapshot[] entities) =>
        RadarFixture.World(entities: entities) with
        {
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
        };

    private static RenderFrame Frame(WorldSnapshot world, MapFrameSnapshot map) =>
        RadarFixture.Frame(world) with { MapFrame = map };

    /// <summary>Where the monster marker landed, by its own colour.</summary>
    private static Vector2 Monster(RecordingCanvas canvas) =>
        canvas.Circles.First(c => c.Colour == Palette.Monster).At;

    // ---- the temporal model ---------------------------------------------------

    [Fact]
    public void WorldSnapshotDoesNotStoreFinalScreenCoordinates()
    {
        // The moment a screen position is cached in the slow snapshot, the map
        // moves at capture rate. Everything positional it carries is world or
        // grid space.
        foreach (var property in typeof(EntitySnapshot).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.DoesNotContain("Screen", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pixel", property.Name, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains(typeof(EntitySnapshot).GetProperties(), p => p.Name == "GridPosition");
    }

    [Fact]
    public void PlayerMovementChangesProjectionWithoutANewWorldSnapshot()
    {
        var world = World(RadarFixture.Entity(0, new Vector3(500, 500, 0)) with { Kind = EntityKind.Monster });

        var (feature, _, _) = Map();

        var before = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world, playerGrid: new Vector2(0, 0))), before);

        var after = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world, playerGrid: new Vector2(40, 0))), after);

        // Same world snapshot, different player: the monster marker has to move.
        // The player's own marker never does — it is the centre by construction.
        Assert.NotEqual(Monster(before), Monster(after));
    }

    [Fact]
    public void ShiftChangesProjectionWithoutANewWorldSnapshot()
    {
        var world = World(RadarFixture.Entity(0, new Vector3(500, 500, 0)) with { Kind = EntityKind.Monster });
        var (feature, _, _) = Map();

        var before = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), before);

        var after = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world, shiftX: 120f, shiftY: 60f)), after);

        Assert.Equal(120f, Monster(after).X - Monster(before).X, 2);
        Assert.Equal(60f, Monster(after).Y - Monster(before).Y, 2);
    }

    [Fact]
    public void ZoomChangesProjectionWithoutANewWorldSnapshot()
    {
        var world = World(RadarFixture.Entity(0, new Vector3(500, 500, 0)) with { Kind = EntityKind.Monster });
        var (feature, _, _) = Map();

        var near = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world, zoom: 0.5f)), near);

        var far = new RecordingCanvas();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world, zoom: 1.0f)), far);

        var centre = new Vector2(RadarFixture.Client.Width / 2f, (RadarFixture.Client.Height / 2f) - 20f);

        var nearOffset = Monster(near).X - centre.X;
        var farOffset = Monster(far).X - centre.X;

        // Twice the zoom, twice the distance from the map centre.
        Assert.Equal(nearOffset * 2f, farOffset, 2);
    }

    [Fact]
    public void MapFrameAndWorldAreaMustMatch()
    {
        // The world describes a real area; the fast frame describes another. The
        // entities in hand belong to a place the player has left.
        var world = World(RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Monster })
            with { Area = RadarFixture.RealArea };

        var elsewhere = RadarFixture.MapFrame(world) with { Area = default };

        var (feature, _, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, elsewhere), canvas);

        Assert.Equal(0, canvas.Drawn);
        Assert.Contains("area", feature.Status, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerrainDoesNotRebuildWhenThePlayerMoves()
    {
        var world = World();
        var (feature, _, _) = Map();

        for (var step = 0; step < 12; step++)
        {
            feature.Draw(
                Frame(world, RadarFixture.MapFrame(world, playerGrid: new Vector2(step * 5, step * 3))),
                new RecordingCanvas());
        }

        // One build for the area, and nothing after that. Rebuilding several
        // million cells per step is the difference between a map and a stutter.
        Assert.Equal(1, feature.TerrainBuilds);
    }

    // ---- filtering -------------------------------------------------------------

    [Fact]
    public void UnknownEntitiesAreHiddenByDefault()
    {
        var world = World(
            RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/Something") with
            {
                Kind = EntityKind.Other,
            });

        var (feature, stats, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        Assert.Equal(1, stats.MapEntitiesIgnored);
        Assert.Equal(0, stats.MapEntitiesDrawn);
    }

    [Fact]
    public void RawEntityDebugIsOptIn()
    {
        var entity = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Other };
        var world = World(entity);

        Assert.False(new NativeMapSettings().ShowRawEntities);

        var (feature, stats, _) = Map(new NativeMapSettings { ShowRawEntities = true });

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), new RecordingCanvas());

        Assert.Equal(1, stats.MapEntitiesDrawn);
    }

    [Fact]
    public void DeadEnemiesAreHidden()
    {
        var alive = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Monster, IsAlive = true };
        var dead = RadarFixture.Entity(1, new Vector3(10, 10, 0)) with { Kind = EntityKind.Monster, IsAlive = false };

        var world = World(alive, dead);

        var (feature, stats, _) = Map();
        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), new RecordingCanvas());

        Assert.Equal(1, stats.MapEntitiesDrawn);
        Assert.Equal(1, stats.MapEntitiesIgnored);
    }

    [Fact]
    public void UsefulCategoriesRender()
    {
        var world = World(
            RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Monster },
            RadarFixture.Entity(1, new Vector3(10, 0, 0)) with { Kind = EntityKind.Npc },
            // Marked, because a real chest is: the client puts an icon on the
            // ones worth opening and leaves the scenery pots alone.
            RadarFixture.Entity(2, new Vector3(20, 0, 0)) with { Kind = EntityKind.Chest, IsPoi = true },
            RadarFixture.Entity(3, new Vector3(30, 0, 0)) with { Kind = EntityKind.Transition },
            RadarFixture.Entity(4, new Vector3(40, 0, 0)) with { Kind = EntityKind.OtherPlayer },
            RadarFixture.Entity(5, new Vector3(50, 0, 0)) with { Kind = EntityKind.Object, IsPoi = true });

        var (feature, stats, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        Assert.Equal(6, stats.MapEntitiesDrawn);

        // Six categories, six distinct colours: the map is meant to be read at a
        // glance, not decoded.
        var colours = new HashSet<uint>();

        foreach (var circle in canvas.Circles) colours.Add(circle.Colour);
        foreach (var polygon in canvas.Polygons) colours.Add(polygon.Colour);
        foreach (var rect in canvas.RectColours) colours.Add(rect);

        colours.Remove(Palette.Shadow);

        Assert.True(colours.Count >= 6, $"only {colours.Count} distinct marker colours");
    }

    // ---- the classifier --------------------------------------------------------

    [Theory]
    [InlineData("Metadata/Monsters/NPC/Alva", EntityKind.Npc)]
    [InlineData("Metadata/Monsters/Skeleton/Skeleton1", EntityKind.Monster)]
    [InlineData("Metadata/Monsters/PlayerSummoned/SummonedSkeleton", EntityKind.Ally)]
    [InlineData("Metadata/Monsters/Totems/RejuvenationTotem", EntityKind.Ally)]
    [InlineData("Metadata/Monsters/MonsterMods/AuraDaemon", EntityKind.Junk)]
    [InlineData("Metadata/Chests/StrongBox", EntityKind.Chest)]
    [InlineData("Metadata/Chests/BreakableUrn", EntityKind.Other)]
    [InlineData("Metadata/MiscellaneousObjects/AreaTransition", EntityKind.Transition)]
    [InlineData("Metadata/Effects/Attachments/Glow", EntityKind.Junk)]
    [InlineData("Metadata/Monsters/Daemon/InvisibleThing", EntityKind.Junk)]
    // Object, not junk: POE2Radar's Categorize sends /Terrain/ to Object, and
    // that is where the client files waypoints, seals and portals.
    [InlineData("Metadata/Terrain/Blank", EntityKind.Object)]
    public void TheClassifierFollowsTheReferenceOrdering(string metadata, EntityKind expected)
    {
        Assert.Equal(expected, EntityClassifier.Classify(metadata));
    }

    [Fact]
    public void TheCompanionFollowingYouIsAnAllyAndNotAThreat()
    {
        // The one that was always sitting next to the player blip, drawn red.
        // Its metadata says nothing about being yours — it is the DiesAfterTime
        // component that separates a temporary summon from a monster.
        const string companion = "Metadata/Monsters/MarakethSamdDjinn/SandDjinn@22";

        Assert.Equal(EntityKind.Monster, EntityClassifier.Classify(companion));

        Assert.Equal(EntityKind.Ally, EntityClassifier.Classify(
            companion, componentNames: new[] { "Monster", "Positioned", EntityClassifier.TemporarySummonComponent }));
    }

    [Fact]
    public void ReactionOverridesTheComponentHeuristic()
    {
        const string companion = "Metadata/Monsters/MarakethSamdDjinn/SandDjinn@22";

        // The client saying "this one is yours" beats any guess from components.
        Assert.Equal(EntityKind.Ally, EntityClassifier.Classify(companion, isFriendly: true));

        // And saying "this one is not" beats the DiesAfterTime shortcut, which
        // would otherwise paint an enemy necromancer's summons green.
        Assert.Equal(EntityKind.Monster, EntityClassifier.Classify(
            "Metadata/Monsters/Skeletons/EnemySummoned@9",
            componentNames: new[] { EntityClassifier.TemporarySummonComponent },
            isFriendly: false));
    }

    [Fact]
    public void SkillEffectsAreNotEntities()
    {
        // A single Firewall put fourteen markers on the map. PoE files skill
        // effects as monsters; they are your own spells, not information.
        Assert.Equal(EntityKind.Junk, EntityClassifier.Classify("Metadata/Monsters/Anomalies/Firewall"));
    }

    /// <summary>
    /// The marker itself carries the rank: blue is magic, yellow is rare, orange
    /// is unique, red is an ordinary monster. Same convention the game already
    /// uses on item names and monster health bars, so there is nothing to learn.
    /// </summary>
    [Theory]
    [InlineData(MonsterRarity.Unknown)]
    [InlineData(MonsterRarity.Normal)]
    [InlineData(MonsterRarity.Magic)]
    [InlineData(MonsterRarity.Rare)]
    [InlineData(MonsterRarity.Unique)]
    public void TheMarkerColourIsTheRank(MonsterRarity rarity)
    {
        var canvas = DrawEnemy(rarity);

        Assert.Contains(canvas.Circles, c => c.Colour == RankColour(rarity));
    }

    [Fact]
    public void NoRankIsDrawnWithARing()
    {
        // Rings were tried and rejected: at map scale a two-pixel annulus around
        // a three-pixel dot is mud, not information.
        foreach (var rarity in AllRanks) Assert.Empty(DrawEnemy(rarity).Outlines);
    }

    [Fact]
    public void EveryRankIsAColourOfItsOwn()
    {
        var colours = new HashSet<uint>();

        foreach (var rarity in AllRanks) colours.Add(RankColour(rarity));

        Assert.Equal(AllRanks.Length, colours.Count);
    }

    /// <summary>
    /// Size rises with rank as well as colour, so a unique is still the marker
    /// that reads first in the middle of the pack it is standing in.
    /// </summary>
    [Fact]
    public void TheRankHierarchyIsVisual()
    {
        float RadiusOf(MonsterRarity rarity) =>
            DrawEnemy(rarity).Circles.First(c => c.Colour == RankColour(rarity)).Radius;

        var normal = RadiusOf(MonsterRarity.Normal);
        var magic = RadiusOf(MonsterRarity.Magic);
        var rare = RadiusOf(MonsterRarity.Rare);
        var unique = RadiusOf(MonsterRarity.Unique);

        Assert.True(magic > normal, "a magic monster must outrank a normal one");
        Assert.True(rare > magic, "a rare monster must outrank a magic one");
        Assert.True(unique > rare, "a unique must outrank everything");
    }

    /// <summary>Uniques go down last, so a boss is never buried under its pack.</summary>
    [Fact]
    public void RarerEnemiesAreDrawnOnTop()
    {
        var world = World(
            RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
            {
                Kind = EntityKind.Monster, Rarity = MonsterRarity.Unique,
            },
            RadarFixture.Entity(1, new Vector3(1, 0, 0)) with
            {
                Kind = EntityKind.Monster, Rarity = MonsterRarity.Normal,
            });

        var (feature, _, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        var normal = canvas.Circles.FindIndex(c => c.Colour == Palette.Monster);
        var unique = canvas.Circles.FindIndex(c => c.Colour == Palette.Unique);

        // The snapshot lists the unique first; the draw order has to override
        // that, not follow it.
        Assert.True(unique > normal, "the unique has to be painted after the normal monster");
    }

    [Theory]
    [InlineData(MonsterRarity.Rare)]
    [InlineData(MonsterRarity.Unique)]
    public void ADeadEnemyIsHiddenWhateverItsRank(MonsterRarity rarity)
    {
        // A dead boss pinned to the map is a lie about where the danger is.
        var canvas = DrawEnemy(rarity, alive: false);

        Assert.DoesNotContain(canvas.Circles, c => c.Colour == RankColour(rarity));
    }

    [Fact]
    public void RarityDoesNotChangeTheClassification()
    {
        // Rank decides how a monster is painted. It never decides whether the
        // thing is a monster — that is the classifier's job, and mixing the two
        // is how a rare chest ends up drawn as a boss.
        foreach (var rarity in AllRanks)
        {
            var entity = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
            {
                Kind = EntityKind.Monster,
                Rarity = rarity,
            };

            Assert.Equal(EntityKind.Monster, entity.Kind);
            Assert.True(entity.IsInteresting);
        }
    }

    [Theory]
    [InlineData(MonsterRarity.Magic)]
    [InlineData(MonsterRarity.Rare)]
    [InlineData(MonsterRarity.Unique)]
    public void ARankCanBeTurnedOffOnItsOwn(MonsterRarity rarity)
    {
        var options = rarity switch
        {
            MonsterRarity.Magic => new NativeMapSettings { ShowMagicMonsters = false },
            MonsterRarity.Rare => new NativeMapSettings { ShowRareMonsters = false },
            _ => new NativeMapSettings { ShowUniqueMonsters = false },
        };

        Assert.DoesNotContain(DrawEnemy(rarity, options: options).Circles, c => c.Colour == RankColour(rarity));

        // And turning one off leaves the others alone.
        Assert.Contains(DrawEnemy(MonsterRarity.Normal, options: options).Circles, c => c.Colour == Palette.Monster);
    }

    private static readonly MonsterRarity[] AllRanks =
    {
        MonsterRarity.Normal, MonsterRarity.Magic, MonsterRarity.Rare, MonsterRarity.Unique,
    };

    private static uint RankColour(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => Palette.Magic,
        MonsterRarity.Rare => Palette.Rare,
        MonsterRarity.Unique => Palette.Unique,
        _ => Palette.Monster,
    };

    private static RecordingCanvas DrawEnemy(
        MonsterRarity rarity, bool alive = true, NativeMapSettings? options = null)
    {
        var world = World(RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Monster,
            Rarity = rarity,
            IsAlive = alive,
        });

        var (feature, _, _) = Map(options);
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        return canvas;
    }

    [Fact]
    public void AChestIsDrawnAsAChest()
    {
        var world = World(RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Chest,
            IsPoi = true,
        });

        var (feature, _, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        // POE2Radar's "Chest" icon: a lidded body with a keyhole, so several
        // filled rings rather than one dot.
        Assert.True(
            canvas.Polygons.Count >= 3,
            $"only {canvas.Polygons.Count} filled rings; the chest icon has a body, a lid band and a keyhole");

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Chest);
    }

    [Fact]
    public void APermanentMonsterStaysAThreat()
    {
        Assert.Equal(EntityKind.Monster, EntityClassifier.Classify(
            "Metadata/Monsters/Skeleton/Skeleton1",
            componentNames: new[] { "Monster", "Positioned", "Life" }));
    }

    [Fact]
    public void YourMinionsAreAlliesAndNotThreats()
    {
        // They live under /Monsters/ like everything else that moves, so without
        // an explicit check a pack of your own skeletons draws as red danger.
        var minion = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Ally };
        var world = World(minion);

        var (feature, stats, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        Assert.Equal(1, stats.MapEntitiesDrawn);
        Assert.Contains(canvas.Circles, c => c.Colour == Palette.Ally);
        Assert.DoesNotContain(canvas.Circles, c => c.Colour == Palette.Monster);
    }

    [Fact]
    public void NpcIsTestedBeforeMonster()
    {
        // Friendly NPCs are filed under Metadata/Monsters/NPC/. Getting the order
        // wrong draws every vendor in town as something to kill.
        Assert.Equal(EntityKind.Npc, EntityClassifier.Classify("Metadata/Monsters/NPC/Merchant"));
    }

    [Fact]
    public void TheLocalCharacterIsNotAnotherPlayer()
    {
        Assert.Equal(EntityKind.Player,
            EntityClassifier.Classify("Metadata/Characters/Int/IntFourb", isLocalPlayer: true));

        Assert.Equal(EntityKind.OtherPlayer,
            EntityClassifier.Classify("Metadata/Characters/Int/IntFourb", isLocalPlayer: false));
    }

    // ---- the reader split ------------------------------------------------------

    [Fact]
    public void EveryLaneHasItsOwnReader()
    {
        // The reader caches memory regions in a list with no locking, so sharing
        // one across threads corrupts it. There are three cadences now — the
        // entity walk, the frame, and the item-UI sweep — and a shared reader
        // would also turn three independent rates back into a queue, which is
        // exactly what putting the sweep on the render lane did.
        var session = typeof(EasyExile.Core.Runtime.GameSession);

        var readers = session
            .GetProperties(BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(p => p.PropertyType.Name == "IMemoryReader")
            .Select(p => p.Name)
            .ToArray();

        Assert.Equal(3, readers.Length);
        Assert.Contains("Memory", readers);
        Assert.Contains("FastMemory", readers);
        Assert.Contains("UiMemory", readers);
    }

    [Theory]
    [InlineData("Metadata/MiscellaneousObjects/AreaTransitionBlockage", EntityKind.Other)]
    [InlineData("Metadata/MiscellaneousObjects/AreaTransition_Animate", EntityKind.Transition)]
    [InlineData("Metadata/MiscellaneousObjects/AreaTransition", EntityKind.Transition)]
    public void ABlockageIsNotAWayOut(string metadata, EntityKind expected)
    {
        // The barrier ACROSS an exit is not an exit. The client spawns one per
        // blocked door and they outnumber the real ones twenty to one: in
        // Clearfell Encampment 27 entities matched "Transition" and exactly one
        // had a destination. Routing to a wall and labelling it as a way out
        // both came from treating these as exits.
        Assert.Equal(expected, EntityClassifier.Classify(metadata));
    }


    [Fact]
    public void AnUnmarkedChestIsSceneryAndStaysOffTheMap()
    {
        // The client files destructible props under /Chests with regional names
        // — "EzomyteChest_01" is a pot — so no keyword list separates them and a
        // town filled with chest markers. Measured live: eight of them in one
        // area, every one with the client's own icon absent.
        var world = World(RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Chest,
            IsPoi = false,
        });

        var (feature, _, _) = Map();
        var canvas = new RecordingCanvas();

        feature.Draw(Frame(world, RadarFixture.MapFrame(world)), canvas);

        Assert.Empty(canvas.Polygons);
    }


    [Fact]
    public void GroundIsSeenInACircleAroundThePlayer()
    {
        // Round, not square: a square reveal leaves corners behind as you walk,
        // which reads as a rendering artefact rather than as ground you covered.
        var terrain = new TerrainSnapshot(new byte[64 * 64], 64, 64);
        var map = new ExplorationMap();

        map.Observe(1, terrain, new Vector2(32, 32), radius: 10);

        Assert.True(map.IsSeen(32, 32));
        Assert.True(map.IsSeen(32, 41));
        Assert.False(map.IsSeen(32, 43));

        // The corner of the bounding square is outside the circle.
        Assert.False(map.IsSeen(42, 42));
    }

    [Fact]
    public void ChangingAreaForgetsWhereYouWalked()
    {
        // A memory of the last zone's exploration painted onto this one would
        // be worse than none at all.
        var terrain = new TerrainSnapshot(new byte[64 * 64], 64, 64);
        var map = new ExplorationMap();

        map.Observe(1, terrain, new Vector2(10, 10), radius: 5);
        Assert.True(map.IsSeen(10, 10));

        map.Observe(2, terrain, new Vector2(50, 50), radius: 5);

        Assert.False(map.IsSeen(10, 10));
        Assert.True(map.IsSeen(50, 50));
    }

    [Fact]
    public void SeeingNothingNewDoesNotAskForARepaint()
    {
        // The texture rebuilds on the version, so a version that moves without
        // anything being seen is a rebuild that paints the same picture.
        var terrain = new TerrainSnapshot(new byte[64 * 64], 64, 64);
        var map = new ExplorationMap();

        map.Observe(1, terrain, new Vector2(10, 10), radius: 5);

        var version = map.Version;

        map.Observe(1, terrain, new Vector2(10, 10), radius: 5);

        Assert.Equal(version, map.Version);
    }

    [Fact]
    public void TheUnexploredTintIsASixtyFourthOfTheTerrainsPixels()
    {
        // The point of splitting it out. Repainting the terrain to show
        // exploration cost the size of the MAP rather than the size of the
        // change, so it lurched however it was throttled; this is small enough
        // to rebuild every frame.
        var terrain = new TerrainSnapshot(new byte[2048 * 2048], 2048, 2048);
        var uploaded = new List<(int Width, int Height)>();

        var overlay = new ExplorationOverlay((_, image, _) =>
        {
            uploaded.Add((image.Width, image.Height));
            return 1;
        });

        overlay.Ensure(1, terrain, new ExplorationMap(), unchecked((int)0xFF5A5AFF), 0.2f);

        var (width, height) = uploaded.Single();

        Assert.Equal(256, width);
        Assert.Equal(256, height);
        // A block of eight cells per texel is 1/64 of the pixels, not the
        // "hundredth" I first wrote — the test caught the exaggeration.
        Assert.Equal(terrain.Width * terrain.Height / 64, width * height);
    }

    [Fact]
    public void TheTintIsNotRebuiltWhenNothingNewWasSeen()
    {
        var terrain = new TerrainSnapshot(new byte[64 * 64], 64, 64);
        var explored = new ExplorationMap();
        var overlay = new ExplorationOverlay((_, _, _) => 1);

        overlay.Ensure(1, terrain, explored, 0, 0.2f);
        var builds = overlay.Builds;

        overlay.Ensure(1, terrain, explored, 0, 0.2f);

        Assert.Equal(builds, overlay.Builds);
    }

    [Fact]
    public void MovingTheStrengthSliderRepaintsTheTint()
    {
        // Strength is a setting, so it changes without anything new being seen.
        // A version check alone would leave the old wash on screen while the
        // slider moved, which reads as the slider doing nothing.
        var terrain = new TerrainSnapshot(new byte[64 * 64], 64, 64);
        var explored = new ExplorationMap();
        var overlay = new ExplorationOverlay((_, _, _) => 1);

        overlay.Ensure(1, terrain, explored, 0, 0.2f);
        var builds = overlay.Builds;

        overlay.Ensure(1, terrain, explored, 0, 0.8f);

        Assert.Equal(builds + 1, overlay.Builds);
    }
}
