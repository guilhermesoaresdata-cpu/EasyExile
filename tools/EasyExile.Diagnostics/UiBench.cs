using System.Diagnostics;
using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// What the item-UI sweep costs, split by what it is doing.
/// </summary>
/// <remarks>
/// The sweep grew three jobs in one session - slots, the tooltip search, and
/// the captions - and the overlay's CPU roughly tripled. Which of the three is
/// paying for that is a question with a number as its answer, so it gets one
/// rather than an opinion.
/// </remarks>
public static class UiBench
{
    public static int Run(GameSession session, int passes)
    {
        Console.WriteLine();
        Console.WriteLine("== UI SWEEP BENCH ==========================================");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null)
        {
            Console.WriteLine(" sem cadeia.");

            return 1;
        }

        var caches = new CaptureCaches();

        // Warm, so the first pass does not pay for everything the rest reuse.
        ItemSlotReader.Read(session.Memory, chain.InGameState, 1f, caches);

        foreach (var (name, labels) in new[] { ("sem legendas", false), ("com legendas", true) })
        {
            var clock = Stopwatch.StartNew();

            var slots = 0;
            var tooltips = 0;

            for (var i = 0; i < passes; i++)
            {
                var (found, tip, captions) = ItemSlotReader.Read(
                    session.Memory, chain.InGameState, 1f, caches, labels);

                slots = found.Length;
                tooltips = tip?.ModRows.Length ?? 0;

                if (i == passes - 1 && labels)
                    Console.WriteLine($" legendas coletadas: {captions.Length}");
            }

            clock.Stop();

            Console.WriteLine(
                $" {name,-14} {clock.Elapsed.TotalMilliseconds / passes,7:0.0} ms/passada" +
                $"   slots={slots} linhas_de_mod={tooltips}");
        }

        Console.WriteLine();
        Console.WriteLine(" a varredura roda a cada 125 ms, entao 12 ms = ~10% de uma thread.");
        Console.WriteLine();

        return 0;
    }
}
