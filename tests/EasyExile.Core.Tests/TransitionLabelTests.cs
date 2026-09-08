using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// Exits carry their destination's name on the map.
/// </summary>
public class TransitionLabelTests
{
    [Theory]
    // The path the diagnostics actually read in Mastodon Badlands.
    [InlineData("Metadata/Terrain/Act2/2_5/Objects/LightlessPassageTransition", "Lightless Passage")]
    [InlineData("Metadata/Terrain/Act1/Objects/AreaTransition", "Area")]
    [InlineData("Metadata/Terrain/Act2/Objects/BoneAbyssTransition_03", "Bone Abyss")]
    // The interior digit is KEPT and spaced, which is the reference's rule: only
    // a TRAILING digit run is dropped.
    [InlineData("Metadata/MiscellaneousObjects/Expedition2/Expedition2Encounter", "Expedition 2 Encounter")]
    public void APathBecomesAReadableName(string metadata, string expected)
    {
        Assert.Equal(expected, EntityLabels.Pretty(metadata));
    }

    [Fact]
    public void TheWordTransitionIsNotPartOfTheName()
    {
        // Every exit's path ends in it, so keeping it would put the same useless
        // word on every label.
        foreach (var path in new[]
                 {
                     "Metadata/Terrain/X/LightlessPassageTransition",
                     "Metadata/Terrain/X/SomewhereElseTransition_02",
                 })
            Assert.DoesNotContain("Transition", EntityLabels.Pretty(path), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDestinationNameWinsOverThePath()
    {
        // The whole point. Every exit in the game shares the metadata path
        // AreaTransition_Animate, so the path can only ever say "Area
        // Transition"; the client's own destination says "The Well of Souls".
        var exit = Transition(0, "Metadata/MiscellaneousObjects/AreaTransition_Animate", new Vector3(0, 0, 0))
            with { FriendlyName = "The Well of Souls" };

        var canvas = Render(World(exit), new NativeMapSettings());

        Assert.Contains(canvas.Texts, t => t.Text == "The Well of Souls");
        Assert.DoesNotContain(canvas.Texts, t => t.Text.Contains("Area Transition", StringComparison.Ordinal));
    }

    [Fact]
    public void ALandmarkAlreadyNamingTheSpotIsNotDuplicated()
    {
        // Both layers are right; printing both is not.
        var exit = Transition(0, "Metadata/MiscellaneousObjects/AreaTransition_Animate", new Vector3(0, 0, 0))
            with { FriendlyName = "The Well of Souls" };

        var world = World(exit) with
        {
            Landmarks = new[]
            {
                new LandmarkSnapshot("The Well of Souls", "Metadata/Terrain/x.tdt", exit.GridPosition!.Value, 4),
            }.ToImmutableArray(),
        };

        var canvas = Render(world, new NativeMapSettings());

        Assert.Equal(1, canvas.Texts.Count(t => t.Text == "The Well of Souls"));
    }

    [Fact]
    public void ALandmarkFarAwayIsNotTheSameSpot()
    {
        var exit = Transition(0, "Metadata/MiscellaneousObjects/AreaTransition_Animate", new Vector3(0, 0, 0))
            with { FriendlyName = "The Well of Souls" };

        // Measured live: the nearest landmark was 636 cells from the transition.
        // Suppressing on name alone would have lost the label entirely.
        var world = World(exit) with
        {
            Landmarks = new[]
            {
                new LandmarkSnapshot("The Well of Souls", "Metadata/Terrain/x.tdt", new Vector2(600, 600), 4),
            }.ToImmutableArray(),
        };

        Assert.Contains(Render(world, new NativeMapSettings()).Texts, t => t.Text == "The Well of Souls");
    }

    [Fact]
    public void ThePathIsOnlyUsedWhenThereIsNoRealName()
    {
        var exit = Transition(0, "Metadata/Terrain/Act2/Objects/LightlessPassageTransition", new Vector3(0, 0, 0));

        Assert.Null(exit.FriendlyName);
        Assert.Contains(Render(World(exit), new NativeMapSettings()).Texts, t => t.Text == "Lightless Passage");
    }

    [Fact]
    public void ATransitionIsDrawnWithItsName()
    {
        var canvas = Draw(new NativeMapSettings());

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen);
        Assert.Contains(canvas.Texts, t => t.Text == "Lightless Passage");
    }

    [Fact]
    public void TheNameCanBeTurnedOffWithoutLosingTheMarker()
    {
        var canvas = Draw(new NativeMapSettings { ShowTransitionNames = false });

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen);
        Assert.DoesNotContain(canvas.Texts, t => t.Text == "Lightless Passage");
    }

    [Fact]
    public void AWaypointDrawsOneMarkerNotEight()
    {
        // Measured live in a town: eight AreaTransition entities, one per
        // destination, all on the SAME cell. A label each piled eight names on
        // one pixel, which is what read as markers scattered off their targets.
        var at = new Vector3(0, 0, 0);

        var world = World(
            Transition(0, "Metadata/MiscellaneousObjects/AreaTransition_Animate", at) with
            {
                FriendlyName = "Keth",
            },
            Transition(1, "Metadata/MiscellaneousObjects/AreaTransition_Animate", at) with
            {
                FriendlyName = "Mawdun Quarry",
            },
            Transition(2, "Metadata/MiscellaneousObjects/AreaTransition_Animate", at) with
            {
                FriendlyName = "The Halani Gates",
            });

        var canvas = Render(world, new NativeMapSettings());

        // One label, and it says how many ways out there are rather than picking
        // a favourite destination and hiding the rest.
        Assert.Single(canvas.Texts);
        Assert.Equal("3 saidas", canvas.Texts[0].Text);
    }

    [Fact]
    public void TwoExitsGetTwoDifferentNames()
    {
        // The point of the whole thing: one rule, a different name per exit.
        // A row of identical green staircases answers nothing.
        var world = World(
            Transition(0, "Metadata/Terrain/Act2/Objects/LightlessPassageTransition", new Vector3(0, 0, 0)),
            Transition(1, "Metadata/Terrain/Act2/Objects/BonePitsTransition", new Vector3(200, 0, 0)));

        var canvas = Render(world, new NativeMapSettings());

        Assert.Contains(canvas.Texts, t => t.Text == "Lightless Passage");
        Assert.Contains(canvas.Texts, t => t.Text == "Bone Pits");
    }

    [Fact]
    public void AnExitStaysOnTheMapAfterTheClientUnloadsIt()
    {
        // Why they only appeared up close: the client loads entities near the
        // player and deletes the rest, so walking away from an exit removed it.
        // It has not gone anywhere - a transition is fixed terrain furniture.
        var exit = Transition(0, "Metadata/MiscellaneousObjects/AreaTransition_Animate", new Vector3(0, 0, 0))
            with { FriendlyName = "The Well of Souls" };

        var gone = World() with { Remembered = new[] { exit }.ToImmutableArray() };

        var canvas = Render(gone, new NativeMapSettings());

        Assert.Contains(canvas.Texts, t => t.Text == "The Well of Souls");
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Dim(Palette.TransitionGreen));
    }

    [Fact]
    public void ARememberedExitIsDrawnDimmer()
    {
        // It is remembered, not observed, and the map should not claim otherwise.
        Assert.NotEqual(Palette.TransitionGreen, Palette.Dim(Palette.TransitionGreen));

        var full = (Palette.TransitionGreen >> 24) & 0xFF;
        var dim = (Palette.Dim(Palette.TransitionGreen) >> 24) & 0xFF;

        Assert.True(dim < full, "a remembered marker has to be fainter than a live one");
    }

    [Theory]
    // Real architecture from one live area. None of these is a way anywhere, and
    // each one used to get a marker reading "Entrance" a few tiles off the thing
    // it named.
    [InlineData("Metadata/Terrain/Desert/BuriedCity/Features/Cityroof_Entrance.tdt")]
    [InlineData("Metadata/Terrain/Desert/BuriedCity/Features/Citywall_2_Entrance.tdt")]
    [InlineData("Metadata/Terrain/Desert/BuriedCity/Features/Keth_Stairs_RuinEntrance.tdt")]
    public void DecorativeArchitectureIsNotAnExit(string path)
    {
        Assert.False(
            EasyExile.Core.World.LandmarkReader.IsWayOutForTests(path),
            "a roof, a wall and a staircase are not ways out of the area");
    }

    [Theory]
    [InlineData("Metadata/Terrain/Desert/BuriedCity/Features/Keth_SinkholeTransition_01.tdt")]
    [InlineData("Metadata/Terrain/X/AreaTransition_BadlandsToPits_01.tdt")]
    public void ATransitionTileIsAnExit(string path)
    {
        Assert.True(EasyExile.Core.World.LandmarkReader.IsWayOutForTests(path));
    }

    [Fact]
    public void AnExitTileIsNamedBeforeAnythingHasLoaded()
    {
        // The terrain layer knows every exit from the moment the area loads,
        // which is the half of the problem the entity list cannot solve.
        var world = World() with
        {
            Landmarks = new[]
            {
                new LandmarkSnapshot("Badlands To Pits", "Metadata/Terrain/X/AreaTransition_BadlandsToPits_01.tdt",
                    new Vector2(0, 0), 6, IsWayOut: true),
            }.ToImmutableArray(),
        };

        var canvas = Render(world, new NativeMapSettings());

        Assert.Contains(canvas.Texts, t => t.Text == "Badlands To Pits");

        // Drawn as an exit, not as a generic landmark, so the two layers never
        // look like two different things.
        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.TransitionGreen);
        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.Landmark);
    }

    private static EntitySnapshot Transition(int id, string metadata, Vector3 at) =>
        RadarFixture.Entity(id, at, metadata) with { Kind = EntityKind.Transition };

    private static WorldSnapshot World(params EntitySnapshot[] entities) =>
        RadarFixture.World(entities: entities) with
        {
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
        };

    private static RecordingCanvas Draw(NativeMapSettings options) =>
        Render(
            World(Transition(0, "Metadata/Terrain/Act2/2_5/Objects/LightlessPassageTransition", new Vector3(0, 0, 0))),
            options);

    private static RecordingCanvas Render(WorldSnapshot world, NativeMapSettings options)
    {
        var settings = new RadarSettings { NativeMap = options };
        var feature = new NativeMapRadarFeature(settings, new RadarStats(), (_, _, _) => 0x1234);
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = RadarFixture.MapFrame(world) }, canvas);

        return canvas;
    }
}
