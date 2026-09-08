using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// Parity with POE2Radar's point-of-interest and transition handling.
/// </summary>
/// <remarks>
/// These assert equivalence with the reference, not with our own reading of it.
/// The categories come from <c>Poe2Live.Categorize</c>, the precedence from the
/// rule order <c>DisplayRules.CategoryDefaults</c> builds, and the colours and
/// sizes from <c>RadarSettings.Transition</c> and <c>RadarSettings.Poi</c>.
///
/// The one that matters most is <see cref="TerrainEntitiesAreObjectsNotJunk"/>.
/// Dropping <c>/Terrain/</c> is what kept every waypoint and portal off the map,
/// and it was a divergence from the reference we had introduced ourselves.
/// </remarks>
public class MinimapIconPortTests
{
    private static (NativeMapRadarFeature Feature, RadarStats Stats) Map(NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var stats = new RadarStats();

        return (new NativeMapRadarFeature(settings, stats, (_, _, _) => 0x1234), stats);
    }

    private static WorldSnapshot World(params EntitySnapshot[] entities) =>
        RadarFixture.World(entities: entities) with
        {
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
        };

    private static RecordingCanvas Draw(WorldSnapshot world, NativeMapSettings? options = null)
    {
        var (feature, _) = Map(options);
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = RadarFixture.MapFrame(world) }, canvas);

        return canvas;
    }

    private static EntitySnapshot Poi(string metadata, EntityKind kind = EntityKind.Object) =>
        RadarFixture.Entity(0, new Vector3(0, 0, 0), metadata) with { Kind = kind, IsPoi = true };

    // ---- the reader ------------------------------------------------------------

    /// <summary>
    /// <c>Poe2Live.Categorize</c> sends <c>/Terrain/</c> to
    /// <c>EntityCategory.Object</c>. We were sending it to Junk, which dropped it
    /// before the snapshot — and with it every waypoint, seal and portal, because
    /// that is where the client files them.
    /// </summary>
    [Theory]
    [InlineData("Metadata/Terrain/Leagues/Expedition/AncientSeal2Portal")]
    [InlineData("Metadata/Terrain/Act1/Waypoints/Waypoint")]
    public void TerrainEntitiesAreObjectsNotJunk(string metadata)
    {
        var kind = EntityClassifier.Classify(metadata);

        Assert.Equal(EntityKind.Object, kind);
        Assert.NotEqual(EntityKind.Junk, kind);
    }

    /// <summary>
    /// The paths the probe actually found carrying a MinimapIcon, live: 80 of
    /// 1792 entities, and these were all of them.
    /// </summary>
    [Theory]
    [InlineData("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other)]
    [InlineData("Metadata/MiscellaneousObjects/MultiplexPortal", EntityKind.Other)]
    [InlineData("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter", EntityKind.Other)]
    [InlineData("Metadata/QuestObjects/TitanValley_Switch", EntityKind.Other)]
    [InlineData("Metadata/Terrain/Leagues/Expedition/AncientSeal2Portal", EntityKind.Object)]
    public void EveryPathTheClientMarksSurvivesClassification(string metadata, EntityKind expected)
    {
        // Whatever else happens to these, they must not be filtered out before
        // the POI rule ever gets to look at them.
        Assert.Equal(expected, EntityClassifier.Classify(metadata));
    }

    /// <summary>
    /// Surfacing /Terrain/ must not cost the snapshot its monsters.
    /// </summary>
    /// <remarks>
    /// This is the trap the change walked into live: an area holds hundreds of
    /// rocks and walls under /Terrain/, the capture budget is 512, and letting
    /// unmarked scenery into it pushed everything worth drawing out. The
    /// reference has no budget to protect and ignores a non-POI Object at draw
    /// time; we have to drop it earlier, which reaches the same map.
    /// </remarks>
    [Fact]
    public void UnmarkedSceneryNeverReachesTheSnapshot()
    {
        using var memory = WorldFixture.Build(monsters: 4, scenery: 2000);

        var result = SnapshotCapture.Capture(
            memory, WorldFixture.ModuleBase, new AreaEpoch(), CaptureOptions.Default);

        var captured = result.Snapshot!.Entities;

        Assert.DoesNotContain(captured, e => e.Kind == EntityKind.Object && !e.IsPoi);
        Assert.Equal(4, captured.Count(e => e.Kind == EntityKind.Monster));
    }

    // ---- the semantics ---------------------------------------------------------

    [Fact]
    public void APoiIsInterestingWhateverItsCategory()
    {
        // The client already decided. That is the entire value of the component:
        // no path matching, no guessing at what a "MultiplexPortal" is.
        Assert.True(Poi("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other).IsInteresting);
        Assert.True(Poi("Metadata/Terrain/Whatever").IsInteresting);
    }

    [Fact]
    public void AnObjectWithoutTheIconIsNotDrawn()
    {
        var scenery = RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Terrain/Rock") with
        {
            Kind = EntityKind.Object,
        };

        var canvas = Draw(World(scenery));

        // Terrain stops being junk, which means the map is now offered hundreds
        // of rocks. The icon is what separates them from a waypoint.
        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.PoiBlue);
    }

    [Fact]
    public void APoiIsDrawnAsABluePin()
    {
        var canvas = Draw(World(Poi("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other)));

        // POE2Radar's Poi default: MapPin, #8CBFFF at 0.80 opacity.
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.PoiBlue);
    }

    [Fact]
    public void ATransitionIsDrawnAsGreenStairs()
    {
        var transition = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Transition };

        var canvas = Draw(World(transition));

        // POE2Radar's Transition default: Stairs, #66FF99 at 0.95.
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen);

        // The reference's Stairs path has ten points. A shape with fewer is not
        // that path.
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen && p.Points.Length == 10);
    }

    /// <summary>
    /// Rule precedence, from the order <c>DisplayRules.CategoryDefaults</c>
    /// builds: the category rules come first and the POI rule is last, so an
    /// entity that is both still draws as its category.
    /// </summary>
    [Fact]
    public void ACategoryRuleBeatsThePoiRule()
    {
        var transition = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Transition,
            IsPoi = true,
        };

        var canvas = Draw(World(transition));

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen);
        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.PoiBlue);
    }

    [Fact]
    public void APoiMonsterIsStillDrawnAsAMonster()
    {
        // The reference's POI rule is gated on Object|Other for exactly this
        // reason: a league boss carrying a map icon is a fight first.
        var boss = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Monster,
            Rarity = MonsterRarity.Unique,
            IsPoi = true,
        };

        var canvas = Draw(World(boss));

        Assert.Contains(canvas.Circles, c => c.Colour == Palette.Unique);
        Assert.DoesNotContain(canvas.Circles, c => c.Colour == Palette.PoiBlue);
    }

    [Fact]
    public void PoisCanBeTurnedOff()
    {
        var canvas = Draw(
            World(Poi("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other)),
            new NativeMapSettings { ShowPois = false });

        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.PoiBlue);
    }

    /// <summary>
    /// The completed flag rides in the snapshot but changes nothing yet. The
    /// reference does not fade it by default either — that is an
    /// <c>Encounter: Complete</c> display rule, which lands with the icon system.
    /// </summary>
    [Fact]
    public void ACompletedPoiIsStillDrawn()
    {
        // Deliberately NOT the expedition device: that path now matches a
        // mechanic rule and draws a flag, which is the reference's precedence
        // and the subject of its own test.
        var claimed = Poi("Metadata/MiscellaneousObjects/Checkpoint", EntityKind.Other)
            with { IconComplete = true };

        Assert.Contains(Draw(World(claimed)).Polygons, p => p.Colour == Palette.PoiBlue);
    }
}
