using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Features.Loot;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// A mark on every slot holding a top-tier roll.
/// </summary>
/// <remarks>
/// The first version was a panel beside the hovered item listing every affix
/// with its internal name. It was wrong in three ways at once, and the
/// screenshots showed all three: it landed on top of the client's own tooltip,
/// it printed developer strings like LocalIncreasedEnergyShield that appear
/// nowhere in the game, and it spoke about rolls nobody cares about.
///
/// So it stopped being a reader and became a marker. One small number in the
/// corner of the slot, only when the item holds a roll good enough to bother
/// with, in a colour per tier. Nothing to read, nothing to hover, nothing that
/// can cover the tooltip because it is drawn on the grid the tooltip is beside.
///
/// That also makes it useful where it was not: scanning a full stash is now a
/// glance instead of thirty hovers.
/// </remarks>
public sealed class ModTierFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly ModTierTable _tiers;
    private readonly Func<Vector2> _cursor;

    public ModTierFeature(RadarSettings settings, ModTierTable tiers, Func<Vector2> cursor)
    {
        _settings = settings;
        _tiers = tiers;
        _cursor = cursor;
    }

    public string Name => T("Tier dos mods");

    public bool Enabled => _settings.Loot.Enabled && _settings.Loot.ShowModTiers && _tiers.IsLoaded;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled) return;

        var cursor = _cursor();
        var options = _settings.Loot;
        var scale = options.TextScale * options.ModTierScale;

        Lines(frame, canvas, cursor, scale);

        foreach (var slot in frame.Panels.Slots)
        {
            // Out of the way of the client's own tooltip. The overlay paints
            // last and has no z-order with the game, so a mark belonging to a
            // covered slot lands on top of the item's lines — which is what the
            // screenshots showed, two of them stamped across the text.
            //
            // Only the covered ones go. Standing them all down fixed the same
            // thing by giving up the feature at the moment it is most useful,
            // and there is no longer any need: the tooltip's own rectangle is
            // known now.
            if (frame.Panels.IsCovered(slot, cursor.X, cursor.Y)) continue;

            if (Best(slot.Item.Affixes) is not { } best) continue;

            var text = best.Count > 1
                ? $"{best.Kind}{best.Rank}x{best.Count}"
                : $"{best.Kind}{best.Rank}";

            var size = canvas.MeasureText(text, scale);
            var (left, top) = SlotHighlightFeature.Corner(options.ModTierCorner, slot, size);

            var min = new Vector2(left - 3f, top - 2f);
            var max = new Vector2(left + size.X + 3f, top + size.Y + 2f);

            var colour = Colour(best.Rank);

            // Opaque, and framed in the tier's own colour. At anything less the
            // item art reads straight through a two-character label and the
            // mark stops being one.
            canvas.Rect(min, max, Palette.Rgba(8, 8, 12));
            canvas.Rect(min, max, colour, filled: false);
            canvas.Text(new Vector2(left, top), colour, text, scale);
        }
    }

    /// <summary>
    /// A tier at the start of each mod line the game is showing.
    /// </summary>
    /// <remarks>
    /// The mark on a cell says an item is worth a look but cannot say which roll
    /// earned it. Reading the tooltip is exactly when that becomes the question,
    /// and the client leaves a small empty box at the start of every mod line —
    /// so the answer lands where the eye already is and covers nothing.
    ///
    /// The panel carries no entity, so the item is whichever slot the cursor is
    /// over. And the badges are drawn only when the number of mod lines matches
    /// the number of rolls: without that they would be attributed by position to
    /// lines that may not correspond, and a confident T1 against the wrong line
    /// is worse than no badge at all.
    /// </remarks>
    private void Lines(RenderFrame frame, IOverlayCanvas canvas, Vector2 cursor, float scale)
    {
        if (!_settings.Loot.ModTierOnTooltip) return;

        var tooltip = frame.Panels.Tooltip;
        var hovered = Hovered(frame, cursor);

        if (tooltip is not { ModRows.Length: > 0 }) return;
        if (hovered is null || hovered.Value.Length != tooltip.ModRows.Length) return;

        for (var i = 0; i < tooltip.ModRows.Length; i++)
        {
            if (Roll(hovered.Value[i]) is not { } roll) continue;

            var row = tooltip.ModRows[i];
            var text = $"{roll.Kind}{roll.Rank}";

            var size = canvas.MeasureText(text, scale);

            // At the START of the line, in the margin the centred text leaves.
            // A row runs the tooltip's whole width and its words sit in the
            // middle, so the left of it is empty and nothing has to be covered.
            var at = new Vector2(row.X + 4f, row.Y + ((row.Height - size.Y) / 2f));

            canvas.Rect(
                new Vector2(at.X - 3f, at.Y - 1f),
                new Vector2(at.X + size.X + 3f, at.Y + size.Y + 1f),
                Palette.Rgba(8, 8, 12));

            canvas.Text(at, Colour(roll.Rank), text, scale);
        }
    }

    /// <summary>The rolls on the item under the cursor, in the order the client lists them.</summary>
    private static System.Collections.Immutable.ImmutableArray<string>? Hovered(
        RenderFrame frame, Vector2 cursor)
    {
        foreach (var slot in frame.Panels.Slots)
        {
            if (slot.Contains(cursor.X, cursor.Y)) return slot.Item.Affixes;
        }

        return null;
    }

    /// <summary>
    /// What one roll is worth, and which half of the item it came from.
    /// </summary>
    /// <remarks>
    /// The leading letter used to be T, which said nothing: it was T on every
    /// mark ever drawn. Spending that character on prefix-or-suffix costs
    /// nothing and answers the question a good roll actually raises — whether
    /// the other half of the item is still open.
    /// </remarks>
    private (int Rank, char Kind)? Roll(string affix)
    {
        if (_tiers.Find(affix) is not { } tier) return null;

        var mine = tier.Top - tier.Tier + 1;

        return mine <= _settings.Loot.ModTierAlert ? (mine, tier.IsPrefix ? 'P' : 'S') : null;
    }

    private uint Colour(int rank) => unchecked((uint)(rank switch
    {
        1 => _settings.Loot.ModTier1Colour,
        2 => _settings.Loot.ModTier2Colour,
        _ => _settings.Loot.ModTier3Colour,
    }));

    /// <summary>
    /// The best roll on the item, if any of them clears the threshold.
    /// </summary>
    /// <remarks>
    /// One mark per item rather than one per mod. A slot is sixty pixels and an
    /// item has six affixes; listing them there would be the same clutter in a
    /// smaller box. The count says there is more than one worth looking at, and
    /// looking is what the tooltip is for.
    ///
    /// Rank is the player's: 1 is the best roll a family can have, where the
    /// game's own number counts the other way.
    ///
    /// The letter is P or S, and PS when the best rolls are one of each. Two
    /// top-tier prefixes and two top-tier suffixes are not the same item, and a
    /// single letter that quietly picked one of them would say they were.
    /// </remarks>
    private (int Rank, int Count, string Kind)? Best(
        System.Collections.Immutable.ImmutableArray<string> affixes)
    {
        var alert = _settings.Loot.ModTierAlert;

        var rank = int.MaxValue;
        var count = 0;
        var prefixes = 0;

        foreach (var id in affixes)
        {
            if (_tiers.Find(id) is not { } tier) continue;

            var mine = tier.Top - tier.Tier + 1;

            if (mine > alert) continue;

            if (mine < rank)
            {
                rank = mine;
                count = 1;
                prefixes = tier.IsPrefix ? 1 : 0;
            }
            else if (mine == rank)
            {
                count++;

                if (tier.IsPrefix) prefixes++;
            }
        }

        if (count == 0) return null;

        var kind = prefixes == count ? "P" : prefixes == 0 ? "S" : "PS";

        return (rank, count, kind);
    }
}
