using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Features.HpBars;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// Health bars over monsters, in the world.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>RadarApp.BuildHpSpecs</c> and
/// <c>OverlayRenderer.DrawNameplates</c>. The filter, the geometry, the
/// per-rarity widths and borders, the low-health colour change and the offset
/// above the monster are all the reference's.
///
/// Two of its rules are worth stating because they are easy to lose. A bar is a
/// MONSTER concept gated by the per-rarity toggles, not by a display rule — but
/// the rule is still consulted for two things: a hidden monster gets no bar, and
/// the bar's fill is the colour of its dot, so a rare's bar and a rare's marker
/// are the same yellow.
///
/// It draws whether the game's map is open or not. It is a combat overlay, and
/// combat does not stop because a map is up.
/// </remarks>
public sealed class MonsterHpBars : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;

    /// <summary>
    /// The same smoothing the map markers use.
    /// </summary>
    /// <remarks>
    /// The reference re-reads each monster's live position on the render thread
    /// through cached component addresses. We cannot: the Radar never touches
    /// the client. Interpolating between captures is the same answer applied at
    /// the boundary we do have, and it is the component already proven for it.
    /// </remarks>
    private readonly EntityMotion _motion = new();

    private DisplayRules? _rules;

    public MonsterHpBars(RadarSettings settings, RadarStats stats)
    {
        _settings = settings;
        _stats = stats;
    }

    public string Name => T("Barras de vida");

    public bool Enabled => _settings.HpBars.Enabled;

    /// <summary>Below this fraction the fill turns red, whatever the rank's colour is.</summary>
    private const float LowHealth = 0.3f;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.HpBars = 0;

        if (!Enabled || !frame.HasWorld) return;

        var snapshot = frame.Snapshot!;
        var options = _settings.HpBars;

        // The camera off the fast frame when there is one: it pans with the
        // player, and at capture rate every bar would trail its monster.
        var camera = frame.MapFrame?.Camera ?? snapshot.Camera;

        if (camera is null) return;

        if (_rules is null || _rules.Signature != DisplayRules.SignatureFor(_settings.NativeMap))
            _rules = new DisplayRules(_settings.NativeMap);

        var bounds = frame.ClientBounds;

        var capturedAt = snapshot.Timestamp;
        var now = capturedAt + frame.SnapshotAge;

        _motion.BeginFrame();

        foreach (var entity in snapshot.Entities)
        {
            // A bar is a monster concept, and it needs a pool with a denominator.
            if (entity.Kind != EntityKind.Monster) continue;

            var (current, max) = entity.Life;

            if (!entity.IsAlive || max <= 0 || current <= 0) continue;

            if (!options.ShowsRank(entity.Rarity)) continue;

            // The rule decides two things and no more: whether the monster is
            // hidden, and what colour its bar fills in.
            if (_rules.Resolve(entity) is not { } rule) continue;

            var width = options.WidthFor(entity.Rarity);

            if (width <= 0f) continue;

            if (entity.WorldPosition is not { } captured) continue;

            var world = _motion.Position(
                frame.Epoch, entity.Id, captured, capturedAt, now, _settings.NativeMap.SmoothMovement);

            var point = camera.Project(world);

            // Behind the camera, or off the client area: the reference skips
            // both rather than clamping either.
            if (point.Status != ScreenStatus.OnScreen) continue;

            var sx = point.Screen.X;
            var sy = point.Screen.Y;

            if (sx < bounds.X || sx > bounds.Right || sy < bounds.Y || sy > bounds.Bottom) continue;

            var fraction = Math.Clamp((float)current / max, 0f, 1f);

            var left = sx - (width / 2f) + options.OffsetX;
            var top = sy + options.OffsetY;

            var min = new Vector2(left, top);
            var max2 = new Vector2(left + width, top + options.Height);

            canvas.Rect(min, max2, Palette.Shadow);

            canvas.Rect(
                min,
                new Vector2(left + (width * fraction), top + options.Height),
                fraction < LowHealth ? Palette.Monster : rule.Colour);

            var border = options.BorderFor(entity.Rarity);

            if (border > 0f) canvas.Rect(min, max2, rule.Colour, filled: false);

            // A threat mark sits BESIDE the bar, never on top of the rank
            // colour: the bar already says what rank this is, and the mark says
            // it also carries something worth respecting.
            if (_rules.ResolveOverlay(entity) is { } threat)
            {
                MapIcons.Draw(canvas, threat.Shape, new Vector2(left - 7f, top + (options.Height / 2f)),
                    5f, threat.Colour);

                _stats.HpBarThreats++;
            }

            _stats.HpBars++;
        }

        _motion.Forget(now);
    }
}
