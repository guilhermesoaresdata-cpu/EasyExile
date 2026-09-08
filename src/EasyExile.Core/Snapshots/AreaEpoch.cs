namespace EasyExile.Core.Snapshots;

/// <summary>
/// Counts how many distinct area instances this session has seen.
/// </summary>
/// <remarks>
/// The one piece of state the Core keeps, and it earns its place: an area
/// address alone cannot tell "still in the same instance" from "left and came
/// back", because the client is free to reuse the allocation. A consumer that
/// caches anything keyed on <see cref="EntityId"/> only needs to watch this
/// number to know when the whole cache became meaningless.
///
/// The address alone is not enough, and this is not theoretical: walking from
/// one zone to another, the client handed back the SAME AreaInstance allocation.
/// The epoch never moved, so every per-area cache kept answering for the zone
/// that had been left — the terrain most visibly, which simply did not change.
/// The area's own code is the second signal, and the pair is what identity now
/// means here.
///
/// Not thread-safe, and not meant to be: capture is pull-based and runs on the
/// caller's thread.
/// </remarks>
internal sealed class AreaEpoch
{
    private nint _lastArea;
    private string? _lastCode;
    private long _epoch;

    /// <param name="areaCode">
    /// The area's own identifier, e.g. <c>G2_5_1</c>. Null when unreadable, in
    /// which case the address decides alone.
    /// </param>
    public long Observe(nint areaInstance, string? areaCode = null)
    {
        var moved = areaInstance != _lastArea ||
                    (areaCode is not null && _lastCode is not null &&
                     !string.Equals(areaCode, _lastCode, StringComparison.Ordinal));

        if (moved) _epoch++;

        _lastArea = areaInstance;

        if (areaCode is not null) _lastCode = areaCode;

        return _epoch;
    }
}
