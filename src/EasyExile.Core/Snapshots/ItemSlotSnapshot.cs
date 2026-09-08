namespace EasyExile.Core.Snapshots;

/// <summary>
/// A visible item slot in the UI, and what sits in it.
/// </summary>
/// <remarks>
/// The rectangle is the slot's, not the tooltip's. The reference makes that
/// choice explicitly and gives the reason: the tooltip is hosted behind
/// invisible containers whose rect arithmetic does not reproduce reliably, so
/// anchoring to it mislocated the chip. The slot is laid out by the same
/// projection as every other element.
/// </remarks>
public sealed record ItemSlotSnapshot(
    ItemSnapshot Item, int Stack, float X, float Y, float Width, float Height,
    EntityId Entity = default)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public float Area => Width * Height;

    public bool Contains(float x, float y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    /// <summary>
    /// One item shown in two places is still one item.
    /// </summary>
    /// <remarks>
    /// While the game shows a tooltip, the tooltip itself is a UI element
    /// carrying the very same item entity — so the sweep finds the item twice
    /// and outlines an empty square at the tooltip's corner. The entity is what
    /// says they are the same thing; the rectangles cannot.
    /// </remarks>
    public static IEnumerable<ItemSlotSnapshot> Distinct(
        IEnumerable<ItemSlotSnapshot> slots, float cursorX, float cursorY)
    {
        var best = new Dictionary<EntityId, ItemSlotSnapshot>();

        foreach (var slot in slots)
        {
            if (!best.TryGetValue(slot.Entity, out var kept))
            {
                best[slot.Entity] = slot;
                continue;
            }

            // The one under the cursor is the one being pointed at, and the
            // tooltip is never where the cursor is. Failing that, the smaller:
            // a preview is drawn larger than the cell it came from.
            var mine = slot.Contains(cursorX, cursorY);
            var theirs = kept.Contains(cursorX, cursorY);

            if (mine && !theirs) best[slot.Entity] = slot;
            else if (mine == theirs && slot.Area < kept.Area) best[slot.Entity] = slot;
        }

        return best.Values;
    }

    /// <summary>Stack size, never below one: a single item is a stack of one.</summary>
    public int Count => Stack < 1 ? 1 : Stack;
}
