using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Features.Loot;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// A mark on items that help a resistance still short of the cap.
/// </summary>
/// <remarks>
/// The whole point is the word "still". Every ring in the game rolls
/// resistances, so marking items that grant one marks everything and says
/// nothing; marking the ones that fill a gap you actually have is a different
/// list, and it shrinks as the character improves.
///
/// It goes quiet in two cases, both on purpose: when the stats could not be
/// read at all, and when everything is already capped. A number that might be
/// wrong is worse than no number, and a mark that is always on is not a mark.
/// </remarks>
public sealed class ResistanceFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly Func<Vector2> _cursor;

    public ResistanceFeature(RadarSettings settings, Func<Vector2> cursor)
    {
        _settings = settings;
        _cursor = cursor;
    }

    public string Name => T("Resistencias que faltam");

    public bool Enabled => _settings.Loot.Enabled && _settings.Loot.ShowResistanceHelp;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled || !frame.HasWorld) return;

        var resists = frame.Snapshot!.Player.Resists;

        // Unknown stats and a capped character both mean there is nothing
        // useful to say, and saying it anyway would put a mark on every ring.
        if (!resists.AnyMissing) return;

        var cursor = _cursor();
        var options = _settings.Loot;
        var scale = options.TextScale * options.ModTierScale;

        foreach (var slot in frame.Panels.Slots)
        {
            if (frame.Panels.IsCovered(slot, cursor.X, cursor.Y)) continue;

            var helps = Helps(slot.Item.Affixes, resists);

            if (helps.Length == 0) continue;

            var size = canvas.MeasureText(helps, scale);
            var (left, top) = SlotHighlightFeature.Corner(options.ResistanceCorner, slot, size);

            var min = new Vector2(left - 3f, top - 2f);
            var max = new Vector2(left + size.X + 3f, top + size.Y + 2f);

            canvas.Rect(min, max, Palette.Rgba(8, 8, 12));
            canvas.Rect(min, max, unchecked((uint)options.ResistanceColour), filled: false);

            // A letter at a time, each in its element's own colour. Nobody
            // should have to remember that G is cold; orange, blue, yellow and
            // purple are the four the game itself has used for a decade.
            var at = left;

            foreach (var c in helps)
            {
                var letter = c.ToString();

                canvas.Text(new Vector2(at, top), Elemental(c, options), letter, scale);

                at += canvas.MeasureText(letter, scale).X;
            }
        }
    }

    /// <summary>
    /// Which missing resistances this item would help, as a short mark.
    /// </summary>
    /// <remarks>
    /// The mod's family names the element - FireResist, ColdResistance,
    /// AllResistances - so no table is needed, and one that grants all of them
    /// counts for every gap at once.
    /// </remarks>
    private static string Helps(
        System.Collections.Immutable.ImmutableArray<string> affixes, ResistanceSnapshot resists)
    {
        var mark = string.Empty;

        foreach (var affix in affixes)
        {
            var family = ModTierTable.FamilyOf(affix);

            if (!family.Contains("resist", StringComparison.OrdinalIgnoreCase)) continue;

            var all = family.Contains("all", StringComparison.OrdinalIgnoreCase);

            foreach (var (kind, letter, word) in Kinds)
            {
                if (!all && !family.Contains(word, StringComparison.OrdinalIgnoreCase)) continue;
                if (resists.Missing(kind) <= 0) continue;
                if (mark.Contains(letter, StringComparison.Ordinal)) continue;

                mark += letter;
            }
        }

        return mark.Length == 0 ? string.Empty : "+" + mark;
    }

    /// <summary>The colour the game has used for each element for a decade.</summary>
    private static uint Elemental(char letter, Settings.Loot.LootSettings options) => letter switch
    {
        'F' => Palette.Rgba(255, 140, 60),
        'C' => Palette.Rgba(90, 200, 255),
        'L' => Palette.Rgba(255, 230, 90),
        'X' => Palette.Rgba(190, 120, 255),
        _ => unchecked((uint)options.ResistanceColour),
    };

    /// <summary>The four, with the word that names each in a family.</summary>
    private static readonly (ResistanceKind Kind, string Letter, string Word)[] Kinds =
    [
        // English initials, matching the words the item and the character sheet
        // both use. The first version took them from Portuguese - G for gelo, R
        // for raio - and collided with itself as well as costing a translation.
        (ResistanceKind.Fire, "F", "fire"),
        (ResistanceKind.Cold, "C", "cold"),
        (ResistanceKind.Lightning, "L", "lightning"),
        (ResistanceKind.Chaos, "X", "chaos"),
    ];
}
