using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;

namespace EasyExile.Core.World;

/// <summary>
/// The game's own loot tags: the text it draws over each item on the ground,
/// with the rectangle it drew it in.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>Poe2Live.ResolveGroundLabelContainer</c> and
/// <c>Poe2Live.ScanLootLabels</c>.
///
/// Two things here are the reference's design and both matter.
///
/// The container is found by the FLAGS of the elements on the way down, not by
/// child indices. Indices drift every patch; the role bits do not. Three hops,
/// each matching a fingerprint with the visible bit masked off on both sides,
/// backtracking when a branch dead-ends.
///
/// And the scan is confined to that subtree rather than run over the whole UI
/// tree. That confinement is the entire safety property: a stash, vendor or
/// reward panel can easily contain the text "Exalted Orb", and a value chip
/// landing there would be worse than no chip at all.
///
/// Why bother when we already project the item's world position: because the
/// game does not draw its tag at the item. It lays the tags out in a column to
/// keep them from overlapping, so a world-projected chip sits somewhere near the
/// item and nowhere near the name it belongs to. The rect is the game's own
/// arithmetic, already done.
/// </remarks>
internal static class LootLabelReader
{
    /// <summary>Bounded so a pathological tree cannot stall the world walk.</summary>
    private const int MaxNodes = 20000;

    private const int MaxChildren = 8192;

    /// <summary>
    /// The tags, each paired with the UI element it was read from.
    /// </summary>
    /// <remarks>
    /// The element comes back with the tag because finding a tag and knowing
    /// where it is now are two different jobs on two different clocks. Finding
    /// one means walking the tree, which is why this runs a few times a second.
    /// Where it is now changes every time the camera moves — sixty or a hundred
    /// and forty times a second — and once the element is known, asking again is
    /// a handful of reads.
    ///
    /// The address never leaves Core. The renderer is handed rectangles.
    /// </remarks>
    public static ImmutableArray<(LootLabelSnapshot Label, nint Element)> Read(
        IMemoryReader memory, nint inGameState, float uiScale)
    {
        var containers = Containers(memory, inGameState);

        if (containers.Count == 0) return ImmutableArray<(LootLabelSnapshot, nint)>.Empty;

        var visibleBit = 1u << GameLayout.Ui.VisibleBit;
        var labels = ImmutableArray.CreateBuilder<(LootLabelSnapshot, nint)>();

        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();
        var roots = new HashSet<nint>(containers);

        foreach (var container in containers) queue.Enqueue(container);

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            var visible = memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags) &&
                          (flags & visibleBit) != 0;

            // An invisible subtree is pruned rather than descended: the game
            // keeps tags allocated after the item is gone. The container itself
            // is always descended, because its own visibility says nothing about
            // whether it holds live tags.
            if (!visible && !roots.Contains(element)) continue;

            foreach (var child in Children(memory, element)) queue.Enqueue(child);

            var text = memory.TryReadWideString(
                element + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer,
                GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity,
                maxLength: 128);

            if (text is null || text.Length < 2) continue;

            // A tag can carry more than one line; the first is the item name,
            // which is the price key.
            var newline = text.IndexOf('\n');
            var firstLine = (newline >= 0 ? text[..newline] : text).Trim();

            if (firstLine.Length < 2) continue;

            var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);

            labels.Add((
                new LootLabelSnapshot(firstLine, x * uiScale, y * uiScale, w * uiScale, h * uiScale),
                element));
        }

        return labels.ToImmutable();
    }

    /// <summary>
    /// Every element the fingerprint path reaches, not just the first.
    /// </summary>
    /// <remarks>
    /// Taking the first full-depth match was wrong in a way a single run hid:
    /// the layer holding NPC and waypoint nameplates carries the same role bits
    /// as the one holding item tags, so the walk stopped on whichever branch it
    /// met first and the item tags were never scanned.
    ///
    /// Collecting all of them costs one more pass over a shallow tree and cannot
    /// mislabel anything: a chip is only drawn where the tag's text matches a
    /// priced item, and no NPC is called "Exalted Orb".
    /// </remarks>
    private static List<nint> Containers(IMemoryReader memory, nint inGameState)
    {
        var found = new List<nint>();

        if (memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var root) && root != 0)
            Descend(memory, root, 0, found);

        return found;
    }

    private static void Descend(IMemoryReader memory, nint parent, int step, List<nint> found)
    {
        if (step == GameLayout.GroundLabels.Fingerprints.Length)
        {
            found.Add(parent);
            return;
        }

        var visibleBit = 1u << GameLayout.Ui.VisibleBit;
        var target = (uint)GameLayout.GroundLabels.Fingerprints[step] & ~visibleBit;

        foreach (var child in Children(memory, parent))
        {
            if (!memory.TryRead<uint>(child + GameLayout.Ui.Flags, out var flags)) continue;
            if ((flags & ~visibleBit) != target) continue;

            Descend(memory, child, step + 1, found);
        }
    }

    private static IEnumerable<nint> Children(IMemoryReader memory, nint element)
    {
        if (!memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) || first == 0)
            yield break;

        // Children is the vector's First; End is one word further along. Derived
        // rather than exported, because it is not an independent fact.
        if (!memory.TryReadPointer(
                element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last))
            yield break;

        var count = (last - first) / 8;

        if (count is <= 0 or > MaxChildren) yield break;

        for (var i = 0; i < count; i++)
        {
            if (memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                yield return child;
        }
    }
}
