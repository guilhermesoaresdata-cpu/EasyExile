using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

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
///
/// That is the automatic rule, and it is the wrong one often enough to need a
/// way out. Somebody over-capping for a map's elemental damage is short of
/// nothing and still shopping; somebody whose stats could not be read gets
/// silence for ever. So the player can take the decision instead and name the
/// elements they want to see, which is the same mark driven by a different
/// question.
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

        var options = _settings.Loot;
        var resists = frame.Snapshot!.Player.Resists;
        var chosen = options.ResistanceMode == ResistanceMode.Chosen;

        // Automatic: unknown stats and a character already at the target both
        // mean there is nothing useful to say, and saying it anyway would put a
        // mark on every ring in the game.
        //
        // Chosen: the character's own numbers are not consulted at all, which
        // is the point - it answers "show me cold" rather than "show me what I
        // am short of", and it still answers when the stats are unreadable.
        if (chosen)
        {
            if ((ResistanceWatch)options.ResistanceWatchMask == ResistanceWatch.None) return;
        }
        else if (!resists.AnyBelow(options.ResistanceTarget))
        {
            return;
        }

        var cursor = _cursor();
        var scale = options.TextScale * options.ModTierScale;

        foreach (var slot in frame.Panels.Slots)
        {
            if (frame.Panels.IsCovered(slot, cursor.X, cursor.Y)) continue;

            var helps = Helps(slot.Item.Affixes, resists, options);

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
        System.Collections.Immutable.ImmutableArray<string> affixes,
        ResistanceSnapshot resists,
        Settings.Loot.LootSettings options)
    {
        var chosen = options.ResistanceMode == ResistanceMode.Chosen;
        var watched = (ResistanceWatch)options.ResistanceWatchMask;
        var mark = string.Empty;

        foreach (var affix in affixes)
        {
            var family = ModTierTable.FamilyOf(affix);

            if (!family.Contains("resist", StringComparison.OrdinalIgnoreCase)) continue;

            var all = family.Contains("all", StringComparison.OrdinalIgnoreCase);

            foreach (var (kind, letter, word, flag) in Kinds)
            {
                if (!all && !family.Contains(word, StringComparison.OrdinalIgnoreCase)) continue;

                // The one line that differs between the two modes: what makes
                // an element worth a letter.
                if (chosen ? (watched & flag) == 0 : resists.Missing(kind, options.ResistanceTarget) <= 0)
                    continue;

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
    private static readonly (ResistanceKind Kind, string Letter, string Word, ResistanceWatch Flag)[] Kinds =
    [
        // English initials, matching the words the item and the character sheet
        // both use. The first version took them from Portuguese - G for gelo, R
        // for raio - and collided with itself as well as costing a translation.
        (ResistanceKind.Fire, "F", "fire", ResistanceWatch.Fire),
        (ResistanceKind.Cold, "C", "cold", ResistanceWatch.Cold),
        (ResistanceKind.Lightning, "L", "lightning", ResistanceWatch.Lightning),
        (ResistanceKind.Chaos, "X", "chaos", ResistanceWatch.Chaos),
    ];
}
