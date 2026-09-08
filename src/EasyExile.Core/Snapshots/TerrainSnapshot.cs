namespace EasyExile.Core.Snapshots;

/// <summary>
/// The walkable mask of the area, one byte per grid cell, row-major.
/// </summary>
/// <remarks>
/// Ported from POE2Radar's <c>Poe2Live.Terrain</c> (MIT). The packing is one
/// nibble per cell, two cells per byte, so the addressable width is
/// <c>BytesPerRow * 2</c> — which is what the reference uses for the bitmap
/// dimensions and therefore what the terrain quad is built from. Keeping that
/// convention is the point: a width of <c>TilesX * 23</c> is the true cell count
/// but would put the quad's right edge in the wrong place by half a byte on
/// odd-width areas.
/// </remarks>
public sealed record TerrainSnapshot(byte[] Walkable, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0 || Walkable.Length == 0;

    public bool IsWalkable(int x, int y) =>
        x >= 0 && y >= 0 && x < Width && y < Height && Walkable[(y * Width) + x] != 0;
}

/// <summary>
/// The game's own map element: whether it is showing, where the player panned
/// it to, and how far it is zoomed.
/// </summary>
/// <remarks>
/// Ported from POE2Radar's <c>Poe2Live.ReadMap</c> (MIT). Everything a native
/// map overlay draws with comes from here: there is no panel of our own, so
/// these three numbers are the whole coordinate system.
/// </remarks>
/// <param name="OriginX">
/// The map element's own position, in the client's UI space. Zero when it could
/// not be read, in which case the window centre stands in.
/// </param>
public sealed record MapSnapshot(
    bool IsVisible, float ShiftX, float ShiftY, float Zoom, float OriginX = 0f, float OriginY = 0f)
{
    public static readonly MapSnapshot Closed = new(false, 0f, 0f, 0f);

    /// <summary>
    /// Whether the element told us where it sits.
    /// </summary>
    /// <remarks>
    /// This is what makes the map stay aligned while a panel is open. The game
    /// does not move the map with <see cref="ShiftX"/> — measured live, that
    /// stays at zero — it moves the ELEMENT: opening the inventory took its UI-
    /// space X from 1422 to 929. POE2Radar projects from the window centre and
    /// never asks where the element is, which is why it is right until a panel
    /// opens.
    /// </remarks>
    public bool HasOrigin => float.IsFinite(OriginX) && float.IsFinite(OriginY) && OriginX > 0f && OriginY > 0f;

    /// <summary>A zoom outside this range means the element is not a usable map.</summary>
    public bool IsUsable => IsVisible && float.IsFinite(Zoom) && Zoom is > 0.05f and < 8f;
}
