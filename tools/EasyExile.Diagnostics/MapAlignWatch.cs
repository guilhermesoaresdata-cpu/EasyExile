using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Diagnostics;

/// <summary>
/// Measures our map projection against the game's own map labels.
/// </summary>
/// <remarks>
/// "It looks off" is not a measurement, and two rounds of reading screenshots
/// produced two wrong diagnoses. The game draws its own names on its map as UI
/// elements, and a UI element knows where it is — so the delta between where the
/// game puts a label and where we project the matching entity is readable, in
/// pixels, rather than estimated from a compressed image.
///
/// A consistent delta across every pair is a projection error. A delta that
/// differs per pair means we are drawing different things than the labels name.
/// Those need opposite fixes, which is the whole reason to measure first.
/// </remarks>
public static class MapAlignWatch
{
    public static int Run(GameSession session, int width, int height)
    {
        var result = session.Capture(new CaptureOptions(MaxEntities: 2000));

        if (result.Snapshot is not { } snapshot)
        {
            Console.WriteLine(" sem snapshot.");
            return 1;
        }

        var map = session.CaptureMapFrame();

        if (map is null || !map.Map.IsUsable)
        {
            Console.WriteLine(" abra o mapa do jogo (Tab) e rode de novo.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("== MAP ALIGNMENT ===========================================");
        Console.WriteLine($" cliente {width}x{height}   zoom {map.Map.Zoom:0.000}   origem {map.Map.OriginX:0},{map.Map.OriginY:0}");

        var scale = map.Map.Zoom * (height / 677f);
        var uiScale = height / 1600f;

        var centreX = (map.Map.HasOrigin ? map.Map.OriginX * uiScale : width * 0.5f) + map.Map.ShiftX;
        var centreY = (map.Map.HasOrigin ? map.Map.OriginY * uiScale : height * 0.5f) + map.Map.ShiftY - 20f;

        Console.WriteLine($" centro {centreX:0},{centreY:0}   escala {scale:0.000} px/celula");
        Console.WriteLine();

        // Only what the MAP has written, not every panel in the game. The map
        // element is the one seen both shown and hidden — the toggler.
        var mapElement = session.MapCandidates().FirstOrDefault(c => c.Toggler).Element;

        if (mapElement == 0) mapElement = session.MapCandidates().FirstOrDefault().Element;

        var labels = session.VisibleLabels(mapElement).ToList();

        Console.WriteLine($" rotulos do jogo visiveis: {labels.Count}");
        Console.WriteLine();
        Console.WriteLine($" {"rotulo",-24} {"jogo x,y",13} {"nosso x,y",13} {"delta",13}");

        // Paired by PROXIMITY, not by name: the game's labels are NPC names and
        // ours are metadata tails, and they will never match as strings. What
        // matters is whether a marker sits on the label it belongs to, and the
        // nearest label is the honest guess at which one that is.
        var deltas = new List<(string Label, float Lx, float Ly, float Sx, float Sy)>();

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Kind is not (EntityKind.Npc or EntityKind.Transition) && !entity.IsPoi) continue;
            if (entity.GridPosition is not { } grid) continue;

            var (sx, sy) = Project(grid, map.PlayerGrid, centreX, centreY, scale);

            if (sx < 0 || sx > width || sy < 0 || sy > height) continue;

            var best = float.MaxValue;
            (string Text, float X, float Y) nearest = default;

            foreach (var label in labels)
            {
                var d = ((label.X - sx) * (label.X - sx)) + ((label.Y - sy) * (label.Y - sy));

                if (d >= best) continue;

                best = d;
                nearest = label;
            }

            if (nearest.Text is null || MathF.Sqrt(best) > 220f) continue;

            deltas.Add((nearest.Text, nearest.X, nearest.Y, sx, sy));
        }

        var matched = deltas.Count;
        var totalX = 0f;
        var totalY = 0f;

        foreach (var (label, lx, ly, sx, sy) in deltas)
        {
            totalX += sx - lx;
            totalY += sy - ly;

            Console.WriteLine(
                $" {Short(label),-24} {lx,6:0},{ly,-6:0} {sx,6:0},{sy,-6:0} {sx - lx,6:0},{sy - ly,-6:0}");
        }

        Console.WriteLine();

        // What we would actually draw on screen, whatever the labels did. This
        // answers the question a screenshot cannot: what IS that dot.
        Console.WriteLine($" {"tipo",-14} {"nome",-26} {"grid",13} {"tela x,y",13}");

        var drawn = 0;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.GridPosition is not { } grid) continue;

            var kind = entity.Kind switch
            {
                EntityKind.Npc => "NPC",
                EntityKind.OtherPlayer => "OUTRO JOGADOR",
                EntityKind.Transition => "TRANSICAO",
                EntityKind.Chest => "BAU",
                _ when entity.IsPoi => "PONTO",
                _ => null,
            };

            if (kind is null) continue;

            var (sx, sy) = Project(grid, map.PlayerGrid, centreX, centreY, scale);

            if (sx < 0 || sx > width || sy < 0 || sy > height) continue;

            var name = entity.FriendlyName ?? Names(entity).LastOrDefault() ?? "?";

            Console.WriteLine($" {kind,-14} {Short(name),-26} {grid.X,6:0},{grid.Y,-6:0} {sx,6:0},{sy,-6:0}");

            drawn++;
        }

        Console.WriteLine();
        Console.WriteLine($" {drawn} marcador(es) na tela");
        Console.WriteLine();

        if (matched == 0)
        {
            Console.WriteLine(" nenhum rotulo do mapa pareou — o delta nao pode ser medido nesta execucao.");
            return 0;
        }

        Console.WriteLine($" pares medidos  {matched}");
        Console.WriteLine($" delta medio    {totalX / matched:0.0}, {totalY / matched:0.0} px");
        Console.WriteLine();
        Console.WriteLine(" delta consistente  -> erro de projecao");
        Console.WriteLine(" delta variavel     -> estamos desenhando outra coisa que o rotulo nomeia");
        Console.WriteLine();

        return 0;
    }

    /// <summary>The reference's projection, the same one the overlay draws with.</summary>
    private static (float X, float Y) Project(
        EasyExile.Core.Spatial.Vector2 cell, EasyExile.Core.Spatial.Vector2 player,
        float centreX, float centreY, float scale)
    {
        const float gridToWorld = 250f / 23f;

        _ = gridToWorld;

        var cos = (float)Math.Cos(38.7 * Math.PI / 180.0);
        var sin = (float)Math.Sin(38.7 * Math.PI / 180.0);

        var dx = cell.X - player.X;
        var dy = cell.Y - player.Y;

        return (centreX + (scale * (dx - dy) * cos), centreY + (scale * -(dx + dy) * sin));
    }

    private static IEnumerable<string> Names(EntitySnapshot entity)
    {
        if (entity.FriendlyName is { Length: > 0 } friendly) yield return friendly;

        var parts = entity.Metadata.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 0) yield return parts[^1];
    }

    private static string Short(string text) => text.Length <= 24 ? text : text[..24];
}
