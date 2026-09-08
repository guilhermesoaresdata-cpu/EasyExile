using EasyExile.Core.Spatial;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings.Levelling;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Levelling;

/// <summary>
/// The current zone's steps, drawn on the game rather than in a settings window.
/// </summary>
/// <remarks>
/// "Eu nao vou jogar com o painel aberto." Correct, and it is the whole point:
/// a walkthrough you have to stop and open is a website, and there are already
/// good websites. The value of reading the game is that the guide can sit on it.
///
/// Deliberately small. It draws where you are, what to do now, and the next few
/// things — nothing that needs reading twice while something is hitting you.
/// The route on the ground says where; this says what.
/// </remarks>
public static class StepPanel
{
    private const float Pad = 8f;
    private const float LineGap = 3f;
    private const float Margin = 12f;

    public static void Draw(
        IOverlayCanvas canvas, ScreenRect bounds, LevellingSettings options,
        CampaignZone zone, int number, int count, string objective, bool routing,
        CampaignStep? current = null)
    {
        var lines = Lines(zone, number, count, objective, routing, options, current);

        if (lines.Count == 0) return;

        var scale = Math.Clamp(options.TextScale, 0.6f, 3f);

        var width = 0f;
        var height = 0f;

        foreach (var line in lines)
        {
            var size = canvas.MeasureText(line.Text, scale * line.Scale);

            width = Math.Max(width, size.X);
            height += size.Y + LineGap;
        }

        if (width <= 0f || height <= 0f) return;

        height -= LineGap;

        var at = Corner(bounds, options.Corner, width + (Pad * 2f), height + (Pad * 2f));

        var alpha = (byte)Math.Clamp(options.Opacity * 255f, 0f, 255f);

        // A plate, not a wash. Over a bright tileset the text is unreadable
        // without one, and over a dark one an opaque box is a hole in the game.
        canvas.Rect(at, new Vector2(at.X + width + (Pad * 2f), at.Y + height + (Pad * 2f)),
            Palette.Rgba(12, 12, 16, alpha));

        canvas.Rect(at, new Vector2(at.X + width + (Pad * 2f), at.Y + height + (Pad * 2f)),
            Palette.Rgba(90, 90, 110, (byte)(alpha / 2)), filled: false);

        var y = at.Y + Pad;

        foreach (var line in lines)
        {
            canvas.Text(new Vector2(at.X + Pad, y), line.Colour, line.Text, scale * line.Scale);

            y += canvas.MeasureText(line.Text, scale * line.Scale).Y + LineGap;
        }
    }

    /// <summary>
    /// What the panel says, in the order it says it.
    /// </summary>
    /// <remarks>
    /// The objective comes first and in colour, because it is the one line worth
    /// glancing at mid-fight. The steps below it are context: what this zone is
    /// for, and what is coming after the thing being done now.
    /// </remarks>
    private static List<(string Text, uint Colour, float Scale)> Lines(
        CampaignZone zone, int number, int count, string objective, bool routing,
        LevellingSettings options, CampaignStep? current)
    {
        var lines = new List<(string, uint, float)>
        {
            (Ascii($"{zone.Name}   {number}/{count}"), Palette.Rgba(180, 190, 210), 0.85f),

            // Green only when a path is actually being drawn. "It knows where to
            // go and does not do it" was true and invisible once; the colour is
            // the difference between an intention and a line on the ground.
            (Ascii(objective), routing ? Palette.Good : Palette.Warning, 1.05f),
        };

        // The hint belongs to the step being done, and only to that one. Printed
        // against every step it made each line longer than the screen.
        if (current?.Hint is { Length: > 0 } hint)
            lines.Add((Ascii("   " + hint), Palette.Muted, 0.8f));

        if (options.VisibleSteps <= 0) return lines;

        var shown = 0;

        foreach (var step in zone.Steps)
        {
            if (step.Optional && !options.ShowOptional) continue;
            if (shown >= options.VisibleSteps) break;

            // The objective line already says what is being done, so repeating
            // it underneath was the duplicate in the screenshot.
            if (ReferenceEquals(step, current)) continue;

            lines.Add((
                Ascii((step.Optional ? "  opt  " : "   -   ") + step.Text),
                step.Optional ? Palette.Muted : Palette.EntityLabel,
                0.8f));

            shown++;
        }

        return lines;
    }

    /// <summary>
    /// Text the overlay's font can actually draw.
    /// </summary>
    /// <remarks>
    /// The font is the ImGui default, which is Latin-1 only: an em dash, a
    /// middle dot or a curly quote comes out as a literal question mark. The
    /// screenshot was full of them, and they read as corruption rather than as a
    /// missing glyph.
    ///
    /// Substituted rather than stripped, because the punctuation is load-bearing
    /// — a dash between a step and its hint is the thing that separates them.
    /// </remarks>
    public static string Ascii(string text)
    {
        if (text.All(c => c < 128)) return text;

        var built = new System.Text.StringBuilder(text.Length);

        foreach (var c in text)
        {
            built.Append(c switch
            {
                '—' or '–' or '−' => "-",
                '·' or '•' => "-",
                '’' or '‘' => "'",
                '“' or '”' => "\"",
                '…' => "...",
                'ç' => "c",
                'ã' or 'á' or 'à' or 'â' => "a",
                'é' or 'ê' => "e",
                'í' => "i",
                'ó' or 'ô' or 'õ' => "o",
                'ú' => "u",
                _ => c < 128 ? c.ToString() : "",
            });
        }

        return built.ToString();
    }

    /// <summary>Where the plate sits, kept inside the client's own rectangle.</summary>
    private static Vector2 Corner(ScreenRect bounds, ChipCorner corner, float width, float height)
    {
        var left = bounds.X + Margin;
        var right = bounds.Right - width - Margin;

        // Below the client's own top-left cluster — minimap, buffs — rather than
        // under it. Above the flask bar at the bottom for the same reason.
        var top = bounds.Y + (Margin * 6f);
        var bottom = bounds.Bottom - height - (Margin * 10f);

        return corner switch
        {
            ChipCorner.TopRight => new Vector2(right, top),
            ChipCorner.BottomLeft => new Vector2(left, bottom),
            ChipCorner.BottomRight => new Vector2(right, bottom),
            _ => new Vector2(left, top),
        };
    }
}
