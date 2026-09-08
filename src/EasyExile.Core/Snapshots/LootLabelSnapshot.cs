namespace EasyExile.Core.Snapshots;

/// <summary>
/// One of the game's own loot tags: the name it drew, and where it drew it.
/// </summary>
/// <remarks>
/// The rectangle is in client pixels, already scaled. It is the game's own
/// arithmetic rather than our projection, which is the point: the game lays its
/// tags out in a column so they do not overlap, so an item's tag is routinely
/// nowhere near the item. A value chip belongs on the tag, not on the drop.
/// </remarks>
public sealed record LootLabelSnapshot(string Text, float X, float Y, float Width, float Height)
{
    public float Right => X + Width;

    public float CentreY => Y + (Height / 2f);

    public float Bottom => Y + Height;

    public float CentreX => X + (Width / 2f);
}
