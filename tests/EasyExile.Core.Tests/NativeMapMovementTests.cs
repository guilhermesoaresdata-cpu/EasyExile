using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// Enemy movement is smoothed between world captures. Nothing else is.
/// </summary>
/// <remarks>
/// The distinction is the whole point of the pass, and it is what most of these
/// tests are really guarding: an enemy's own walk is the one quantity that
/// arrives at 30 Hz, so it is the one quantity worth interpolating. The map, the
/// player, the pan and the zoom already arrive on every frame, and smoothing
/// them would trade a real problem for a worse one — an overlay that lags the
/// game it is drawn on.
/// </remarks>
public class NativeMapMovementTests
{
    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>One world capture at 30 Hz.</summary>
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(33);

    private static (NativeMapRadarFeature Feature, RadarStats Stats) Map(NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var stats = new RadarStats();

        return (new NativeMapRadarFeature(settings, stats, (_, _, _) => 0x1234), stats);
    }

    private static WorldSnapshot World(TimeSpan at, long epoch, params EntitySnapshot[] entities) =>
        RadarFixture.World(epoch: epoch, entities: entities) with
        {
            Timestamp = T0 + at,
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
        };

    /// <summary>
    /// A frame rendered <paramref name="age"/> after its snapshot was captured.
    /// Both clocks come off the snapshot, so a test can place a frame anywhere
    /// inside a tick without touching a wall clock.
    /// </summary>
    private static RenderFrame At(WorldSnapshot world, MapFrameSnapshot map, TimeSpan age) =>
        RadarFixture.Frame(world) with { MapFrame = map, SnapshotAge = age };

    private static EntitySnapshot Monster(Vector3 at) =>
        RadarFixture.Entity(0, at) with { Kind = EntityKind.Monster };

    private static Vector2 MarkerOf(RecordingCanvas canvas, uint colour) =>
        canvas.Circles.First(c => c.Colour == colour).At;

    private static Vector2 Enemy(RecordingCanvas canvas) => MarkerOf(canvas, Palette.Monster);

    private static Vector2 Player(RecordingCanvas canvas) => MarkerOf(canvas, Palette.Player);

    /// <summary>Draws one frame and hands back what landed on the canvas.</summary>
    private static RecordingCanvas Draw(
        NativeMapRadarFeature feature, WorldSnapshot world, MapFrameSnapshot map, TimeSpan age)
    {
        var canvas = new RecordingCanvas();
        feature.Draw(At(world, map, age), canvas);

        return canvas;
    }

    /// <summary>Where a raw world position projects on a given frame, unsmoothed.</summary>
    private static Vector2 Raw(Vector3 world, MapFrameSnapshot map)
    {
        var bounds = RadarFixture.Client;

        return NativeMapProjection.Project(
            NativeMapProjection.ToGrid(world),
            map.PlayerGrid,
            NativeMapProjection.MapCentre(map.Map, bounds.Width, bounds.Height),
            NativeMapProjection.ScaleFor(map.Map.Zoom, bounds.Height));
    }

    // ---- the interpolation itself ---------------------------------------------

    [Fact]
    public void MovingEntityInterpolatesBetweenWorldSnapshots()
    {
        var (feature, stats) = Map();

        var first = World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0)));
        var second = World(Tick, 1, Monster(new Vector3(200, 0, 0)));

        var map = RadarFixture.MapFrame(first);

        Draw(feature, first, map, TimeSpan.Zero);

        // Rendered one tick behind the newest sample, so the marker walks from
        // the old position to the new one across the tick rather than jumping to
        // it the instant the capture lands.
        var start = Enemy(Draw(feature, second, map, TimeSpan.Zero));

        var middle = Enemy(Draw(feature, second, map, Tick / 2));
        var interpolatedMidTick = stats.MapInterpolated;

        var end = Enemy(Draw(feature, second, map, Tick));

        Assert.Equal(Raw(new Vector3(0, 0, 0), map).X, start.X, 1);
        Assert.Equal(Raw(new Vector3(200, 0, 0), map).X, end.X, 1);

        Assert.True(
            middle.X > start.X && middle.X < end.X,
            $"the marker has to be between the two samples, not at one of them ({middle.X})");

        Assert.Equal(1, interpolatedMidTick);

        // A tick later the next capture has not arrived. Holding at the last
        // real position is the honest answer; extrapolating past it would invent
        // a walk the monster may not have continued.
        Assert.Equal(0, stats.MapInterpolated);
        Assert.Equal(1, stats.MapSnapped);
    }

    [Fact]
    public void StationaryEntityDoesNotJitter()
    {
        var (feature, _) = Map();

        var standing = new Vector3(500, 500, 0);
        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Vector2? placed = null;

        // Twelve frames spread across four captures. A monster that is not
        // moving must land on exactly the same pixel every time: an epsilon that
        // does not cover read noise turns a standing pack into a shimmer.
        for (var capture = 0; capture < 4; capture++)
        {
            var world = World(Tick * capture, 1, Monster(standing));

            for (var frame = 0; frame < 3; frame++)
            {
                var at = Enemy(Draw(feature, world, map, (Tick / 3) * frame));

                placed ??= at;
                Assert.Equal(placed.Value, at);
            }
        }

        Assert.Equal(Raw(standing, map), placed!.Value);
    }

    [Fact]
    public void NewEntityDoesNotInterpolateFromGarbage()
    {
        var (feature, stats) = Map();

        // First sighting. There is no earlier sample, and anything other than a
        // snap would fly the marker in from wherever the default happens to be.
        var world = World(TimeSpan.Zero, 1, Monster(new Vector3(900, 300, 0)));
        var map = RadarFixture.MapFrame(world);

        var canvas = Draw(feature, world, map, TimeSpan.Zero);

        Assert.Equal(Raw(new Vector3(900, 300, 0), map), Enemy(canvas));
        Assert.Equal(0, stats.MapInterpolated);
        Assert.Equal(1, stats.MapSnapped);
    }

    [Fact]
    public void LargeTeleportSnapsImmediately()
    {
        var (feature, stats) = Map();

        var here = new Vector3(0, 0, 0);
        var far = new Vector3(600, -600, 0);

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, Monster(here)), map, TimeSpan.Zero);

        // Nothing walks that in 33 ms. Either it blinked or the client reused the
        // address for something else; sliding a marker across the map would draw
        // a path nothing took.
        var canvas = Draw(feature, World(Tick, 1, Monster(far)), map, Tick / 2);

        Assert.Equal(Raw(far, map), Enemy(canvas));
        Assert.Equal(0, stats.MapInterpolated);
    }

    [Fact]
    public void EpochChangeSnapsAndClearsInterpolation()
    {
        var (feature, stats) = Map();

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0))), map, TimeSpan.Zero);
        Draw(feature, World(Tick, 1, Monster(new Vector3(200, 0, 0))), map, TimeSpan.Zero);

        // A new area reallocates every entity, so the id that was a rhoa here is
        // something else there. Carrying a track across would interpolate between
        // two unrelated creatures.
        var after = World(Tick * 2, 2, Monster(new Vector3(1500, 1500, 0)));
        var newMap = RadarFixture.MapFrame(after);

        var canvas = Draw(feature, after, newMap, TimeSpan.Zero);

        Assert.Equal(Raw(new Vector3(1500, 1500, 0), newMap), Enemy(canvas));
        Assert.Equal(0, stats.MapInterpolated);
        Assert.Equal(1, stats.MapTracked);
    }

    [Fact]
    public void DroppedCaptureSnapsRatherThanStretchingTheWalk()
    {
        var (feature, stats) = Map();

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0))), map, TimeSpan.Zero);

        // Two seconds between captures is a stall, not a tick. The pair of
        // samples is no longer a path, so there is nothing honest to draw
        // between them.
        var late = World(TimeSpan.FromSeconds(2), 1, Monster(new Vector3(200, 0, 0)));

        var canvas = Draw(feature, late, map, TimeSpan.Zero);

        Assert.Equal(Raw(new Vector3(200, 0, 0), map), Enemy(canvas));
        Assert.Equal(0, stats.MapInterpolated);
    }

    [Fact]
    public void SmoothingCanBeTurnedOff()
    {
        var (feature, stats) = Map(new NativeMapSettings { SmoothMovement = false });

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0))), map, TimeSpan.Zero);

        var canvas = Draw(feature, World(Tick, 1, Monster(new Vector3(200, 0, 0))), map, Tick / 2);

        Assert.Equal(Raw(new Vector3(200, 0, 0), map), Enemy(canvas));
        Assert.Equal(0, stats.MapInterpolated);
    }

    [Fact]
    public void SlowCapturesAreNotSmoothedAtAll()
    {
        var (feature, stats) = Map();

        // Measured live: a busy area walks 512 entities at 6-7 Hz, not 30. At a
        // 166 ms tick the smoothing made monsters read as SLOWER — a quick slide
        // followed by a long pause, where the eye takes the slide for the marker
        // struggling to arrive. A hard step at least arrives at once.
        var slow = TimeSpan.FromMilliseconds(166);

        var to = new Vector3(200, 0, 0);

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0))), map, TimeSpan.Zero);

        var second = World(slow, 1, Monster(to));

        Assert.Equal(Raw(to, map).X, Enemy(Draw(feature, second, map, TimeSpan.Zero)).X, 1);
        Assert.Equal(Raw(to, map).X, Enemy(Draw(feature, second, map, TimeSpan.FromMilliseconds(50))).X, 1);

        Assert.Equal(0, stats.MapInterpolated);
    }

    // ---- what must NEVER be smoothed ------------------------------------------

    [Fact]
    public void PlayerMovementStillUsesLatestFastMapFrame()
    {
        var (feature, _) = Map();

        // One world snapshot, a monster standing still in it, and a player that
        // walks. The monster is not moving, so it must slide across the map at
        // render rate — the smoothing must not hold it back by a tick while the
        // ground moves underneath.
        var world = World(TimeSpan.Zero, 1, Monster(new Vector3(500, 500, 0)));

        var before = RadarFixture.MapFrame(world, playerGrid: new Vector2(0, 0));
        var after = RadarFixture.MapFrame(world, playerGrid: new Vector2(40, 0));

        Assert.Equal(Raw(new Vector3(500, 500, 0), before), Enemy(Draw(feature, world, before, TimeSpan.Zero)));
        Assert.Equal(Raw(new Vector3(500, 500, 0), after), Enemy(Draw(feature, world, after, TimeSpan.Zero)));
    }

    [Fact]
    public void EntityInterpolationDoesNotDelayMapShift()
    {
        var (feature, _) = Map();

        // A monster mid-walk, so a track is live and interpolating.
        var first = World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0)));
        var second = World(Tick, 1, Monster(new Vector3(200, 0, 0)));

        Draw(feature, first, RadarFixture.MapFrame(first), TimeSpan.Zero);

        var still = RadarFixture.MapFrame(second);
        var panned = RadarFixture.MapFrame(second, shiftX: 120f, shiftY: -45f);

        var before = Player(Draw(feature, second, still, Tick / 2));
        var after = Player(Draw(feature, second, panned, Tick / 2));

        // The pan lands whole on the frame it arrived on. Dragging the map is the
        // one interaction where a tick of lag is felt immediately.
        Assert.Equal(before.X + 120f, after.X, 3);
        Assert.Equal(before.Y - 45f, after.Y, 3);
    }

    [Fact]
    public void EntityInterpolationDoesNotDelayZoom()
    {
        var (feature, _) = Map();

        var first = World(TimeSpan.Zero, 1, Monster(new Vector3(0, 0, 0)));
        var second = World(Tick, 1, Monster(new Vector3(200, 0, 0)));

        Draw(feature, first, RadarFixture.MapFrame(first), TimeSpan.Zero);

        var near = RadarFixture.MapFrame(second, zoom: 0.5f);
        var far = RadarFixture.MapFrame(second, zoom: 1.0f);

        var mid = Tick / 2;

        var a = Enemy(Draw(feature, second, near, mid));
        var b = Enemy(Draw(feature, second, far, mid));

        var centre = NativeMapProjection.MapCentre(near.Map, RadarFixture.Client.Width, RadarFixture.Client.Height);

        // Doubling the zoom doubles the marker's distance from the centre on the
        // very same frame, with the same interpolated world position underneath.
        Assert.Equal((a.X - centre.X) * 2f, b.X - centre.X, 2);
        Assert.Equal((a.Y - centre.Y) * 2f, b.Y - centre.Y, 2);
    }

    // ---- the bookkeeping ------------------------------------------------------

    [Fact]
    public void LostEntityDoesNotRemainForever()
    {
        var motion = new EntityMotion();
        var id = RadarFixture.World(entities: RadarFixture.Entity(0, new Vector3(0, 0, 0))).Entities[0].Id;

        motion.Position(1, id, new Vector3(0, 0, 0), T0, T0, smooth: true);

        Assert.Equal(1, motion.Tracked);

        // Still around: a monster that missed one capture has not gone anywhere.
        motion.Forget(T0 + TimeSpan.FromMilliseconds(200));
        Assert.Equal(1, motion.Tracked);

        // Gone. Without this a monster killed off screen holds its track until
        // the area changes, and a long map leaks one entry per corpse.
        motion.Forget(T0 + TimeSpan.FromSeconds(5));
        Assert.Equal(0, motion.Tracked);
    }

    [Fact]
    public void CountersSplitInterpolatedFromSnapped()
    {
        var (feature, stats) = Map();

        var walker = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with { Kind = EntityKind.Monster };
        var stander = RadarFixture.Entity(1, new Vector3(300, 300, 0)) with { Kind = EntityKind.Monster };

        var map = RadarFixture.MapFrame(World(TimeSpan.Zero, 1));

        Draw(feature, World(TimeSpan.Zero, 1, walker, stander), map, TimeSpan.Zero);

        var moved = walker with { WorldPosition = new Vector3(200, 0, 0) };

        Draw(feature, World(Tick, 1, moved, stander), map, Tick / 2);

        // One of the two is walking. A panel that cannot tell them apart cannot
        // answer the only question worth asking of this feature: is it smoothing
        // anything at all?
        Assert.Equal(1, stats.MapInterpolated);
        Assert.Equal(1, stats.MapSnapped);
        Assert.Equal(2, stats.MapTracked);
    }
}
