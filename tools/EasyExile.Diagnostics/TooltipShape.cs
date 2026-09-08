using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// How a tooltip is built, taken from one that is not on screen.
/// </summary>
/// <remarks>
/// The question is whether the client keeps an element per MOD or only an
/// element per LINE. It decides how a tier badge can be attached: per mod, a
/// hybrid roll spanning three lines is one thing with one badge; per line, it is
/// three lines with no way to say which belongs to which roll.
///
/// Waiting for the right hover to answer it kept failing on timing, and it never
/// needed a hover: the client builds a tooltip for every item it has drawn and
/// leaves them all in the tree with the visible bit clear. Their structure is
/// the same structure. So this walks the whole tree, hidden branches included,
/// finds a subtree that reads like an item, and prints its shape.
/// </remarks>
public static class TooltipShape
{
    private const int MaxNodes = 120000;

    public static int Run(GameSession session)
    {
        Console.WriteLine();
        Console.WriteLine("== TOOLTIP SHAPE ===========================================");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        if (!session.Memory.TryReadPointer(chain.InGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
        {
            Console.WriteLine(" sem raiz de UI.");

            return 1;
        }

        // Hidden branches included. A tooltip nobody is looking at is built the
        // same way as the one they are.
        var parents = new Dictionary<nint, nint>();
        var texts = new Dictionary<nint, string>();

        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            foreach (var child in Children(session, element))
            {
                parents[child] = element;
                queue.Enqueue(child);
            }

            if (Line(session, element) is { } text) texts[element] = text;
        }

        Console.WriteLine($" nos: {seen.Count}   com linha de texto: {texts.Count}");

        var anchor = texts.FirstOrDefault(t => t.Value.StartsWith("Item Level:", StringComparison.Ordinal));

        if (anchor.Key == 0)
        {
            Console.WriteLine(" nenhuma linha 'Item Level:' na arvore.");

            return 0;
        }

        // Up until the subtree is big enough to be the whole item rather than
        // one of its rows.
        var top = anchor.Key;

        for (var step = 0; step < 12; step++)
        {
            if (!parents.TryGetValue(top, out var up) || up == 0) break;

            top = up;

            if (Count(session, top, texts) >= 12) break;
        }

        var (x, y, w, h) = MapUiReader.AbsoluteRect(session.Memory, top);

        Console.WriteLine();
        Console.WriteLine($" TOOLTIP 0x{top:X}  ({x:0},{y:0}) {w:0}x{h:0}");

        // How deep it sits decides whether the sweep can look for it cheaply.
        // Reading one extra field on every node of a forty-thousand node walk
        // would cost more than the whole sweep is allowed.
        var depth = 0;
        var climb = top;

        while (parents.TryGetValue(climb, out var above) && above != 0 && depth < 40)
        {
            climb = above;
            depth++;
        }

        Console.WriteLine($" profundidade a partir da raiz: {depth}");
        Console.WriteLine($" carrega TileSlotItem: " +
                          (session.Memory.TryReadPointer(top + GameLayout.Ui.TileSlotItem, out var carried) &&
                           carried > 0x10000 ? $"sim 0x{carried:X}" : "nao"));
        Console.WriteLine();

        Dump(session, top, texts, 0);

        Console.WriteLine();

        return 0;
    }

    private static int Count(GameSession session, nint element, Dictionary<nint, string> texts)
    {
        var found = 0;
        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();

        queue.Enqueue(element);

        while (queue.Count > 0 && seen.Count < 4000)
        {
            var node = queue.Dequeue();

            if (node == 0 || !seen.Add(node)) continue;

            if (texts.ContainsKey(node)) found++;

            foreach (var child in Children(session, node)) queue.Enqueue(child);
        }

        return found;
    }

    private static void Dump(
        GameSession session, nint element, Dictionary<nint, string> texts, int depth)
    {
        if (depth > 6) return;

        var (x, y, w, h) = MapUiReader.AbsoluteRect(session.Memory, element);
        var children = Children(session, element).ToList();
        var pad = new string((char)32, depth * 3);

        var text = texts.GetValueOrDefault(element);

        Console.WriteLine(
            $" {pad}0x{element:X} ({x:0},{y:0}) {w:0}x{h:0} f={children.Count}" +
            (text is null ? string.Empty : $"  \"{text}\""));

        foreach (var child in children) Dump(session, child, texts, depth + 1);
    }

    private static List<nint> Children(GameSession session, nint element)
    {
        var children = new List<nint>();

        if (!session.Memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) ||
            first == 0 ||
            !session.Memory.TryReadPointer(
                element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last))
            return children;

        var count = (last - first) / 8;

        if (count is <= 0 or > 8192) return children;

        for (var i = 0; i < count; i++)
        {
            if (session.Memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                children.Add(child);
        }

        return children;
    }

    /// <summary>
    /// The element's rendered line, when what it points at really is one.
    /// </summary>
    /// <remarks>
    /// The field holds a stale or unrelated pointer on plenty of elements, and
    /// the first dump printed the result as CJK gibberish. Prose the client
    /// drew is overwhelmingly Latin-1, so that is the test.
    /// </remarks>
    private static string? Line(GameSession session, nint element)
    {
        if (!session.Memory.TryReadPointer(element + GameLayout.Ui.LineText, out var at) || at == 0)
            return null;

        if (session.Memory.TryReadUtf16(at, 300) is not { Length: > 2 } text) return null;

        var plain = text.Count(c => c is >= (char)32 and <= (char)126);

        return plain < text.Length * 0.9 ? null : text;
    }
}
