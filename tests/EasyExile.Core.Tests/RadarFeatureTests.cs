using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.World;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Player;

namespace EasyExile.Core.Tests;

/// <summary>
/// What the features draw, and more importantly what they refuse to draw.
/// </summary>
public class RadarFeatureTests
{
    /// <summary>
    /// The world HUD debug layer, switched on. It ships off — the map radar is
    /// the main feature and a label on every entity buries the game — so these
    /// tests, which are about what it draws, have to ask for it.
    /// </summary>
    private static (DebugWorldFeature Feature, RadarStats Stats, RadarSettings Settings) Debug()
    {
        var settings = new RadarSettings();
        settings.DebugEntities = settings.DebugEntities with { Enabled = true };

        var stats = new RadarStats();

        return (new DebugWorldFeature(settings, stats), stats, settings);
    }

    [Fact]
    public void The_world_hud_debug_layer_ships_switched_off()
    {
        // The course correction, pinned: the radar is the product, the world HUD
        // is a secondary view, and it does not turn itself on.
        Assert.False(new RadarSettings().DebugEntities.Enabled);
    }

    // ---- projection decides what is drawn -----------------------------------

    [Fact]
    public void Only_entities_on_screen_get_a_marker()
    {
        var (feature, stats, _) = Debug();
        var canvas = new RecordingCanvas();

        var world = RadarFixture.World(
            entities: new[]
            {
                RadarFixture.Entity(0, new Vector3(0, 0, 0)),          // centre of the viewport
                RadarFixture.Entity(1, new Vector3(500_000, 0, 0)),    // far off to the side
                RadarFixture.Entity(2, null),                          // no position at all
            });

        feature.Draw(RadarFixture.Frame(world), canvas);

        Assert.Equal(3, stats.EntitiesReceived);
        Assert.Equal(1, stats.EntitiesRendered);
        Assert.Equal(1, stats.OnScreen);
        Assert.Equal(1, stats.OffScreen);
        Assert.Equal(1, stats.InvalidProjection);

        // Every circle drawn sits on the one entity that is actually on screen.
        // A marker is a shadow plus a fill, so the count is per-marker and not
        // per-entity; what matters is that nothing was drawn anywhere else.
        Assert.NotEmpty(canvas.Circles);
        Assert.All(canvas.Circles, c => Assert.Equal(960f, c.At.X, 1));
    }

    [Fact]
    public void An_entity_behind_the_camera_is_counted_and_never_drawn()
    {
        var (feature, stats, _) = Debug();
        var canvas = new RecordingCanvas();

        var world = RadarFixture.World(
            camera: RadarFixture.CameraFacingAway(),
            entities: new[] { RadarFixture.Entity(0, new Vector3(10, 10, 10)) });

        feature.Draw(RadarFixture.Frame(world), canvas);

        // Folding it onto a screen edge would put a marker somewhere the player
        // could walk towards, on something that is behind them.
        Assert.Equal(1, stats.BehindCamera);
        Assert.Equal(0, stats.EntitiesRendered);
        Assert.Empty(canvas.Circles);
    }

    [Fact]
    public void A_snapshot_without_a_camera_draws_nothing_and_does_not_throw()
    {
        var (feature, stats, _) = Debug();
        var canvas = new RecordingCanvas();

        var world = RadarFixture.World(camera: null, entities: new[] { RadarFixture.Entity(0, new Vector3(0, 0, 0)) })
            with { Camera = null };

        feature.Draw(RadarFixture.Frame(world), canvas);

        Assert.Equal(0, stats.EntitiesRendered);
        Assert.Empty(canvas.Circles);
    }

    // ---- states where there is no world -------------------------------------

    [Fact]
    public void No_area_produces_no_entity_markers()
    {
        var (feature, stats, _) = Debug();
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(null, CaptureStatus.NoArea), canvas);

        Assert.Equal(0, stats.EntitiesReceived);
        Assert.Equal(0, canvas.Drawn);
    }

    [Fact]
    public void A_transition_does_not_crash_and_draws_nothing_once_the_snapshot_is_stale()
    {
        var (feature, stats, _) = Debug();
        var canvas = new RecordingCanvas();

        var world = RadarFixture.World(entities: new[] { RadarFixture.Entity(0, new Vector3(0, 0, 0)) });

        // The last snapshot still exists but describes an area the character has
        // left. Drawing it would put markers on monsters that no longer exist.
        var frame = RadarFixture.Frame(world, CaptureStatus.InvalidatedByTransition, stale: true);

        feature.Draw(frame, canvas);

        Assert.Equal(0, canvas.Drawn);
        Assert.Equal(0, stats.EntitiesRendered);
    }

    [Fact]
    public void A_disabled_feature_still_reports_what_it_received()
    {
        var (feature, stats, settings) = Debug();
        settings.DebugEntities = settings.DebugEntities with { Enabled = false };

        var canvas = new RecordingCanvas();

        feature.Draw(
            RadarFixture.Frame(RadarFixture.World(entities: new[] { RadarFixture.Entity(0, new Vector3(0, 0, 0)) })),
            canvas);

        Assert.Equal(1, stats.EntitiesReceived);
        Assert.Equal(0, canvas.Drawn);
    }

    // ---- the epoch rule -----------------------------------------------------

    [Fact]
    public void The_label_cache_is_dropped_when_the_epoch_moves()
    {
        var cache = new EntityVisualCache<string>();
        var id = RadarFixture.World(entities: new[] { RadarFixture.Entity(0, null) }).Entities[0].Id;

        Assert.Equal("first", cache.Get(epoch: 1, id, () => "first"));
        Assert.Equal("first", cache.Get(epoch: 1, id, () => "second"));

        // Same id, new area: a different creature at a reused address.
        Assert.Equal("second", cache.Get(epoch: 2, id, () => "second"));

        Assert.Equal(2, cache.Invalidations);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void An_area_change_clears_the_feature_visual_cache()
    {
        var (feature, _, settings) = Debug();
        settings.DebugEntities = settings.DebugEntities with { ShowMetadata = true };

        var canvas = new RecordingCanvas();

        var first = RadarFixture.World(
            epoch: 1,
            entities: new[] { RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Monsters/Old/Zombie") });

        feature.Draw(RadarFixture.Frame(first), canvas);
        Assert.Contains(canvas.Texts, t => t.Text == "Zombie");

        var second = RadarFixture.World(
            epoch: 2,
            entities: new[] { RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Monsters/New/Rhoa") });

        canvas.Texts.Clear();
        feature.Draw(RadarFixture.Frame(second), canvas);

        // The same EntityId in a new epoch must not keep the old label.
        Assert.Contains(canvas.Texts, t => t.Text == "Rhoa");
        Assert.DoesNotContain(canvas.Texts, t => t.Text == "Zombie");
        Assert.True(feature.CacheInvalidations >= 2);
    }

    // ---- the player ---------------------------------------------------------

    [Fact]
    public void The_player_marker_lands_on_the_projected_character()
    {
        // Asked for by name rather than taken from the default, which is now the
        // fixed HUD: this test is about where the AT-CHARACTER mode draws.
        var settings = new RadarSettings
        {
            Player = new EasyExile.Radar.Settings.Player.PlayerSettings
            {
                Anchor = EasyExile.Radar.Settings.Player.PlayerAnchor.AtCharacter,
            },
        };
        var feature = new PlayerWorldFeature(settings);
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(RadarFixture.World()), canvas);

        Assert.NotEmpty(canvas.Circles);
        Assert.All(canvas.Circles, c =>
        {
            Assert.Equal(960f, c.At.X, 1);
            Assert.Equal(540f, c.At.Y, 1);
        });

        Assert.Contains(canvas.Texts, t => t.Text.Contains("gravataicity", StringComparison.Ordinal));
        Assert.Contains(canvas.Texts, t => t.Text.Contains("359/359", StringComparison.Ordinal));
    }

    [Fact]
    public void The_player_can_be_anchored_to_a_fixed_corner_instead()
    {
        var settings = new RadarSettings
        {
            Player = new PlayerSettings { Anchor = PlayerAnchor.FixedHud },
        };

        var canvas = new RecordingCanvas();

        new PlayerWorldFeature(settings).Draw(RadarFixture.Frame(RadarFixture.World()), canvas);

        // Fixed HUD draws text only, and nowhere near the centre of the screen.
        Assert.Empty(canvas.Circles);
        Assert.NotEmpty(canvas.Texts);
        Assert.All(canvas.Texts, t => Assert.True(t.At.Y > RadarFixture.Client.Height / 2f));
    }

    [Fact]
    public void The_player_is_not_drawn_without_a_world()
    {
        var canvas = new RecordingCanvas();

        new PlayerWorldFeature(new RadarSettings()).Draw(RadarFixture.Frame(null, CaptureStatus.NoArea), canvas);

        Assert.Equal(0, canvas.Drawn);
    }
}
