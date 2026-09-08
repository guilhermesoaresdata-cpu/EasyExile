namespace EasyExile.Core.World;

/// <summary>
/// What an entity's TYPE says, remembered for as long as the area lives.
/// </summary>
/// <remarks>
/// Two facts hang off an entity's <c>details</c> pointer — its type descriptor —
/// rather than off the entity: the metadata path, and the table mapping
/// component names to slots. Every skeleton in an area shares one of each.
///
/// Read per entity they dominated a capture: a chunked UTF-16 string read and
/// roughly fifteen UTF-8 reads, on five hundred entities, thirty times a second,
/// for answers that cannot change. Measured, moving them here took a capture
/// from 179 ms to 44 ms.
///
/// Owned by whoever is walking one area through one reader, and dropped when the
/// area changes. Never static: these are addresses, and an address means
/// something else in the next area.
/// </remarks>
internal sealed class EntityTypeCache
{
    public Dictionary<nint, ComponentLayout> Layouts { get; } = new();

    public Dictionary<nint, string?> Paths { get; } = new();

    /// <summary>
    /// The type descriptor's component table pointer, which is itself a fact
    /// about the type. Reading it cost one crossing per component resolved —
    /// six or seven per entity — for a value that is fixed for the area.
    /// </summary>
    public Dictionary<nint, nint> Lookups { get; } = new();

    public void Clear()
    {
        Layouts.Clear();
        Paths.Clear();
        Lookups.Clear();
    }
}
