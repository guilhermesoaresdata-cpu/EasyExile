using System.Diagnostics;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Debug;

namespace EasyExile.Radar.Features.World;

/// <summary>
/// World HUD: a dot on every entity, where the client renders it.
/// </summary>
/// <remarks>
/// This exists to prove one chain end to end: correct entity, correct world
/// position, correct projection, correct pixel. It classifies nothing. Monsters,
/// chests and loot all get the same marker on purpose — deciding what an entity
/// *is* comes after being sure about where it is, and a classifier built on an
/// unverified projection would be debugged twice.
/// </remarks>
public sealed class DebugWorldFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;

    // Shortening metadata means splitting a string per entity per frame. At 144
    // Hz over a few hundred entities that is pure waste, and the result only
    // changes when the entity does.
    private readonly EntityVisualCache<string> _labels = new();

    public DebugWorldFeature(RadarSettings settings, RadarStats stats)
    {
        _settings = settings;
        _stats = stats;
    }

    public string Name => "Debug (HUD)";

    private DebugEntitySettings Options => _settings.DebugEntities;

    public bool Enabled => Options.Enabled;

    /// <summary>Exposed so the debug panel can show that the epoch rule is firing.</summary>
    public int CacheInvalidations => _labels.Invalidations;

    public int CachedLabels => _labels.Count;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.ResetEntityCounters();

        if (!frame.HasWorld)
        {
            // No world means no entities to remember. Holding the labels would
            // just leave them to be dropped at the next epoch anyway.
            _labels.Clear();
            return;
        }

        var snapshot = frame.Snapshot!;
        _stats.EntitiesReceived = snapshot.Entities.Length;

        if (!Enabled) return;

        var options = Options;
        var player = snapshot.Player.WorldPosition;

        var clock = Stopwatch.StartNew();

        foreach (var entity in snapshot.Entities)
        {
            if (entity.WorldPosition is not { } world)
            {
                _stats.InvalidProjection++;
                continue;
            }

            if (frame.Project(world) is not { } point)
            {
                _stats.InvalidProjection++;
                continue;
            }

            switch (point.Status)
            {
                case ScreenStatus.OffScreen:
                    _stats.OffScreen++;
                    continue;

                case ScreenStatus.BehindCamera:
                    _stats.BehindCamera++;
                    continue;

                case ScreenStatus.Invalid:
                    _stats.InvalidProjection++;
                    continue;
            }

            _stats.OnScreen++;

            Draw(canvas, frame, options, entity, point.Screen, player, world);

            _stats.EntitiesRendered++;
        }

        _stats.RecordProjection(clock.Elapsed.TotalMilliseconds * 1000.0);
    }

    private void Draw(
        IOverlayCanvas canvas,
        RenderFrame frame,
        DebugEntitySettings options,
        EntitySnapshot entity,
        Vector2 at,
        Vector3? player,
        Vector3 world)
    {
        if (options.ShowMarker)
        {
            canvas.Circle(at, options.MarkerRadius + 1f, Palette.Shadow);
            canvas.Circle(at, options.MarkerRadius, Palette.Entity);
        }

        var y = at.Y + options.MarkerRadius + 3f;

        if (options.ShowDistance && player is { } origin)
        {
            canvas.TextCentred(new Vector2(at.X, y), Palette.EntityLabel, Distance(origin, world));
            y += 13f;
        }

        if (options.ShowMetadata)
        {
            var label = _labels.Get(frame.Epoch, entity.Id, () => Shorten(entity.Metadata));
            canvas.TextCentred(new Vector2(at.X, y), Palette.EntityLabel, label);
            y += 13f;
        }

        if (options.ShowEntityId)
        {
            canvas.TextCentred(new Vector2(at.X, y), Palette.Muted, entity.Id.ToString());
            y += 13f;
        }

        if (options.ShowComponentNames && entity.ComponentNames.Length > 0)
            canvas.TextCentred(new Vector2(at.X, y), Palette.Muted, string.Join(" ", entity.ComponentNames));
    }

    /// <summary>Metres, using the client's own world-units-per-grid-cell ratio.</summary>
    private static string Distance(Vector3 from, Vector3 to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;

        return $"{MathF.Sqrt((dx * dx) + (dy * dy)) / 10f:0}m";
    }

    /// <summary>The tail of a metadata path, which is the part that identifies it.</summary>
    private static string Shorten(string metadata)
    {
        var parts = metadata.Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length == 0 ? metadata : parts[^1];
    }
}
