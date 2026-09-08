using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Radar.Features.Navigation;

/// <summary>
/// Something on the map you can point at and say: route me there.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT), <c>RadarApp.NavTarget</c> and
/// <c>TryResolveTargetGrid</c>. The id scheme is the reference's — <c>t:</c> for
/// a tile landmark keyed by its cluster, <c>e:</c> for an entity keyed by its id
/// — and it is the whole reason a target survives from one tick to the next
/// without anyone holding a pointer.
///
/// A point of interest needs no third form: in the client a POI IS an entity, so
/// it resolves through <c>e:</c> like any other. The reference does the same, and
/// building a parallel catalogue for it would be inventing a problem.
/// </remarks>
/// <param name="Id">Stable within the area. Identity, not a handle.</param>
/// <param name="Label">What the route calls it.</param>
/// <param name="Grid">Where it was last seen, in grid cells.</param>
public sealed record NavTarget(string Id, string Label, Vector2 Grid, bool IsEntity)
{
    public static string IdFor(LandmarkSnapshot landmark) => "t:" + landmark.Key;

    public static string IdFor(EntitySnapshot entity) => "e:" + entity.Id;
}

/// <summary>
/// The targets an area currently offers, rebuilt from each world snapshot.
/// </summary>
public static class NavTargets
{
    /// <summary>Cap on entity targets, so a busy area cannot make the list unusable.</summary>
    private const int MaxEntityTargets = 60;

    /// <summary>
    /// Everything worth routing to: every tile landmark, then every entity the
    /// client itself marks or that reads as a destination.
    /// </summary>
    public static List<NavTarget> From(WorldSnapshot snapshot)
    {
        var targets = new List<NavTarget>();

        foreach (var landmark in snapshot.Marks)
            targets.Add(new NavTarget(NavTarget.IdFor(landmark), landmark.Name, landmark.Centre, false));

        foreach (var entity in snapshot.Entities)
        {
            if (targets.Count >= MaxEntityTargets) break;

            // A monster is not a destination — it moves, it dies, and routing to
            // one is how the list fills with noise. The client's own markers and
            // the exits are what people navigate to.
            var worth = entity.IsPoi ||
                        entity.Kind is EntityKind.Transition or EntityKind.Chest or EntityKind.Npc;

            if (!worth || entity.GridPosition is not { } grid) continue;

            targets.Add(new NavTarget(NavTarget.IdFor(entity), Label(entity), grid, true));
        }

        return targets;
    }

    /// <summary>
    /// Where a selected target is NOW, from this snapshot. Null when it has gone
    /// — the reference's behaviour: the id stays selected so the route resumes if
    /// the thing comes back into range, but nothing is drawn meanwhile.
    /// </summary>
    /// <summary>A route target that is a place rather than a thing.</summary>
    /// <remarks>
    /// The guide often knows roughly WHERE something is without there being any
    /// entity to point at — a boss that has not spawned yet, a walkthrough line
    /// saying the exit is opposite the entrance. Encoding the spot as an id lets
    /// it through the same planner, tracker and renderer as everything else
    /// instead of growing a second kind of route.
    /// </remarks>
    public static string IdFor(Vector2 grid) =>
        $"g:{grid.X.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}," +
        $"{grid.Y.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)}";

    public static Vector2? Resolve(string id, WorldSnapshot snapshot)
    {
        if (id.StartsWith("g:", StringComparison.Ordinal))
        {
            var comma = id.IndexOf(',');

            if (comma < 3) return null;

            return float.TryParse(id[2..comma], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) &&
                float.TryParse(id[(comma + 1)..], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var y)
                ? new Vector2(x, y)
                : null;
        }

        if (id.Length < 2) return null;

        if (id.StartsWith("t:", StringComparison.Ordinal))
        {
            var key = id[2..];

            foreach (var landmark in snapshot.Marks)
            {
                if (landmark.Key == key) return landmark.Centre;
            }

            return null;
        }

        if (!id.StartsWith("e:", StringComparison.Ordinal)) return null;

        foreach (var entity in snapshot.Entities)
        {
            if (NavTarget.IdFor(entity) != id) continue;

            // A finished encounter is dropped, as the reference drops it: the
            // client fades the icon, and a route still pointing there is a lie.
            return entity.IconComplete ? null : entity.GridPosition;
        }

        return null;
    }

    /// <summary>
    /// What a selected target is called, from this snapshot. The same name the
    /// map prints, so the route's end and the marker under it agree.
    /// </summary>
    public static string? Name(string id, WorldSnapshot snapshot)
    {
        if (id.StartsWith("t:", StringComparison.Ordinal))
        {
            var key = id[2..];

            foreach (var landmark in snapshot.Marks)
            {
                if (landmark.Key == key) return landmark.Name;
            }

            return null;
        }

        foreach (var entity in snapshot.Entities)
        {
            if (NavTarget.IdFor(entity) == id) return Label(entity);
        }

        foreach (var entity in snapshot.Recalled)
        {
            if (NavTarget.IdFor(entity) == id) return Label(entity);
        }

        return null;
    }

    /// <summary>The same name the map draws, so a route row and a marker agree.</summary>
    private static string Label(EntitySnapshot entity) =>
        entity.FriendlyName ?? NativeMap.EntityLabels.Pretty(entity.Metadata);
}
