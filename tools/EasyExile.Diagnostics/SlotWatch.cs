using System.Text.Json;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Diagnostics;

/// <summary>
/// Waits for an item panel to be open, then reports what every slot in it
/// matched and why.
/// </summary>
/// <remarks>
/// The funnel takes one capture and prints it, which is fine for the floor and
/// useless for a panel: by the time a run finishes the inventory has been shut.
/// Three attempts to measure a wrong price died that way. Polling until the
/// panel appears costs a few seconds and removes the race entirely.
/// </remarks>
public static class SlotWatch
{
    private const int FlaskSlots = 2;

    public static int Run(GameSession session)
    {
        // Also to a file. A measurement is useless when the person who can run
        // it and the person who can read it are not the same — this is the
        // second watch to learn that lesson.
        var log = Path.Combine(AppContext.BaseDirectory, "slot-watch.txt");
        var lines = new List<string>();

        void Say(string line)
        {
            Console.WriteLine(line);
            lines.Add(line);
        }

        Console.WriteLine();
        Say("== SLOT WATCH ==============================================");
        Say(" abra o inventario. Esperando ate 30 segundos.");

        var deadline = DateTime.UtcNow.AddSeconds(30);
        WorldSnapshot? found = null;

        while (DateTime.UtcNow < deadline)
        {
            var result = session.Capture(new CaptureOptions(MaxEntities: 256));

            if (result.Snapshot is { } snapshot && snapshot.Slots.Length > FlaskSlots)
            {
                found = snapshot;
                break;
            }

            Thread.Sleep(400);
        }

        if (found is null)
        {
            Say(" nenhum painel de item apareceu.");
            File.WriteAllLines(log, lines);
            return 1;
        }

        Say($" {found.Slots.Length} slots visiveis");
        Console.WriteLine();

        var cache = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "EasyExile.Radar", "bin", "Debug", "net8.0-windows", "prices.json");

        Dictionary<string, (double Exalted, string Name)> byArt = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, (double Exalted, string Name)> byName = new(StringComparer.OrdinalIgnoreCase);
        var perDivine = 0d;

        if (File.Exists(cache))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(cache));

            perDivine = document.RootElement.TryGetProperty("ExPerDivine", out var d) ? d.GetDouble() : 0;
            byArt = Rows(document.RootElement, "ByArt");
            byName = Rows(document.RootElement, "ByName");
        }

        Say(" raridade  nome base                       arte                          casou com");

        foreach (var slot in found.Slots)
        {
            var item = slot.Item;

            // The exact rule the overlay uses, printed so a wrong number on
            // screen can be traced to a key rather than guessed at.
            var hit = item.IsUnique
                ? (item.Art is { Length: > 0 } a && byArt.TryGetValue(a, out var ra) ? ra : default)
                : (item.BaseName is { Length: > 0 } n && byName.TryGetValue(n, out var rn) ? rn : default);

            var matched = hit.Name is null
                ? "SEM PRECO"
                : $"{hit.Name}  =  {hit.Exalted:0.##} ex" +
                  (perDivine > 1 && hit.Exalted >= perDivine ? $"  ({hit.Exalted / perDivine:0.##} div)" : "");

            Say($" {item.Rarity,-9} {item.BaseName ?? "-",-30} {item.Art ?? "-",-29} {matched}");
        }

        File.WriteAllLines(log, lines);
        Console.WriteLine($" gravado em {log}");

        return 0;
    }

    private static Dictionary<string, (double, string)> Rows(JsonElement root, string property)
    {
        var rows = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(property, out var node)) return rows;

        foreach (var entry in node.EnumerateObject())
        {
            var exalted = entry.Value.TryGetProperty("Exalted", out var ex) ? ex.GetDouble() : 0;
            var name = entry.Value.TryGetProperty("Name", out var n) ? n.GetString() ?? "" : "";

            rows[entry.Name] = (exalted, name);
        }

        return rows;
    }
}
