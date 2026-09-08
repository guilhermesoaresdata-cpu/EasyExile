using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Loot;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// A mark on every item carrying a mod the chosen build wants.
/// </summary>
/// <remarks>
/// Levelling loot is a firehose and almost none of it matters. What matters
/// depends entirely on what is being built, and the game has no idea what that
/// is — so the player says once, and every drop and every vendor page answers
/// the question without being hovered.
///
/// A mark, not a reading. Two characters in a corner say "this one is worth
/// opening"; the tooltip is where the actual numbers live and it is one hover
/// away. Anything longer would be a second tooltip fighting the first, which
/// this project has already built once and taken back out.
/// </remarks>
public sealed class BuildTagFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly Func<Vector2> _cursor;

    public BuildTagFeature(RadarSettings settings, Func<Vector2> cursor)
    {
        _settings = settings;
        _cursor = cursor;
    }

    public string Name => T("Itens para a build");

    public bool Enabled => _settings.Loot.Enabled && Wanted != BuildTag.None;

    private BuildTag Wanted => (BuildTag)_settings.Loot.BuildTagMask;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled) return;

        var cursor = _cursor();
        var options = _settings.Loot;
        var scale = options.TextScale * options.ModTierScale;

        foreach (var slot in frame.Panels.Slots)
        {
            // The same rule the tier mark follows: nothing of ours goes on top
            // of the item the client is describing.
            if (frame.Panels.IsCovered(slot, cursor.X, cursor.Y)) continue;

            var tags = BuildTags.Of(slot.Item.Affixes, Wanted);

            if (tags == BuildTag.None) continue;

            var text = BuildTags.Mark(tags);

            if (text.Length == 0) continue;

            var size = canvas.MeasureText(text, scale);
            var (left, top) = SlotHighlightFeature.Corner(options.BuildTagCorner, slot, size);

            var min = new Vector2(left - 3f, top - 2f);
            var max = new Vector2(left + size.X + 3f, top + size.Y + 2f);

            var colour = unchecked((uint)options.BuildTagColour);

            // Opaque, like the tier mark: item art reads straight through two
            // characters and a mark you have to squint at is not one.
            canvas.Rect(min, max, Palette.Rgba(8, 8, 12));
            canvas.Rect(min, max, colour, filled: false);
            canvas.Text(new Vector2(left, top), colour, text, scale);
        }
    }
}
