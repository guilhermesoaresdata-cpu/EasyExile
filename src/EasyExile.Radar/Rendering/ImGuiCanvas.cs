using ImGuiNET;
using EasyExile.Core.Spatial;
using Numerics = System.Numerics;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// The only place a feature's drawing meets ImGui.
/// </summary>
/// <remarks>
/// Everything goes to the background draw list, so world markers stay underneath
/// the settings panel instead of punching through it, and none of it needs a
/// window or reacts to input.
/// </remarks>
internal sealed class ImGuiCanvas : IOverlayCanvas
{
    private ImDrawListPtr _list;

    /// <summary>Grabs the frame's draw list. Must be called inside a frame.</summary>
    public void Begin() => _list = ImGui.GetBackgroundDrawList();

    public void Circle(Vector2 at, float radius, uint colour, bool filled = true)
    {
        if (filled) _list.AddCircleFilled(To(at), radius, colour);
        else _list.AddCircle(To(at), radius, colour);
    }

    public void CircleOutline(Vector2 at, float radius, uint colour, float thickness) =>
        _list.AddCircle(To(at), radius, colour, 0, thickness);

    public void Line(Vector2 from, Vector2 to, uint colour, float thickness = 1f) =>
        _list.AddLine(To(from), To(to), colour, thickness);

    public void Polygon(ReadOnlySpan<Vector2> points, uint colour)
    {
        if (points.Length < 3) return;

        // ImGui builds the fill from the path it has accumulated, so the points
        // go in one at a time and the fill closes the loop itself.
        foreach (var point in points) _list.PathLineTo(To(point));

        _list.PathFillConcave(colour);
    }

    public void Rect(Vector2 min, Vector2 max, uint colour, bool filled = true)
    {
        if (filled) _list.AddRectFilled(To(min), To(max), colour);
        else _list.AddRect(To(min), To(max), colour);
    }

    public void Text(Vector2 at, uint colour, string text)
    {
        // A one-pixel shadow, because the game behind the overlay is not a
        // controlled background and light text on a snow tileset is unreadable.
        _list.AddText(To(new Vector2(at.X + 1, at.Y + 1)), Palette.Shadow, text);
        _list.AddText(To(at), colour, text);
    }

    public void TextCentred(Vector2 at, uint colour, string text)
    {
        var size = MeasureText(text);
        Text(new Vector2(at.X - (size.X / 2f), at.Y), colour, text);
    }

    public void Text(Vector2 at, uint colour, string text, float scale)
    {
        if (MathF.Abs(scale - 1f) < 0.01f)
        {
            Text(at, colour, text);
            return;
        }

        var font = ImGui.GetFont();
        var size = ImGui.GetFontSize() * scale;

        _list.AddText(font, size, To(new Vector2(at.X + 1, at.Y + 1)), Palette.Shadow, text);
        _list.AddText(font, size, To(at), colour, text);
    }

    public Vector2 MeasureText(string text)
    {
        var size = ImGui.CalcTextSize(text);
        return new Vector2(size.X, size.Y);
    }

    public Vector2 MeasureText(string text, float scale)
    {
        var size = ImGui.CalcTextSize(text);
        return new Vector2(size.X * scale, size.Y * scale);
    }

    public void PushClip(Vector2 min, Vector2 max) => _list.PushClipRect(To(min), To(max), true);

    public void PopClip() => _list.PopClipRect();

    public void ImageQuad(nint texture, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft,
        float opacity = 1f)
    {
        if (texture == 0) return;

        var alpha = (uint)Math.Clamp(opacity * 255f, 0f, 255f);
        var tint = (alpha << 24) | 0x00FFFFFFu;

        _list.AddImageQuad(
            texture,
            To(topLeft), To(topRight), To(bottomRight), To(bottomLeft),
            new Numerics.Vector2(0, 0), new Numerics.Vector2(1, 0),
            new Numerics.Vector2(1, 1), new Numerics.Vector2(0, 1),
            tint);
    }

    private static Numerics.Vector2 To(Vector2 value) => new(value.X, value.Y);
}
