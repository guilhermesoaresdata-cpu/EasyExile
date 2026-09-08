using EasyExile.Core.Spatial;
using EasyExile.Radar.Overlay;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// Everything a feature is allowed to draw with.
/// </summary>
/// <remarks>
/// Features never touch ImGui or Direct3D. Two things follow: a feature can be
/// tested offline against a recording canvas, with no device and no window; and
/// replacing the rendering backend does not reach into the features. Positions
/// are in screen pixels, the same space <see cref="ScreenPoint.Screen"/> uses.
/// </remarks>
public interface IOverlayCanvas
{
    void Circle(Vector2 at, float radius, uint colour, bool filled = true);

    /// <summary>An unfilled circle with a controllable stroke width.</summary>
    void CircleOutline(Vector2 at, float radius, uint colour, float thickness);

    void Line(Vector2 from, Vector2 to, uint colour, float thickness = 1f);

    /// <summary>
    /// Fills a convex polygon. The primitive the icon shapes are built from —
    /// they are outlines in the reference, and a run of rectangles cannot hold a
    /// diagonal edge.
    /// </summary>
    void Polygon(ReadOnlySpan<Vector2> points, uint colour);

    void Rect(Vector2 min, Vector2 max, uint colour, bool filled = true);

    /// <summary>Draws text with its top-left corner at <paramref name="at"/>.</summary>
    void Text(Vector2 at, uint colour, string text);

    /// <summary>Draws text horizontally centred on <paramref name="at"/>.</summary>
    void TextCentred(Vector2 at, uint colour, string text);

    Vector2 MeasureText(string text);

    /// <summary>Text at a size multiplier.</summary>
    /// <remarks>
    /// Defaulted so a canvas that does not care about type size — every test
    /// fake — stays correct without implementing it. The measurement scales
    /// even in the default, so layout built on it still lines up.
    /// </remarks>
    void Text(Vector2 at, uint colour, string text, float scale) => Text(at, colour, text);

    Vector2 MeasureText(string text, float scale)
    {
        var size = MeasureText(text);
        return new Vector2(size.X * scale, size.Y * scale);
    }

    /// <summary>
    /// Restricts drawing to a rectangle until the matching <see cref="PopClip"/>.
    /// </summary>
    /// <remarks>
    /// The radar panel needs this. Rejecting markers whose centre falls outside
    /// the panel still lets the edge of a dot, or a label, spill past the frame,
    /// and a marker half outside its own panel reads as a bug rather than as a
    /// marker near the edge.
    /// </remarks>
    void PushClip(Vector2 min, Vector2 max);

    void PopClip();

    /// <summary>
    /// Draws a texture stretched over four corners, in order: top-left,
    /// top-right, bottom-right, bottom-left.
    /// </summary>
    /// <remarks>
    /// This is how the terrain mask reaches the screen: several million cells as
    /// one call, with the isometric transform carried entirely by where the
    /// corners land. Drawing it cell by cell would be thousands of times more
    /// expensive for the same picture.
    /// </remarks>
    void ImageQuad(nint texture, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft,
        float opacity = 1f);
}

/// <summary>
/// Packed colours, in the 0xAABBGGRR order ImGui uses.
/// </summary>
/// <remarks>
/// A handful of readable colours rather than a theme. A theme belongs in
/// Rendering/Theme when there is something to theme; right now there are markers
/// and text, and inventing a palette system for them would be furniture.
/// </remarks>
public static class Palette
{
    public static uint Rgba(byte r, byte g, byte b, byte a = 255) =>
        (uint)(a << 24 | b << 16 | g << 8 | r);

    /// <summary>
    /// A packed colour as four floats in 0..1, and back.
    /// </summary>
    /// <remarks>
    /// The settings store persists ints, and ImGui's colour picker works in
    /// floats, so a colour has to survive both round trips without drifting.
    /// Packing is little-endian RGBA, which is what the draw list expects.
    /// </remarks>
    public static void Unpack(uint colour, Span<float> rgba)
    {
        rgba[0] = (colour & 0xFF) / 255f;
        rgba[1] = ((colour >> 8) & 0xFF) / 255f;
        rgba[2] = ((colour >> 16) & 0xFF) / 255f;
        rgba[3] = ((colour >> 24) & 0xFF) / 255f;
    }

    public static uint Pack(ReadOnlySpan<float> rgba) => Rgba(
        (byte)Math.Clamp(rgba[0] * 255f, 0f, 255f),
        (byte)Math.Clamp(rgba[1] * 255f, 0f, 255f),
        (byte)Math.Clamp(rgba[2] * 255f, 0f, 255f),
        (byte)Math.Clamp(rgba[3] * 255f, 0f, 255f));

    public static readonly uint Player = Rgba(90, 200, 255);
    public static readonly uint Entity = Rgba(235, 235, 235, 210);
    public static readonly uint EntityLabel = Rgba(190, 190, 190, 190);
    public static readonly uint Good = Rgba(120, 220, 140);
    public static readonly uint Warning = Rgba(255, 190, 90);
    public static readonly uint Bad = Rgba(255, 110, 110);
    public static readonly uint Muted = Rgba(150, 150, 150, 200);
    public static readonly uint Shadow = Rgba(0, 0, 0, 190);

    // Map categories. Colours are close to the reference's defaults: hostile red,
    // friendly green, loot amber, exits blue-white, other players cyan.
    public static readonly uint Monster = Rgba(235, 80, 70);
    public static readonly uint Npc = Rgba(110, 220, 130);

    /// <summary>Your minions. Light green, so a pack of skeletons never reads as a threat.</summary>
    public static readonly uint Ally = Rgba(170, 255, 170);
    public static readonly uint Chest = Rgba(245, 195, 90);
    public static readonly uint ChestLid = Rgba(180, 130, 45);

    /// <summary>
    /// Area exits. POE2Radar's Transition default: <c>#66FF99</c> at 0.95.
    /// </summary>
    public static readonly uint TransitionGreen = Rgba(102, 255, 153, 242);

    /// <summary>
    /// Anything the client itself marks on the map. POE2Radar's POI default:
    /// <c>#8CBFFF</c> at 0.80 — deliberately quieter than a threat colour,
    /// because there are a lot of them and none of them are urgent.
    /// </summary>
    public static readonly uint PoiBlue = Rgba(140, 191, 255, 204);

    /// <summary>
    /// The same colour at reduced opacity, for something remembered rather than
    /// currently seen.
    /// </summary>
    public static uint Dim(uint colour, float factor = 0.45f)
    {
        var alpha = (uint)((colour >> 24) & 0xFF);

        return (colour & 0x00FFFFFF) | ((uint)(alpha * factor) << 24);
    }

    /// <summary>
    /// Guidance routes, one colour per selection slot. POE2Radar's
    /// <c>PathPalette</c> verbatim: green, orange, sky, pink, yellow, violet,
    /// teal, salmon — eight, because eight routes is the cap and past that the
    /// colours stop being tellable apart.
    /// </summary>
    public static readonly uint[] Route =
    {
        Rgba(51, 230, 102),
        Rgba(255, 140, 26),
        Rgba(77, 179, 255),
        Rgba(255, 77, 179),
        Rgba(242, 230, 51),
        Rgba(153, 102, 255),
        Rgba(51, 255, 217),
        Rgba(255, 102, 102),
    };

    /// <summary>The colour a route draws in, by selection slot.</summary>
    public static uint RouteColour(int slot) => Route[((slot % Route.Length) + Route.Length) % Route.Length];

    /// <summary>
    /// A dangerous monster. POE2Radar's seeded Abyss rule: <c>#B450FF</c>.
    /// </summary>
    public static readonly uint Threat = Rgba(180, 80, 255);

    /// <summary>
    /// Tile landmarks. POE2Radar's Landmark default: <c>#F259F2</c> at 1.0 — a
    /// magenta nothing in the game shares, because these carry a text label and
    /// have to be findable among everything else on the map.
    /// </summary>
    public static readonly uint Landmark = Rgba(242, 89, 242);

    // Rank colours. The marker itself is the rank — the same convention the game
    // uses on an item name and on a monster's own health bar, so it needs no
    // learning. Red is a normal monster; anything that is not red is worth more
    // attention than one.
    public static readonly uint Magic = Rgba(110, 165, 255);
    public static readonly uint Rare = Rgba(255, 230, 70);
    public static readonly uint Unique = Rgba(255, 160, 40);
    public static readonly uint Bone = Rgba(245, 240, 225);
    public static readonly uint Transition = Rgba(160, 200, 255);
    public static readonly uint OtherPlayer = Rgba(90, 220, 220);
    public static readonly uint Poi = Rgba(220, 150, 255);
    public static readonly uint Raw = Rgba(150, 150, 150, 130);
}
