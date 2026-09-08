using System.Collections.Immutable;
using EasyExile.Core.Navigation;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Features.Navigation;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// The navigation port: A*, smoothing, destinations and the drawn route.
/// </summary>
public class NavigationPortTests
{
    /// <summary>
    /// A grid from ASCII. '#' is a wall, anything else is walkable — so a test's
    /// terrain is legible as terrain instead of as an index calculation.
    /// </summary>
    private static TerrainSnapshot Grid(params string[] rows)
    {
        var width = rows[0].Length;
        var cells = new byte[width * rows.Length];

        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < width; x++) cells[(y * width) + x] = (byte)(rows[y][x] == '#' ? 0 : 1);
        }

        return new TerrainSnapshot(cells, width, rows.Length);
    }

    // ---- A* ---------------------------------------------------------------------

    [Fact]
    public void AStarFindsReferencePath()
    {
        var terrain = Grid(
            "..........",
            "..........",
            "..........");

        var reader = new TerrainCellReader(terrain);
        var path = new AStar(terrain.Width, terrain.Height)
            .FindPath(reader, new PathCell(0, 1), new PathCell(9, 1), flatCost: true);

        Assert.True(path.Found);
        Assert.Equal(new PathCell(0, 1), path.Cells[0]);
        Assert.Equal(new PathCell(9, 1), path.Cells[^1]);

        // Nine cardinal steps on a flat grid. The reference's cost model gives a
        // cardinal step 1 and a diagonal root-two, so a straight line is exact.
        Assert.Equal(9f, path.Cost, 3);
    }

    [Fact]
    public void AStarWalksAroundAWall()
    {
        var terrain = Grid(
            ".....#....",
            ".....#....",
            ".....#....",
            "..........");

        var reader = new TerrainCellReader(terrain);
        var path = new AStar(terrain.Width, terrain.Height)
            .FindPath(reader, new PathCell(0, 0), new PathCell(9, 0), flatCost: true);

        Assert.True(path.Found);
        Assert.DoesNotContain(path.Cells, c => reader.Read(c.X, c.Y) == 0);

        // It has to go under the wall, so it cannot stay on the top row.
        Assert.Contains(path.Cells, c => c.Y == 3);
    }

    [Fact]
    public void BlockedDestinationHandled()
    {
        // The reference snaps either end to the nearest walkable cell within ~32,
        // so aiming at a wall still routes you to its edge rather than failing.
        var terrain = Grid(
            "..........",
            "....###...",
            "....###...",
            "..........");

        var reader = new TerrainCellReader(terrain);
        var path = new AStar(terrain.Width, terrain.Height)
            .FindPath(reader, new PathCell(0, 0), new PathCell(5, 1), flatCost: true);

        Assert.True(path.Found);
        Assert.True(reader.Read(path.Cells[^1].X, path.Cells[^1].Y) > 0, "the snapped goal must be walkable");
    }

    [Fact]
    public void NoPathReturnsCleanly()
    {
        var terrain = Grid(
            "..#..",
            "..#..",
            "..#..");

        var reader = new TerrainCellReader(terrain);
        var path = new AStar(terrain.Width, terrain.Height)
            .FindPath(reader, new PathCell(0, 0), new PathCell(4, 0), flatCost: true);

        // A sealed wall spanning the grid: no path, and no exception.
        Assert.False(path.Found);
        Assert.Empty(path.Cells);
    }

    [Fact]
    public void PathSmoothingMatchesReference()
    {
        var terrain = Grid(
            "..........",
            "..........",
            "..........");

        var reader = new TerrainCellReader(terrain);
        var raw = new AStar(terrain.Width, terrain.Height)
            .FindPath(reader, new PathCell(0, 1), new PathCell(9, 1), flatCost: true);

        var smoothed = PathSmoother.Smooth(reader, raw.Cells);

        // Open ground with a clear line: the reference collapses the whole run to
        // its endpoints. Ten cells become two waypoints.
        Assert.Equal(10, raw.Cells.Count);
        Assert.Equal(2, smoothed.Count);
        Assert.Equal(raw.Cells[0], smoothed[0]);
        Assert.Equal(raw.Cells[^1], smoothed[^1]);
    }

    [Fact]
    public void SmoothingNeverCutsThroughAWall()
    {
        var terrain = Grid(
            ".....#....",
            ".....#....",
            ".....#....",
            "..........");

        var reader = new TerrainCellReader(terrain);
        var planner = new PathPlanner();

        var waypoints = planner.Plan(terrain, (0, 0), (9, 0));

        Assert.NotEmpty(waypoints);

        // Every straight leg the smoother kept must itself be walkable — that is
        // what the Bresenham line-of-sight check buys, and shortcutting past a
        // corner is the failure it exists to prevent.
        for (var i = 0; i < waypoints.Count - 1; i++)
        {
            Assert.True(
                PathSmoother.HasLineOfSight(
                    reader, waypoints[i].X, waypoints[i].Y, waypoints[i + 1].X, waypoints[i + 1].Y),
                $"leg {i} cuts through terrain");
        }
    }

    // ---- destinations -----------------------------------------------------------

    private static WorldSnapshot World(
        EntitySnapshot[]? entities = null, LandmarkSnapshot[]? landmarks = null, long epoch = 1) =>
        RadarFixture.World(epoch: epoch, entities: entities ?? Array.Empty<EntitySnapshot>()) with
        {
            Terrain = Grid("..........", "..........", "..........", "..........", ".........."),
            Landmarks = (landmarks ?? Array.Empty<LandmarkSnapshot>()).ToImmutableArray(),
        };

    [Fact]
    public void LandmarkCanBecomeDestination()
    {
        var landmark = new LandmarkSnapshot("The Ardura Caravan", "Metadata/Terrain/x.tdt", new Vector2(8, 2), 5);
        var world = World(landmarks: new[] { landmark });

        var targets = NavTargets.From(world);

        Assert.Contains(targets, t => t.Label == "The Ardura Caravan");

        // The friendly name is what the route is called, not the asset path.
        var id = NavTarget.IdFor(landmark);

        Assert.Equal(new Vector2(8, 2), NavTargets.Resolve(id, world));
    }

    [Fact]
    public void PoiCanBecomeDestination()
    {
        var checkpoint = RadarFixture.Entity(0, new Vector3(50, 20, 0), "Metadata/MiscellaneousObjects/Checkpoint")
            with { Kind = EntityKind.Other, IsPoi = true };

        var world = World(new[] { checkpoint });

        Assert.Contains(NavTargets.From(world), t => t.Id == NavTarget.IdFor(checkpoint));
        Assert.NotNull(NavTargets.Resolve(NavTarget.IdFor(checkpoint), world));
    }

    [Fact]
    public void EntityCanBecomeDestination()
    {
        var exit = RadarFixture.Entity(0, new Vector3(50, 20, 0), "Metadata/Terrain/AreaTransition")
            with { Kind = EntityKind.Transition };

        var world = World(new[] { exit });

        Assert.Contains(NavTargets.From(world), t => t.Id == NavTarget.IdFor(exit));

        // An entity destination follows the entity: it is resolved from the
        // latest snapshot every tick, never from a stored position.
        var moved = World(new[] { exit with { GridPosition = new Vector2(3, 4) } });

        Assert.Equal(new Vector2(3, 4), NavTargets.Resolve(NavTarget.IdFor(exit), moved));
    }

    [Fact]
    public void ACompletedEncounterStopsBeingADestination()
    {
        var claimed = RadarFixture.Entity(0, new Vector3(50, 20, 0), "Metadata/MiscellaneousObjects/Expedition2")
            with { Kind = EntityKind.Other, IsPoi = true, IconComplete = true };

        Assert.Null(NavTargets.Resolve(NavTarget.IdFor(claimed), World(new[] { claimed })));
    }

    [Fact]
    public void AMonsterIsNotOfferedAsADestination()
    {
        var monster = RadarFixture.Entity(0, new Vector3(50, 20, 0)) with { Kind = EntityKind.Monster };

        Assert.Empty(NavTargets.From(World(new[] { monster })));
    }

    // ---- routes -----------------------------------------------------------------

    private static (NativeMapRadarFeature Feature, Navigator Navigator, RadarStats Stats) Map()
    {
        var settings = new RadarSettings { NativeMap = new NativeMapSettings() };
        var stats = new RadarStats();
        var feature = new NativeMapRadarFeature(settings, stats, (_, _, _) => 0x1234);
        var navigator = new Navigator();

        feature.UseNavigator(navigator);

        return (feature, navigator, stats);
    }

    /// <summary>Drives the navigator until its worker has produced a route.</summary>
    private static WorldSnapshot Route(Navigator navigator, LandmarkSnapshot landmark, long epoch = 1)
    {
        var world = World(landmarks: new[] { landmark }, epoch: epoch);

        navigator.Toggle(NavTarget.IdFor(landmark));

        for (var i = 0; i < 100 && !navigator.Routes().Any(); i++)
        {
            navigator.Maintain(world, new Vector2(0, 0));
            Thread.Sleep(10);
        }

        return world;
    }

    private static readonly LandmarkSnapshot Far =
        new("Far", "Metadata/Terrain/x.tdt", new Vector2(9, 4), 3);

    [Fact]
    public void RouteUsesNativeMapProjection()
    {
        var (feature, navigator, stats) = Map();
        using var _ = navigator;

        var world = Route(navigator, Far);

        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = RadarFixture.MapFrame(world) }, canvas);

        Assert.True(canvas.Lines > 0, "the route has to reach the canvas");
        Assert.Equal(1, stats.MapRoutes);
    }

    [Fact]
    public void PanMovesRouteWithMap()
    {
        var (feature, navigator, _) = Map();
        using var _n = navigator;

        var world = Route(navigator, Far);

        var before = Draw(feature, world, RadarFixture.MapFrame(world));
        var after = Draw(feature, world, RadarFixture.MapFrame(world, shiftX: 120f, shiftY: -45f));

        Assert.NotEmpty(before);
        Assert.Equal(before.Count, after.Count);

        // The pan lands whole, on the same frame, on every point of the route.
        for (var i = 0; i < before.Count; i++)
        {
            Assert.Equal(before[i].X + 120f, after[i].X, 2);
            Assert.Equal(before[i].Y - 45f, after[i].Y, 2);
        }
    }

    [Fact]
    public void ZoomScalesRouteWithMap()
    {
        var (feature, navigator, _) = Map();
        using var _n = navigator;

        var world = Route(navigator, Far);

        var near = RadarFixture.MapFrame(world, zoom: 0.5f);
        var far = RadarFixture.MapFrame(world, zoom: 1.0f);

        var a = Draw(feature, world, near);
        var b = Draw(feature, world, far);

        var centre = NativeMapProjection.MapCentre(
            near.Map, RadarFixture.Client.Width, RadarFixture.Client.Height);

        Assert.NotEmpty(a);

        // Doubling the zoom doubles every point's distance from the centre, on
        // the same frame — the route shares the map's projection, not a copy.
        for (var i = 0; i < a.Count; i++)
        {
            Assert.Equal((a[i].X - centre.X) * 2f, b[i].X - centre.X, 1);
            Assert.Equal((a[i].Y - centre.Y) * 2f, b[i].Y - centre.Y, 1);
        }
    }

    [Fact]
    public void EpochClearsRoute()
    {
        var (_, navigator, _) = Map();
        using var _n = navigator;

        Route(navigator, Far);

        Assert.Equal(1, navigator.RouteCount);

        // A new area reallocates the entities and replaces the terrain, so a
        // route planned on the old map means nothing on the new one.
        navigator.Maintain(World(landmarks: new[] { Far }, epoch: 2), new Vector2(0, 0));

        Assert.Equal(0, navigator.RouteCount);
        Assert.Empty(navigator.Routes());
    }

    [Fact]
    public void MultiRouteKeepsRoutesIndependent()
    {
        var (_, navigator, _) = Map();
        using var _n = navigator;

        var first = new LandmarkSnapshot("A", "Metadata/Terrain/a.tdt", new Vector2(9, 0), 3);
        var second = new LandmarkSnapshot("B", "Metadata/Terrain/b.tdt", new Vector2(9, 4), 3);

        var world = World(landmarks: new[] { first, second });

        navigator.Toggle(NavTarget.IdFor(first));
        navigator.Toggle(NavTarget.IdFor(second));

        for (var i = 0; i < 100 && navigator.Routes().Count() < 2; i++)
        {
            navigator.Maintain(world, new Vector2(0, 0));
            Thread.Sleep(10);
        }

        var routes = navigator.Routes().ToList();

        Assert.Equal(2, routes.Count);

        // Each keeps its own slot, so each keeps its own colour.
        Assert.Equal(new[] { 0, 1 }, routes.Select(r => r.Slot));
        Assert.NotEqual(Palette.RouteColour(0), Palette.RouteColour(1));

        // And clearing one leaves the other alone.
        navigator.Toggle(NavTarget.IdFor(first));
        navigator.Maintain(world, new Vector2(0, 0));

        Assert.Equal(1, navigator.RouteCount);
    }

    [Fact]
    public void TheRouteCapIsRespected()
    {
        var (_, navigator, _) = Map();
        using var _n = navigator;

        for (var i = 0; i < Navigator.MaxRoutes; i++) Assert.True(navigator.Toggle("t:target" + i));

        Assert.False(navigator.Toggle("t:one-too-many"));
        Assert.Equal(Navigator.MaxRoutes, navigator.RouteCount);
    }

    private static List<Vector2> Draw(NativeMapRadarFeature feature, WorldSnapshot world, MapFrameSnapshot map)
    {
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = map }, canvas);

        return canvas.LinePoints;
    }
}
