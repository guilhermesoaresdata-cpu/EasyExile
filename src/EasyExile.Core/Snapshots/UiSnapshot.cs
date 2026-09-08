using System.Collections.Immutable;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The game's own item UI: the loot tags on the ground and the slots in
/// whatever panels are open.
/// </summary>
/// <remarks>
/// Its own snapshot, on its own cadence, because it belongs to neither lane.
/// It was read on the world walk and cost more than half the world rate —
/// thirty ticks a second down to twelve — since each sweep visits the whole
/// visible UI tree. Everything the overlay draws is interpolated between world
/// ticks, so a slow world is visible everywhere, and paying for panels out of
/// the entity budget is paying in the wrong currency.
///
/// The camera travels with it. A rectangle measured here is corrected by how
/// far the view has moved since, and correcting against a camera from some
/// other instant overshoots.
/// </remarks>
public sealed record UiSnapshot(
    ImmutableArray<LootLabelSnapshot> Labels,
    ImmutableArray<ItemSlotSnapshot> Slots,
    CameraSnapshot? Camera,
    TooltipSnapshot? Tooltip = null,
    ImmutableArray<TextLabelSnapshot> Texts = default)
{
    /// <summary>
    /// Whether the cursor is over an item, which means the client is drawing a
    /// tooltip.
    /// </summary>
    /// <remarks>
    /// Kept for the case the tooltip itself cannot be located. Where it can,
    /// <see cref="IsCovered"/> is the better answer: it hides the handful of
    /// marks the tooltip is actually sitting on instead of all of them.
    /// </remarks>
    public bool IsInspecting(float cursorX, float cursorY)
    {
        foreach (var slot in Slots)
        {
            if (slot.Contains(cursorX, cursorY)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the tooltip is drawn over this slot.
    /// </summary>
    /// <remarks>
    /// The overlay paints last and has no z-order with the game, so a mark
    /// belonging to a covered slot lands on top of the item's own lines — which
    /// is what the screenshots showed, two of them stamped across the text.
    ///
    /// Standing every mark down while a tooltip was up fixed that by giving up
    /// the feature at the moment it is most useful. Only the covered ones need
    /// to go.
    /// </remarks>
    public bool IsCovered(ItemSlotSnapshot slot, float cursorX, float cursorY) =>
        Tooltip is { } tip
            ? tip.Covers(slot.X, slot.Y, slot.Width, slot.Height)

            // No rectangle means no idea which slots are underneath, and the
            // cursor resting on an item is the only remaining evidence that a
            // tooltip is up at all. Everything stands down.
            //
            // This is the case that reached a screenshot: the panel was not
            // being found, every slot answered "not covered", and two marks
            // belonging to cells behind the tooltip were stamped across the
            // item's own name and one of its lines. Losing the marks for the
            // moment an item is being read costs nothing next to that.
            : IsInspecting(cursorX, cursorY);

    /// <summary>The captions on screen, never default so a feature need not check.</summary>
    public ImmutableArray<TextLabelSnapshot> Captions =>
        Texts.IsDefault ? ImmutableArray<TextLabelSnapshot>.Empty : Texts;

    /// <summary>
    /// The smallest caption the cursor is on that the caller will accept.
    /// </summary>
    /// <remarks>
    /// The filter is not a convenience. Choosing the caption first and judging
    /// it afterwards is what made this fail every time the client opened its own
    /// tooltip: the tooltip's captions are smaller than a skill row, so they won
    /// the hit test, and then failed the lookup and the whole thing went quiet
    /// while the mouse had not moved an inch.
    ///
    /// Smallest, because captions nest — a name sits inside a row inside a panel
    /// — and among the ones worth having, the innermost is what is being pointed
    /// at.
    /// </remarks>
    public TextLabelSnapshot? CaptionAt(
        float x, float y, Func<TextLabelSnapshot, bool>? accept = null)
    {
        TextLabelSnapshot? best = null;

        foreach (var caption in Captions)
        {
            if (!caption.IsUnder(x, y)) continue;
            if (accept is not null && !accept(caption)) continue;

            // Nearest above wins. A block's region reaches down over its own
            // sockets, so the block above reaches into this one too — and the
            // caption the cursor is actually under is the lowest of the ones
            // that claim it. Area only breaks a tie between captions on the
            // same line.
            if (best is { } kept &&
                (kept.Y > caption.Y || (kept.Y == caption.Y && kept.Area <= caption.Area)))
                continue;

            best = caption;
        }

        return best;
    }

    public static readonly UiSnapshot Empty = new(
        ImmutableArray<LootLabelSnapshot>.Empty,
        ImmutableArray<ItemSlotSnapshot>.Empty,
        null);
}
