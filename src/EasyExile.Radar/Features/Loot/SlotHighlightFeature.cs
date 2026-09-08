using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Radar.Features.Loot;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// A loot filter for item panels: marks what is worth something, and what might
/// be, without asking you to hover each square.
/// </summary>
/// <remarks>
/// The hover chip answers "what is this one worth". This answers the question
/// you actually have when a ritual window or a full stash opens, which is "which
/// of these forty do I care about" — and hovering forty squares to find out is
/// the work the tool exists to remove.
///
/// Two tiers, because they mean different things:
///
/// A PRICED tier, drawn when the stack's value clears the floor for its
/// category. The number is known.
///
/// An UNKNOWN tier, for what could be worth something and cannot be priced yet:
/// uniques the game has not let you identify, and anything else the price book
/// has no row for. Marking these differently is the honest thing — a border that
/// promised value and delivered a white base would be worse than no border. This
/// one says "look", not "worth".
/// </remarks>
public sealed class SlotHighlightFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;
    private readonly PriceBook _prices;
    private readonly Func<Vector2> _cursor;

    public SlotHighlightFeature(
        RadarSettings settings, RadarStats stats, PriceBook prices, Func<Vector2> cursor)
    {
        _settings = settings;
        _stats = stats;
        _prices = prices;
        _cursor = cursor;
    }

    public string Name => T("Destaque de itens valiosos");

    public bool Enabled => _settings.Loot.Enabled && _settings.Loot.HighlightSlots;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.SlotsHighlighted = 0;

        if (!Enabled || !frame.HasWorld) return;

        var snapshot = frame.Snapshot!;

        if (frame.Panels.Slots.Length == 0) return;

        var options = _settings.Loot;
        var bounds = frame.ClientBounds;

        // While the game is showing a tooltip, its panel covers whatever is
        // beside the hovered slot — and we draw after the game, so our chips
        // land ON that panel and read as part of it. We cannot know the
        // tooltip's rectangle, but we know when one is up, and the values of
        // OTHER slots are exactly what it buries.
        var cursor = _cursor();

        // The tooltip buries whatever is BESIDE the hovered slot, so those
        // values go. The hovered slot's own does not: it is the one you are
        // asking about, and hiding it made two identical runes look like they
        // were being treated differently.
        var hovered = Hovered(frame.Panels, cursor);

        // The tooltip carries the hovered item's entity too, so without this
        // the sweep outlines an empty square at the tooltip's corner.
        // The same rule the tier marks follow: while a tooltip is up, the
        // outline of a slot it covers is drawn across the item's own text.
        if (frame.Panels.IsInspecting(cursor.X, cursor.Y)) return;

        var slots = ItemSlotSnapshot.Distinct(frame.Panels.Slots, cursor.X, cursor.Y);

        foreach (var slot in slots)
        {
            if (slot.Right < bounds.X || slot.X > bounds.Right ||
                slot.Bottom < bounds.Y || slot.Y > bounds.Bottom) continue;

            if (Price(slot.Item) is not { } price)
            {
                // No row in the price book. Worth a look only when the item
                // itself says it might matter — a unique the game is hiding, or
                // one it has not priced. Everything else here is a white base
                // and bordering those would border the whole panel.
                if (!Unknown(slot.Item)) continue;

                Border(canvas, slot, unchecked((uint)options.UnknownColour), thin: true);
                _stats.SlotsHighlighted++;
                continue;
            }

            var total = price.Exalted * slot.Count;

            if (total < options.MinimumFor(price.Category, slot.Item.IsUnique)) continue;

            var rich = total >= options.HighlightExalted;

            var colour = unchecked((uint)(rich ? options.RichColour : options.PricedColour));

            Border(canvas, slot, colour, thin: !rich);

            if (options.ShowSlotValues && (hovered is null || slot.Entity == hovered.Entity))
            {
                var text = _prices.Describe(price, total, options.MinQuantity,
                    options.NameUniques && slot.Item.IsUnique);
                var size = canvas.MeasureText(text, options.TextScale);

                var (left, top) = Corner(options.SlotValueCorner, slot, size);

                canvas.Rect(
                    new Vector2(left - 2f, top - 1f),
                    new Vector2(left + size.X + 2f, top + size.Y + 1f),
                    Palette.Shadow);

                canvas.Text(new Vector2(left, top), colour, text, options.TextScale);
            }

            _stats.SlotsHighlighted++;
        }
    }

    /// <summary>
    /// The smallest slot under the cursor, or none.
    /// </summary>
    public static ItemSlotSnapshot? Hovered(UiSnapshot panels, Vector2 cursor)
    {
        ItemSlotSnapshot? best = null;

        foreach (var slot in ItemSlotSnapshot.Distinct(panels.Slots, cursor.X, cursor.Y))
        {
            if (!slot.Contains(cursor.X, cursor.Y)) continue;

            // Grids nest a slot inside panel elements, so several rectangles
            // hold the cursor at once and only the smallest is the item.
            if (best is null || slot.Area < best.Area) best = slot;
        }

        return best;
    }

    /// <summary>
    /// Where a chip of this size sits against a slot.
    /// </summary>
    public static (float Left, float Top) Corner(
        ChipCorner corner, ItemSlotSnapshot slot, Vector2 size) => corner switch
    {
        ChipCorner.BottomRight => (slot.Right - size.X - 2f, slot.Bottom - size.Y - 2f),
        ChipCorner.TopLeft => (slot.X + 2f, slot.Y + 2f),
        ChipCorner.TopRight => (slot.Right - size.X - 2f, slot.Y + 2f),
        ChipCorner.Above => (slot.X, slot.Y - size.Y - 6f),
        ChipCorner.Below => (slot.X, slot.Bottom + 6f),
        _ => (slot.X + 2f, slot.Bottom - size.Y - 2f),
    };

    /// <summary>
    /// Whether an unpriced item is still worth a second look.
    /// </summary>
    /// <remarks>
    /// A unique qualifies whether or not the book knows it: the book keys
    /// uniques on art, so a miss there means an unidentified or unlisted unique
    /// — the most interesting thing a panel can contain. A plain rare does not.
    /// A rare's value is in its mods, which no price source knows, and
    /// outlining every rare outlines most of the panel.
    /// </remarks>
    private static bool Unknown(ItemSnapshot item) => item.IsUnique;

    private static void Border(IOverlayCanvas canvas, ItemSlotSnapshot slot, uint colour, bool thin)
    {
        var min = new Vector2(slot.X, slot.Y);
        var max = new Vector2(slot.Right, slot.Bottom);

        canvas.Rect(min, max, colour, filled: false);

        // A second inset rectangle rather than a thicker stroke: the canvas
        // draws one-pixel rects, and one pixel disappears against the game's
        // own bright slot art.
        if (thin) return;

        canvas.Rect(
            new Vector2(min.X + 1f, min.Y + 1f),
            new Vector2(max.X - 1f, max.Y - 1f),
            colour,
            filled: false);
    }

    /// <summary>
    /// Art for a unique, name for everything else. No cross-fallback.
    /// </summary>
    /// <remarks>
    /// The reference is strict about this and it is not fussiness. The art index
    /// holds uniques; the name index holds everything. Letting a non-unique fall
    /// back to art is how a rare body armour picks up some unique's price and
    /// reads "3.8 div" on a level-five character — which is exactly what it did.
    ///
    /// The other direction is wrong too, more quietly: one .dds is shared across
    /// a currency's tiers, so pricing an Orb of Augmentation by art returns
    /// whichever tier happened to index that icon.
    /// </remarks>
    private PriceResult? Price(ItemSnapshot item) =>
        item.IsUnique
            ? (item.Art is { Length: > 0 } art ? _prices.TryByArt(art, item.BaseName) : null)
            : (item.BaseName is { Length: > 0 } name ? _prices.TryByName(name) : null);

}
