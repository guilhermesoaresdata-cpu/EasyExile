using System.Collections.Immutable;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The tooltip the game is drawing for the item under the cursor.
/// </summary>
/// <remarks>
/// For a long time this was thought not to exist. Four exhaustive walks of the
/// UI tree found no element whose text was an item's, and all four were asking
/// the wrong field: the client keeps a rendered line as a bare pointer to its
/// characters, not in the std::wstring every other reader uses.
///
/// It does not say which item it belongs to, because the client does not say:
/// the panel carries no entity, unlike the cell it was opened from. The item is
/// whatever the cursor is over, which the renderer knows and a Core reader has
/// no business knowing.
///
/// Knowing where the tooltip is matters twice over. The overlay paints last and
/// has no z-order with the game, so marks belonging to slots the tooltip covers
/// used to land on top of the item's own text — and a badge can now go where it
/// belongs instead, at the start of the line it describes.
/// </remarks>
public sealed record TooltipSnapshot(
    float X, float Y, float Width, float Height,
    ImmutableArray<ModRowSnapshot> ModRows)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    /// <summary>Whether it is drawn over this rectangle.</summary>
    public bool Covers(float x, float y, float width, float height) =>
        x < Right && x + width > X && y < Bottom && y + height > Y;
}

/// <summary>
/// The spot at the start of one mod line.
/// </summary>
/// <remarks>
/// The client draws every tooltip line as a full-width row with its text
/// centred, and pins a small box to the left edge of the rows that carry a mod —
/// and only those. Item Level, Requires and the rest have none.
///
/// That box is both the marker that says "this line is a mod" and the place a
/// badge belongs, which is why this is its rectangle rather than the row's: the
/// game already left the room, so nothing has to be covered to use it.
/// </remarks>
public readonly record struct ModRowSnapshot(float X, float Y, float Width, float Height);
