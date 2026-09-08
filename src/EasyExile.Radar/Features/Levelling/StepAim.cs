using EasyExile.Core.Snapshots;
using EasyExile.Radar.Features.Navigation;

namespace EasyExile.Radar.Features.Levelling;

/// <summary>
/// What in this instance a campaign step is talking about.
/// </summary>
/// <remarks>
/// "Eu sei que voce nao sabe exatamente onde esta o boss, mas temos no mapa
/// registrado onde ele provavelmente esta" — exactly right, and the map already
/// had it. The Bone Pits carries two landmarks and the tool had already found
/// both: Blackrib Pit, and the way back to Mastodon Badlands. One of them is the
/// arena and the other is the exit, and the client says which by marking the
/// exit as a way out.
///
/// So a kill aims at the pit and a door aims at the way out, with no guessing
/// from the walkthrough's wording. Everything matched here is something the
/// CLIENT put in the world: a rare or unique, a named tile cluster, an NPC, an
/// icon it has not yet crossed off. The words say what; the world says where.
///
/// The first version fell back from "no rare in sight" to "nearest marked icon"
/// and produced "Matar: Waypoint", which is why the fallbacks below are typed
/// rather than a single nearest-anything.
/// </remarks>
public static class StepAim
{
    /// <summary>Where to route for this step, and what to call it.</summary>
    public static (string Id, string Label)? For(CampaignStep step, WorldSnapshot snapshot) =>
        step.Action switch
        {
            // The arena before the monster: a boss that has not spawned yet has
            // no entity, and its pit is on the map from the moment you arrive.
            StepAction.Kill =>
                Entity(snapshot, Boss) ?? Arena(snapshot) ?? Entity(snapshot, Objective),

            StepAction.Take =>
                Entity(snapshot, Objective) ??
                Entity(snapshot, e => e.Kind == EntityKind.Chest) ??
                Arena(snapshot),

            StepAction.Talk => Entity(snapshot, e => e.Kind == EntityKind.Npc),

            // The one step where a waypoint is the right answer.
            StepAction.Waypoint => Entity(snapshot, Waypoint),

            _ => null,
        };

    /// <summary>The way out, when no door has spawned near enough to see.</summary>
    public static (string Id, string Label)? Exit(WorldSnapshot snapshot) =>
        Landmark(snapshot, m => m.IsWayOut);

    /// <summary>A named place that is not an exit — which in practice is the fight.</summary>
    private static (string Id, string Label)? Arena(WorldSnapshot snapshot) =>
        Landmark(snapshot, m => !m.IsWayOut);

    /// <summary>
    /// A boss: unique only, never a rare.
    /// </summary>
    /// <remarks>
    /// A rare is a yellow pack leader and there are dozens of them in a zone, so
    /// routing to the nearest one meant the guide kept redrawing a line at
    /// whatever trash had just spawned. Unique is the rank the client gives a
    /// named fight, and it is the only one that means anything here.
    /// </remarks>
    private static bool Boss(EntitySnapshot e) =>
        e is { Kind: EntityKind.Monster, IsAlive: true, Rarity: MonsterRarity.Unique };

    /// <summary>
    /// An icon the game itself still shows as unfinished, minus the furniture.
    /// </summary>
    /// <remarks>
    /// Waypoints and checkpoints carry the same marker as a quest encounter and
    /// outnumber them, so without this exclusion the nearest unfinished icon to
    /// anyone standing in a zone is the waypoint they walked in past — which is
    /// how a step that said kill the boss came out as "Matar: Waypoint".
    /// </remarks>
    private static bool Objective(EntitySnapshot e) =>
        e is { IsPoi: true, IconComplete: false } && !Waypoint(e);

    private static bool Waypoint(EntitySnapshot e) =>
        e.Metadata.Contains("Waypoint", StringComparison.OrdinalIgnoreCase) ||
        e.Metadata.Contains("Checkpoint", StringComparison.OrdinalIgnoreCase);

    private static (string Id, string Label)? Landmark(
        WorldSnapshot snapshot, Func<LandmarkSnapshot, bool> fits)
    {
        foreach (var mark in snapshot.Marks)
        {
            if (fits(mark)) return (NavTarget.IdFor(mark), mark.Name);
        }

        return null;
    }

    /// <summary>
    /// The closest match to the player.
    /// </summary>
    /// <remarks>
    /// Closest rather than first, because the enumeration order is the client's
    /// allocation order and a route that jumps between two equally valid rares
    /// as they load is worse than no route.
    /// </remarks>
    private static (string Id, string Label)? Entity(
        WorldSnapshot snapshot, Func<EntitySnapshot, bool> fits)
    {
        EntitySnapshot? best = null;
        var closest = float.MaxValue;

        var from = snapshot.Player.GridPosition;

        foreach (var entity in snapshot.Entities)
        {
            if (!fits(entity) || entity.GridPosition is not { } at) continue;

            if (from is not { } player) return Found(entity);

            var dx = at.X - player.X;
            var dy = at.Y - player.Y;
            var distance = (dx * dx) + (dy * dy);

            if (distance >= closest) continue;

            closest = distance;
            best = entity;
        }

        return best is null ? null : Found(best);
    }

    private static (string Id, string Label) Found(EntitySnapshot entity) =>
        (NavTarget.IdFor(entity),
            entity.FriendlyName is { Length: > 0 } name
                ? name
                : NativeMap.EntityLabels.Pretty(entity.Metadata));
}
