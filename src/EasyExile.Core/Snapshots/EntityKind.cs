namespace EasyExile.Core.Snapshots;

/// <summary>
/// What an entity is, as far as a map overlay needs to care.
/// </summary>
/// <remarks>
/// Ported from POE2Radar's <c>Poe2Live.EntityCategory</c> and <c>Categorize</c>
/// (MIT). The ordering of the checks is load-bearing and comes from there:
/// friendly NPCs live under <c>Metadata/Monsters/NPC/…</c>, so NPC has to be
/// tested before monster or vendors would be drawn as things to kill.
/// </remarks>
public enum EntityKind
{
    /// <summary>Not worth a marker: effects, attachments, daemons, props.</summary>
    Junk,

    /// <summary>Recognised, but nothing the map has a use for yet.</summary>
    Other,

    Player,
    OtherPlayer,
    Monster,

    /// <summary>Your own: minions, totems, anything you summoned.</summary>
    Ally,
    Npc,
    Chest,
    Transition,

    /// <summary>
    /// Static world scenery: everything under <c>/Terrain/</c>.
    /// </summary>
    /// <remarks>
    /// Its own category and not junk, because this is where the client files
    /// most of what it marks on the map — portals, seals, waypoint devices. The
    /// ones that matter are picked out by
    /// <see cref="EntitySnapshot.IsPoi"/>, which is the client's own answer;
    /// the rest are simply never drawn.
    ///
    /// Matches POE2Radar's <c>EntityCategory.Object</c>.
    /// </remarks>
    Object,
}
