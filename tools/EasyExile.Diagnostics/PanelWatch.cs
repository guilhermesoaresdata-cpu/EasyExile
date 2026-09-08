using System.Runtime.InteropServices;
using EasyExile.Core.Contract;
using EasyExile.Core.Diagnostics;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Every panel the sweep considers, and why each one is or is not a tooltip.
/// </summary>
/// <remarks>
/// The reader kept coming back empty and that has two possible causes which look
/// identical from outside: nothing was hovered, or the thresholds are wrong. A
/// probe that cannot tell them apart is worth nothing, and saying "it does not
/// work" on the strength of one would be inventing a result.
///
/// So this prints the candidates rather than a verdict: how many containers were
/// the right size, how many of their children were full-width rows, and how many
/// of those rows carried the little box that marks a mod.
/// </remarks>
public static class PanelWatch
{
    private const int MaxNodes = 40000;

    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine();
        Console.WriteLine("== PANEL WATCH =============================================");
        Console.WriteLine($" passe o mouse sobre um item. {seconds} segundos.");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        // On the game screen, not in this terminal. The terminal is behind the
        // window the player is looking at, which is how a whole session of these
        // ran out unanswered.
        // The banner is refreshed every pass rather than written once, so it
        // reports what the probe can actually see. Every run today was answered
        // from a second monitor: the mouse was on this terminal, which is the
        // one place it cannot be for an item to be under it, and a static
        // instruction had no way to say so.
        var bounds = WindowRect(session);

        var best = string.Empty;
        var bestRows = 0;
        var until = DateTime.UtcNow.AddSeconds(seconds);

        var candidates = new List<string>();

        // It waits instead of asking the player to freeze inside a fixed window.
        // Every earlier run failed on that coordination rather than on anything
        // it was measuring: forty-five seconds is a long time to hold a mouse
        // still and a short time to happen to be at the keyboard.
        while (DateTime.UtcNow < until)
        {
            var pass = Scan(session, chain.InGameState, out var rows);

            if (rows > bestRows)
            {
                bestRows = rows;
                best = pass.FirstOrDefault(l => l.Contains("marcadas=") &&
                                                !l.EndsWith("marcadas=0", StringComparison.Ordinal))
                       ?? string.Empty;
            }

            // The moment the client is drawing an item's text, this pass is the
            // one worth reporting — whether or not the row test agreed.
            if (ItemLines > 0)
            {
                ProbePrompt.Show("ANALISE: e isso, nao mexa", TimeSpan.FromSeconds(2));

                candidates = pass;

                break;
            }

            ProbePrompt.Show(Asking(bounds), until - DateTime.UtcNow);

            Thread.Sleep(150);
        }

        ProbePrompt.Clear();

        Console.WriteLine($" nos visitados: {Visited} (teto {MaxNodes})");

        foreach (var line in candidates) Console.WriteLine($"   {line}");

        Console.WriteLine(bestRows > 0
            ? $" MELHOR: {best}"
            : ItemLines > 0
                ? $" TOOLTIP ABERTO ({ItemLines} linhas de item na tela) mas nenhuma fileira" +
                  " marcada: o teste de fileira/marcador esta errado."
                : " nenhum texto de item na tela: nao houve tooltip aberto nesta janela." +
                  Environment.NewLine + $" ultimo estado: {Asking(bounds)}");

        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// What the probe still needs, said in terms of where the mouse is.
    /// </summary>
    /// <remarks>
    /// Naming the monitor matters. "Hover an item" is unanswerable advice to
    /// someone whose mouse is on the other screen reading the request, and that
    /// was every run of this today.
    /// </remarks>
    private static string Asking((int L, int T, int R, int B) bounds)
    {
        GetCursorPos(out var mouse);

        if (bounds.R <= bounds.L)
            return "ANALISE: pare o mouse em cima de um item do inventario";

        return mouse.X < bounds.L || mouse.X > bounds.R || mouse.Y < bounds.T || mouse.Y > bounds.B
            ? $"ANALISE: o mouse esta fora da janela do jogo ({mouse.X},{mouse.Y}). Traga para um item"
            : "ANALISE: mouse no jogo. Pare em cima de um item do inventario";
    }

    private static (int L, int T, int R, int B) WindowRect(GameSession session)
    {
        var handle = session.Process.MainWindowHandle;

        return handle != 0 && GetWindowRect(handle, out var rect)
            ? (rect.Left, rect.Top, rect.Right, rect.Bottom)
            : (0, 0, 0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out Rect rect);

    /// <summary>Whether an item tooltip was on screen at all, in the last pass.</summary>
    private static int ItemLines;

    /// <summary>How far the walk got, to rule out the node cap as the reason.</summary>
    private static int Visited;

    private static List<string> Scan(GameSession session, nint inGameState, out int bestRows)
    {
        var report = new List<string>();

        bestRows = 0;
        ItemLines = 0;

        if (!session.Memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
            return report;

        var visibleBit = 1u << GameLayout.Ui.VisibleBit;

        var queue = new Queue<nint>();
        var seen = new HashSet<nint>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < MaxNodes)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            var visible = session.Memory.TryRead<uint>(element + GameLayout.Ui.Flags, out var flags) &&
                          (flags & visibleBit) != 0;

            if (!visible && element != root) continue;

            var children = Children(session, element);

            foreach (var child in children) queue.Enqueue(child);

            // Independent of every threshold below: whether the client is
            // drawing an item's text at all. Without this the probe cannot tell
            // "nothing was hovered" from "the row test is wrong", and reporting
            // either one on the strength of an empty result would be a guess
            // dressed as a finding.
            if (Line(session, element) is { } line &&
                (line.StartsWith("Item Level:", StringComparison.Ordinal) ||
                 line.Contains("Requires: Level", StringComparison.Ordinal)))
                ItemLines++;

            if (children.Count is < 6 or > 80) continue;

            var (px, py, pw, ph) = MapUiReader.AbsoluteRect(session.Memory, element);

            if (pw < 200f || ph < 80f) continue;

            var rows = 0;
            var marked = 0;

            foreach (var row in children)
            {
                var (_, _, rw, rh) = MapUiReader.LocalRect(session.Memory, row);

                if (rh is < 18f or > 44f || rw < pw * 0.8f) continue;

                rows++;

                foreach (var mark in Children(session, row))
                {
                    var (mx, _, mw, mh) = MapUiReader.LocalRect(session.Memory, mark);

                    if (mw is < 12f or > 34f || mh < 18f || mx > 26f) continue;

                    marked++;

                    break;
                }
            }

            if (rows == 0) continue;

            report.Add(
                $"0x{element:X} ({px:0},{py:0}) {pw:0}x{ph:0} " +
                $"filhos={children.Count} fileiras={rows} marcadas={marked}");

            if (marked > bestRows) bestRows = marked;
        }

        Visited = seen.Count;

        return report;
    }

    /// <summary>The element's rendered line, when what it points at really is one.</summary>
    private static string? Line(GameSession session, nint element)
    {
        if (!session.Memory.TryReadPointer(element + GameLayout.Ui.LineText, out var at) || at == 0)
            return null;

        if (session.Memory.TryReadUtf16(at, 120) is not { Length: > 2 } text) return null;

        return text.Count(c => c is >= (char)32 and <= (char)126) < text.Length * 0.9 ? null : text;
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
}
