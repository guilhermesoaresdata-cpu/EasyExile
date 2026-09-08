using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;
using System.Text.Json;

namespace EasyExile.Diagnostics;

/// <summary>
/// Walks the price pipeline stage by stage and reports where it stops.
/// </summary>
/// <remarks>
/// The feature either draws a price or does not, and a screenshot cannot say
/// which of six gates rejected the drop. This runs the same stages in the same
/// order and counts survivors at each, so the answer is a number rather than a
/// guess.
/// </remarks>
public static class LootFunnelWatch
{
    public static int Run(GameSession session)
    {
        var result = session.Capture(new CaptureOptions(MaxEntities: 2000));

        if (result.Snapshot is not { } snapshot)
        {
            Console.WriteLine(" sem snapshot.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("== LOOT FUNNEL =============================================");
        Console.WriteLine($" area {snapshot.Zone}   league {snapshot.League ?? "<null>"}");

        Exits(snapshot);

        Console.WriteLine();
        Console.WriteLine($" landmarks no mapa: {snapshot.Marks.Length}");

        foreach (var m in snapshot.Marks)
            Console.WriteLine($"   {m.Name,-28} ({m.Centre.X:0},{m.Centre.Y:0})  tiles {m.TileCount}  saida={m.IsWayOut}");

        Tags(snapshot);
        Slots(snapshot);
        Console.WriteLine($" entidades no snapshot   {snapshot.Entities.Length}");
        Console.WriteLine();

        var withItem = 0;
        var withIdentity = 0;

        // What the metadata of a drop actually looks like, which is the thing
        // the capture gate is testing against.
        var kinds = new Dictionary<EntityKind, int>();
        var paths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entity in snapshot.Entities)
        {
            var looksLikeLoot = entity.ComponentNames.Contains("WorldItem");

            if (!looksLikeLoot) continue;

            kinds[entity.Kind] = kinds.GetValueOrDefault(entity.Kind) + 1;
            paths[entity.Metadata] = paths.GetValueOrDefault(entity.Metadata) + 1;

            if (entity.Item is not { } item) continue;

            withItem++;

            if (item.HasIdentity) withIdentity++;
        }

        Console.WriteLine($" carregando o componente WorldItem   {kinds.Values.Sum()}");
        Console.WriteLine($" com Item preenchido pela captura    {withItem}");
        Console.WriteLine($" com arte ou nome base               {withIdentity}");
        Console.WriteLine();

        if (kinds.Count > 0)
        {
            Console.WriteLine(" classificados como:");

            foreach (var (kind, count) in kinds.OrderByDescending(p => p.Value))
                Console.WriteLine($"   {kind,-14} {count}");

            Console.WriteLine();
            Console.WriteLine(" metadata dos drops (o que o gate da captura testa):");

            foreach (var (path, count) in paths.OrderByDescending(p => p.Value).Take(6))
                Console.WriteLine($"   {count,4}  {path}");
        }
        else
        {
            Console.WriteLine(" NENHUMA entidade com WorldItem chegou ao snapshot.");
            Console.WriteLine(" Ou nao ha loot no chao, ou o classificador as descarta antes.");
        }

        Console.WriteLine();

        // Details of the first few, so a stage that silently returns null is
        // visible rather than merely counted.
        var shown = 0;

        foreach (var entity in snapshot.Entities)
        {
            if (!entity.ComponentNames.Contains("WorldItem") || shown >= 6) continue;

            shown++;

            var item = entity.Item;

            Console.WriteLine(
                $"   {entity.Kind,-12} item={(item is null ? "<null>" : "ok")}" +
                $"  arte={item?.Art ?? "-"}  nome={item?.BaseName ?? "-"}" +
                $"  raridade={item?.Rarity.ToString() ?? "-"}  id={(item?.Identified.ToString() ?? "-")}");
        }

        // The remaining gates: does a price exist, and does it survive the
        // filters. A drop that reads perfectly and is still not drawn is a
        // filter doing its job, and that is worth being able to see.
        //
        // The cache is read directly rather than through the Radar's PriceBook:
        // a diagnostic that reaches into the presentation assembly to answer a
        // data question would be coupling for no gain.
        var cache = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "EasyExile.Radar", "bin", "Debug", "net8.0-windows", "prices.json");

        Console.WriteLine();

        if (!File.Exists(cache))
        {
            Console.WriteLine($" prices.json nao encontrado em {Path.GetFullPath(cache)}");
            Console.WriteLine(" rode o radar uma vez para baixar o indice.");

            return 0;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(cache));

        var byArt = Index(document.RootElement, "ByArt");
        var byName = Index(document.RootElement, "ByName");

        Console.WriteLine($" price book: {byArt.Count} artes, {byName.Count} nomes");
        Console.WriteLine();

        const float minimum = 1f;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Item is not { HasIdentity: true } item) continue;

            var art = item.Art is null ? null : Find(byArt, item.Art);
            var named = item.BaseName is null ? null : Find(byName, item.BaseName);

            var hit = item.Rarity == MonsterRarity.Unique ? art ?? named : named ?? art;

            var label = item.BaseName ?? item.Art ?? "?";

            if (hit is not { } price)
            {
                Console.WriteLine($"   {label,-30} SEM PRECO — nem a arte nem o nome estao no indice");
                continue;
            }

            var verdict = price.Exalted < minimum ? $"abaixo do minimo ({minimum} ex)" : "DESENHA";

            Console.WriteLine($"   {label,-30} {price.Exalted,9:0.###} ex  [{price.Category,-10}] {verdict}");
        }

        Console.WriteLine();

        return 0;
    }

    /// <summary>
    /// The game's own loot tags, as the capture publishes them.
    /// </summary>
    /// <remarks>
    /// The tag route does not go through the entity list at all: it matches the
    /// text the game printed against the price book. So the question "does a
    /// price draw" splits in two, and only this half is about the UI walk.
    /// </remarks>
    public static void Tags(WorldSnapshot snapshot)
    {
        Console.WriteLine();
        Console.WriteLine($" tags de loot publicadas: {snapshot.Labels.Length}");

        foreach (var label in snapshot.Labels.Take(20))
        {
            Console.WriteLine(
                $"   [{label.X,6:0},{label.Y,6:0} {label.Width,5:0}x{label.Height,4:0}]  {label.Text}");
        }

        if (snapshot.Labels.Length == 0)
        {
            Console.WriteLine("   nenhuma. Ou nao ha loot no chao, ou o container nao resolveu.");
            return;
        }

        // Cross-check the rect arithmetic against something independent. A town
        // label names an NPC we can also project from its world position, so if
        // the two disagree the rect is wrong — and every chip drawn on a tag is
        // wrong with it. Nothing else in the pipeline can tell us that.
        if (snapshot.Camera is not { } camera) return;

        Console.WriteLine();
        Console.WriteLine(" conferencia: retangulo da tag  x  posicao projetada da entidade");

        foreach (var label in snapshot.Labels)
        {
            var match = snapshot.Entities.FirstOrDefault(e =>
                e.FriendlyName is { Length: > 0 } n &&
                string.Equals(n, label.Text, StringComparison.OrdinalIgnoreCase) &&
                e.WorldPosition is not null);

            if (match?.WorldPosition is not { } world) continue;

            var point = camera.Project(world);

            var dx = label.CentreX - point.Screen.X;
            var dy = label.CentreY - point.Screen.Y;

            Console.WriteLine(
                $"   {label.Text,-22} tag ({label.CentreX,6:0},{label.CentreY,6:0})" +
                $"   entidade ({point.Screen.X,6:0},{point.Screen.Y,6:0})" +
                $"   delta ({dx,6:0},{dy,6:0})  {point.Status}");
        }
    }

    /// <summary>Where every exit in this area leads, by code.</summary>
    public static void Exits(WorldSnapshot snapshot)
    {
        Console.WriteLine();
        Console.WriteLine($" area atual: {snapshot.AreaCode ?? "<null>"}");
        Console.WriteLine();

        // Every transition the walk saw, not only the ones that named a
        // destination. "The guide never activates" is either no transitions at
        // all, or transitions whose destination we cannot read — and those are
        // completely different problems.
        var transitions = snapshot.Entities
            .Where(e => e.Kind == EntityKind.Transition ||
                        e.DestinationCode is { Length: > 0 } ||
                        e.Metadata.Contains("AreaTransition", StringComparison.OrdinalIgnoreCase) ||
                        e.Metadata.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Console.WriteLine($" transicoes vistas: {transitions.Length}");

        foreach (var t in transitions)
        {
            Console.WriteLine(
                $"   {t.Kind,-11} destino={t.DestinationCode ?? "<null>",-16} " +
                $"nome={t.FriendlyName ?? "<null>",-28} {Short(t.Metadata)}");
        }

        var named = transitions.Count(t => t.DestinationCode is { Length: > 0 });

        Console.WriteLine();
        Console.WriteLine($" com destino legivel: {named} de {transitions.Length}");

        static string Short(string path)
        {
            var cut = path.LastIndexOf('/');
            return cut > 0 ? path[(cut + 1)..] : path;
        }
    }

    /// <summary>Visible item slots, as the capture publishes them.</summary>
    public static void Slots(WorldSnapshot snapshot)
    {
        Console.WriteLine();
        Console.WriteLine($" slots de item visiveis: {snapshot.Slots.Length}");

        foreach (var slot in snapshot.Slots.Take(20))
        {
            Console.WriteLine(
                $"   [{slot.X,6:0},{slot.Y,6:0} {slot.Width,4:0}x{slot.Height,4:0}]  x{slot.Count,-3}" +
                $"  {slot.Item.BaseName ?? slot.Item.Art ?? "?"}");
        }

        if (snapshot.Slots.Length == 0)
        {
            Console.WriteLine("   nenhum. Abra o inventario, ou o campo de slot nao resolveu.");
            return;
        }

        // Which price row each slot matched, and what it cost. A wrong number
        // on screen is either a wrong lookup or a wrong price, and only the
        // matched row name tells those apart.
        var cache = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "EasyExile.Radar", "bin", "Debug", "net8.0-windows", "prices.json");

        if (!System.IO.File.Exists(cache)) return;

        using var document = JsonDocument.Parse(System.IO.File.ReadAllText(cache));
        var byArt = Index(document.RootElement, "ByArt");
        var byName = Index(document.RootElement, "ByName");

        Console.WriteLine();
        Console.WriteLine(" casamento de preco por slot:");

        foreach (var slot in snapshot.Slots.Take(20))
        {
            var art = slot.Item.Art is null ? null : Find(byArt, slot.Item.Art);
            var named = slot.Item.BaseName is null ? null : Find(byName, slot.Item.BaseName);
            var hit = slot.Item.IsUnique ? art ?? named : named ?? art;

            Console.WriteLine(
                $"   {slot.Item.BaseName ?? "?",-32} arte={slot.Item.Art ?? "-",-28} " +
                (hit is null ? "SEM PRECO" : $"{hit.Value.Exalted,10:0.###} ex  [{hit.Value.Category}]"));
        }
    }

    private static Dictionary<string, (double Exalted, string Category)> Index(
        JsonElement root, string property)
    {
        var index = new Dictionary<string, (double, string)>(StringComparer.OrdinalIgnoreCase);

        if (!root.TryGetProperty(property, out var node)) return index;

        foreach (var entry in node.EnumerateObject())
        {
            var exalted = entry.Value.TryGetProperty("Exalted", out var ex) ? ex.GetDouble() : 0;
            var category = entry.Value.TryGetProperty("Category", out var c) ? c.GetString() ?? "" : "";

            index[entry.Name] = (exalted, category);
        }

        return index;
    }

    private static (double Exalted, string Category)? Find(
        Dictionary<string, (double Exalted, string Category)> index, string key) =>
        index.TryGetValue(key, out var value) ? value : null;
}
