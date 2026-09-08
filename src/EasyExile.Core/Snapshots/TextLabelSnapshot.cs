namespace EasyExile.Core.Snapshots;

/// <summary>
/// A line of text the client is drawing, and where.
/// </summary>
/// <remarks>
/// Deliberately says nothing about what the text IS. The skill panel does not
/// hang an entity off its entries the way a grid cell hangs its item, so there
/// is no structural way to know a label names a skill — it was measured and it
/// does not.
///
/// What settles it instead is the lookup: a label whose text is a skill the
/// advice table knows about is a skill, and one it does not know about is not
/// interesting anyway. That keeps this reader from having to guess, and keeps
/// the guessing where it can be tested.
/// </remarks>
public sealed record TextLabelSnapshot(string Text, float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float Bottom => Y + Height;

    public bool Contains(float x, float y) => x >= X && x <= Right && y >= Y && y <= Bottom;

    public float Area => Width * Height;

    /// <summary>
    /// Whether the cursor is on the block this caption heads.
    /// </summary>
    /// <remarks>
    /// A skill is not a line of text, it is a block: the name, sometimes a
    /// "Command:" line under it, and then the row of round sockets holding the
    /// gem itself and its supports. The name is the only part that is a caption.
    ///
    /// So the region reaches left, past the small icon beside the name, and
    /// DOWN across the sockets — which is where the gem actually is and what a
    /// person points at. Reaching only sideways answered nothing for the one
    /// spot that matters, which is exactly how this was reported.
    ///
    /// Overlapping blocks are settled by the caller taking the nearest caption
    /// above the cursor, not by these numbers.
    /// </remarks>
    public bool IsUnder(float x, float y) =>
        y >= Y && y <= Y + (Height * DownReach) &&
        x <= Right && x >= X - (Height * LeftReach);

    /// <summary>Left of the caption, past the icon that sits beside the name.</summary>
    private const float LeftReach = 2.2f;

    /// <summary>Down from the caption, across the socket row under it.</summary>
    private const float DownReach = 4f;
}
