using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Diagnostics;

/// <summary>
/// Watches what the capture uses to decide "this is a different area".
/// </summary>
/// <remarks>
/// Written because a fix did not hold: the map kept showing the previous zone's
/// terrain after walking to a new one. Rather than guess at which of the two
/// signals is failing, this prints both on every change and lets the transition
/// answer for itself.
/// </remarks>
public static class AreaIdentityWatch
{
    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine("== AREA IDENTITY WATCH =====================================");
        Console.WriteLine($" atravesse uma transicao nos proximos {seconds}s");
        Console.WriteLine();
        Console.WriteLine($" {"epoch",6}  {"area",16}  {"code",12}  {"terrain",12}  {"entidades",9}");

        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        var epoch = new AreaEpoch();

        string? last = null;
        var changes = 0;

        while (DateTime.UtcNow < deadline)
        {
            var result = session.Capture(new CaptureOptions(MaxEntities: 64));

            if (result.Snapshot is { } snapshot)
            {
                var terrain = snapshot.Terrain is { } t ? $"{t.Width}x{t.Height}" : "-";

                var line =
                    $" {snapshot.Epoch,6}  {snapshot.Area,16}  {snapshot.Zone,12}  " +
                    $"{terrain,12}  {snapshot.Entities.Length,9}";

                if (line != last)
                {
                    Console.WriteLine(line);
                    last = line;
                    changes++;
                }
            }

            Thread.Sleep(400);
        }

        Console.WriteLine();
        Console.WriteLine($" {changes} estado(s) distintos observados");
        Console.WriteLine();

        return 0;
    }
}
