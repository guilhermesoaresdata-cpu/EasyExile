using System.Runtime.InteropServices;
using EasyExile.Core.Contract;
using EasyExile.Core.Diagnostics;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Reads the tooltip's own lines, at the offset the value hunt found.
/// </summary>
/// <remarks>
/// Four exhaustive walks of the UI tree reported nothing, and every one of them
/// asked the same question: does this element's <c>Ui.Text</c> say so. Scanning
/// the process for the string itself and then for whoever points at it answered
/// what those walks could not — the text is held at +0x538 and +0x710, not at
/// +0x360.
///
/// So this repeats the walk with the question fixed. It is still a probe rather
/// than a product: nothing here may move into the overlay until the Analyzer has
/// proved the offset and the contract carries it.
/// </remarks>
public static class TextLineProbe
{
    /// <summary>Where a rendered line of text keeps its characters.</summary>
    private const int Line = 0x538;

    /// <summary>Where an item element keeps its whole description.</summary>
    private const int Block = 0x710;

    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine();
        Console.WriteLine("== TEXT LINES ==============================================");
        Console.WriteLine($" deixe o mouse sobre um item. {seconds} segundos.");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        var roots = Roots(session, chain.InGameState);

        Console.WriteLine($" raizes: {roots.Count}");

        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        // Every line came back marked hidden, including whichever one the client
        // is drawing right now. Either the flag means something else on this
        // branch or the container moves when it is shown, and the ancestors say
        // which.
        var chains = new Dictionary<nint, string>();

        // Whether the mouse was ever actually over an item. Without this the
        // probe cannot tell "there is no tooltip in the tree" from "nothing was
        // hovered", and reporting the first when it was the second would be a
        // false negative dressed as a finding.
        var hovered = new Dictionary<string, string>(StringComparer.Ordinal);
        var counted = new Dictionary<int, int>();
        var walked = 0;

        ProbePrompt.Show("ANALISE: abra o inventario e passe o mouse num item", TimeSpan.FromSeconds(seconds));

        var until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until)
        {
            var seen = new HashSet<nint>();

            GetCursorPos(out var mouse);

            foreach (var root in roots)
                Walk(session, root, seen, found, counted, mouse, hovered, chains);

            walked = seen.Count;

            Thread.Sleep(250);
        }

        ProbePrompt.Clear();

        GetCursorPos(out var last);

        Console.WriteLine($" nos visitados: {walked}   cursor ({last.X},{last.Y})");
        Console.WriteLine(hovered.Count == 0
            ? " o mouse NAO ficou sobre nenhum item durante a sondagem."
            : $" o mouse ficou sobre {hovered.Count} item(ns):");

        foreach (var (_, item) in hovered) Console.WriteLine($"   {item}");

        Console.WriteLine();
        Console.WriteLine("== CADEIA DE PAIS ==========================================");

        foreach (var (_, ancestry) in chains) Console.WriteLine(ancestry);

        Console.WriteLine();
        Console.WriteLine($" com texto em +0x538: {counted.GetValueOrDefault(Line)}" +
                          $"   em +0x710: {counted.GetValueOrDefault(Block)}");
        Console.WriteLine();

        if (found.Count == 0)
        {
            Console.WriteLine(" NADA nesses dois campos em toda a arvore.");
        }
        else
        {
            foreach (var (key, line) in found.OrderBy(f => f.Value, StringComparer.Ordinal).Take(80)) Console.WriteLine($" {key}  {line}");
        }

        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// Every UI root hanging off the in-game state.
    /// </summary>
    /// <remarks>
    /// An element identifies itself: its Self field points back at it. That
    /// needs no other offset to be right, so it finds roots the contract does
    /// not name.
    /// </remarks>
    private static List<nint> Roots(GameSession session, nint inGameState)
    {
        var roots = new List<nint>();

        for (var at = 0; at < 0x900; at += 8)
        {
            if (!session.Memory.TryReadPointer(inGameState + at, out var candidate) ||
                candidate == 0 || candidate % 8 != 0)
                continue;

            if (!session.Memory.TryReadPointer(candidate + GameLayout.Ui.Self, out var self)) continue;
            if (self != candidate) continue;

            roots.Add(candidate);
        }

        return roots;
    }

    private static void Walk(
        GameSession session, nint root, HashSet<nint> seen,
        Dictionary<string, string> found, Dictionary<int, int> counted,
        Point mouse, Dictionary<string, string> hovered, Dictionary<nint, string> chains)
    {
        var queue = new Queue<nint>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < 60000)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            if (session.Memory.TryReadPointer(element + GameLayout.Ui.Children, out var first) &&
                session.Memory.TryReadPointer(
                    element + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last) &&
                first != 0 && last > first)
            {
                var count = (int)((last - first) / 8);

                if (count is > 0 and <= 8192)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if (session.Memory.TryReadPointer(first + (i * 8), out var child) && child != 0)
                            queue.Enqueue(child);
                    }
                }
            }

            foreach (var offset in new[] { Line, Block })
            {
                if (Text(session, element, offset) is not { Length: > 3 } text) continue;

                counted[offset] = counted.GetValueOrDefault(offset) + 1;

                var box = MapUiReader.AbsoluteRect(session.Memory, element);

                if (!Wanted(text, box, offset)) continue;

                // Reported only if the client is actually drawing it. Every
                // closed panel stays in the tree — the friends list, the whole
                // options menu — and they buried the one element that matters.
                //
                // This is not the mistake an earlier probe made: that one used
                // the visible bit to decide where to WALK, and cut the branch
                // holding the answer. Walking is still exhaustive; only the
                // report is filtered.
                var drawn = Drawn(session, element);

                // Not skipped when hidden — reported as hidden. Whether the
                // client considers the tooltip visible is exactly the thing in
                // question, so deciding it here would answer it by assumption.
                if (offset == Block && !drawn) continue;

                if (offset == Block &&
                    mouse.X >= box.X && mouse.X <= box.X + box.W &&
                    mouse.Y >= box.Y && mouse.Y <= box.Y + box.H)
                    hovered[$"0x{element:X}"] =
                        $"({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0}  " +
                        Cut(text.Replace((char)10, (char)124), 110);
                var lines = text.Split((char)10).Length;

                if (offset == Line && chains.Count < 4 && !chains.ContainsKey(element))
                    chains[element] = Chain(session, element, text);

                found[$"0x{element:X}+{offset:X}"] =
                    $"+0x{offset:X3} {(drawn ? "VIS" : "---")} ({box.X:0},{box.Y:0}) " +
                    $"{box.W:0}x{box.H:0} {lines}L  " +
                    Cut(text.Replace((char)10, (char)124), 150);
            }
        }
    }

    /// <summary>
    /// Worth printing.
    /// </summary>
    /// <remarks>
    /// The block field is unambiguous — only an item writes "Item Class" — but
    /// the line field is on tens of thousands of elements, so it is narrowed by
    /// shape instead: a rendered tooltip line is a wide, one-line box on screen.
    /// Narrowing by phrase is what made the earlier probes miss; narrowing by
    /// geometry keeps whatever the client actually chose to say.
    /// </remarks>
    private static bool Wanted(string text, (float X, float Y, float W, float H) box, int offset)
    {
        if (offset == Block)
            return text.Contains("Item Class", StringComparison.Ordinal);

        // No geometry. The cursor and the UI turned out not to share a
        // coordinate space — the quest tracker sits at x 2770 on a desktop that
        // is not that wide — so filtering a line by where it is filters on a
        // number this probe has no right to trust yet.
        //
        // What it can trust is the wording: these are phrases the client writes
        // on an item and nowhere else.
        return Needles.Any(n => text.Contains(n, StringComparison.Ordinal));
    }

    /// <summary>An element's ancestors, with the flags and rect of each.</summary>
    private static string Chain(GameSession session, nint element, string text)
    {
        var report = new List<string> { $" LINHA 0x{element:X}  {Cut(text, 60)}" };

        var node = element;

        for (var step = 0; step < 10 && node != 0; step++)
        {
            session.Memory.TryRead<uint>(node + GameLayout.Ui.Flags, out var flags);

            var box = MapUiReader.AbsoluteRect(session.Memory, node);
            var visible = (flags & (1u << GameLayout.Ui.VisibleBit)) != 0;

            report.Add(
                $"   {new string((char)32, step)}0x{node:X} flags 0x{flags:X8} vis={visible} " +
                $"({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0}");

            if (!session.Memory.TryReadPointer(node + GameLayout.Ui.Parent, out var parent) ||
                parent == 0 || parent == node)
                break;

            node = parent;
        }

        return string.Join(Environment.NewLine, report);
    }

    /// <summary>Phrases that only appear on an item.</summary>
    private static readonly string[] Needles =
    [
        "Requires: Level", "Item Level:", "increased", "to maximum",
        "Resistance", "Adds ", "reduced", "to Accuracy",
    ];

    /// <summary>On screen, and so is every ancestor between it and the root.</summary>
    private static bool Drawn(GameSession session, nint element)
    {
        var node = element;

        for (var step = 0; step < 24 && node != 0; step++)
        {
            if (!session.Memory.TryRead<uint>(node + GameLayout.Ui.Flags, out var flags)) return false;
            if ((flags & (1u << GameLayout.Ui.VisibleBit)) == 0) return false;

            if (!session.Memory.TryReadPointer(node + GameLayout.Ui.Parent, out var parent) ||
                parent == 0 || parent == node)
                return true;

            node = parent;
        }

        return true;
    }

    private static string? Text(GameSession session, nint element, int offset)
    {
        if (!session.Memory.TryReadPointer(element + offset, out var at) || at == 0) return null;

        return session.Memory.TryReadUtf16(at, 2000);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..length];
}
