using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;

namespace EasyExile.Core.World;

/// <summary>
/// Every visible item slot in the UI, with what sits in it.
/// </summary>
/// <remarks>
/// Port of POE2Radar (MIT) <c>Poe2Live.ReadHoveredItem</c>, with one deliberate
/// difference: the reference takes the cursor as an argument and returns the one
/// slot under it. This publishes them all and lets the renderer pick.
///
/// That inversion buys two things. The cursor moves every frame and the UI tree
/// walk cannot run every frame, so passing the cursor in would pin the answer to
/// wherever the pointer happened to be one world tick ago. And it keeps the
/// cursor — a render-thread concern — out of a Core reader that has no business
/// knowing about input.
///
/// One field covers inventory, stash, vendor, reward grids and the flask bar,
/// which is why this is a slot reader and not five panel readers.
/// </remarks>
internal static class ItemSlotReader
{
    private const int MaxNodes = 40000;
    private const int MaxChildren = 8192;

    /// <summary>A slot must be big enough to be an icon and small enough not to be a panel.</summary>
    private const float MinSide = 8f;

    private const float MaxSide = 400f;

    // ---- the tooltip's own geometry, in UI units ----------------------------

    /// <summary>A line of tooltip text measured 26 high on every row of it.</summary>
    private const float MinRow = 18f;

    private const float MaxRow = 44f;

    /// <summary>
    /// How deep the tooltip may hang from the UI root.
    /// </summary>
    /// <remarks>
    /// A dump put it at depth 2, and a filter built on that one measurement
    /// found nothing live - the candidate list came back full of inventory
    /// panels with the tooltip nowhere in it. One sample is where it sits once,
    /// not where it may sit.
    /// </remarks>
    private const int MaxTooltipDepth = 10;

    /// <summary>How far inside it the mod block sits. Measured at 2.</summary>
    private const int MaxBlockDepth = 3;

    /// <summary>
    /// An item has two to six explicit mods worth finding this way.
    /// </summary>
    /// <remarks>
    /// Two, not one. A single row proves nothing about being evenly stacked -
    /// every container with one child of about the right size qualifies - and
    /// the first run of this rule duly picked a piece of the quest tracker. The
    /// cost is that an item with exactly one explicit mod gets no badge, which
    /// is a fair trade for never pointing at the wrong panel.
    /// </remarks>
    private const int MinModRows = 2;

    private const int MaxModRows = 8;

    /// <summary>A mod line runs most of the tooltip's width.</summary>
    private const float MinRowWidth = 300f;

    /// <summary>How many candidates are worth measuring in one sweep.</summary>
    private const int MaxPanels = 256;

    private const float MinPanel = 300f;

    /// <summary>
    /// A tooltip is a panel, not a screen.
    /// </summary>
    /// <remarks>
    /// These were loose enough to admit the inventory and the stash - a
    /// thousand units wide and the full sixteen hundred tall - and the search
    /// then walked their entire subtrees looking for rows. Twenty milliseconds
    /// a sweep, eight sweeps a second, to look inside things that could never
    /// be the answer.
    /// </remarks>
    private const float MaxPanel = 1000f;

    private const float MinPanelHeight = 100f;

    private const float MaxPanelHeight = 900f;

    /// <summary>How many candidates are worth walking into.</summary>
    private const int MaxSearched = 6;

    /// <summary>How many nodes one candidate may cost before it is abandoned.</summary>
    private const int MaxGathered = 120;

    /// <summary>A caption worth keeping is a name, not a paragraph and not a digit.</summary>
    private const int MinLabel = 3;

    private const int MaxLabel = 48;

    public static (
        ImmutableArray<ItemSlotSnapshot> Slots,
        TooltipSnapshot? Tooltip,
        ImmutableArray<TextLabelSnapshot> Labels) Read(
        IMemoryReader memory, nint inGameState, float uiScale, CaptureCaches caches,
        bool readLabels = false)
    {
        if (!memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var root) || root == 0)
            return (ImmutableArray<ItemSlotSnapshot>.Empty, null,
                ImmutableArray<TextLabelSnapshot>.Empty);

        var visibleBit = 1u << GameLayout.Ui.VisibleBit;
        var slots = ImmutableArray.CreateBuilder<ItemSlotSnapshot>();

        // Containers big enough to be the item panel. The panel carries no
        // entity — that was assumed for a while and measured false — so it is
        // found by its shape instead, and the child count comes free from the
        // vector this walk already reads.
        var panels = new List<nint>();


        // Off unless something needs it. Reading a line of text on every visible
        // node is two more crossings per node, and this walk visits thousands —
        // a cost worth paying for a feature that is on and worth nothing for one
        // that is not.
        var labels = readLabels
            ? ImmutableArray.CreateBuilder<TextLabelSnapshot>()
            : null;

        var queue = new Queue<(nint Element, int Depth)>();
        var seen = new HashSet<nint>();

        queue.Enqueue((root, 0));

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            var (element, depth) = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            var visible = memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags) &&
                          (flags & visibleBit) != 0;

            // A closed panel keeps its slots populated, so descending an
            // invisible subtree would price a stash the user cannot see.
            if (!visible && element != root) continue;

            if (memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) && first != 0 &&
                memory.TryReadPointer(element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last))
            {
                var count = (last - first) / 8;

                if (count is > 0 and <= MaxChildren)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (memory.TryReadPointer(first + (i * 8), out var child))
                            queue.Enqueue((child, depth + 1));
                    }
                }

                // The item tooltip hangs two levels from the UI root, so the
                // search for it is shallow and cheap. Measured, not assumed: a
                // dump taken with one open put it at depth 2 with two children.
                //
                // Sized here rather than later, and that is the whole of why an
                // earlier version found nothing: the root has 124 children, a
                // breadth-first walk reaches every one of them before it reaches
                // depth 2, and a cap on the candidate list filled up with them
                // while the tooltip was still a level away. One extra read
                // apiece keeps the list to the handful that could be a panel.
                if (depth <= MaxTooltipDepth && count is >= 1 and <= 24 &&
                    panels.Count < MaxPanels)
                {
                    var (_, _, ew, eh) = MapUiReader.LocalRect(memory, element);

                    if (ew is >= MinPanel and <= MaxPanel && eh >= MinPanelHeight)
                    {
                        panels.Add(element);
                    }
                }
            }

            if (element == root) continue;

            if (labels is not null) Label(memory, element, uiScale, labels);

            if (!memory.TryReadPointer(element + GameLayout.Ui.TileSlotItem, out var item) || item == 0)
                continue;

            var entity = new GameEntity(memory, item, caches.Types);

            // Identity, not Item: the slot points at the item itself, with no
            // ground wrapper to unwrap.
            if (entity.ItemIdentity() is not { HasIdentity: true } identity) continue;

            var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);

            var width = w * uiScale;
            var height = h * uiScale;

            if (width is < MinSide or > MaxSide || height is < MinSide or > MaxSide) continue;

            slots.Add(new ItemSlotSnapshot(
                identity, entity.StackCount(), x * uiScale, y * uiScale, width, height,
                new EntityId(item)));
        }

        return (
            slots.ToImmutable(),
            Tooltip(memory, panels, uiScale),
            labels?.ToImmutable() ?? ImmutableArray<TextLabelSnapshot>.Empty);
    }

    /// <summary>
    /// One drawn line of text, when the element is drawing one worth keeping.
    /// </summary>
    /// <remarks>
    /// The string is filtered before the rectangle is measured, and that order
    /// is the whole cost control: reading the text is one crossing, measuring
    /// where it sits is one per ancestor. Most elements point at something that
    /// is not a caption at all — the field holds stale pointers, and an early
    /// dump of it printed CJK gibberish — so the cheap test runs first.
    /// </remarks>
    private static void Label(
        IMemoryReader memory, nint element, float uiScale,
        ImmutableArray<TextLabelSnapshot>.Builder into)
    {
        // Two fields, because the client uses two kinds of element for what
        // looks like one column. In the skills panel the minion rows keep their
        // name as a bare pointer at LineText and the rows with a DPS or DMG
        // readout keep it in the std::wstring at Text - same width, same height,
        // same place on screen, different class underneath.
        //
        // Reading only the first lost exactly those rows, which is how it was
        // reported: "Spark and Volcano do not show".
        var text = Caption(memory, element);

        if (text is null || text.Length is < MinLabel or > MaxLabel) return;

        foreach (var c in text)
        {
            if (c is < (char)32 or > (char)126) return;
        }

        var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);

        if (w <= 0 || h <= 0) return;

        into.Add(new TextLabelSnapshot(
            text, x * uiScale, y * uiScale, w * uiScale, h * uiScale));
    }

    /// <summary>Whichever of the two fields this element keeps its caption in.</summary>
    private static string? Caption(IMemoryReader memory, nint element)
    {
        if (memory.TryReadPointer(element + GameLayout.Ui.LineText, out var at) && at != 0 &&
            memory.TryReadUtf16(at, MaxLabel) is { Length: >= MinLabel } line)
            return line;

        return memory.TryReadWideString(
            element + GameLayout.Ui.Text,
            GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity, MaxLabel);
    }

    /// <summary>
    /// The item tooltip among the candidates, and where each mod line starts.
    /// </summary>
    /// <remarks>
    /// The rule comes from a dump of a live tooltip rather than from a guess,
    /// and the guess it replaces was wrong in an instructive way: an earlier
    /// version looked for a small box pinned to the left of each row, which
    /// existed on the one cached tooltip that had been examined and on nothing
    /// else.
    ///
    /// What actually holds is plainer. The mod lines live in a container of
    /// their own, and every one of them is the same width and the same height
    /// and sits exactly one height below the last. Nothing else in a tooltip
    /// looks like that: the name, the item level, the requirements and the
    /// separators are all different sizes from each other.
    ///
    /// There are TWO such containers - the plain lines and the advanced ones
    /// carrying roll ranges - and which is drawn depends on a client setting, so
    /// the visible one wins.
    /// </remarks>
    private static TooltipSnapshot? Tooltip(IMemoryReader memory, List<nint> panels, float uiScale)
    {
        TooltipSnapshot? best = null;

        var searched = 0;

        foreach (var panel in panels)
        {
            if (searched >= MaxSearched) break;

            var (px, py, pw, ph) = MapUiReader.AbsoluteRect(memory, panel);

            if (pw is < MinPanel or > MaxPanel) continue;
            if (ph is < MinPanelHeight or > MaxPanelHeight) continue;

            searched++;

            var rows = Stack(memory, panel, px, py, uiScale);

            if (rows.Length == 0) continue;

            var found = new TooltipSnapshot(
                px * uiScale, py * uiScale, pw * uiScale, ph * uiScale, rows);

            // More lines means more of the item, which is the one being read
            // rather than a comparison drawn beside it.
            if (best is null || found.ModRows.Length > best.ModRows.Length) best = found;
        }

        return best;
    }

    /// <summary>
    /// The longest run of identical, evenly stacked rows under this element.
    /// </summary>
    /// <remarks>
    /// A run rather than a whole container, and the difference is the entire
    /// bug. Requiring every child of a block to match reads well and fails on
    /// the real thing: with advanced descriptions on, the block holding the mod
    /// lines also holds an 84-wide column of tier labels beside them, so one odd
    /// child disqualified the four good ones. The same layout also leaves one
    /// mod row OUTSIDE the block entirely, as a sibling.
    ///
    /// Collecting row-shaped elements and taking the longest evenly spaced run
    /// of identical ones handles both, and it stops caring which container the
    /// client chose to put them in - which is the part that kept changing.
    ///
    /// Positions are accumulated on the way down rather than resolved per node:
    /// a child's offset is relative to its parent, so carrying the parent's
    /// origin costs one read per node instead of one walk to the root.
    /// </remarks>
    private static ImmutableArray<ModRowSnapshot> Stack(
        IMemoryReader memory, nint element, float originX, float originY, float uiScale)
    {
        var rows = new List<(float X, float Y, float W, float H)>();

        Gather(memory, element, originX, originY, 0, rows, new HashSet<nint>());

        if (rows.Count < MinModRows) return ImmutableArray<ModRowSnapshot>.Empty;

        var best = new List<(float X, float Y, float W, float H)>();

        foreach (var group in rows.GroupBy(r => ((int)r.W, (int)r.X)))
        {
            // Every row of the group, not only a contiguous run of them. With
            // advanced descriptions on the client splits the mods into prefix
            // and suffix groups with a label between, so insisting on
            // contiguity found three of six and the count guard then rejected
            // the lot.
            //
            // Deduplicated by position, because a row is reachable through more
            // than one path and the same rectangle turning up twice would
            // inflate the count past the number of rolls.
            var ordered = group
                .GroupBy(r => (int)r.Y)
                .Select(g => g.First())
                .OrderBy(r => r.Y)
                .ToList();

            if (ordered.Count > best.Count) best = ordered;
        }

        if (best.Count is < MinModRows or > MaxModRows)
            return ImmutableArray<ModRowSnapshot>.Empty;

        return best
            .Select(r => new ModRowSnapshot(r.X * uiScale, r.Y * uiScale, r.W * uiScale, r.H * uiScale))
            .ToImmutableArray();
    }

    /// <summary>Every drawn, row-shaped element under here, in absolute units.</summary>
    private static void Gather(
        IMemoryReader memory, nint element, float originX, float originY, int depth,
        List<(float X, float Y, float W, float H)> into, HashSet<nint> visited)
    {
        if (!visited.Add(element)) return;

        // Bounded twice: by depth, and by how many nodes it is willing to look
        // at at all. Without the second, one candidate that happens to be a
        // container of hundreds costs more than the whole rest of the sweep.
        if (depth > MaxBlockDepth || into.Count > 64 || visited.Count > MaxGathered) return;

        foreach (var child in Children(memory, element))
        {
            if (!Drawn(memory, child)) continue;

            var (dx, dy, w, h) = MapUiReader.LocalRect(memory, child);

            var x = originX + dx;
            var y = originY + dy;

            if (h is >= MinRow and <= MaxRow && w >= MinRowWidth) into.Add((x, y, w, h));

            Gather(memory, child, x, y, depth + 1, into, visited);
        }
    }

    private static bool Drawn(IMemoryReader memory, nint element) =>
        memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags) &&
        (flags & (1u << GameLayout.Ui.VisibleBit)) != 0;

    private static List<nint> Children(IMemoryReader memory, nint element)
    {
        var children = new List<nint>();

        if (!memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) || first == 0 ||
            !memory.TryReadPointer(
                element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last))
            return children;

        var count = (last - first) / 8;

        if (count is <= 0 or > MaxChildren) return children;

        for (var i = 0; i < count; i++)
        {
            if (memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                children.Add(child);
        }

        return children;
    }
}
