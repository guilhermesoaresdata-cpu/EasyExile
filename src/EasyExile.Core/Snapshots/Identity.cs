namespace EasyExile.Core.Snapshots;

/// <summary>
/// Identity of an area instance, stable for as long as that instance lives.
/// </summary>
/// <remarks>
/// Opaque on purpose. The client does expose an area address, and that address
/// is the only identity available — <c>AreaInstance.AreaInfo</c> is live
/// validated but not approved for export, so there is no name or id to use
/// instead. Wrapping it means a consumer can compare and key on identity
/// without ever holding something it could mistake for a readable pointer.
/// </remarks>
public readonly record struct AreaId
{
    private readonly long _value;

    internal AreaId(nint address) => _value = address;

    public bool IsValid => _value != 0;

    public override string ToString() => IsValid ? $"area:{_value:X}" : "area:none";
}

/// <summary>
/// Identity of an entity within one area. Comparable and usable as a dictionary
/// key, which is what a renderer needs to keep a marker attached to the same
/// monster across updates.
/// </summary>
/// <remarks>
/// Same reasoning as <see cref="AreaId"/>: <c>Entity.Id</c> exists in the client
/// but is not an approved export, so the allocation address is the only identity
/// on offer. It is never valid across an area change — every entity is
/// reallocated, proven live during the P1 closeout — which is what
/// <see cref="WorldSnapshot.Epoch"/> is for.
/// </remarks>
public readonly record struct EntityId
{
    private readonly long _value;

    internal EntityId(nint address) => _value = address;

    public bool IsValid => _value != 0;

    /// <summary>
    /// The raw address. Internal, and only for diagnostics that walk the client
    /// directly — nothing outside the Core is allowed to see it.
    /// </summary>
    internal nint Address => (nint)_value;

    public override string ToString() => IsValid ? $"entity:{_value:X}" : "entity:none";
}
