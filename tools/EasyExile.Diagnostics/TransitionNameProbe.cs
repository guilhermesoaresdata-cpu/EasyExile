using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Diagnostics;

/// <summary>
/// Asks where a transition's real name could come from.
/// </summary>
/// <remarks>
/// The metadata path gives "Area Transition_Animate", which names nothing. The
/// destination — "The Well of Souls" — has to be read from somewhere, and there
/// are three candidates: a curated landmark sitting on the same spot, a string
/// hanging off the entity itself, or one off its MinimapIcon component. This
/// prints all three so the choice is made on evidence.
/// </remarks>
public static class TransitionNameProbe
{
    private static int Count(CaptureResult result) =>
        result.Snapshot?.Entities.Count(e => e.Kind == EntityKind.Transition) ?? 0;

    public static int Run(GameSession session)
    {
        // Two captures, the second with the cap lifted. If the count changes,
        // the transitions were being discarded by the budget rather than being
        // absent from the client.
        var capped = session.Capture(new CaptureOptions(MaxEntities: 512));
        var uncapped = session.Capture(new CaptureOptions(MaxEntities: 20000));

        Console.WriteLine();
        Console.WriteLine($" com teto 512    {Count(capped)} transicao(oes) de {capped.Snapshot?.Entities.Length ?? 0} entidades");
        Console.WriteLine($" sem teto        {Count(uncapped)} transicao(oes) de {uncapped.Snapshot?.Entities.Length ?? 0} entidades");

        var result = uncapped;

        if (result.Snapshot is not { } snapshot)
        {
            Console.WriteLine(" sem snapshot.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("== TRANSITION NAME PROBE ===================================");
        Console.WriteLine($" area {snapshot.Zone}   {snapshot.Marks.Length} landmark(s)");
        Console.WriteLine();

        foreach (var landmark in snapshot.Marks)
            Console.WriteLine($" landmark  {landmark.Centre.X,7:0} {landmark.Centre.Y,7:0}  {landmark.Name}");

        Console.WriteLine();

        var transitions = 0;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Kind != EntityKind.Transition && !entity.IsPoi) continue;

            transitions++;

            var grid = entity.GridPosition;

            Console.WriteLine($" {(entity.Kind == EntityKind.Transition ? "TRANSITION" : "poi")}  {entity.Metadata}");
            Console.WriteLine($"   grid      {(grid is { } g ? $"{g.X:0}, {g.Y:0}" : "-")}");
            Console.WriteLine($"   componentes {string.Join(", ", entity.ComponentNames)}");

            // The nearest landmark, and how near. If the destination's name is
            // already on the map at the same spot, that is the name to use and
            // the duplicate to suppress.
            if (grid is { } at)
            {
                LandmarkSnapshot? nearest = null;
                var best = float.MaxValue;

                foreach (var landmark in snapshot.Marks)
                {
                    var dx = landmark.Centre.X - at.X;
                    var dy = landmark.Centre.Y - at.Y;
                    var distance = MathF.Sqrt((dx * dx) + (dy * dy));

                    if (distance >= best) continue;

                    best = distance;
                    nearest = landmark;
                }

                Console.WriteLine(nearest is null
                    ? "   landmark  nenhum na area"
                    : $"   landmark  \"{nearest.Name}\" a {best:0} celulas");
            }

            foreach (var (offset, text) in session.StringsNear(entity.Id))
                Console.WriteLine($"   entidade +0x{offset:X3}  \"{text}\"");

            // The components are where a destination would live: this entity
            // carries an AreaTransition, and that is the one worth opening.
            foreach (var (name, address) in session.ComponentsOf(entity.Id))
            {
                if (address == 0) continue;

                foreach (var (offset, text) in session.StringsAt(address, 0x120))
                {
                    var where = offset >= 0x1000
                        ? $"+0x{offset / 0x1000:X2}->+0x{offset % 0x1000:X2}"
                        : $"+0x{offset:X2}";

                    Console.WriteLine($"   {name,-20} {where,-16} \"{text}\"");
                }
            }

            Console.WriteLine();
        }

        Console.WriteLine($" {transitions} transicao(oes)/POI inspecionado(s)");
        Console.WriteLine();

        return 0;
    }
}
