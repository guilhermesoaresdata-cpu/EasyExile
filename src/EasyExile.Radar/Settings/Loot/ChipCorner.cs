namespace EasyExile.Radar.Settings.Loot;

/// <summary>
/// Where a value chip sits relative to the slot it describes.
/// </summary>
/// <remarks>
/// Not a cosmetic preference. The game writes its own things in these corners —
/// stack counts, socket art, quality marks — and which corner is free depends on
/// the panel and on what the item is. There is no answer that is right
/// everywhere, so it is a setting.
/// </remarks>
public enum ChipCorner
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    Above,
    Below,
}
