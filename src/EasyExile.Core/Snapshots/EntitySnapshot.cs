using System.Collections.Immutable;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// One entity as it stood at capture time. Captured state, not a handle: there
/// is no reader here and no method that goes back for more.
/// </summary>
public sealed record EntitySnapshot(
    EntityId Id,
    string Metadata,
    ImmutableArray<string> ComponentNames,
    Vector3? WorldPosition,
    Vector2? GridPosition,
    ImmutableArray<Vital> Vitals,
    EntityKind Kind = EntityKind.Other,
    bool IsAlive = true,
    MonsterRarity Rarity = MonsterRarity.Unknown,
    bool IsPoi = false,
    bool IconComplete = false,
    ImmutableArray<string> Mods = default,
    string? FriendlyName = null,
    ItemSnapshot? Item = null,
    string? DestinationCode = null)
{
    /// <summary>
    /// Worth a marker on the map by default. A point of interest qualifies on
    /// the client's own say-so, whatever its category: that is the whole value
    /// of the MinimapIcon component — the game already decided.
    /// </summary>
    public bool IsInteresting => IsPoi || Kind is
        EntityKind.Monster or EntityKind.Ally or EntityKind.Npc or EntityKind.Chest or
        EntityKind.Transition or EntityKind.OtherPlayer;

    /// <summary>
    /// The second path segment of the metadata — Monsters, Chests, NPC, Terrain
    /// and so on. The coarsest classification the client hands over for free,
    /// and the one a radar filters on first.
    /// </summary>
    public string Category
    {
        get
        {
            var parts = Metadata.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 1 ? parts[1] : "Unknown";
        }
    }

    public bool HasComponent(string name) => ComponentNames.Contains(name);

    /// <summary>
    /// The monster's health pool, or (0, 0) when it has none.
    /// </summary>
    /// <remarks>
    /// A pool of zero maximum is the fail-closed answer everywhere it is used:
    /// a bar cannot be drawn for a fraction that has no denominator.
    /// </remarks>
    public (int Current, int Max) Life
    {
        get
        {
            foreach (var vital in Vitals)
            {
                if (vital.Name == "Health") return (vital.Current, vital.Max);
            }

            return (0, 0);
        }
    }

    /// <summary>
    /// The client's own word for this thing: an exit's destination, or the name
    /// on its map icon. Null when it has neither, which is most entities.
    /// </summary>
    public bool HasFriendlyName => !string.IsNullOrEmpty(FriendlyName);

    /// <summary>The monster's rolled affix mod ids, never default.</summary>
    public ImmutableArray<string> ModIds => Mods.IsDefault ? ImmutableArray<string>.Empty : Mods;
}
