using EasyExile.Core.Contract;
using EasyExile.Core.Memory;

namespace EasyExile.Core.World;

/// <summary>
/// Enumerates the entity containers. They are red-black trees, so the walk is
/// breadth-first with a visited set and a budget — a stale link would otherwise
/// turn enumeration into a hang.
/// </summary>
internal static class EntityWorld
{
    /// <summary>
    /// The span of a node that the walk actually reads: the three links and the
    /// entity the node maps to. Taken from the contract rather than rounded up,
    /// so a layout change moves it instead of silently reading past the object.
    /// </summary>
    private static int NodeSpan =>
        Math.Max(
            Math.Max(GameLayout.Native.MapLeft, GameLayout.Native.MapParent),
            Math.Max(GameLayout.Native.MapRight, GameLayout.Native.MapEntityValue)) + 8;

    public static nint[] Enumerate(
        IMemoryReader memory, nint areaInstance, int containerOffset, EntityTypeCache? types = null,
        int budget = 8000)
    {
        var found = new List<nint>();

        if (!memory.TryReadPointer(areaInstance + containerOffset, out var head) || head == 0)
            return found.ToArray();

        var span = NodeSpan;

        // A node is a handful of pointers inside fifty bytes. Read one field at a
        // time it was four crossings per node, and this walk visits thousands —
        // it was, measured, four fifths of the whole capture. The price of a
        // crossing is the crossing, not the payload, so the node comes over
        // whole.
        if (span is <= 0 or > 4096) return found.ToArray();

        var seen = new HashSet<nint>();
        var seenEntities = new HashSet<nint>();
        var queue = new Queue<nint>();
        queue.Enqueue(head);

        while (queue.Count > 0 && budget-- > 0)
        {
            var node = queue.Dequeue();
            if (node == 0 || !seen.Add(node)) continue;
            if (!GameEntity.IsPlausibleAddress(node)) continue;

            if (!memory.TryReadBytes(node, span, out var bytes)) continue;

            // The mapped entity, read straight from the offset the contract
            // confirmed. Nothing is searched for here: the consumer is told.
            //
            // The type cache is handed in rather than left null. Validity is
            // "does this address still describe an entity", which reads the
            // metadata path — and without the cache that was a chunked string
            // read of a path shared by four hundred siblings, on every sleeping
            // entity, on every capture.
            var entity = Link(bytes, GameLayout.Native.MapEntityValue);

            if (GameEntity.IsPlausibleAddress(entity) &&
                seenEntities.Add(entity) &&
                new GameEntity(memory, entity, types).IsValid)
            {
                found.Add(entity);
            }

            // All three links, as before. Following the parent re-dequeues
            // nodes the walk has already seen, but a seen node costs nothing
            // now that its links came over in one block — and the container is
            // the client's, not ours, so assuming left and right alone reach
            // everything is an assumption we would find out about by silently
            // losing entities.
            Enqueue(queue, Link(bytes, GameLayout.Native.MapLeft));
            Enqueue(queue, Link(bytes, GameLayout.Native.MapParent));
            Enqueue(queue, Link(bytes, GameLayout.Native.MapRight));
        }

        return found.ToArray();
    }

    private static void Enqueue(Queue<nint> queue, nint node)
    {
        if (GameEntity.IsPlausibleAddress(node)) queue.Enqueue(node);
    }

    private static nint Link(byte[] node, int offset) =>
        offset >= 0 && node.Length >= offset + 8 ? (nint)BitConverter.ToInt64(node, offset) : 0;
}
