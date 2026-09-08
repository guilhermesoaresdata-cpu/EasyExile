using EasyExile.Core.Navigation;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Features.Navigation;

/// <summary>
/// The route on the ground, for when the game's map is closed.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>OverlayRenderer.DrawPathsWorld</c>. The same route
/// the native map draws — same A* result, same smoothed waypoints, same cursor —
/// projected through the camera instead of through the map transform. Nothing is
/// planned here; this is a second renderer for one navigation state.
///
/// Two details are the reference's and both matter. The line's head is pinned to
/// the player's LIVE world position every frame, so the first leg is always
/// "you to the next waypoint" rather than a stump left behind by the last world
/// tick. And a waypoint behind the camera BREAKS the line rather than being
/// clamped to an edge: a clamped point is a marker on the screen pointing at
/// somewhere the player cannot walk.
///
/// The route sits on the player's feet plane. On a steep slope it can float or
/// sink; the reference notes the same limitation and takes the same trade.
/// </remarks>
public sealed class WorldRouteFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;
    private readonly Navigator _navigator;

    public WorldRouteFeature(RadarSettings settings, RadarStats stats, Navigator navigator)
    {
        _settings = settings;
        _stats = stats;
        _navigator = navigator;
    }

    public string Name => "Rota no mundo";

    public bool Enabled => _settings.NativeMap.ShowRoutes && _settings.NativeMap.ShowWorldRoute;

    /// <summary>POE2Radar's world-route stroke and marker sizes.</summary>
    private const float Thickness = 3f;
    private const float WaypointRadius = 4f;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.WorldRouteWaypoints = 0;

        if (!Enabled || !frame.HasWorld) return;
        if (frame.MapFrame is not { } live) return;

        // One decision per frame, and it is the reference's: the map owns the
        // route while it is open. Drawing both would put the same information on
        // screen twice, in two coordinate systems, disagreeing.
        if (live.Map.IsUsable) return;

        // The camera off the fast frame, not the world snapshot. It pans with
        // the player, and at capture rate the whole trail would drag behind.
        if (live.Camera is not { } camera) return;

        var bounds = frame.ClientBounds;

        // The ground plane is the player's feet. Every waypoint is a grid cell
        // with no height of its own.
        var ground = live.PlayerWorld.Z;

        var anchor = Screen(camera, live.PlayerWorld, bounds);

        foreach (var (slot, points) in _navigator.Routes())
        {
            if (points.Count == 0) continue;

            var colour = Palette.RouteColour(slot);

            // Pinned to the live player every frame.
            var previous = anchor;

            for (var i = 0; i < points.Count; i++)
            {
                var (gx, gy) = points[i];

                var world = new Vector3(
                    gx * NativeMapProjectionGridToWorld,
                    gy * NativeMapProjectionGridToWorld,
                    ground);

                if (Screen(camera, world, bounds) is not { } at)
                {
                    // Behind the camera: break the line rather than clamp it.
                    previous = null;
                    continue;
                }

                if (previous is { } from) canvas.Line(from, at, colour, Thickness);

                var last = i == points.Count - 1;

                if (last) Destination(canvas, at, colour, _navigator.LabelOf(slot));
                else canvas.Circle(at, WaypointRadius, colour);

                previous = at;

                _stats.WorldRouteWaypoints++;
            }
        }
    }

    /// <summary>
    /// The end of the route, told apart from the waypoints leading to it.
    /// </summary>
    /// <remarks>
    /// An addition, not a port: the reference draws every point the same and
    /// leaves you to infer which one is the end. A star costs nothing and
    /// removes the inference.
    /// </remarks>
    private void Destination(IOverlayCanvas canvas, Vector2 at, uint colour, string? label)
    {
        MapIconsStar(canvas, at, colour);

        if (!_settings.NativeMap.ShowDestinationName || label is not { Length: > 0 }) return;

        canvas.Text(new Vector2(at.X + 10f, at.Y - 8f), colour, label);
    }

    private static void MapIconsStar(IOverlayCanvas canvas, Vector2 at, uint colour) =>
        NativeMap.MapIcons.Draw(canvas, "Star", at, 8f, colour);

    /// <summary>
    /// Projects a world point, or null when it is behind the camera or off the
    /// client area.
    /// </summary>
    private static Vector2? Screen(
        EasyExile.Core.Snapshots.CameraSnapshot camera, Vector3 world, Overlay.ScreenRect bounds)
    {
        var point = camera.Project(world);

        if (point.Status != ScreenStatus.OnScreen) return null;

        var at = new Vector2(point.Screen.X, point.Screen.Y);

        return at.X < bounds.X || at.X > bounds.Right || at.Y < bounds.Y || at.Y > bounds.Bottom
            ? null
            : at;
    }

    /// <summary>
    /// One grid cell in world units. The same constant the map projection uses;
    /// the two renderers must agree about what a waypoint means.
    /// </summary>
    private const float NativeMapProjectionGridToWorld = 250f / 23f;
}
