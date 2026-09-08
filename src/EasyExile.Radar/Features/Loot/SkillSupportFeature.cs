using EasyExile.Core.Diagnostics;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Levelling;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Loot;

/// <summary>
/// The supports that go in the skill under the cursor.
/// </summary>
/// <remarks>
/// The skill panel hangs no entity off its entries, unlike a grid cell, so there
/// is no structural way to know which caption on screen names a skill — that was
/// measured, not assumed. What settles it is the lookup: a caption whose text is
/// a key in the advice table is a skill, and one that is not is not interesting.
///
/// So the panel is drawn from the caption's own rectangle, beside it, and it
/// appears for exactly the labels the table knows.
///
/// The list is poe2db's recommendation, ranked, not a count of what real builds
/// run. The heading says "recomendados" rather than "mais usados" for that
/// reason: it would be easy to write the stronger word and it would not be true.
/// </remarks>
public sealed class SkillSupportFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly SupportAdvice _advice;
    private readonly Func<Vector2> _cursor;

    public SkillSupportFeature(RadarSettings settings, SupportAdvice advice, Func<Vector2> cursor)
    {
        _settings = settings;
        _advice = advice;
        _cursor = cursor;
    }

    public string Name => "Suportes da skill";

    public bool Enabled => _settings.Loot.ShowSkillSupports && _advice.IsLoaded;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled) return;

        var cursor = _cursor();

        // The lookup is part of the choice, not a test applied after it. The
        // client's own skill tooltip carries captions smaller than a skill row,
        // so they used to win the hit test and then fail the lookup — and the
        // panel went quiet at the exact moment the tooltip appeared, which is
        // the moment someone is looking at the skill.
        var picked = frame.Panels.CaptionAt(cursor.X, cursor.Y, Known);

        if (_settings.Loot.DebugSkillCaptions)
        {
            Outline(frame, canvas, picked);

            // Written down as well as drawn. A drawn outline answers "is the
            // rectangle right"; the file answers "was the caption there at
            // all", which is the question that cannot be asked live because it
            // needs the panel open at the instant a probe runs.
            CaptionLog.Write(
                frame.Panels.Captions.Select(c => (c.Text, c.X, c.Y, c.Width, c.Height)));
        }


        if (picked is not { } caption) return;

        var supports = _advice.Find(caption.Text);
        var options = _settings.Loot;
        var wanted = Math.Clamp(options.SkillSupportCount, 1, 10);

        var scale = options.TextScale;

        var lines = new List<(string Text, uint Colour, float Scale)>
        {
            (StepPanel.Ascii(caption.Text.Trim()), Palette.Rgba(255, 236, 168), scale * 1.05f),
            ("supports recomendados:", Palette.Muted, scale * 0.85f),
        };

        for (var i = 0; i < supports.Length && i < wanted; i++)
            lines.Add((StepPanel.Ascii($"{i + 1}. {supports[i]}"), Palette.EntityLabel, scale));

        Panel(canvas, frame, caption, options, lines);
    }

    /// <summary>A caption the advice table has something to say about.</summary>
    private bool Known(Core.Snapshots.TextLabelSnapshot caption) =>
        _advice.Find(caption.Text).Length > 0;

    /// <summary>
    /// Every caption that names a skill, and the one the cursor is on.
    /// </summary>
    /// <remarks>
    /// Whether a rectangle lands where its text is drawn cannot be answered
    /// from a terminal without the panel being open at that exact instant, and
    /// that coordination has failed more often than it has worked. Drawn on the
    /// screen it is one screenshot.
    /// </remarks>
    private void Outline(
        RenderFrame frame, IOverlayCanvas canvas, Core.Snapshots.TextLabelSnapshot? picked)
    {
        // Only the captions that name a skill. Outlining all of them drew
        // rectangles across the whole world — the client captions plenty that
        // has nothing to do with this — and buried the two that mattered.
        foreach (var caption in frame.Panels.Captions)
        {
            if (!Known(caption)) continue;

            var hit = picked is { } chosen && ReferenceEquals(chosen, caption);

            // The region that is tested, not the text box. Outlining the text
            // hid the whole point: the sockets a person actually points at are
            // below it, and the box said nothing about whether they counted.
            canvas.Rect(
                new Vector2(caption.X - (caption.Height * 2.2f), caption.Y),
                new Vector2(caption.Right, caption.Y + (caption.Height * 4f)),
                hit ? Palette.Good : Palette.Rgba(90, 90, 120), filled: false);
        }
    }

    /// <summary>
    /// Where the list goes, and drawing it there.
    /// </summary>
    /// <remarks>
    /// Beside the caption is the obvious place and collides with the client's
    /// own skill tooltip, which occupies the same left-hand space — the first
    /// version was reported as "meio escondido" for exactly that reason, and it
    /// was drawn, just sharing a rectangle with the game's own panel.
    /// </remarks>
    private static void Panel(
        IOverlayCanvas canvas, RenderFrame frame, Core.Snapshots.TextLabelSnapshot caption,
        LootSettings options, List<(string Text, uint Colour, float Scale)> lines)
    {
        const float padding = 8f;
        const float gap = 10f;

        var width = 0f;
        var height = padding * 2f;

        foreach (var (text, _, scale) in lines)
        {
            var size = canvas.MeasureText(text, scale);

            width = Math.Max(width, size.X);
            height += size.Y + 2f;
        }

        width += padding * 2f;

        var bounds = frame.ClientBounds;

        var (left, top) = options.SkillSupportAnchor switch
        {
            SkillSupportAnchor.Free => (
                (bounds.Width - width) * Math.Clamp(options.SkillSupportX, 0f, 1f),
                (bounds.Height - height) * Math.Clamp(options.SkillSupportY, 0f, 1f)),
            SkillSupportAnchor.TopCentre => ((bounds.Width - width) / 2f, bounds.Height * 0.14f),
            SkillSupportAnchor.TopLeft => (gap, gap),
            SkillSupportAnchor.TopRight => (bounds.Width - width - gap, gap),
            SkillSupportAnchor.BottomLeft => (gap, bounds.Height - height - gap),
            SkillSupportAnchor.BottomRight => (bounds.Width - width - gap, bounds.Height - height - gap),
            // Past the panel the skill lives in, not just past its name.
            _ => (caption.Right + gap + (bounds.Width * Math.Clamp(options.SkillSupportGap, 0f, 0.6f)),
                  caption.Y),
        };

        // Kept inside the window. Beside the skill means beside it, and a skill
        // near the bottom of a long list would otherwise put half the panel off
        // the screen.
        left = Math.Clamp(left, gap, Math.Max(gap, bounds.Width - width - gap));
        top = Math.Clamp(top, gap, Math.Max(gap, bounds.Height - height - gap));

        var min = new Vector2(left, top);
        var max = new Vector2(left + width, top + height);

        // Opaque and framed. At anything less the tileset reads straight
        // through a list of gem names and the panel stops being one.
        canvas.Rect(min, max, Palette.Rgba(6, 6, 10));
        canvas.Rect(min, max, Palette.Warning, filled: false);

        var y = top + padding;

        foreach (var (text, colour, scale) in lines)
        {
            canvas.Text(new Vector2(left + padding, y), colour, text, scale);

            y += canvas.MeasureText(text, scale).Y + 2f;
        }
    }
}
