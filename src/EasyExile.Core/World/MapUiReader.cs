using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;

namespace EasyExile.Core.World;

/// <summary>
/// Finds the game's map elements and reads their state.
/// </summary>
/// <remarks>
/// Ported from <c>POE2Radar.Core/Game/Poe2Live.cs</c>, methods <c>ReadMap</c>,
/// <c>DiscoverMapElements</c> and <c>TryReadMapElement</c> (MIT).
///
/// The discovery is by signature rather than through the MapParent chain,
/// because the three references disagree about that chain across builds. An
/// element is a map when DefaultShift is exactly (0,-20) and the zoom is
/// plausible. The reference's own note applies here too: the game exposes
/// several such elements, some always on and some always off, and only the one
/// that has been seen both visible and hidden is a genuine toggler. Until a
/// toggle is observed, more than the always-on baseline being visible stands in.
/// </remarks>
internal sealed class MapUiReader
{
    private readonly List<nint> _elements = new();
    private readonly HashSet<nint> _everVisible = new();
    private readonly HashSet<nint> _everHidden = new();

    private nint _discoveredFor = -1;

    /// <summary>Bounded so a stale link cannot turn the walk into a hang.</summary>
    private const int MaxElements = 30000;

    public MapSnapshot Read(IMemoryReader memory, nint inGameState, nint areaInstance)
    {
        if (areaInstance != _discoveredFor || _elements.Count == 0)
        {
            _discoveredFor = areaInstance;
            _elements.Clear();
            _everVisible.Clear();
            _everHidden.Clear();

            Discover(memory, inGameState);
        }

        var visibleCount = 0;

        var any = false;
        var anyState = MapSnapshot.Closed;

        var sawToggler = false;
        var togglerVisible = false;
        var haveToggler = false;
        var togglerState = MapSnapshot.Closed;

        foreach (var element in _elements)
        {
            if (!TryRead(memory, element, out var state)) continue;

            if (state.IsVisible)
            {
                _everVisible.Add(element);
                visibleCount++;
            }
            else
            {
                _everHidden.Add(element);
            }

            if (!any)
            {
                any = true;
                anyState = state;
            }

            // A permanently-on or permanently-off element cannot be the toggle
            // signal; only one seen in both states qualifies.
            if (!_everVisible.Contains(element) || !_everHidden.Contains(element)) continue;

            sawToggler = true;
            if (state.IsVisible) togglerVisible = true;

            if (state.IsVisible || !haveToggler)
            {
                togglerState = state;
                haveToggler = true;
            }
        }

        if (!any) return MapSnapshot.Closed;

        if (sawToggler)
            return togglerState with { IsVisible = togglerVisible };

        // No toggle observed yet in this area: opening the map lights up one
        // element beyond the always-on baseline.
        return anyState with { IsVisible = visibleCount >= 2 };
    }

    /// <summary>
    /// Every candidate and what it currently says. Diagnostics only.
    /// </summary>
    /// <remarks>
    /// The aggregate answer hides which element supplied it, and that is exactly
    /// what a misalignment question needs: whether the game moved the map, or
    /// whether we started reading a different element.
    /// </remarks>
    internal IEnumerable<(nint Element, MapSnapshot State, bool Toggler)> Candidates(
        IMemoryReader memory, nint inGameState, nint areaInstance)
    {
        Read(memory, inGameState, areaInstance);

        foreach (var element in _elements)
        {
            if (!TryRead(memory, element, out var state)) continue;

            yield return (element, state, _everVisible.Contains(element) && _everHidden.Contains(element));
        }
    }

    /// <summary>
    /// An element's absolute position, by summing the parent chain.
    /// </summary>
    /// <remarks>
    /// Diagnostics only. The reference projects from the WINDOW centre and never
    /// looks at where the map element actually sits, so this is here to answer
    /// whether that assumption still holds when a panel is open — not to be used
    /// by the renderer unless the answer says it must.
    ///
    /// The walk is the layout the contract describes: a relative position per
    /// element, plus a modifier that applies only while its flag bit is set,
    /// scaled by the element's own multiplier.
    /// </remarks>
    /// <summary>
    /// Where an element actually sits on screen, walking up the parents.
    /// </summary>
    /// <remarks>
    /// One read per node, not one per field. A node contributes its relative
    /// position, possibly a position modifier gated on a flag, and its parent —
    /// six crossings for values that live inside a few hundred bytes of each
    /// other. This is asked of every ground tag on every frame, so the
    /// difference is the difference between smooth and not.
    /// </remarks>
    internal static (float X, float Y, float W, float H) AbsoluteRect(IMemoryReader memory, nint element)
    {
        var from = Math.Min(
            Math.Min(GameLayout.Ui.Parent, GameLayout.Ui.RelativeX),
            Math.Min(GameLayout.Ui.PositionModifierX, Math.Min(GameLayout.Ui.Flags, GameLayout.Ui.SizeWidth)));

        var to = Math.Max(
            Math.Max(GameLayout.Ui.Parent + 8, GameLayout.Ui.RelativeY + 4),
            Math.Max(GameLayout.Ui.PositionModifierY + 4,
                Math.Max(GameLayout.Ui.Flags + 4, GameLayout.Ui.SizeHeight + 4)));

        var span = to - from;

        // A layout that scattered these would ask for an absurd read; take the
        // slow path rather than dragging a megabyte across per node.
        if (from < 0 || span is <= 0 or > 8192) return Walk(memory, element);

        float x = 0, y = 0, w = 0, h = 0;

        var node = element;

        for (var depth = 0; depth < 32 && node != 0; depth++)
        {
            if (!memory.TryReadBytes(node + from, span, out var bytes)) break;

            x += Float(bytes, GameLayout.Ui.RelativeX - from);
            y += Float(bytes, GameLayout.Ui.RelativeY - from);

            var flags = (uint)BitConverter.ToInt32(bytes, GameLayout.Ui.Flags - from);

            if ((flags & (1u << GameLayout.Ui.ModifyPositionBit)) != 0)
            {
                x += Float(bytes, GameLayout.Ui.PositionModifierX - from);
                y += Float(bytes, GameLayout.Ui.PositionModifierY - from);
            }

            if (depth == 0)
            {
                w = Float(bytes, GameLayout.Ui.SizeWidth - from);
                h = Float(bytes, GameLayout.Ui.SizeHeight - from);
            }

            var parent = (nint)BitConverter.ToInt64(bytes, GameLayout.Ui.Parent - from);

            if (parent == node) break;

            node = parent;
        }

        return (x, y, w, h);
    }

    /// <summary>
    /// An element's offset from its parent and its size, in one crossing.
    /// </summary>
    /// <remarks>
    /// <see cref="AbsoluteRect"/> pays a read per ancestor, which is the right
    /// price when the answer is wanted for one element out of nowhere. Walking a
    /// container's children is the other case: the parent's absolute rect is
    /// already known, so each child costs one read instead of ten.
    /// </remarks>
    internal static (float X, float Y, float W, float H) LocalRect(
        IMemoryReader memory, nint element)
    {
        var from = Math.Min(
            GameLayout.Ui.RelativeX,
            Math.Min(GameLayout.Ui.PositionModifierX,
                Math.Min(GameLayout.Ui.Flags, GameLayout.Ui.SizeWidth)));

        var to = Math.Max(
            GameLayout.Ui.RelativeY + 4,
            Math.Max(GameLayout.Ui.PositionModifierY + 4,
                Math.Max(GameLayout.Ui.Flags + 4, GameLayout.Ui.SizeHeight + 4)));

        var span = to - from;

        if (from < 0 || span is <= 0 or > 8192) return (0f, 0f, 0f, 0f);
        if (!memory.TryReadBytes(element + from, span, out var bytes)) return (0f, 0f, 0f, 0f);

        var x = Float(bytes, GameLayout.Ui.RelativeX - from);
        var y = Float(bytes, GameLayout.Ui.RelativeY - from);

        var flags = (uint)BitConverter.ToInt32(bytes, GameLayout.Ui.Flags - from);

        if ((flags & (1u << GameLayout.Ui.ModifyPositionBit)) != 0)
        {
            x += Float(bytes, GameLayout.Ui.PositionModifierX - from);
            y += Float(bytes, GameLayout.Ui.PositionModifierY - from);
        }

        return (x, y,
            Float(bytes, GameLayout.Ui.SizeWidth - from),
            Float(bytes, GameLayout.Ui.SizeHeight - from));
    }

    private static float Float(byte[] bytes, int at) =>
        at >= 0 && bytes.Length >= at + 4 ? BitConverter.ToSingle(bytes, at) : 0f;

    /// <summary>The field-at-a-time walk, kept for a layout the block read cannot span.</summary>
    internal static (float X, float Y, float W, float H) Walk(IMemoryReader memory, nint element)
    {
        float x = 0, y = 0;

        var node = element;

        for (var depth = 0; depth < 32 && node != 0; depth++)
        {
            if (memory.TryRead<float>(node + GameLayout.Ui.RelativeX, out var rx) &&
                memory.TryRead<float>(node + GameLayout.Ui.RelativeY, out var ry))
            {
                x += rx;
                y += ry;
            }

            if (memory.TryRead<uint>(node + GameLayout.Ui.Flags, out var flags) &&
                (flags & (1u << GameLayout.Ui.ModifyPositionBit)) != 0 &&
                memory.TryRead<float>(node + GameLayout.Ui.PositionModifierX, out var mx) &&
                memory.TryRead<float>(node + GameLayout.Ui.PositionModifierY, out var my))
            {
                x += mx;
                y += my;
            }

            if (!memory.TryReadPointer(node + GameLayout.Ui.Parent, out var parent) || parent == node) break;

            node = parent;
        }

        memory.TryRead<float>(element + GameLayout.Ui.SizeWidth, out var w);
        memory.TryRead<float>(element + GameLayout.Ui.SizeHeight, out var h);

        return (x, y, w, h);
    }

    private void Discover(IMemoryReader memory, nint inGameState)
    {
        if (!memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var uiRoot) || uiRoot == 0) return;

        var seen = new HashSet<nint>();
        var queue = new Queue<nint>();
        queue.Enqueue(uiRoot);

        while (queue.Count > 0 && seen.Count < MaxElements)
        {
            var element = queue.Dequeue();

            if (element == 0 || (long)element % 8 != 0 || !seen.Add(element)) continue;

            // A live UiElement points back at itself; a freed slot does not.
            if (!memory.TryReadPointer(element + GameLayout.Ui.Self, out var self) || self != element) continue;

            if (IsMapElement(memory, element)) _elements.Add(element);

            if (!memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) || first == 0) continue;
            if (!memory.TryReadPointer(element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last)) continue;

            var count = ((long)last - (long)first) / 8;
            if (count is <= 0 or > 8192) continue;

            for (long i = 0; i < count; i++)
            {
                if (memory.TryReadPointer(first + (nint)(i * 8), out var child)) queue.Enqueue(child);
            }
        }
    }

    /// <summary>
    /// The signature. Decorative elements carry the (0,-20) pair too — 51 of
    /// them live — so the zoom is part of the test and not a bonus check.
    /// </summary>
    private static bool IsMapElement(IMemoryReader memory, nint element)
    {
        if (!memory.TryRead<float>(element + GameLayout.MapUi.DefaultShift, out var x) || x != 0f) return false;
        if (!memory.TryRead<float>(element + GameLayout.MapUi.DefaultShift + 4, out var y) || y != -20f) return false;
        if (!memory.TryRead<float>(element + GameLayout.MapUi.Zoom, out var zoom)) return false;

        return float.IsFinite(zoom) && zoom is > 0.05f and < 8f;
    }

    private static bool TryRead(IMemoryReader memory, nint element, out MapSnapshot state)
    {
        state = MapSnapshot.Closed;

        if (!memory.TryRead<float>(element + GameLayout.MapUi.DefaultShift + 4, out var defaultY) || defaultY != -20f)
            return false;

        memory.TryRead<float>(element + GameLayout.MapUi.Shift, out var shiftX);
        memory.TryRead<float>(element + GameLayout.MapUi.Shift + 4, out var shiftY);
        memory.TryRead<float>(element + GameLayout.MapUi.Zoom, out var zoom);

        memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags);

        var visible = (flags & (1u << GameLayout.Ui.VisibleBit)) != 0;

        // Where the element actually sits. The game repositions it when a panel
        // opens — the inventory moved it 493 UI units left, measured live —
        // while Shift stayed at zero throughout.
        var (x, y, _, _) = AbsoluteRect(memory, element);

        state = new MapSnapshot(visible, shiftX, shiftY, zoom, x, y);
        return true;
    }
}
