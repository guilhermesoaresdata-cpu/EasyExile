using EasyExile.Core.Runtime;

namespace EasyExile.Diagnostics;

/// <summary>
/// Watches the map UI while panels are opened and closed.
/// </summary>
/// <remarks>
/// The question is narrow: when the inventory opens, does the GAME move the map
/// — changing Shift on the element we already read — or does it start rendering
/// a DIFFERENT element than the one we picked? Those two need opposite fixes,
/// and no amount of reading the code answers it.
///
/// Only changes are printed. A per-frame log of a value that holds still buries
/// the moment it moves.
/// </remarks>
public static class MapUiWatch
{
    public static int Run(GameSession session, int seconds, int width, int height)
    {
        Console.WriteLine("== MAP UI WATCH ============================================");
        Console.WriteLine($" abra e feche o inventario nos proximos {seconds}s");
        Console.WriteLine($" cliente {width}x{height}");
        var root = session.UiRootRect();

        Console.WriteLine($" raiz da UI      {root.X:0}, {root.Y:0}   ({root.W:0} x {root.H:0})");
        Console.WriteLine();
        Console.WriteLine(
            $" {"elemento",14} {"vis",4} {"shiftX",7} {"shiftY",7} {"zoom",6} " +
            $"{"rect x,y",13} {"rect w,h",13} tog");

        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        string? last = null;
        var changes = 0;

        while (DateTime.UtcNow < deadline)
        {
            var lines = new List<string>();

            foreach (var (element, state, toggler) in session.MapCandidates())
            {
                // The reference's centre, verbatim: window centre, plus the
                // element's own shift, plus the constant -20.
                var cx = (width * 0.5f) + state.ShiftX;
                var cy = (height * 0.5f) + state.ShiftY - 20f;

                var rect = session.RectOf(element);

                _ = cx;
                _ = cy;

                lines.Add(
                    $" {element,14:X} {(state.IsVisible ? "SIM" : "nao"),4} {state.ShiftX,7:0.0} " +
                    $"{state.ShiftY,7:0.0} {state.Zoom,6:0.000} {rect.X,6:0},{rect.Y,-6:0} " +
                    $"{rect.W,6:0},{rect.H,-6:0} {(toggler ? "T" : "")}");
            }

            var snapshot = string.Join("\n", lines);

            if (snapshot != last && lines.Count > 0)
            {
                Console.WriteLine($" -- {DateTime.Now:HH:mm:ss.f} --");
                Console.WriteLine(snapshot);

                last = snapshot;
                changes++;
            }

            Thread.Sleep(150);
        }

        Console.WriteLine();
        Console.WriteLine($" {changes} estado(s) distintos observados");
        Console.WriteLine();

        return 0;
    }
}
