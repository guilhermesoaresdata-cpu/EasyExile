using EasyExile.Core.Contract;
using EasyExile.Core.Diagnostics;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// The shape of a live item tooltip, line by line.
/// </summary>
/// <remarks>
/// Finding the tooltip needs no cursor and no guess about where it is drawn.
/// The client hangs the item entity on the tooltip through the very same field
/// it uses for a slot in the grid, so the tooltip is simply the second element
/// carrying an entity some slot already carries — and the larger of the two.
///
/// What this is really asking is whether the tooltip keeps one element per MOD
/// rather than only one per line. If it does, a badge can be attached to the
/// mod it belongs to by position, and a hybrid mod spanning three lines stops
/// being a special case.
/// </remarks>
public static class TooltipTree
{
    private const int MaxNodes = 60000;

    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine();
        Console.WriteLine("== TOOLTIP TREE ============================================");
        Console.WriteLine($" passe o mouse sobre um item raro. {seconds} segundos.");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        ProbePrompt.Show("ANALISE: passe o mouse sobre um item raro", TimeSpan.FromSeconds(seconds));

        var until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until)
        {
            if (Sweep(session, chain.InGameState))
            {
                ProbePrompt.Clear();

                return 0;
            }

            Thread.Sleep(200);
        }

        ProbePrompt.Clear();

        Console.WriteLine(" nenhum item apareceu duas vezes na arvore, entao nenhum tooltip");
        Console.WriteLine(" estava aberto. Sem hover isto nao diz nada.");
        Console.WriteLine();

        return 0;
    }

    private static bool Sweep(GameSession session, nint inGameState)
    {
        if (!session.Memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
            return false;

        var visibleBit = 1u << GameLayout.Ui.VisibleBit;

        // Grouped by the grandparent, because that is the question: if the
        // client keeps one element per MOD rather than one per line, a hybrid
        // spanning three lines stops being a special case and a badge can be
        // attached to the mod it belongs to.
        var groups = new Dictionary<nint, List<(nint Parent, float X, float Y, float W, string Text)>>();

        var queue = new Queue<(nint Element, nint Parent, nint Grand)>();
        var seen = new HashSet<nint>();

        queue.Enqueue((root, 0, 0));

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            var (element, parent, grand) = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            var visible = session.Memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags) &&
                          (flags & visibleBit) != 0;

            if (!visible && element != root) continue;

            var kids = new Queue<nint>();

            Children(session, element, kids);

            while (kids.Count > 0) queue.Enqueue((kids.Dequeue(), element, parent));

            if (Read(session, element, GameLayout.Ui.LineText) is not { Length: > 0 } text) continue;

            var (x, y, w, _) = MapUiReader.AbsoluteRect(session.Memory, element);

            if (!groups.TryGetValue(grand, out var lines)) groups[grand] = lines = [];

            lines.Add((parent, x, y, w, text));
        }

        // Grouping by the grandparent found nothing, which is itself the
        // answer: if every mod carries its own container then a grandparent
        // holds only that one mod's lines, and no group is ever big.
        //
        // So this stops grouping and just lists what is drawn, minus the chat
        // log — the one block that is genuinely enormous and never an item.
        var chat = groups.OrderByDescending(g => g.Value.Count).FirstOrDefault();

        var drawn = groups
            .Where(g => g.Key != chat.Key)
            .SelectMany(g => g.Value.Select(l => (Grand: g.Key, Line: l)))
            .Where(l => l.Line.Y > -200 && l.Line.Y < 1700)
            .OrderBy(l => l.Line.Y)
            .ToList();

        if (drawn.Count == 0) return false;

        Console.WriteLine($" linhas desenhadas fora do chat: {drawn.Count}");
        Console.WriteLine();

        foreach (var (grand, line) in drawn.Take(70))
            Console.WriteLine(
                $"   ({line.X:0},{line.Y:0}) w={line.W:0}  pai 0x{line.Parent:X}  " +
                $"avo 0x{grand:X}  {Cut(line.Text, 70)}");

        Console.WriteLine();

        return true;
    }

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..length];

    /// <summary>How far apart the extremes are.</summary>
    private static float Spread(IEnumerable<float> values)
    {
        var all = values.ToList();

        return all.Count == 0 ? 0f : all.Max() - all.Min();
    }

    /// <summary>The subtree, indented, with whatever each node draws.</summary>
    private static void Dump(GameSession session, nint element, int depth)
    {
        if (depth > 8) return;

        var (x, y, w, h) = MapUiReader.AbsoluteRect(session.Memory, element);

        var text = Read(session, element, GameLayout.Ui.LineText);
        var pad = new string((char)32, depth * 2);

        var kids = new Queue<nint>();

        Children(session, element, kids);

        Console.WriteLine(
            $"   {pad}({x:0},{y:0}) {w:0}x{h:0} filhos={kids.Count}" +
            (text is { Length: > 0 } ? $"  \"{text}\"" : string.Empty));

        while (kids.Count > 0)
        {
            var child = kids.Dequeue();

            if (session.Memory.TryRead<uint>(child + GameLayout.Ui.Flags, out var flags) &&
                (flags & (1u << GameLayout.Ui.VisibleBit)) != 0)
                Dump(session, child, depth + 1);
        }
    }

    private static void Children(GameSession session, nint element, Queue<nint> into)
    {
        if (!session.Memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) ||
            first == 0 ||
            !session.Memory.TryReadPointer(
                element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last))
            return;

        var count = (last - first) / 8;

        if (count is <= 0 or > 8192) return;

        for (var i = 0; i < count; i++)
        {
            if (session.Memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                into.Enqueue(child);
        }
    }

    private static string? Read(GameSession session, nint element, int offset)
    {
        if (!session.Memory.TryReadPointer(element + offset, out var at) || at == 0) return null;

        return session.Memory.TryReadUtf16(at, 300);
    }
}
