namespace EasyExile.Radar.Settings.Loot;

/// <summary>
/// Where the skill's support list is drawn.
/// </summary>
/// <remarks>
/// Beside the skill reads best when it works and worst when it does not: the
/// client puts its own skill tooltip over the same space, and two panels sharing
/// a rectangle is how the first version came out "meio escondido". The corners
/// cost a glance and never collide.
/// </remarks>
public enum SkillSupportAnchor
{
    BesideSkill,

    /// <summary>
    /// Wherever you put it.
    /// </summary>
    /// <remarks>
    /// The default, and not out of laziness. Every fixed choice here has been
    /// wrong once: beside the skill shares its space with the client's own
    /// tooltip, the right-hand corners belong to the quest tracker, the bottom
    /// to the orbs and belt. Which corner is free depends on the panel, the
    /// resolution and what the player keeps open - and only the player can see
    /// all three.
    /// </remarks>
    Free,

    /// <summary>
    /// Centred, below the top edge.
    /// </summary>
    /// <remarks>
    /// The default, because in a real layout every corner is taken: the client's
    /// skill tooltip owns the left, the quest tracker and realm lines own the
    /// right, and the orbs and belt own the bottom. The middle of the view is
    /// the only place consistently free, and the panel is opaque so a busy
    /// tileset behind it costs nothing.
    /// </remarks>
    TopCentre,

    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
}
