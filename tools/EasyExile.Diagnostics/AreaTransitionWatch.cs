using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Diagnostics;

/// <summary>
/// Watches the client across an area transition. It observes and never
/// instructs: the player plays, this records what the chain did.
/// </summary>
public static class AreaTransitionWatch
{
    private sealed record Capture(
        SessionState State,
        int Awake,
        int Sleeping,
        nint[] AwakeAddresses,
        string? PlayerName,
        int Level,
        Vital[] Vitals,
        Vector3? World,
        string[] Components,
        nint Camera,
        bool CameraProjects);

    public static int Run(GameSession session, int seconds)
    {
        Console.WriteLine("== AREA TRANSITION WATCH ===================================");
        Console.WriteLine($" observando por {seconds}s");
        Console.WriteLine();

        var deadline = DateTime.UtcNow.AddSeconds(seconds);

        var before = Sample(session);
        if (before is null)
        {
            Console.WriteLine(" a cadeia nao resolveu no inicio da janela.");
            Console.WriteLine();
            Console.WriteLine(" AREA TRANSITION LIVE = FAIL (sem estado inicial)");
            return 1;
        }

        Report("BEFORE", before);

        Capture? after = null;
        var samples = 1;
        var unresolved = 0;
        var transitions = 0;

        Console.WriteLine();
        Console.WriteLine($" {"t",6}  {"area",-16} {"player",-16} {"awake",6} {"sleep",6}  evento");

        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(1000);

            // Every sample re-resolves from the module base. Nothing is carried
            // across, because after a transition everything carried is a pointer
            // into memory the client has already reused.
            var now = Sample(session);
            samples++;

            var t = seconds - (deadline - DateTime.UtcNow).TotalSeconds;

            if (now is null)
            {
                unresolved++;
                Console.WriteLine($" {t,6:0.0}  {"-",-16} {"-",-16} {"-",6} {"-",6}  carregando / sem area");
                continue;
            }

            var changed = now.State.AreaInstance != before.State.AreaInstance;

            Console.WriteLine(
                $" {t,6:0.0}  0x{now.State.AreaInstance,-14:X} 0x{now.State.LocalPlayer,-14:X} " +
                $"{now.Awake,6} {now.Sleeping,6}  {(changed ? "AREA CHANGE" : "")}");

            if (!changed) continue;

            transitions++;
            after = now;
            break;
        }

        if (after is null)
        {
            Console.WriteLine();
            Console.WriteLine($" amostras {samples}, nao resolvidas {unresolved}, transicoes 0");
            Console.WriteLine();
            Console.WriteLine(" AREA TRANSITION LIVE = FAIL (nenhuma transicao observada na janela)");
            return 1;
        }

        // Let the new area settle before judging it; entities stream in.
        Thread.Sleep(3000);
        after = Sample(session) ?? after;

        Console.WriteLine();
        Report("AFTER", after);
        Console.WriteLine();

        return Judge(before, after, samples, unresolved, transitions);
    }

    private static int Judge(Capture before, Capture after, int samples, int unresolved, int transitions)
    {
        Console.WriteLine("== VEREDITO ================================================");

        var survivors = after.AwakeAddresses.Intersect(before.AwakeAddresses).Count();

        var checks = new List<(string Name, bool Passed, string Detail)>
        {
            ("area mudou",
                after.State.AreaInstance != before.State.AreaInstance,
                $"0x{before.State.AreaInstance:X} -> 0x{after.State.AreaInstance:X}"),

            ("cadeia re-resolvida da raiz",
                after.State.GameStateRoot != 0 && after.State.InGameState != 0,
                $"root 0x{after.State.GameStateRoot:X}, state 0x{after.State.InGameState:X}"),

            ("entidades da area antiga sumiram",
                survivors < before.AwakeAddresses.Length / 2,
                $"{survivors} de {before.AwakeAddresses.Length} enderecos antigos ainda leem como entidade"),

            ("component resolver funciona",
                after.Components.Length >= 8,
                $"{after.Components.Length} componentes"),

            ("Life funciona",
                after.Vitals.Any(v => v.Name == "Health" && v.Max > 0),
                string.Join("  ", after.Vitals.Select(v => $"{v.Name} {v.Current}/{v.Max}"))),

            ("Player funciona",
                !string.IsNullOrEmpty(after.PlayerName) && after.Level > 0,
                $"{after.PlayerName} lvl {after.Level}"),

            ("Player e o mesmo personagem",
                after.PlayerName == before.PlayerName,
                $"{before.PlayerName} -> {after.PlayerName}"),

            ("Camera funciona",
                after.Camera != 0 && after.CameraProjects,
                after.Camera == 0 ? "camera nao resolveu" : $"0x{after.Camera:X}, projecao valida"),

            ("posicao do mundo legivel",
                after.World is not null,
                after.World is { } w ? $"({w.X:0.}, {w.Y:0.}, {w.Z:0.})" : "-"),

            ("novos mapas de entidade povoados",
                after.Awake > 0,
                $"awake {before.Awake} -> {after.Awake}, sleeping {before.Sleeping} -> {after.Sleeping}"),
        };

        foreach (var (name, passed, detail) in checks)
            Console.WriteLine($" {(passed ? "PASS" : "FAIL")}  {name,-42} {detail}");

        Console.WriteLine();
        Console.WriteLine($" amostras {samples}, nao resolvidas {unresolved}, transicoes {transitions}");
        Console.WriteLine();

        var ok = checks.All(c => c.Passed);
        Console.WriteLine($" AREA TRANSITION LIVE = {(ok ? "PASS" : "FAIL")}");

        return ok ? 0 : 1;
    }

    private static void Report(string label, Capture c)
    {
        Console.WriteLine($" -- {label} --------------------------------------------");
        Console.WriteLine($" GameStateRoot   0x{c.State.GameStateRoot:X}");
        Console.WriteLine($" InGameState     0x{c.State.InGameState:X}");
        Console.WriteLine($" AreaInstance    0x{c.State.AreaInstance:X}");
        Console.WriteLine($" LocalPlayer     0x{c.State.LocalPlayer:X}");
        Console.WriteLine($" AwakeEntities   {c.Awake}");
        Console.WriteLine($" SleepingEnt.    {c.Sleeping}");
        Console.WriteLine($" player          {c.PlayerName ?? "-"} lvl {c.Level}");
        Console.WriteLine($" vitals          {string.Join("  ", c.Vitals.Select(v => $"{v.Name} {v.Current}/{v.Max}"))}");
        Console.WriteLine($" world           {(c.World is { } w ? $"({w.X:0.}, {w.Y:0.}, {w.Z:0.})" : "-")}");
        Console.WriteLine($" components      {c.Components.Length}");
        Console.WriteLine($" camera          {(c.Camera == 0 ? "-" : $"0x{c.Camera:X}")}");
    }

    private static Capture? Sample(GameSession session)
    {
        var state = session.Resolve();
        if (state is null) return null;

        var player = new GameEntity(session.Memory, state.LocalPlayer);
        var components = player.Components();

        var awake = EntityWorld.Enumerate(session.Memory, state.AreaInstance, GameLayout.World.AwakeEntities);
        var sleeping = EntityWorld.Enumerate(session.Memory, state.AreaInstance, GameLayout.World.SleepingEntities);

        var camera = GameCamera.Resolve(session.Memory, state.InGameState);
        var world = player.WorldPosition();

        var projects = false;
        if (camera is not null && world is { } position)
            projects = camera.WorldToScreen(position).Status is ScreenStatus.OnScreen or ScreenStatus.OffScreen;

        var (name, level) = player.Identity();

        return new Capture(
            state,
            awake.Length,
            sleeping.Length,
            awake,
            name,
            level,
            player.Vitals(),
            world,
            components.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            camera?.Address ?? 0,
            projects);
    }
}
