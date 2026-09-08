using EasyExile.Core.Snapshots;

namespace EasyExile.Core.World;

/// <summary>
/// What an entity is, from its metadata path.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT): <c>Poe2Live.Categorize</c>, <c>IsNonCombat</c>,
/// <c>IsBreakableProp</c> and <c>JunkFilter.JunkPatterns</c>.
///
/// The order of the checks is not stylistic. Friendly NPCs live under
/// <c>Metadata/Monsters/NPC/…</c>, so NPC has to be tested before monster or
/// every vendor in town is drawn as something to fight. Junk is tested first
/// because a cosmetic attachment can sit under any path at all.
/// </remarks>
public static class EntityClassifier
{
    /// <summary>
    /// Paths with no gameplay value: render-side nodes, invisible daemons,
    /// cosmetics, pets and clones. Case-insensitive substring match, and the
    /// slash anchoring is the reference's — a bare token matches too much.
    /// </summary>
    private static readonly string[] JunkPatterns =
    {
        // Visual and cosmetic asset nodes.
        "/attachments", "microtransactions", "/timelines/", "stashskins", "hairstyles", "/outfits/",

        // Engine asset and effect definitions, not world entities.
        "/fx/", "/mat/", "/ao/", "/epk/", "/graph/", "/audio/", "/environment/",

        // Invisible logic carriers with no model.
        "monstermods", "essencemoddaemons", "tormentedspirits", "/daemon/",

        // Pets and clones stay junk. "playersummoned" does NOT: those are the
        // player's own minions, which belong on the map in their own colour.
        "/pet/", "/clone/",

        // Surfaced as points of interest rather than as raw dots.
        "bossroomminimapicon", "/runemarked",

        // Skill effects. PoE files them as monsters — a Firewall, a tornado, a
        // ground slam — and a single cast puts a dozen of them on the map. They
        // are your own spells, and they are not information.
        "/anomalies/",
    };

    /// <summary>Effect and summon carriers that sit under /Monsters/ but are not fights.</summary>
    private static readonly string[] NonCombatMonsters =
    {
        "MonsterMods", "Daemon", "Mirage", "Clone",
    };

    /// <summary>The player's own summons. Not enemies, and not clutter.</summary>
    private static readonly string[] AllyTokens =
    {
        "PlayerSummoned", "/Summoned", "Totem",
    };

    /// <summary>Destructible scenery under /Chests/ that is not a loot chest.</summary>
    private static readonly string[] BreakableProps =
    {
        // "Box" is deliberately absent: it would swallow StrongBox, which is a
        // real loot chest. The reference errs the same way round — it would
        // rather draw a barrel than hide a strongbox.
        "Urn", "Vase", "Pot", "Jar", "Sack", "Barrel", "Crate",
    };

    /// <summary>
    /// The component every temporary summon carries. Your minions and your
    /// companion both expire; the things trying to kill you generally do not.
    /// </summary>
    /// <remarks>
    /// The right discriminator is <c>Positioned.Reaction</c>, which is what the
    /// reference uses. It is not in the contract yet: probed live, the player,
    /// the summons and the companion all read 1, and no confirmed hostile turned
    /// up in the sample to prove the field separates the two sides. Until that
    /// contrast exists, this component is the honest signal.
    ///
    /// Known limit: an enemy necromancer's own temporary summons carry it too,
    /// and would be drawn as allies. Erring that way keeps a pack of your own
    /// skeletons from reading as a threat, which is the mistake that actually
    /// costs you something.
    /// </remarks>
    public const string TemporarySummonComponent = "DiesAfterTime";

    public static EntityKind Classify(
        string metadata, bool isLocalPlayer = false, IReadOnlyList<string>? componentNames = null,
        bool? isFriendly = null)
    {
        if (string.IsNullOrEmpty(metadata)) return EntityKind.Junk;

        foreach (var pattern in JunkPatterns)
        {
            if (metadata.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return EntityKind.Junk;
        }

        // NPC before monster: friendly NPCs are filed under /Monsters/NPC/.
        if (metadata.Contains("/NPC/", StringComparison.Ordinal)) return EntityKind.Npc;

        // Minions before monster, for the same reason NPCs go before it: they
        // live under /Monsters/ too, and drawing them red would put a threat
        // marker on the player's own skeletons — or on the companion following
        // you around, which is the one that never leaves the blip.
        if (Matches(metadata, AllyTokens)) return EntityKind.Ally;

        if (metadata.Contains("/Monsters/", StringComparison.Ordinal))
        {
            // Reaction is the real answer and takes precedence: the client says
            // outright whose side the thing is on.
            if (isFriendly == true) return EntityKind.Ally;

            // Only when the client did not say. A temporary summon is almost
            // always yours.
            if (isFriendly is null &&
                componentNames is not null &&
                componentNames.Contains(TemporarySummonComponent))
                return EntityKind.Ally;
        }

        if (metadata.Contains("/Monsters/", StringComparison.Ordinal))
            return Matches(metadata, NonCombatMonsters) ? EntityKind.Other : EntityKind.Monster;

        if (metadata.Contains("/Characters/", StringComparison.Ordinal))
            return isLocalPlayer ? EntityKind.Player : EntityKind.OtherPlayer;

        if (metadata.Contains("/Chests", StringComparison.Ordinal))
            return Matches(metadata, BreakableProps) ? EntityKind.Other : EntityKind.Chest;

        // A blockage is the barrier ACROSS a transition, not a transition. The
        // client spawns one per blocked exit and they outnumber the real ones
        // twenty to one — in Clearfell Encampment, 27 entities matched
        // "Transition" and exactly 1 had a destination. Routing to a wall and
        // labelling it as an exit are both wrong, and both came from here.
        if (metadata.Contains("Transition", StringComparison.Ordinal))
        {
            return metadata.Contains("Blockage", StringComparison.OrdinalIgnoreCase)
                ? EntityKind.Other
                : EntityKind.Transition;
        }

        // Object, not junk. This is where the client files most of what it marks
        // on its own map — portals, seals, waypoint devices — and dropping the
        // whole branch is why none of them ever reached the overlay. They are
        // still not DRAWN unless the client flags them: see EntitySnapshot.IsPoi.
        if (metadata.Contains("/Terrain/", StringComparison.Ordinal)) return EntityKind.Object;

        return EntityKind.Other;
    }

    private static bool Matches(string metadata, string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (metadata.Contains(token, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
