using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Features.Navigation;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// The route on the ground, and the one decision that picks between renderers.
/// </summary>
public class WorldRouteTests
{
    private static readonly LandmarkSnapshot Target =
        new("Lightless Passage", "Metadata/Terrain/x.tdt", new Vector2(9, 4), 3);

    private static WorldSnapshot World(long epoch = 1) =>
        RadarFixture.World(epoch: epoch) with
        {
            Terrain = new TerrainSnapshot(Walkable(), 10, 5),
            Landmarks = new[] { Target }.ToImmutableArray(),
        };

    private static byte[] Walkable()
    {
        var cells = new byte[10 * 5];

        Array.Fill(cells, (byte)1);

        return cells;
    }

    /// <summary>A fast frame with the map open or closed, and a camera on it.</summary>
    private static MapFrameSnapshot Frame(WorldSnapshot world, bool mapOpen) =>
        RadarFixture.MapFrame(world) with
        {
            Map = mapOpen ? new MapSnapshot(true, 0f, 0f, 0.5f, 1422f, 800f) : MapSnapshot.Closed,
            Camera = RadarFixture.Camera(),
        };

    private sealed record Rig(
        NativeMapRadarFeature Map, WorldRouteFeature Route, Navigator Navigator, RadarStats Stats);

    private static Rig Build(NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var stats = new RadarStats();
        var navigator = new Navigator();

        var map = new NativeMapRadarFeature(settings, stats, (_, _, _) => 0x1234);

        map.UseNavigator(navigator);

        return new Rig(map, new WorldRouteFeature(settings, stats, navigator), navigator, stats);
    }

    /// <summary>Drives the navigator until the worker has produced a route.</summary>
    private static WorldSnapshot Route(Navigator navigator, long epoch = 1)
    {
        var world = World(epoch);

        navigator.Toggle(NavTarget.IdFor(Target));

        for (var i = 0; i < 100 && !navigator.Routes().Any(); i++)
        {
            navigator.Maintain(world, new Vector2(0, 0));
            Thread.Sleep(10);
        }

        return world;
    }

    private static (RecordingCanvas Map, RecordingCanvas Route) Draw(Rig rig, WorldSnapshot world, bool mapOpen)
    {
        var frame = RadarFixture.Frame(world) with { MapFrame = Frame(world, mapOpen) };

        var onMap = new RecordingCanvas();
        var onGround = new RecordingCanvas();

        rig.Map.Draw(frame, onMap);
        rig.Route.Draw(frame, onGround);

        return (onMap, onGround);
    }

    [Fact]
    public void MapOpenDrawsNativeRouteOnly()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        var world = Route(rig.Navigator);

        var (onMap, onGround) = Draw(rig, world, mapOpen: true);

        Assert.True(onMap.Lines > 0, "the map has to draw the route while it is open");
        Assert.Equal(0, onGround.Lines);
        Assert.Equal(0, rig.Stats.WorldRouteWaypoints);
    }

    [Fact]
    public void MapClosedDrawsWorldRouteOnly()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        var world = Route(rig.Navigator);

        var (onMap, onGround) = Draw(rig, world, mapOpen: false);

        Assert.True(onGround.Lines > 0, "the ground has to draw the route while the map is closed");
        Assert.Equal(0, onMap.Lines);
    }

    [Fact]
    public void SameNavigationPathFeedsBothRenderers()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        Route(rig.Navigator);

        // One navigation state, two renderers. Nothing is planned twice.
        var points = rig.Navigator.Routes().Single().Points;

        Assert.NotEmpty(points);
        Assert.Equal(1, rig.Navigator.RouteCount);
    }

    [Fact]
    public void PassedWaypointsAreNotRendered()
    {
        // Tested on the tracker rather than through the navigator: walking far
        // enough to reach a waypoint also trips the "moved far" replan trigger,
        // which correctly hands back a fresh path of similar length. The rule
        // being checked here is the cursor's, and it is the reference's.
        var tracker = new RouteTracker();

        var path = new List<(int x, int y)> { (0, 0), (10, 0), (20, 0), (30, 0) };

        tracker.ApplyResult(path, new System.Numerics.Vector2(30, 0));

        Assert.Equal(4, tracker.CurrentPoints.Count);

        // Standing on the second waypoint: the first two are behind us.
        tracker.Maintain(new System.Numerics.Vector2(10, 0));

        Assert.True(tracker.CurrentPoints.Count < 4, "a reached waypoint has to leave the drawn route");
        Assert.DoesNotContain((0, 0), tracker.CurrentPoints);

        // The goal is never consumed, however close you stand to it.
        tracker.Maintain(new System.Numerics.Vector2(30, 0));

        Assert.NotEmpty(tracker.CurrentPoints);
        Assert.Equal((30, 0), tracker.CurrentPoints[^1]);
    }

    [Fact]
    public void BehindCameraWaypointHidden()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        var world = Route(rig.Navigator);

        // Every point projects behind the camera. Clamping one to a screen edge
        // would put a marker on something the player cannot walk toward.
        var frame = RadarFixture.Frame(world) with
        {
            MapFrame = Frame(world, mapOpen: false) with { Camera = RadarFixture.CameraFacingAway() },
        };

        var canvas = new RecordingCanvas();

        rig.Route.Draw(frame, canvas);

        Assert.Equal(0, canvas.Lines);
        Assert.Equal(0, rig.Stats.WorldRouteWaypoints);
    }

    [Fact]
    public void DestinationMarkerRendered()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        var world = Route(rig.Navigator);

        var (_, onGround) = Draw(rig, world, mapOpen: false);

        // The end of the route is a star, not another dot: the reference draws
        // every point the same and leaves you to infer which is the end.
        Assert.NotEmpty(onGround.Polygons);
        Assert.Contains(onGround.Texts, t => t.Text == "Lightless Passage");
    }

    [Fact]
    public void TheDestinationNameCanBeTurnedOff()
    {
        var rig = Build(new NativeMapSettings { ShowDestinationName = false });
        using var _ = rig.Navigator;

        var world = Route(rig.Navigator);

        var (_, onGround) = Draw(rig, world, mapOpen: false);

        Assert.NotEmpty(onGround.Polygons);
        Assert.DoesNotContain(onGround.Texts, t => t.Text == "Lightless Passage");
    }

    [Fact]
    public void MultiRouteColorsPreserved()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        var second = new LandmarkSnapshot("Other", "Metadata/Terrain/b.tdt", new Vector2(9, 0), 3);

        var world = World() with { Landmarks = new[] { Target, second }.ToImmutableArray() };

        rig.Navigator.Toggle(NavTarget.IdFor(Target));
        rig.Navigator.Toggle(NavTarget.IdFor(second));

        for (var i = 0; i < 100 && rig.Navigator.Routes().Count() < 2; i++)
        {
            rig.Navigator.Maintain(world, new Vector2(0, 0));
            Thread.Sleep(10);
        }

        var frame = RadarFixture.Frame(world) with { MapFrame = Frame(world, mapOpen: false) };
        var canvas = new RecordingCanvas();

        rig.Route.Draw(frame, canvas);

        // Each route keeps the colour its selection slot earns it, on the ground
        // exactly as on the map.
        var colours = canvas.Circles.Select(c => c.Colour).Concat(canvas.Polygons.Select(p => p.Colour))
            .Distinct().ToArray();

        Assert.Contains(Palette.RouteColour(0), colours);
        Assert.Contains(Palette.RouteColour(1), colours);
    }

    [Fact]
    public void EpochChangeClearsWorldRoute()
    {
        var rig = Build();
        using var _ = rig.Navigator;

        Route(rig.Navigator);

        // A new area invalidates the route: the terrain is different and the
        // landmark key belongs to the old map.
        var elsewhere = World(epoch: 2);

        rig.Navigator.Maintain(elsewhere, new Vector2(0, 0));

        var canvas = new RecordingCanvas();

        rig.Route.Draw(
            RadarFixture.Frame(elsewhere) with { MapFrame = Frame(elsewhere, mapOpen: false) }, canvas);

        Assert.Equal(0, canvas.Lines);
        Assert.Equal(0, rig.Navigator.RouteCount);
    }

    [Fact]
    public void SettingsPersistWorldRouteToggle()
    {
        // Its own file. Two test classes sharing the app's settings.txt made the
        // suite flaky, and a flaky suite teaches you to re-run instead of look.
        var path = TempSettings();

        try
        {
            var settings = new RadarSettings
            {
                NativeMap = new NativeMapSettings { ShowWorldRoute = false, ShowDestinationName = false },
            };

            SettingsStore.Save(settings, path);

            var reloaded = new RadarSettings();

            Assert.True(reloaded.NativeMap.ShowWorldRoute, "the default has to differ, or this proves nothing");

            SettingsStore.Load(reloaded, path);

            Assert.False(reloaded.NativeMap.ShowWorldRoute);
            Assert.False(reloaded.NativeMap.ShowDestinationName);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string TempSettings() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"easyexile-worldroute-{Guid.NewGuid():N}.txt");
}
