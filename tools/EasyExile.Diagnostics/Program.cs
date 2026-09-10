using System.Diagnostics;
using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

Console.OutputEncoding = System.Text.Encoding.UTF8;

var clock = Stopwatch.StartNew();

Console.WriteLine();
Console.WriteLine("== CONTRACT ================================================");
Console.WriteLine($" fingerprint    {GameLayout.Build.Fingerprint}");
Console.WriteLine($" schema         {GameLayout.Build.SchemaVersion}");
Console.WriteLine($" export hash    {GameLayout.Build.ExportHash}");
Console.WriteLine();

var process = GameSession.FindClient();
if (process is null)
{
    Console.WriteLine("PathOfExile nao esta rodando.");
    return 2;
}

GameSession session;
try
{
    session = GameSession.Attach(process);
}
catch (BuildMismatchException mismatch)
{
    // Refusing is the whole point: the contract describes one build and this is
    // another, so every offset below would read the wrong field.
    Console.WriteLine("== BUILD ===================================================");
    Console.WriteLine(" OFFSETS BUILD MISMATCH");
    Console.WriteLine($" {mismatch.Check.Detail}");
    Console.WriteLine();
    Console.WriteLine(" REFUSING TO RUN");
    Console.WriteLine();
    return 3;
}

using (session)
{
    Console.WriteLine("== BUILD ===================================================");
    Console.WriteLine($" executable     {session.Build.ExecutableName}");
    Console.WriteLine($" fingerprint    {session.Build.Fingerprint}  MATCH");
    Console.WriteLine($" pe timestamp   0x{session.Build.PeTimestamp:X8}");
    Console.WriteLine($" image size     0x{session.Build.ImageSize:X8}");
    Console.WriteLine();

    // Before the bail-out below, because this probe exists to explain it.
    if (args.Contains("--chain-probe"))
        return EasyExile.Diagnostics.ChainProbe.Run(session);

    var state = session.Resolve();
    if (state is null)
    {
        Console.WriteLine(" chain nao resolveu. O personagem esta numa area?");
        return 1;
    }

    Console.WriteLine("== CHAIN ===================================================");
    Console.WriteLine($" GameStateRoot  0x{state.GameStateRoot:X}");
    Console.WriteLine($" InGameState    0x{state.InGameState:X}");
    Console.WriteLine($" AreaInstance   0x{state.AreaInstance:X}");
    Console.WriteLine($" LocalPlayer    0x{state.LocalPlayer:X}");
    Console.WriteLine();

    var player = new GameEntity(session.Memory, state.LocalPlayer);
    var components = player.Components();

    Console.WriteLine("== PLAYER ==================================================");
    Console.WriteLine($" metadata       {player.Metadata}");

    var (name, level) = player.Identity();
    Console.WriteLine($" name           {name ?? "-"}");
    Console.WriteLine($" level          {level}");

    foreach (var vital in player.Vitals())
        Console.WriteLine($" {vital.Name,-14} {vital.Current}/{vital.Max}   (id {vital.Id})");

    var world = player.WorldPosition();
    var grid = player.GridPosition();
    Console.WriteLine($" world          {(world is { } w ? $"({w.X:0.#}, {w.Y:0.#}, {w.Z:0.#})" : "-")}");
    Console.WriteLine($" grid           {(grid is { } g ? $"({g.X:0.#}, {g.Y:0.#})" : "-")}");
    Console.WriteLine();

    Console.WriteLine("== COMPONENT SYSTEM ========================================");
    Console.WriteLine($" {components.Count} components on the local player");
    Console.WriteLine($" {string.Join(", ", components.Keys.OrderBy(k => k, StringComparer.Ordinal))}");

    // The owner back-reference is what proves the resolver landed on real
    // components rather than on readable memory.
    var owners = components.Values.Count(c => player.OwnerOf(c) == state.LocalPlayer);
    Console.WriteLine($" owner back-reference matches on {owners}/{components.Count}");
    Console.WriteLine();

    Console.WriteLine("== CAMERA ==================================================");
    var camera = GameCamera.Resolve(session.Memory, state.InGameState);

    if (camera is null)
    {
        Console.WriteLine(" camera nao resolveu");
    }
    else
    {
        var viewport = camera.Viewport();
        var matrix = camera.ViewProjection();

        Console.WriteLine($" address        0x{camera.Address:X}");
        Console.WriteLine($" viewport       {(viewport is { } v ? $"{v.Width}x{v.Height}" : "-")}");
        Console.WriteLine($" projection     {(matrix is null ? "invalid" : "valid")}");

        if (world is { } playerWorld)
        {
            var point = camera.WorldToScreen(playerWorld);
            Console.WriteLine($" player screen  {point.Status} ({point.Screen.X:0.}, {point.Screen.Y:0.})  depth {point.Depth:0.###}");
        }
    }

    Console.WriteLine();

    Console.WriteLine("== ENTITY WORLD ============================================");
    var awake = EntityWorld.Enumerate(session.Memory, state.AreaInstance, GameLayout.World.AwakeEntities);
    var sleeping = EntityWorld.Enumerate(session.Memory, state.AreaInstance, GameLayout.World.SleepingEntities);

    Console.WriteLine($" awake          {awake.Length}");
    Console.WriteLine($" sleeping       {sleeping.Length}");

    Console.WriteLine();

    // The raw sections above prove the layer that reads memory. This one proves
    // the layer the product actually consumes: the same client state, captured
    // once, with no reader anywhere inside the result.
    Console.WriteLine("== WORLD SNAPSHOT ==========================================");

    var capture = session.Capture();

    if (!capture.Success)
    {
        Console.WriteLine($" captura nao produziu snapshot: {capture.Status}");
    }
    else
    {
        var snapshot = capture.Snapshot!;

        Console.WriteLine($" timestamp      {snapshot.Timestamp:HH:mm:ss.fff}");
        Console.WriteLine($" epoch          {snapshot.Epoch}");
        Console.WriteLine($" area           {snapshot.Area}");
        Console.WriteLine($" player         {snapshot.Player.Name ?? "-"} lvl {snapshot.Player.Level}");
        Console.WriteLine($" health         {snapshot.Player.Health}/{snapshot.Player.MaxHealth}");
        Console.WriteLine($" mana           {snapshot.Player.Mana}/{snapshot.Player.MaxMana}");
        Console.WriteLine($" energy shield  {snapshot.Player.EnergyShield}/{snapshot.Player.MaxEnergyShield}");

        Console.WriteLine($" world          {FormatWorld(snapshot.Player.WorldPosition)}");
        Console.WriteLine($" grid           {FormatGrid(snapshot.Player.GridPosition)}");
        Console.WriteLine($" entities       {snapshot.Entities.Length}");
        Console.WriteLine($" map visible    {snapshot.Map.IsVisible}   usable {snapshot.Map.IsUsable}");
        Console.WriteLine($" map shift      ({snapshot.Map.ShiftX:0.#}, {snapshot.Map.ShiftY:0.#})   zoom {snapshot.Map.Zoom:0.###}");
        Console.WriteLine(snapshot.Terrain is { } t
            ? $" terrain        {t.Width} x {t.Height}  ({t.Walkable.Count(b => b != 0)} celulas caminháveis)"
            : " terrain        AUSENTE");
        Console.WriteLine($" can draw map   {snapshot.CanDrawOnMap}");
        Console.WriteLine();
        Console.WriteLine(" categorias:");

        foreach (var group in snapshot.Entities.GroupBy(e => e.Kind).OrderByDescending(g => g.Count()))
            Console.WriteLine($"   {group.Key,-18} {group.Count(),5}");

        var interesting = snapshot.Entities.Count(e => e.IsInteresting);
        var alive = snapshot.Entities.Count(e => e.IsInteresting && e.IsAlive);
        Console.WriteLine($"   {"-> uteis",-18} {interesting,5}   (vivos {alive})");
        Console.WriteLine();
        Console.WriteLine(" tudo que seria desenhado:");

        foreach (var e in snapshot.Entities.Where(e => e.IsInteresting).OrderBy(e => e.Kind).ThenBy(e => e.Metadata))
            Console.WriteLine($"   {e.Kind,-13} vivo={e.IsAlive,-5} marcado={e.IsPoi,-5} {e.Metadata}");

        // Whatever is sitting on top of the player. A marker that never leaves
        // the blip is something mis-categorised, and the metadata names it.
        if (snapshot.Player.GridPosition is { } pg)
        {
            Console.WriteLine();
            Console.WriteLine(" a 12 celulas do player:");

            foreach (var e in snapshot.Entities
                         .Where(e => e.GridPosition is { } g &&
                                     Math.Abs(g.X - pg.X) < 12 && Math.Abs(g.Y - pg.Y) < 12)
                         .OrderBy(e => e.Kind))
            {
                var at = e.GridPosition!.Value;
                Console.WriteLine($"   {e.Kind,-14} vivo={e.IsAlive,-5} d=({at.X - pg.X,5:0.0},{at.Y - pg.Y,5:0.0})  {e.Metadata}");
                Console.WriteLine($"      componentes: {string.Join(" ", e.ComponentNames)}");
            }
        }
        Console.WriteLine($" camera         {(snapshot.Camera is { } c ? $"{c.Width}x{c.Height}" : "-")}");
        Console.WriteLine();

        Console.WriteLine($" {"Category",-14} {"Screen",-22} {"Components",11}  Metadata");

        foreach (var entity in snapshot.Entities.Take(12))
        {
            var screen = "-";

            // Projected from the captured camera, not by reading the client
            // again — which is exactly what a renderer will do.
            if (snapshot.Camera is { } view && entity.WorldPosition is { } position)
            {
                var point = view.Project(position);
                screen = point.Status == ScreenStatus.OnScreen
                    ? $"{point.Status} ({point.Screen.X:0.},{point.Screen.Y:0.})"
                    : point.Status.ToString();
            }

            Console.WriteLine(
                $" {entity.Category,-14} {screen,-22} {entity.ComponentNames.Length,11}  {Trim(entity.Metadata, 44)}");
        }
    }

    Console.WriteLine();

    // --watch observes the client across an area transition. It asks the
    // player for nothing: it reports what it sees.
    if (args.Contains("--watch"))
    {
        var seconds = 120;
        var index = Array.IndexOf(args, "--seconds");
        if (index >= 0 && index + 1 < args.Length) int.TryParse(args[index + 1], out seconds);

        var verdict = EasyExile.Diagnostics.AreaTransitionWatch.Run(session, seconds);

        Console.WriteLine();
        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();
        return verdict;
    }

    if (args.Contains("--slot-watch"))
    {
        var verdict = EasyExile.Diagnostics.SlotWatch.Run(session);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--loot-funnel"))
    {
        var verdict = EasyExile.Diagnostics.LootFunnelWatch.Run(session);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--camera-watch"))
    {
        var view = GameCamera.Resolve(session.Memory, session.Resolve()!.InGameState)?.Viewport();

        var verdict = EasyExile.Diagnostics.CameraWatch.Run(
            session, view?.Width ?? 1920, view?.Height ?? 1080);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--map-align"))
    {
        var view = GameCamera.Resolve(session.Memory, session.Resolve()!.InGameState)?.Viewport();

        var verdict = EasyExile.Diagnostics.MapAlignWatch.Run(
            session, view?.Width ?? 1920, view?.Height ?? 1080);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--transition-names"))
    {
        var verdict = EasyExile.Diagnostics.TransitionNameProbe.Run(session);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--mapui-watch"))
    {
        var howLong = 60;
        var where = Array.IndexOf(args, "--seconds");
        if (where >= 0 && where + 1 < args.Length) int.TryParse(args[where + 1], out howLong);

        var viewport = GameCamera.Resolve(session.Memory, session.Resolve()!.InGameState)?.Viewport();

        var verdict = EasyExile.Diagnostics.MapUiWatch.Run(
            session, howLong, viewport?.Width ?? 1920, viewport?.Height ?? 1080);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--area-watch"))
    {
        var howLong = 45;
        var where = Array.IndexOf(args, "--seconds");
        if (where >= 0 && where + 1 < args.Length) int.TryParse(args[where + 1], out howLong);

        var verdict = EasyExile.Diagnostics.AreaIdentityWatch.Run(session, howLong);

        Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
        Console.WriteLine();

        return verdict;
    }

    if (args.Contains("--confirm-mod-record"))
    {
        // Confirming an offset means showing that ONE candidate explains every
        // sample and that the others explain none. A single probe that happened
        // to read a string is not a confirmation, it is a coincidence.
        Console.WriteLine();
        Console.WriteLine("== CONFIRM ItemModEntry.RecordPointer ======================");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        var caches = new CaptureCaches();
        var (slots, tooltip, _) = ItemSlotReader.Read(session.Memory, chain.InGameState, 1f, caches);

        Console.WriteLine(tooltip is null
            ? " tooltip: nenhum aberto"
            : $" tooltip: ({tooltip.X:0},{tooltip.Y:0}) {tooltip.Width:0}x{tooltip.Height:0}" +
              $"  {tooltip.ModRows.Length} linhas de mod");

        // The UI shows whatever panel happens to be open, which is a thin
        // sample. Every item lying in the area carries the same component and
        // needs no panel, so the two together cover far more item classes.
        var ground = new List<nint>();

        if (session.Capture(new CaptureOptions(MaxEntities: 2000)).Snapshot is { } area)
        {
            foreach (var e in area.Entities)
            {
                if (!e.ComponentNames.Contains(GameLayout.Item.WorldItemComponent)) continue;

                var wrapper = new GameEntity(session.Memory, e.Id.Address, caches.Types);
                var inner = wrapper.Component(GameLayout.Item.WorldItemComponent);

                if (inner != 0 &&
                    session.Memory.TryReadPointer(inner + GameLayout.Item.Inner, out var item) &&
                    item != 0)
                    ground.Add(item);
            }
        }

        var stride = GameLayout.Item.ModStride;
        var hits = new int[stride / 8];
        var samples = 0;
        var classes = new SortedSet<string>(StringComparer.Ordinal);
        var examples = new List<string>();

        var addresses = slots.Select(x => x.Entity.Address).Concat(ground).Distinct().ToArray();

        Console.WriteLine($" itens: {slots.Length} em paineis + {ground.Count} no chao");

        foreach (var address in addresses)
        {
            var entity = new GameEntity(session.Memory, address, caches.Types);
            var mods = entity.Component(GameLayout.Item.ModsComponent);

            if (mods == 0) continue;

            foreach (var at in new[] { GameLayout.Item.ExplicitMods, GameLayout.Item.ImplicitMods })
            {
                if (!session.Memory.TryReadPointer(mods + at, out var first) ||
                    !session.Memory.TryReadPointer(mods + at + 8, out var last) ||
                    first == 0 || last <= first)
                    continue;

                var count = (int)((last - first) / stride);

                if (count is <= 0 or > 32) continue;

                for (var i = 0; i < count; i++)
                {
                    samples++;
                    classes.Add(entity.Metadata ?? "?");

                    // Every 8-byte slot of the entry, dereferenced once and read
                    // as a mod id. Only the true record pointer can produce one
                    // on every sample.
                    for (var off = 0; off < stride; off += 8)
                    {
                        if (!session.Memory.TryReadPointer(first + (i * stride) + off, out var record) ||
                            record == 0)
                            continue;

                        if (!session.Memory.TryReadPointer(record + GameLayout.Rank.ModId, out var text) ||
                            text == 0)
                            continue;

                        if (session.Memory.TryReadUtf16(text, 96) is not { Length: > 3 } id) continue;
                        if (!id.All(c => char.IsLetterOrDigit(c) || c == '_')) continue;

                        hits[off / 8]++;

                        if (off == 0x28 && examples.Count < 8) examples.Add(id);
                    }
                }
            }
        }

        Console.WriteLine($" itens com mods: {classes.Count}   entradas de mod: {samples}");
        Console.WriteLine();
        Console.WriteLine(" offset   ids legiveis   cobertura");

        for (var i = 0; i < hits.Length; i++)
        {
            if (hits[i] == 0) continue;

            Console.WriteLine(
                $"   +0x{i * 8:X2}   {hits[i],10}   {(samples > 0 ? hits[i] * 100.0 / samples : 0):0.0}%");
        }

        Console.WriteLine();

        foreach (var id in examples) Console.WriteLine($"   {id}");

        var best = 0x28 / 8;
        var unique = hits[best] == samples && samples > 0 &&
                     hits.Where((_, i) => i != best).All(h => h == 0);

        Console.WriteLine();
        Console.WriteLine(unique
            ? " CONFIRMADO: 0x28 explica 100% das amostras e nenhum outro offset explica nenhuma."
            : " NAO CONFIRMADO com esta amostra.");

        Console.WriteLine();
    }

    if (args.Contains("--find-text"))
    {
        var where = Array.IndexOf(args, "--find-text");
        var needle = where >= 0 && where + 1 < args.Length
            ? args[where + 1]
            : "increased Movement Speed";

        return EasyExile.Diagnostics.TextHunt.Run(session, needle);
    }

    if (args.Contains("--stats"))
        return EasyExile.Diagnostics.StatDump.Run(session);

    if (args.Contains("--ui-bench"))
    {
        var passes = 20;
        var many = Array.IndexOf(args, "--passes");
        if (many >= 0 && many + 1 < args.Length) int.TryParse(args[many + 1], out passes);

        return EasyExile.Diagnostics.UiBench.Run(session, passes);
    }

    if (args.Contains("--hover-probe"))
    {
        var span = 20;
        var at = Array.IndexOf(args, "--seconds");
        if (at >= 0 && at + 1 < args.Length) int.TryParse(args[at + 1], out span);

        return EasyExile.Diagnostics.HoverProbe.Run(session, span);
    }

    if (args.Contains("--panel-watch"))
    {
        var window = 25;
        var mark = Array.IndexOf(args, "--seconds");
        if (mark >= 0 && mark + 1 < args.Length) int.TryParse(args[mark + 1], out window);

        return EasyExile.Diagnostics.PanelWatch.Run(session, window);
    }

    if (args.Contains("--tooltip-watch"))
    {
        // The production reader, in a loop. A single shot kept missing because
        // the hover and the read have to coincide, and that is a property of the
        // probe rather than of the reader being tested.
        Console.WriteLine();
        Console.WriteLine("== TOOLTIP WATCH ===========================================");
        Console.WriteLine(" passe o mouse sobre um item. 25 segundos.");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        var caches = new CaptureCaches();
        var deadline = DateTime.UtcNow.AddSeconds(25);
        var biggest = 0;

        while (DateTime.UtcNow < deadline)
        {
            var (found, tip, _) = ItemSlotReader.Read(session.Memory, chain.InGameState, 1f, caches);

            biggest = Math.Max(biggest, found.Length);

            if (tip is null) { Thread.Sleep(120); continue; }

            Console.WriteLine($" TOOLTIP ({tip.X:0},{tip.Y:0}) {tip.Width:0}x{tip.Height:0}");
            Console.WriteLine($" linhas de mod: {tip.ModRows.Length}");

            foreach (var row in tip.ModRows)
                Console.WriteLine($"   ({row.X:0},{row.Y:0}) {row.Width:0}x{row.Height:0}");

            // The panel carries no entity, so the item is whichever slot the
            // panel was opened from — the renderer answers that with the cursor.
            Console.WriteLine();

            return 0;
        }

        Console.WriteLine($" nenhum tooltip em 25s (slots vistos: {biggest}).");
        Console.WriteLine();

        return 0;
    }

    if (args.Contains("--tooltip-shape"))
        return EasyExile.Diagnostics.TooltipShape.Run(session);

    if (args.Contains("--tooltip-tree"))
    {
        var span = 20;
        var slot = Array.IndexOf(args, "--seconds");
        if (slot >= 0 && slot + 1 < args.Length) int.TryParse(args[slot + 1], out span);

        return EasyExile.Diagnostics.TooltipTree.Run(session, span);
    }

    if (args.Contains("--text-lines"))
    {
        var howLong = 15;
        var where = Array.IndexOf(args, "--seconds");
        if (where >= 0 && where + 1 < args.Length) int.TryParse(args[where + 1], out howLong);

        return EasyExile.Diagnostics.TextLineProbe.Run(session, howLong);
    }

    if (args.Contains("--ui-roots"))
    {
        // A different hypothesis, since walking the known root exhaustively
        // found nothing: the tooltip may hang off a DIFFERENT root. A UI
        // element is identifiable without any other offset — its Self field
        // points back at itself — so every root can be found by scanning the
        // in-game state for that shape.
        Console.WriteLine();
        Console.WriteLine("== UI ROOTS ================================================");
        Console.WriteLine(" deixe o mouse sobre um item. 10 segundos.");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        var roots = new List<(int Offset, nint Element)>();

        for (var at = 0; at < 0x900; at += 8)
        {
            if (!session.Memory.TryReadPointer(chain.InGameState + at, out var candidate) ||
                candidate == 0 || candidate % 8 != 0)
                continue;

            if (!session.Memory.TryReadPointer(candidate + GameLayout.Ui.Self, out var self)) continue;
            if (self != candidate) continue;

            roots.Add((at, candidate));
        }

        Console.WriteLine($" raizes de UI no InGameState: {roots.Count}");

        foreach (var (at, element) in roots)
        {
            var box = MapUiReader.AbsoluteRect(session.Memory, element);

            Console.WriteLine($"   +0x{at:X3}  0x{element:X}  rect ({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0}");
        }

        Console.WriteLine();

        var needles = new[]
        {
            "Requires: Level", "to maximum Energy Shield", "to maximum Mana",
            "to maximum Life", "increased Movement Speed", "Item Level",
        };

        var until = DateTime.UtcNow.AddSeconds(10);
        var hits = new List<string>();

        while (DateTime.UtcNow < until && hits.Count == 0)
        {
            foreach (var (at, element) in roots)
            {
                var queue = new Queue<nint>();
                var seen = new HashSet<nint>();

                queue.Enqueue(element);

                while (queue.Count > 0 && seen.Count < 20000)
                {
                    var node = queue.Dequeue();

                    if (node == 0 || !seen.Add(node)) continue;

                    if (session.Memory.TryReadPointer(node + GameLayout.Ui.Children, out var first) &&
                        session.Memory.TryReadPointer(
                            node + GameLayout.Ui.Children + GameLayout.Native.VectorLast, out var last) &&
                        first != 0 && last > first)
                    {
                        var count = (int)((last - first) / 8);

                        if (count is > 0 and <= 8192)
                        {
                            for (var i = 0; i < count; i++)
                            {
                                if (session.Memory.TryReadPointer(first + (i * 8), out var child) &&
                                    child != 0)
                                    queue.Enqueue(child);
                            }
                        }
                    }

                    var text = session.Memory.TryReadWideString(
                        node + GameLayout.Ui.Text,
                        GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                        GameLayout.Native.StringCapacity, maxLength: 400);

                    if (text is not { Length: > 25 }) continue;
                    if (!needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;
                    if (text.Contains('[') || text.StartsWith("#", StringComparison.Ordinal)) continue;

                    var box = MapUiReader.AbsoluteRect(session.Memory, node);

                    if (box.Y < -100 || box.Y > 1700) continue;

                    hits.Add(
                        $" ACHADO raiz +0x{at:X3}  ({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0}" +
                        Environment.NewLine + $"   {text.Replace((char)10, '|')}");
                }
            }

            Thread.Sleep(200);
        }

        Console.WriteLine(hits.Count == 0
            ? " NADA em nenhuma raiz."
            : string.Join(Environment.NewLine, hits.Take(10)));

        Console.WriteLine();
    }

    if (args.Contains("--tooltip-probe"))
    {
        // No filters. The two earlier attempts both filtered before looking —
        // once on the visible bit, once on screen position — and both missed.
        // This walks the whole tree and keeps anything whose TEXT could only
        // come from an item tooltip, then prints where it hangs.
        Console.WriteLine();
        Console.WriteLine("== TOOLTIP =================================================");
        Console.WriteLine(" abra o inventario e deixe o mouse parado sobre um item raro.");
        Console.WriteLine(" 45 segundos.");
        Console.WriteLine();

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        if (!session.Memory.TryReadPointer(chain.InGameState + GameLayout.Roots.UiRoot, out var root) ||
            root == 0)
        {
            Console.WriteLine(" sem UI root.");
            return 1;
        }

        // Phrases the client writes on an item and nowhere else on screen.
        var needles = new[]
        {
            "Requires: Level", "increased Movement Speed", "to maximum Energy Shield",
            "to maximum Mana", "to maximum Life", "increased Energy Shield",
            "to Intelligence", "to Strength", "to Dexterity", "Resistance",
        };

        List<string> report = new();
        var deepest = 0;
        var walked = 0;

        var until = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < until)
        {
            var queue = new Queue<(nint Element, nint Parent, int Depth)>();
            var seen = new HashSet<nint>();
            var parents = new Dictionary<nint, nint>();

            queue.Enqueue((root, 0, 0));

            while (queue.Count > 0 && seen.Count < 60000)
            {
                var (element, parent, depth) = queue.Dequeue();

                if (element == 0 || !seen.Add(element)) continue;

                parents[element] = parent;
                deepest = Math.Max(deepest, depth);

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
                                queue.Enqueue((child, element, depth + 1));
                        }
                    }
                }

                var text = session.Memory.TryReadWideString(
                    element + GameLayout.Ui.Text,
                    GameLayout.Native.StringBuffer, GameLayout.Native.StringSize,
                    GameLayout.Native.StringCapacity, maxLength: 160);

                if (text is not { Length: > 25 }) continue;
                if (!needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase))) continue;

                var box = MapUiReader.AbsoluteRect(session.Memory, element);

                // On screen only. The first run stopped on a chat message with a
                // linked item parked ten thousand pixels above the viewport —
                // my own filter, not the client's fault, and it ended the search
                // before the mouse had moved.
                if (box.Y < -100 || box.Y > 1700 || box.X < -100 || box.X > 2700) continue;

                // Not the character sheet either: its headings are short and it
                // is always up behind the inventory.
                if (text.StartsWith("#", StringComparison.Ordinal)) continue;

                // The character sheet writes its own markup — [Fire],
                // [Resistances|Resistance] — and an item tooltip never does.
                // It is always open behind the inventory, so without this the
                // search reports it and nothing else.
                if (text.Contains('[')) continue;

                if (report.Any(r => r.Contains(text, StringComparison.Ordinal))) continue;

                report.Add(
                    $" ACHADO em ({box.X:0},{box.Y:0}) {box.W:0}x{box.H:0} prof {depth}" +
                    Environment.NewLine + $"   texto: {text.Replace((char)10, ' ')}");

                // The chain is the point: it says which container the tooltip
                // hangs from and which link the earlier filters cut.
                var node = element;
                var step = 0;

                while (node != 0 && step++ < 12)
                {
                    session.Memory.TryRead<uint>(node + GameLayout.Ui.Flags, out var flags);

                    var nodeBox = MapUiReader.AbsoluteRect(session.Memory, node);
                    var visible = (flags & (1u << GameLayout.Ui.VisibleBit)) != 0;

                    report.Add(
                        $"   {new string(' ', step)}0x{node:X} flags 0x{flags:X8} " +
                        $"visivel={visible} rect ({nodeBox.X:0},{nodeBox.Y:0}) {nodeBox.W:0}x{nodeBox.H:0}");

                    node = parents.GetValueOrDefault(node);
                }
            }

            walked = seen.Count;

            Thread.Sleep(300);
        }

        Console.WriteLine($" nos visitados: {walked}   profundidade maxima: {deepest}");
        Console.WriteLine();

        if (report.Count == 0)
            Console.WriteLine(" NADA. O texto do tooltip nao esta nesta arvore de UI.");
        else
            foreach (var line in report.Take(60)) Console.WriteLine(line);

        Console.WriteLine();
    }

    if (args.Contains("--item-mods"))
    {
        // Can we read an item's rolled affixes with the offsets already in the
        // contract? The tier a plugin like ShowPoE1ModTier prints comes straight
        // out of the mod's internal NAME — Strength5 is tier 5 — so if the names
        // read, the hard half is already done.
        Console.WriteLine();
        Console.WriteLine("== ITEM MODS ===============================================");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        var caches = new CaptureCaches();
        var (slots, _, _) = ItemSlotReader.Read(session.Memory, chain.InGameState, 1f, caches);

        Console.WriteLine($" slots visiveis: {slots.Length}");
        Console.WriteLine();
        Console.WriteLine("    x     y     w     h    area  item");

        foreach (var s2 in slots.OrderByDescending(s2 => s2.Area))
        {
            Console.WriteLine(
                $" {s2.X,5:0} {s2.Y,5:0} {s2.Width,5:0} {s2.Height,5:0} {s2.Area,7:0}  " +
                $"{s2.Item.BaseName ?? s2.Item.Art ?? "?"}");
        }

        Console.WriteLine();

        foreach (var slot in slots)
        {
            var entity = new GameEntity(session.Memory, slot.Entity.Address, caches.Types);
            var mods = entity.Component(GameLayout.Item.ModsComponent);

            Console.WriteLine(
                $"   {slot.Item.BaseName ?? slot.Item.Art ?? "?"}  [{slot.Item.Rarity}]  " +
                $"Mods={(mods == 0 ? "AUSENTE" : mods.ToString("X"))}");

            if (mods == 0) continue;

            foreach (var (label, at) in new[]
                     {
                         ("explicit", GameLayout.Item.ExplicitMods),
                         ("implicit", GameLayout.Item.ImplicitMods),
                     })
            {
                session.Memory.TryReadPointer(mods + at, out var first);
                session.Memory.TryReadPointer(mods + at + 8, out var last);

                var span = first != 0 && last > first ? (long)(last - first) : 0;

                Console.WriteLine(
                    $"      {label,-9} span {span,4} bytes  = {span / Math.Max(1, GameLayout.Item.ModStride)} mods");

                foreach (var id in ItemMods(session.Memory, mods, at))
                    Console.WriteLine($"         {id}   ->   {Tier(id)}");
            }
        }

        Console.WriteLine();
    }

    if (args.Contains("--label-track"))
    {
        // Does a tag's rectangle track the camera when it is re-read from the
        // element the sweep already found — and what does asking cost. If it
        // does, the chip can move with the game's own label instead of with the
        // sweep that discovered it.
        Console.WriteLine();
        Console.WriteLine("== LABEL TRACK =============================================");

        var chain = GameSession.ResolveChain(session.Memory, session.Memory.ModuleBase);

        if (chain is null) { Console.WriteLine(" sem cadeia."); return 1; }

        var found = LootLabelReader.Read(session.Memory, chain.InGameState, 1f);

        Console.WriteLine($" tags {found.Length}");

        if (found.Length == 0)
        {
            Console.WriteLine(" nenhuma tag no chao. Larga um item e roda de novo.");
        }
        else
        {
            Console.WriteLine(" ande pelo mapa durante 4 segundos.");

            foreach (var (label, element) in found)
            {
                var bloco = MapUiReader.AbsoluteRect(session.Memory, element);
                var campo = MapUiReader.Walk(session.Memory, element);
                var inteiro = session.Memory.TryReadBytes(element + 0xB8, 448, out _);

                Console.WriteLine(
                    $"   {label.Text,-24} bloco ({bloco.X:0},{bloco.Y:0},{bloco.W:0}x{bloco.H:0})" +
                    $"  campo ({campo.X:0},{campo.Y:0},{campo.W:0}x{campo.H:0})" +
                    $"  no inteiro {(inteiro ? "ok" : "FALHOU")}");
            }

            Console.WriteLine();
            Console.WriteLine(" re-lendo cada elemento por 4s:");

            var sizeChanged = new int[found.Length];
            var wild = new int[found.Length];
            var samples = 0;

            var until = DateTime.UtcNow.AddSeconds(4);
            var reads0 = EasyExile.Core.Memory.ReadCounter.Reads;
            var frames = 0;

            while (DateTime.UtcNow < until)
            {
                for (var i = 0; i < found.Length; i++)
                {
                    var now = MapUiReader.AbsoluteRect(session.Memory, found[i].Element);

                    if (Math.Abs(now.W - found[i].Label.Width) > 1f ||
                        Math.Abs(now.H - found[i].Label.Height) > 1f)
                        sizeChanged[i]++;

                    // A tag lives inside the window. Far outside it is not the
                    // tag moving, it is the element no longer being one.
                    if (Math.Abs(now.X) > 20000f || Math.Abs(now.Y) > 20000f) wild[i]++;
                }

                samples++;
                frames++;
                Thread.Sleep(7);
            }

            var reads = EasyExile.Core.Memory.ReadCounter.Since(reads0);

            for (var i = 0; i < found.Length; i++)
            {
                Console.WriteLine(
                    $"   {found[i].Label.Text,-24} tamanho mudou em {sizeChanged[i],4}/{samples}" +
                    $"   posicao absurda em {wild[i],4}/{samples}");
            }

            Console.WriteLine();
            Console.WriteLine($" leituras       {reads / Math.Max(1, frames)} por frame ({found.Length} tags)");
        }

        Console.WriteLine();
    }

    if (args.Contains("--capture-bench"))
    {
        // What the world lane actually costs. The overlay reports this too, but
        // only while the game has focus — and the whole question here is how
        // often the entity list refreshes, which has nothing to do with focus.
        var passes = 30;
        var at = Array.IndexOf(args, "--passes");
        if (at >= 0 && at + 1 < args.Length) int.TryParse(args[at + 1], out passes);

        Console.WriteLine();
        Console.WriteLine("== CAPTURE BENCH ===========================================");

        // One warm-up: the first capture pays for the terrain read and fills the
        // per-area caches, and averaging it in would flatter or slander the rest.
        session.Capture();

        var samples = new List<double>(passes);
        var entities = 0;

        var readsBefore = EasyExile.Core.Memory.ReadCounter.Reads;

        for (var i = 0; i < passes; i++)
        {
            var started = Stopwatch.GetTimestamp();
            var result = session.Capture();
            samples.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (result.Snapshot is { } snap) entities = snap.Entities.Length;
        }

        var reads = EasyExile.Core.Memory.ReadCounter.Since(readsBefore) / (double)passes;

        samples.Sort();

        var median = samples[samples.Count / 2];
        var worst = samples[^1];

        Console.WriteLine($" passes         {passes}");
        Console.WriteLine($" entities       {entities}");
        Console.WriteLine($" median         {median:0.0} ms   ({(median > 0 ? 1000 / median : 0):0.0} Hz)");
        Console.WriteLine($" best           {samples[0]:0.0} ms");
        Console.WriteLine($" worst          {worst:0.0} ms");
        Console.WriteLine($" leituras       {reads:0} por captura   ({(entities > 0 ? reads / entities : 0):0.0} por entidade)");
        Console.WriteLine($" custo          {(reads > 0 ? median * 1000 / reads : 0):0.00} us por leitura");
        Console.WriteLine();
    }

    Console.WriteLine($" elapsed {clock.Elapsed.TotalSeconds:0.0}s");
    Console.WriteLine();
}

return 0;

/// <summary>The internal ids of one mod vector, which is where the tier lives.</summary>
static List<string> ItemMods(EasyExile.Core.Memory.IMemoryReader memory, nint mods, int vector)
{
    var found = new List<string>();

    if (!memory.TryReadPointer(mods + vector, out var first) ||
        !memory.TryReadPointer(mods + vector + 8, out var last) ||
        first == 0 || last <= first)
        return found;

    var stride = GameLayout.Item.ModStride;
    var count = (int)((last - first) / stride);

    if (count is <= 0 or > 32) return found;

    for (var i = 0; i < count; i++)
    {
        var element = first + (i * stride);

        // Found by walking every slot of the record and reading what came back
        // as text: an ITEM mod entry points at its record from 0x28, where the
        // MONSTER one uses 0x08. Live, this yields FlaskIncreasedRecoverySpeed2
        // and CharmImplicitUseOnColdDamage1.
        //
        // NOT a contract offset. It is written here, in a diagnostic, and
        // deliberately not in the overlay: an offset the Analyzer has not
        // confirmed does not belong in EasyExile.
        const int itemModRecord = 0x28;

        if (!memory.TryReadPointer(element + itemModRecord, out var record) || record == 0) continue;

        if (!memory.TryReadPointer(record + GameLayout.Rank.ModId, out var text) || text == 0) continue;

        if (memory.TryReadUtf16(text, 96) is { Length: > 1 } id &&
            id.All(c => char.IsLetterOrDigit(c) || c == '_'))
            found.Add(id);
    }

    return found;
}

/// <summary>
/// The tier a mod id carries in its own name.
/// </summary>
/// <remarks>
/// This is the trick the whole ShowPoE1ModTier plugin turns on, and it is
/// cheaper than it looks: the game names its mods Strength1..Strength8, so the
/// trailing number IS the tier and the letters before it are the family. No
/// stat ranges, no item level arithmetic.
///
/// What is missing without a table is only the CEILING: this can say tier 2, it
/// cannot say tier 2 of how many, which is what turns a number into "T1!".
/// </remarks>
static string Tier(string id)
{
    var end = id.TrimEnd('_');
    var digits = 0;

    while (digits < end.Length && char.IsDigit(end[^(digits + 1)])) digits++;

    if (digits == 0) return "sem tier no nome";

    return $"familia {end[..^digits]}, tier {end[^digits..]}";
}

/// <summary>
/// The biggest column of short text lines sharing a left edge.
/// </summary>
/// <remarks>
/// A tooltip is exactly that shape and almost nothing else on screen is: the
/// chat is one wide column of long lines, the quest tracker is right-aligned
/// and sparse. Finding it by shape rather than by position is what survives the
/// client drawing it wherever it likes.
/// </remarks>
static List<(float X, float Y, float W, float H, int Depth, string Text)> Stack(
    List<(float X, float Y, float W, float H, int Depth, string Text)> all)
{
    var best = new List<(float X, float Y, float W, float H, int Depth, string Text)>();

    foreach (var anchor in all)
    {
        var column = all
            .Where(e => Math.Abs(e.X - anchor.X) < 40 && e.H < 60)
            .OrderBy(e => e.Y)
            .ToList();

        if (column.Count > best.Count) best = column;
    }

    return best;
}

[System.Runtime.InteropServices.DllImport("user32.dll")]
static extern bool GetCursorPos(out CursorPoint point);

static string Trim(string text, int max) =>
    text.Length <= max ? text : "..." + text[^(max - 3)..];

static string FormatWorld(Vector3? world) =>
    world is { } w ? $"({w.X:0.#}, {w.Y:0.#}, {w.Z:0.#})" : "-";

static string FormatGrid(Vector2? grid) =>
    grid is { } g ? $"({g.X:0.#}, {g.Y:0.#})" : "-";

struct CursorPoint { public int X; public int Y; }
