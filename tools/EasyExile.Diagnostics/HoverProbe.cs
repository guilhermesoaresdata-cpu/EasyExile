using System.Runtime.InteropServices;
using EasyExile.Core.Contract;
using EasyExile.Core.Diagnostics;
using EasyExile.Core.Runtime;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Everything the client is drawing, nearest the cursor first.
/// </summary>
/// <remarks>
/// The item probes all filter on item wording, which is right for them and
/// useless for anything else — a skill tooltip says none of it. This one filters
/// on nothing but "is it drawn", and orders by distance from the mouse, because
/// what is under the mouse is the question every hover feature starts from.
///
/// The cursor arrives in desktop pixels and the UI lays itself out in its own
/// space, so one is converted into the other before they are compared. Getting
/// that backwards is what made an earlier probe report the mouse as nowhere near
/// a panel it was sitting on top of.
/// </remarks>
public static class HoverProbe
{
    private const int MaxNodes = 60000;

    /// <summary>The height the client lays its UI out against.</summary>
    private const float DesignHeight = 1600f;

    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine();
        Console.WriteLine("== HOVER PROBE =============================================");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        var window = WindowRect(session);
        var height = window.B - window.T;
        var scale = height > 0 ? height / DesignHeight : 1f;

        Console.WriteLine($" janela {window.L},{window.T} .. {window.R},{window.B}   escala {scale:0.###}");

        List<(float Distance, string Line)> found = new();

        var until = DateTime.UtcNow.AddSeconds(seconds);

        while (DateTime.UtcNow < until && found.Count == 0)
        {
            GetCursorPos(out var mouse);

            // Desktop pixels into the UI's own space, which is what every
            // rectangle below is measured in.
            var ux = (mouse.X - window.L) / scale;
            var uy = (mouse.Y - window.T) / scale;

            ProbePrompt.Show(
                $"ANALISE: pare o mouse na skill ({mouse.X},{mouse.Y})", until - DateTime.UtcNow);

            found = Sweep(session, chain.InGameState, ux, uy);

            if (found.Count > 0) break;

            Thread.Sleep(200);
        }

        ProbePrompt.Clear();

        if (found.Count == 0)
        {
            Console.WriteLine(" nada desenhado com texto perto do cursor.");
            Console.WriteLine();

            return 0;
        }

        foreach (var (distance, line) in found.OrderBy(f => f.Distance).Take(60))
            Console.WriteLine($" d={distance,6:0} {line}");

        Console.WriteLine();

        return 0;
    }

    private static List<(float, string)> Sweep(
        GameSession session, nint inGameState, float ux, float uy)
    {
        var found = new List<(float, string)>();

        if (!session.Memory.TryReadPointer(inGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
            return found;

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

            foreach (var child in Children(session, element)) queue.Enqueue(child);

            // Both text fields. The rows showing a DPS or DMG readout are the
            // exact ones missing from the collection, which says they are a
            // different kind of element - and the contract knows two places a
            // caption can live, not one.
            var wide = session.Memory.TryReadWideString(
                element + GameLayout.Ui.Text,
                GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity, maxLength: 120);

            var text = Read(session, element, GameLayout.Ui.LineText)
                       ?? (wide is { Length: > 1 } ? "[wstr] " + wide : null)
                       ?? Read(session, element, GameLayout.Ui.ItemDescription);

            if (text is not { Length: > 1 }) continue;

            var (x, y, w, h) = MapUiReader.AbsoluteRect(session.Memory, element);

            if (w <= 0 || h <= 0) continue;

            // Distance to the rectangle, zero when the cursor is inside it.
            var dx = Math.Max(0f, Math.Max(x - ux, ux - (x + w)));
            var dy = Math.Max(0f, Math.Max(y - uy, uy - (y + h)));

            var distance = MathF.Sqrt((dx * dx) + (dy * dy));

            // The feature's own rule, applied here so the probe reports what it
            // would pick rather than what is merely nearby: inside the vertical
            // band, and inside the horizontal one after reaching left of the
            // caption by a little over its height.
            var band = uy >= y && uy <= y + h &&
                       ux <= x + w && ux >= x - (h * 1.6f);

            if (distance > 2400f && !band) continue;

            // Whether the entry carries an entity, the way a grid cell carries
            // its item. If it does, the name comes from the client rather than
            // from guessing which drawn labels happen to be skills.
            var carried = string.Empty;

            if (session.Memory.TryReadPointer(element + GameLayout.Ui.TileSlotItem, out var item) &&
                item > 0x10000 && item % 8 == 0)
            {
                var entity = new GameEntity(session.Memory, item);

                if (entity.IsValid) carried = $"  [{entity.Metadata}]";
            }

            found.Add((band ? -1f - (1f / (1f + (w * h))) : distance,
                $"{(band ? "PEGA" : "    ")} area={w * h,9:0} 0x{element:X} ({x:0},{y:0}) {w:0}x{h:0}  " +
                Cut(text.Replace((char)10, (char)124), 90) + carried));
        }

        return found;
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

    /// <summary>The element's text, when what the field points at really is text.</summary>
    private static string? Read(GameSession session, nint element, int offset)
    {
        if (!session.Memory.TryReadPointer(element + offset, out var at) || at == 0) return null;

        if (session.Memory.TryReadUtf16(at, 400) is not { Length: > 1 } text) return null;

        return text.Count(c => c is >= (char)32 and <= (char)126 or (char)10) < text.Length * 0.9
            ? null
            : text;
    }

    private static (int L, int T, int R, int B) WindowRect(GameSession session)
    {
        var handle = session.Process.MainWindowHandle;

        return handle != 0 && GetWindowRect(handle, out var rect)
            ? (rect.Left, rect.Top, rect.Right, rect.Bottom)
            : (0, 0, 0, 0);
    }

    private static string Cut(string text, int length) =>
        text.Length <= length ? text : text[..length];

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
}
