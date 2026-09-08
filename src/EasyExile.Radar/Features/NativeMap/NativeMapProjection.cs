using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Radar.Features.NativeMap;

/// <summary>
/// Grid cells to pixels on the game's own map.
/// </summary>
/// <remarks>
/// Direct port of POE2Radar (MIT):
/// <list type="bullet">
/// <item><c>POE2Radar.Core/Pathfinding/MapProjection.cs</c> — the isometric transform</item>
/// <item><c>POE2Radar.Core/Pathfinding/GridConstants.cs</c> — the grid ratio</item>
/// <item><c>POE2Radar.Overlay/Overlay/OverlayRenderer.cs</c>, <c>DrawMap</c> — the centre and scale</item>
/// </list>
///
/// The numbers are the reference's, not a formula of our own: the camera angle,
/// the 677 reference height, the window-centre origin and the -20 default shift
/// all come from there. Anything "equivalent" would be a different map.
/// </remarks>
public static class NativeMapProjection
{
    /// <summary>PoE renders the map with the camera tilted this far.</summary>
    private const double CameraAngleRadians = 38.7 * Math.PI / 180.0;

    public static readonly float CameraCos = (float)Math.Cos(CameraAngleRadians);
    public static readonly float CameraSin = (float)Math.Sin(CameraAngleRadians);

    /// <summary>One grid cell in world units: PoE2's TileToWorld 250 over TileToGrid 23.</summary>
    public const float GridToWorld = 250f / 23f;

    /// <summary>
    /// Reference window height the zoom is calibrated against, so one zoom value
    /// means the same thing at any resolution.
    /// </summary>
    private const float ReferenceHeight = 677f;

    /// <summary>
    /// The map's default shift. Constant in the client, and the signature that
    /// identifies a map element in the first place.
    /// </summary>
    private const float DefaultShiftY = -20f;

    /// <summary>
    /// Screen offset for a grid delta measured from the player.
    /// <c>MapProjection.GridDeltaToMapDelta</c>.
    /// </summary>
    public static Vector2 GridDeltaToMapDelta(Vector2 delta, float mapScale, float deltaWorldZ = 0f)
    {
        var dz = deltaWorldZ / GridToWorld;

        return new Vector2(
            mapScale * (delta.X - delta.Y) * CameraCos,
            mapScale * (dz - (delta.X + delta.Y)) * CameraSin);
    }

    /// <summary>
    /// Pixels per grid cell. <c>OverlayRenderer.DrawMap</c>:
    /// <c>Map.Zoom * (WindowHeight / 677) * ScaleMul</c>, with ScaleMul at its
    /// default of 1.
    /// </summary>
    public static float ScaleFor(float zoom, float windowHeight) =>
        zoom * (windowHeight / ReferenceHeight);

    /// <summary>
    /// The UI is laid out in a fixed virtual space and scaled to the client by
    /// height. Measured: the map element sits at 1422 UI units, and
    /// 1422 x (1080/1600) is 960 — the client centre, to within a rounding.
    /// </summary>
    private const float BaseUiHeight = 1600f;

    /// <summary>
    /// Where the player sits on the native map.
    /// </summary>
    /// <remarks>
    /// POE2Radar's <c>OverlayRenderer.DrawMap</c> uses the window centre plus
    /// the map's pan plus the constant -20, and that is exactly what this
    /// reduces to while nothing is open — measured, the element's scaled
    /// position IS the window centre then.
    ///
    /// It diverges when a panel opens, and it has to. The game slides the map
    /// element sideways to clear the panel: opening the inventory took its UI-
    /// space X from 1422 to 929, about 333 pixels on a 1080-tall client, while
    /// <c>Shift</c> never moved off zero. Projecting from the window centre
    /// therefore drifts by exactly that much, which is the misalignment. Asking
    /// the element where it is costs nothing and covers every panel at once,
    /// rather than one correction per panel.
    ///
    /// The window centre remains the fallback for when the element will not say.
    /// </remarks>
    public static Vector2 MapCentre(MapSnapshot map, float windowWidth, float windowHeight)
    {
        var x = windowWidth * 0.5f;
        var y = windowHeight * 0.5f;

        if (map.HasOrigin)
        {
            var scale = windowHeight / BaseUiHeight;

            x = map.OriginX * scale;
            y = map.OriginY * scale;
        }

        return new Vector2(x + map.ShiftX, y + map.ShiftY + DefaultShiftY);
    }

    /// <summary>Projects a grid cell to a pixel. <c>OverlayRenderer.Project</c>.</summary>
    public static Vector2 Project(Vector2 cell, Vector2 playerGrid, Vector2 centre, float mapScale)
    {
        var delta = new Vector2(cell.X - playerGrid.X, cell.Y - playerGrid.Y);
        var offset = GridDeltaToMapDelta(delta, mapScale);

        return new Vector2(centre.X + offset.X, centre.Y + offset.Y);
    }

    /// <summary>World units to grid cells, the conversion the whole map is in.</summary>
    public static Vector2 ToGrid(Vector3 world) =>
        new(world.X / GridToWorld, world.Y / GridToWorld);
}
