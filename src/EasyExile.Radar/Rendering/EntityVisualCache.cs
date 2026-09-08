using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// Per-entity visual state that survives between frames but never between areas.
/// </summary>
/// <remarks>
/// An <see cref="EntityId"/> is only meaningful inside the epoch it came from:
/// crossing an area frees every entity and the client reuses the memory, so the
/// same id in a new epoch is a different creature. The cache key is therefore
/// conceptually (epoch, id), implemented as "notice the epoch moved and drop
/// everything" — cheaper than a composite key, and it also releases the entries
/// for entities that no longer exist.
///
/// Without this, a label from the previous zone would sit on whatever now
/// occupies that address, which looks like working software and is not.
/// </remarks>
public sealed class EntityVisualCache<T>
{
    private readonly Dictionary<EntityId, T> _entries = new();

    private long _epoch = -1;

    public int Count => _entries.Count;

    public long Epoch => _epoch;

    /// <summary>Number of times the epoch moved and the cache was dropped.</summary>
    public int Invalidations { get; private set; }

    public T Get(long epoch, EntityId id, Func<T> create)
    {
        if (epoch != _epoch)
        {
            _epoch = epoch;
            Clear();
            Invalidations++;
        }

        if (_entries.TryGetValue(id, out var existing)) return existing;

        var created = create();
        _entries[id] = created;

        return created;
    }

    /// <summary>
    /// Drops everything without touching the recorded epoch. Used when the world
    /// goes away for a reason other than a transition, such as the character
    /// leaving the area entirely.
    /// </summary>
    public void Clear() => _entries.Clear();
}
