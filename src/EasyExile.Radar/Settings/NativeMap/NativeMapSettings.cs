using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Settings.NativeMap;

/// <summary>
/// The native map overlay. It draws on the game's own map, so there is nothing
/// here about position or size — those belong to the game.
/// </summary>
public sealed record NativeMapSettings
{
    public bool Enabled { get; init; } = true;

    public bool ShowTerrain { get; init; } = true;
    public bool ShowPlayer { get; init; } = true;
    public bool ShowEntities { get; init; } = true;

    public bool ShowMonsters { get; init; } = true;

    public bool ShowNormalMonsters { get; init; } = true;
    public bool ShowMagicMonsters { get; init; } = true;
    public bool ShowRareMonsters { get; init; } = true;
    public bool ShowUniqueMonsters { get; init; } = true;

    /// <summary>
    /// Interpolates enemy movement between world captures. The map, the player
    /// and the zoom are never interpolated — only a monster's own motion.
    /// </summary>
    public bool SmoothMovement { get; init; } = true;
    public bool ShowNpcs { get; init; } = true;

    /// <summary>The player's own minions and totems.</summary>
    public bool ShowAllies { get; init; } = true;
    public bool ShowChests { get; init; } = true;

    // ---- terrain ---------------------------------------------------------------

    /// <summary>
    /// Colour the ground you have not walked differently from the ground you
    /// have.
    /// </summary>
    /// <remarks>
    /// It is OUR record, not the client's fog of war — we have no offset for
    /// that — so it says "you have not been here", which is a weaker claim than
    /// "this is unexplored" and the one we can actually stand behind.
    /// </remarks>
    public bool ShowUnvisited { get; init; } = true;

    /// <summary>Ground the player has been near.</summary>
    public int VisitedColour { get; init; } = unchecked((int)0xFFD2AA78);

    /// <summary>
    /// Ground the player has not reached yet.
    /// </summary>
    public int UnvisitedColour { get; init; } = unchecked((int)0xFF5A5AFF);

    /// <summary>
    /// How strong the unexplored wash is, from invisible to solid.
    /// </summary>
    /// <remarks>
    /// Its own slider rather than the colour's alpha channel. Hiding the one
    /// control that actually needs adjusting inside a colour picker is hiding
    /// it: at a close zoom the terrain fills the screen, so this is a tint over
    /// everything being looked at and the right value is a matter of taste and
    /// of the tileset underneath.
    ///
    /// The default is low because the moment it is most visible is on entering
    /// an area, when NOTHING has been explored and it covers the whole map.
    /// </remarks>
    public float UnvisitedStrength { get; init; } = 0.20f;

    /// <summary>
    /// How far around the player counts as seen, in grid cells.
    /// </summary>
    /// <remarks>
    /// Not a physical constant — it is a choice about how generous the record
    /// should be. Deliberately larger than a screen: the client reveals what you
    /// can SEE, not what you stand on, and we cannot read its mask — all four
    /// terrain layers were measured and none of them is it. Ninety cells is the
    /// closest this approximation gets to the real reveal without pretending to
    /// be it.
    /// </remarks>
    public int VisitRadius { get; init; } = 90;

    /// <summary>
    /// Only chests the client itself marks on its minimap.
    /// </summary>
    /// <remarks>
    /// The client files destructible scenery under /Chests as well, with
    /// regional names — "EzomyteChest_01" is a pot, not a chest — so no keyword
    /// list separates them and a town fills with chest markers. The game's own
    /// minimap does separate them: it marks what is worth opening.
    ///
    /// A switch rather than a silent rule, because "the client marks it" is a
    /// judgement borrowed from the client and it will occasionally be stricter
    /// than someone wants.
    /// </remarks>
    public bool OnlyMarkedChests { get; init; } = true;
    public bool ShowTransitions { get; init; } = true;

    /// <summary>
    /// Everything the client itself marks on its map: waypoints, checkpoints,
    /// portals, league devices. The game already decided these matter, which is
    /// why they are on by default.
    /// </summary>
    public bool ShowPois { get; init; } = true;
    public bool ShowOtherPlayers { get; init; } = true;

    /// <summary>
    /// Named terrain features — boss arenas, transitions, mechanic rooms. They
    /// come from the area's tile layout, so they are on the map before you have
    /// been anywhere near them.
    /// </summary>
    public bool ShowLandmarks { get; init; } = true;

    /// <summary>
    /// League mechanics - expedition, ritual, breach, strongboxes, essences,
    /// shrines - each with its own icon instead of a generic dot.
    /// </summary>
    public bool ShowMechanics { get; init; } = true;

    /// <summary>
    /// Marks monsters carrying dangerous affixes, on top of their rank colour.
    /// </summary>
    public bool ShowThreats { get; init; } = true;

    /// <summary>
    /// Routes to the league mechanic automatically on arrival.
    /// </summary>
    /// <remarks>
    /// The default destination, because it is the one thing you are in the area
    /// FOR. In Runes of Aldur that is the runeshape monolith — the persistent
    /// <c>Expedition2Encounter</c> entity, which is also one of the handful the
    /// client puts its own map icon on.
    ///
    /// This is the reference's per-rule "Auto-path" flag. It seeds a selection
    /// you can still change; it is not automation, and nothing is ever sent to
    /// the game.
    /// </remarks>
    public bool AutoRouteLeagueMechanic { get; init; } = true;

    /// <summary>Routes to the area's exits on arrival. Off: the mechanic comes first.</summary>
    public bool AutoRouteExits { get; init; }

    /// <summary>Writes each exit's destination beside its marker.</summary>
    public bool ShowTransitionNames { get; init; } = true;

    /// <summary>
    /// The route on the ground while the game's map is closed. Never drawn at
    /// the same time as the map's own route: one decision per frame, as in the
    /// reference.
    /// </summary>
    public bool ShowWorldRoute { get; init; } = true;

    /// <summary>The destination's name at the end of the world route.</summary>
    public bool ShowDestinationName { get; init; } = true;

    /// <summary>Guidance routes to the selected destinations.</summary>
    public bool ShowRoutes { get; init; } = true;

    /// <summary>Corpses. Off: a cleared pack should stop cluttering the map.</summary>
    public bool ShowDeadMonsters { get; init; }

    /// <summary>
    /// Every remaining entity as a plain dot. Off by default and meant to stay
    /// that way — it is several hundred markers and a debugging tool, not how
    /// the map is meant to look.
    /// </summary>
    public bool ShowRawEntities { get; init; }

    /// <summary>Multiplies the terrain texture's own per-pixel alpha.</summary>
    public float TerrainOpacity { get; init; } = 1f;

    // ---- colours -----------------------------------------------------------
    //
    // Packed the same way the loot marks are: RGBA in an int, so the HUD's
    // colour picker can round-trip one without a converter. The defaults are
    // exactly what the map drew before any of these existed, so a fresh
    // install looks unchanged and only a deliberate change moves anything.

    /// <summary>Ordinary monsters. The one there are three hundred of.</summary>
    public int MonsterNormalColour { get; init; } = unchecked((int)0xFF4650EB);

    public int MonsterMagicColour { get; init; } = unchecked((int)0xFFFFA56E);

    public int MonsterRareColour { get; init; } = unchecked((int)0xFF46E6FF);

    /// <summary>The one worth crossing a map for.</summary>
    public int MonsterUniqueColour { get; init; } = unchecked((int)0xFF28A0FF);

    /// <summary>Minions and anything else fighting for you.</summary>
    public int AllyColour { get; init; } = unchecked((int)0xFFAAFFAA);

    /// <summary>Your own dot on the game's map.</summary>
    public int PlayerColour { get; init; } = unchecked((int)0xFFFFC85A);

    /// <summary>Whether a rank is drawn at all. Unknown counts as normal.</summary>
    public bool ShowsRank(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => ShowMagicMonsters,
        MonsterRarity.Rare => ShowRareMonsters,
        MonsterRarity.Unique => ShowUniqueMonsters,
        _ => ShowNormalMonsters,
    };
}
