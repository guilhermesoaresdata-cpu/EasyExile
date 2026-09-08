using System.Globalization;
using System.Text;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Core.Diagnostics;

/// <summary>
/// The client's UI, written down in a form that answers questions afterwards.
/// </summary>
/// <remarks>
/// This exists because the alternative kept failing. A probe only sees what is
/// on screen while it runs, so every question about a panel needed that panel
/// open at the same instant — and each miss came back looking exactly like a
/// real negative result. More of a session went to that coincidence than to any
/// bug in it.
///
/// Three things make it worth reading rather than merely large.
///
/// It is aimed. The subtree under the cursor is a few dozen lines; the whole
/// tree is fifty thousand and answers nothing you did not already know to ask.
///
/// It is machine-readable. One JSON object per line, so a question can be
/// answered by a query instead of by scrolling — including questions nobody
/// thought of when the file was written.
///
/// It shows the bytes. Every field this project reads was found by seeing a
/// number on the screen and hunting for where it lives, so a dump limited to the
/// fields already known can only confirm what is known. The raw window, read as
/// pointer, integer and float at once, is how the next offset gets found.
/// </remarks>
internal static class UiTreeDump
{
    private const int MaxDepth = 40;

    /// <summary>How many levels of neighbours count as related.</summary>
    private const int SiblingLevels = 3;

    private const int MaxSiblings = 40;

    /// <summary>Walk it and write it. Returns the file, or null if there was nothing.</summary>
    internal static string? Save(
        IMemoryReader memory, nint moduleBase, string directory, DumpOptions options)
    {
        var chain = GameSession.ResolveChain(memory, moduleBase);

        if (chain is null) return null;

        if (!memory.TryReadPointer(chain.InGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
            return null;

        var lines = new List<string>();

        // Search answers a different question and returns a different shape:
        // not a subtree, but every place a value can be seen, nearest the root
        // first - because the shallowest path is the one worth writing code
        // against.
        if (options.Scope == DumpScope.Search)
        {
            var complete = Search(memory, root, options, lines);

            lines.Insert(0, Context(memory, chain, options, lines.Count, complete));

            return Write(directory, options, lines);
        }

        // Where the cursor is, in full detail, before the broad pass. Depth and
        // breadth are different axes and there is no reason to make anyone pick
        // one: the aimed part is small enough to carry its bytes, and the rest
        // is light enough to carry the whole screen.
        var aimed = options.CursorX > 0f || options.CursorY > 0f
            ? Pick(memory, root, options)
            : null;

        var start = options.Scope == DumpScope.UnderCursor ? aimed ?? root : root;

        if (aimed is { } focus && options.Scope == DumpScope.UnderCursor)
        {
            var above = Ancestors(memory, focus);

            foreach (var (element, depth) in above)
                lines.Add(Node(memory, element, depth, options, ancestor: true, root));

            // The neighbours, for the nearest few levels only. A rule that has
            // to tell one row from another needs to see the other rows - the
            // two mod blocks that took an evening were siblings - and going any
            // wider than that is just the screen again.
            foreach (var (element, depth) in above.TakeLast(SiblingLevels))
            {
                foreach (var sibling in Children(memory, element).Take(MaxSiblings))
                {
                    if (sibling == focus) continue;

                    lines.Add(
                        Node(memory, sibling, depth + 1, options, ancestor: false, root)
                            .Replace("\"kind\":\"node\"", "\"kind\":\"irmao\""));
                }
            }
        }

        Walk(memory, start, 0, lines, new HashSet<nint>(), options, root);

        lines.Insert(0, Context(memory, chain, options, lines.Count, lines.Count < options.MaxNodes));

        return Write(directory, options, lines);
    }

    private static string? Write(string directory, DumpOptions options, List<string> lines)
    {
        try
        {
            Directory.CreateDirectory(directory);

            var name = options.Scope switch
            {
                DumpScope.UnderCursor => "pick",
                DumpScope.All => "all",
                DumpScope.Search => "find",
                _ => "ui",
            };

            var path = Path.Combine(directory, $"{name}-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");

            File.WriteAllLines(path, lines);

            return path;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Every element whose text or raw bytes hold the needle, shallowest first.
    /// </summary>
    /// <remarks>
    /// Breadth-first on purpose: the first match is the one nearest the root,
    /// and that is the path worth writing code against. Numbers are matched as
    /// bytes as well as text, because a value seen on screen is usually being
    /// read from a field rather than stored as a caption.
    /// </remarks>
    private static bool Search(
        IMemoryReader memory, nint root, DumpOptions options, List<string> lines)
    {
        if (options.Needle is not { Length: > 0 } needle) return true;

        var asNumber = int.TryParse(needle, out var number);
        var asFloat = float.TryParse(needle, out var real);

        var queue = new Queue<(nint Element, int Depth)>();
        var seen = new HashSet<nint>();

        queue.Enqueue((root, 0));

        while (queue.Count > 0)
        {
            if (seen.Count >= options.MaxNodes || lines.Count >= options.HexNodes) return false;

            var (element, depth) = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            foreach (var child in Children(memory, element)) queue.Enqueue((child, depth + 1));

            if (!Matches(memory, element, needle, asNumber, number, asFloat, real, out var where))
                continue;

            lines.Add(
                Node(memory, element, depth, options, ancestor: false, root)
                    .Replace("\"kind\":\"node\"", $"\"kind\":\"hit\",\"onde\":\"{where}\""));
        }

        return true;
    }

    /// <summary>Whether this element holds the needle, and in which field.</summary>
    private static bool Matches(
        IMemoryReader memory, nint element, string needle,
        bool asNumber, int number, bool asFloat, float real, out string where)
    {
        where = string.Empty;

        foreach (var (offset, name) in new[]
                 {
                     (GameLayout.Ui.LineText, "t538"),
                     (GameLayout.Ui.ItemDescription, "t710"),
                 })
        {
            if (Pointer(memory, element, offset) is { } text &&
                text.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                where = name;

                return true;
            }
        }

        if (memory.TryReadWideString(
                element + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity, 200) is { } wide &&
            wide.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            where = "t360";

            return true;
        }

        if (!asNumber && !asFloat) return false;
        if (!memory.TryReadBytes(element, 0x400, out var bytes)) return false;

        for (var at = 0; at + 4 <= bytes.Length; at += 4)
        {
            if (asNumber && BitConverter.ToInt32(bytes, at) == number)
            {
                where = $"int em +0x{at:X}";

                return true;
            }

            if (!asFloat) continue;

            var value = BitConverter.ToSingle(bytes, at);

            if (float.IsFinite(value) && Math.Abs(value - real) < 0.01f)
            {
                where = $"float em +0x{at:X}";

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The header: everything about the moment, so the file stands alone.
    /// </summary>
    /// <remarks>
    /// A dump with no cursor in it cannot be tied to what somebody was looking
    /// at, which is most of what makes one useful. The build is here too, since
    /// an offset question only means anything against the build it was asked on.
    /// </remarks>
    private static string Context(
        IMemoryReader memory, SessionState chain, DumpOptions options,
        int written, bool complete) =>
        "{" +
        Str("kind", "context") +
        Str("at", DateTime.Now.ToString("O", CultureInfo.InvariantCulture)) +
        Str("build", GameLayout.Build.Fingerprint) +
        Str("scope", options.Scope.ToString()) +
        Str("procurando", options.Needle ?? string.Empty) +
        Num("escritos", written) +

        // Whether the walk finished or a budget cut it short. Without this a
        // negative result is indistinguishable from an unfinished search, which
        // is precisely how a whole evening went: "not found" was read as a fact
        // about the client every time it was a fact about the budget.
        Bool("completo", complete) +
        Num("cursorX", options.CursorX) + Num("cursorY", options.CursorY) +
        Num("uiScale", options.UiScale) +
        Hex("inGameState", chain.InGameState) +
        Hex("areaInstance", chain.AreaInstance) +
        Hex("localPlayer", chain.LocalPlayer) +
        Str("player", new GameEntity(memory, chain.LocalPlayer).Metadata ?? string.Empty) +
        Str("legenda",
            "a=endereco (muda a cada execucao) | path=indices de Children desde a raiz da UI " +
            "(NAO muda) | caminho=a mesma coisa legivel | r=[x,y,w,h] absoluto | l=[dx,dy] " +
            "relativo ao pai | t538/t360/t710=texto e de qual offset veio | " +
            "reads=como ler cada campo | fields=[offset,u64,i32lo,i32hi,f32lo,f32hi,ehPonteiro,texto]") +
        End() + "}";

    /// <summary>
    /// What is under the cursor, described for drawing beside it.
    /// </summary>
    /// <remarks>
    /// The same search the targeted dump starts from, answered in memory rather
    /// than written to a file: the outline and its caption are wanted every few
    /// frames while the picker is on, and a file per frame is not a tool.
    /// </remarks>
    internal static ElementProbe? Probe(
        IMemoryReader memory, nint moduleBase, float x, float y, float uiScale)
    {
        var chain = GameSession.ResolveChain(memory, moduleBase);

        if (chain is null) return null;

        if (!memory.TryReadPointer(chain.InGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
            return null;

        var options = new DumpOptions(DumpScope.UnderCursor, x, y, uiScale);

        if (Pick(memory, root, options) is not { } found) return null;

        var scale = uiScale <= 0f ? 1f : uiScale;
        var (ex, ey, ew, eh) = MapUiReader.AbsoluteRect(memory, found);

        memory.TryRead<uint>(found + GameLayout.Ui.Flags, out var flags);
        memory.TryReadPointer(found + GameLayout.Ui.TileSlotItem, out var carried);

        var ancestry = System.Collections.Immutable.ImmutableArray.CreateBuilder<string>();

        foreach (var (element, _) in Ancestors(memory, found).AsEnumerable().Reverse().Take(6))
        {
            var (px, py, pw, ph) = MapUiReader.AbsoluteRect(memory, element);

            ancestry.Add(
                $"  pai 0x{element:X}  ({px * scale:0},{py * scale:0}) " +
                $"{pw * scale:0}x{ph * scale:0}");
        }

        return new ElementProbe(
            found, ex * scale, ey * scale, ew * scale, eh * scale,
            flags, (flags & (1u << GameLayout.Ui.VisibleBit)) != 0,
            Children(memory, found).Count,
            carried > 0x10000 ? carried : 0,
            Pointer(memory, found, GameLayout.Ui.LineText),
            memory.TryReadWideString(
                found + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity, 200),
            ancestry.ToImmutable());
    }

    /// <summary>The smallest drawn element containing the cursor.</summary>
    private static nint? Pick(IMemoryReader memory, nint root, DumpOptions options)
    {
        var scale = options.UiScale <= 0f ? 1f : options.UiScale;

        nint best = 0;
        var bestArea = float.MaxValue;

        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < options.MaxNodes)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;
            if (element != root && !Drawn(memory, element)) continue;

            foreach (var child in Children(memory, element)) queue.Enqueue(child);

            var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);

            if (w <= 0f || h <= 0f) continue;

            var (sx, sy, sw, sh) = (x * scale, y * scale, w * scale, h * scale);

            if (options.CursorX < sx || options.CursorX > sx + sw) continue;
            if (options.CursorY < sy || options.CursorY > sy + sh) continue;
            if (sw * sh >= bestArea) continue;

            bestArea = sw * sh;
            best = element;
        }

        return best == 0 ? null : best;
    }

    /// <summary>
    /// How to reach this element from the UI root, as child indices.
    /// </summary>
    /// <remarks>
    /// The address is useless tomorrow - it changes every run - and the path
    /// does not. This is what makes a dump something you can act on: it says
    /// which dereferences to perform, in order, to arrive at the same element
    /// in a fresh process.
    /// </remarks>
    private static List<int> PathOf(IMemoryReader memory, nint element, nint root)
    {
        var path = new List<int>();
        var node = element;

        for (var step = 0; step < MaxDepth && node != root; step++)
        {
            if (!memory.TryReadPointer(node + GameLayout.Ui.Parent, out var parent) ||
                parent == 0 || parent == node)
                return [];

            var index = Children(memory, parent).IndexOf(node);

            if (index < 0) return [];

            path.Add(index);
            node = parent;
        }

        path.Reverse();

        return path;
    }

    /// <summary>The chain up to the root, outermost first, at negative depths.</summary>
    private static List<(nint Element, int Depth)> Ancestors(IMemoryReader memory, nint element)
    {
        var chain = new List<nint>();
        var node = element;

        for (var step = 0; step < MaxDepth; step++)
        {
            if (!memory.TryReadPointer(node + GameLayout.Ui.Parent, out var parent) ||
                parent == 0 || parent == node)
                break;

            chain.Add(parent);
            node = parent;
        }

        chain.Reverse();

        return chain.Select((e, i) => (e, i - chain.Count)).ToList();
    }

    private static void Walk(
        IMemoryReader memory, nint element, int depth, List<string> lines,
        HashSet<nint> seen, DumpOptions options, nint root)
    {
        if (depth > MaxDepth || lines.Count > options.MaxNodes) return;
        if (element == 0 || !seen.Add(element)) return;
        if (options.Scope == DumpScope.Visible && depth > 0 && !Drawn(memory, element)) return;

        lines.Add(Node(memory, element, depth, options, ancestor: false, root));

        foreach (var child in Children(memory, element))
            Walk(memory, child, depth + 1, lines, seen, options, root);
    }

    /// <summary>One element, as a JSON object.</summary>
    private static string Node(
        IMemoryReader memory, nint element, int depth, DumpOptions options, bool ancestor,
        nint root)
    {
        var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);
        var (lx, ly, _, _) = MapUiReader.LocalRect(memory, element);

        memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags);
        memory.TryReadPointer(element + GameLayout.Ui.Parent, out var parent);

        var children = Children(memory, element);
        var text = new StringBuilder();

        // Both text fields, each labelled with the offset it came from. Which of
        // the two a caption lives in is invisible from outside — same size, same
        // column, same row — and reading only one hid three skills out of eight
        // before anyone noticed there were two.
        if (Pointer(memory, element, GameLayout.Ui.LineText) is { Length: > 0 } line)
            text.Append(Str("t538", line));

        if (memory.TryReadWideString(
                element + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity, 200) is { Length: > 0 } wide)
            text.Append(Str("t360", wide));

        if (Pointer(memory, element, GameLayout.Ui.ItemDescription) is { Length: > 0 } described)
            text.Append(Str("t710", described));

        var carried = string.Empty;

        if (memory.TryReadPointer(element + GameLayout.Ui.TileSlotItem, out var item) &&
            item > 0x10000 && item % 8 == 0)
        {
            var entity = new GameEntity(memory, item);

            carried = Hex("item", item);

            if (entity.IsValid && entity.Metadata is { Length: > 0 } metadata)
                carried += Str("itemMeta", metadata);
        }

        var path = PathOf(memory, element, root);

        var reach = path.Count == 0
            ? string.Empty
            : Str("caminho",
                $"InGameState+0x{GameLayout.Roots.UiRoot:X} -> " +
                string.Join(" -> ", path.Select(i => $"Children[{i}]")));

        return "{" +
            Str("kind", ancestor ? "ancestor" : "node") +
            Hex("a", element) + Num("d", depth) + Hex("p", parent) +
            $"\"path\":[{string.Join(",", path)}]," + reach +
            Num("n", children.Count) +
            $"\"r\":[{Real(x)},{Real(y)},{Real(w)},{Real(h)}]," +
            $"\"l\":[{Real(lx)},{Real(ly)}]," +
            Str("f", $"0x{flags:X8}") +
            Bool("vis", (flags & (1u << GameLayout.Ui.VisibleBit)) != 0) +
            Bool("mod", (flags & (1u << GameLayout.Ui.ModifyPositionBit)) != 0) +
            carried + text +
            (options.HexBytes > 0 && (ancestor || depth <= 2)
                ? Reads(memory, element)
                : string.Empty) +
            Fields(memory, element, options, ancestor, depth) +
            End() + "}";
    }

    /// <summary>
    /// How to read each field that resolved here, exactly.
    /// </summary>
    /// <remarks>
    /// An offset on its own is half an instruction. What a reader needs is the
    /// offset AND what sits there AND what to do with it - a pointer to chase, a
    /// std::wstring to unpack, a pair of floats to add to the parent's. Writing
    /// that down beside the value turns a dump into something somebody can
    /// implement from without going back to ask.
    /// </remarks>
    private static string Reads(IMemoryReader memory, nint element)
    {
        var reads = new List<string>();

        void Add(string what, string offset, string type, string how) =>
            reads.Add($"{{\"o\":\"{offset}\",\"t\":\"{type}\",\"e\":\"{what}\",\"como\":\"{how}\"}}");

        Add("retangulo x,y", $"0x{GameLayout.Ui.RelativeX:X}", "float[2]",
            "posicao RELATIVA ao pai; somar subindo por Parent ate a raiz");

        Add("tamanho w,h", $"0x{GameLayout.Ui.SizeWidth:X}", "float[2]",
            "absoluto, nao precisa somar");

        Add("flags", $"0x{GameLayout.Ui.Flags:X}", "uint32",
            $"bit {GameLayout.Ui.VisibleBit} = visivel; bit {GameLayout.Ui.ModifyPositionBit} = soma PositionModifier em 0x{GameLayout.Ui.PositionModifierX:X}");

        Add("filhos", $"0x{GameLayout.Ui.Children:X}", "std::vector<UiElement*>",
            $"first em +0x{GameLayout.Native.VectorFirst:X}, last em +0x{GameLayout.Native.VectorLast:X}; count = (last-first)/8");

        Add("pai", $"0x{GameLayout.Ui.Parent:X}", "UiElement*", "ponteiro direto");

        if (Pointer(memory, element, GameLayout.Ui.LineText) is { Length: > 0 })
            Add("texto da linha", $"0x{GameLayout.Ui.LineText:X}", "wchar_t*",
                "ler ponteiro, depois UTF-16 ate NUL");

        if (memory.TryReadWideString(
                element + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity, 8) is { Length: > 0 })
            Add("texto (wstring)", $"0x{GameLayout.Ui.Text:X}", "std::wstring",
                $"buffer em +0x{GameLayout.Native.StringBuffer:X}, size em +0x{GameLayout.Native.StringSize:X}, capacity em +0x{GameLayout.Native.StringCapacity:X}; inline quando capacity <= 7");

        if (Pointer(memory, element, GameLayout.Ui.ItemDescription) is { Length: > 0 })
            Add("descricao do item", $"0x{GameLayout.Ui.ItemDescription:X}", "wchar_t*",
                "ler ponteiro, UTF-16; item inteiro com \n entre as linhas");

        if (memory.TryReadPointer(element + GameLayout.Ui.TileSlotItem, out var item) &&
            item > 0x10000)
            Add("entidade do slot", $"0x{GameLayout.Ui.TileSlotItem:X}", "Entity*",
                "ponteiro para a entidade do item; resolver componentes a partir dela");

        return $"\"reads\":[{string.Join(",", reads)}],";
    }

    /// <summary>
    /// The raw bytes, read three ways at once.
    /// </summary>
    /// <remarks>
    /// This is the part that finds what nobody has named yet. Every offset the
    /// project reads was found by seeing a value on screen and hunting for where
    /// it lives, so a dump limited to known fields can only confirm what is
    /// already known.
    ///
    /// Each slot is reported as pointer, integer and float together, because
    /// which one it is cannot be known in advance — and two plausible floats
    /// side by side is exactly what a position looks like.
    /// </remarks>
    private static string Fields(
        IMemoryReader memory, nint element, DumpOptions options, bool ancestor, int depth)
    {
        if (options.HexBytes <= 0) return string.Empty;
        if (!ancestor && depth > 2) return string.Empty;
        if (!memory.TryReadBytes(element, options.HexBytes, out var bytes)) return string.Empty;

        var slots = new StringBuilder("\"fields\":[");
        var wrote = false;

        for (var at = 0; at + 8 <= bytes.Length; at += 8)
        {
            var raw = BitConverter.ToUInt64(bytes, at);

            if (raw == 0) continue;

            var pointer = raw is > 0x10000 and < 0x7FFFFFFFFFFF && raw % 8 == 0;

            // What a pointer points AT, when it is text. This is the whole
            // trick behind every offset this project reads: a value is seen on
            // screen, and the field holding it is found by looking for it.
            // Printing the target turns a table of numbers into a search.
            var target = pointer && Pointer(memory, element, at) is { Length: > 1 } text
                ? $",\"{Escape(text.Length > 40 ? text[..40] : text)}\""
                : string.Empty;

            slots.Append(
                $"[{at},\"0x{raw:X}\",{BitConverter.ToInt32(bytes, at)}," +
                $"{BitConverter.ToInt32(bytes, at + 4)}," +
                $"{Real(BitConverter.ToSingle(bytes, at))}," +
                $"{Real(BitConverter.ToSingle(bytes, at + 4))}," +
                $"{(pointer ? 1 : 0)}{target}],");

            wrote = true;
        }

        if (wrote) slots.Length--;

        slots.Append("],");

        return slots.ToString();
    }

    /// <summary>A number JSON can hold, or null for the ones it cannot.</summary>
    private static string Real(float value) =>
        float.IsFinite(value) && Math.Abs(value) < 1e9f
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : "null";

    private static string? Pointer(IMemoryReader memory, nint element, int offset)
    {
        if (!memory.TryReadPointer(element + offset, out var at) || at == 0) return null;
        if (memory.TryReadUtf16(at, 200) is not { Length: > 1 } text) return null;

        // The field holds stale pointers on plenty of elements, and an early
        // dump of it printed CJK gibberish.
        return text.Count(c => c is >= (char)32 and <= (char)126) < text.Length * 0.9
            ? null
            : text;
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

        if (count is <= 0 or > 8192) return children;

        for (var i = 0; i < count; i++)
        {
            if (memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                children.Add(child);
        }

        return children;
    }

    // ---- the smallest JSON writer that does the job -------------------------

    private static string Str(string key, string value) => $"\"{key}\":\"{Escape(value)}\",";

    private static string Num(string key, float value) =>
        $"\"{key}\":{value.ToString("0.###", CultureInfo.InvariantCulture)},";

    private static string Hex(string key, nint value) => $"\"{key}\":\"0x{value:X}\",";

    private static string Bool(string key, bool value) => $"\"{key}\":{(value ? "true" : "false")},";

    /// <summary>Closes the object without anyone having to track trailing commas.</summary>
    private static string End() => "\"ok\":1";

    private static string Escape(string value)
    {
        var built = new StringBuilder(value.Length + 8);

        foreach (var c in value)
        {
            built.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => string.Empty,
                '\t' => " ",
                < ' ' => string.Empty,
                _ => c.ToString(),
            });
        }

        return built.ToString();
    }
}
