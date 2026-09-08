namespace EasyExile.Core.Snapshots;

/// <summary>
/// A monster's rarity, the same scale the game colours its name with.
/// </summary>
/// <remarks>
/// The values match the client's own: 0 normal, 1 magic, 2 rare, 3 unique.
/// <see cref="Unknown"/> is not one of the client's — it means the field could
/// not be read, and the map draws such a monster as ordinary rather than
/// inventing a rank for it.
/// </remarks>
public enum MonsterRarity
{
    Unknown = -1,
    Normal = 0,
    Magic = 1,
    Rare = 2,
    Unique = 3,
}
