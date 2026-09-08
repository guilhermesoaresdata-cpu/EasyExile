namespace EasyExile.Radar.Settings.Loot;

/// <summary>
/// Prices over drops.
/// </summary>
/// <remarks>
/// The reference's <c>GroundItems</c> settings, kept to what actually gates
/// something: whether to draw, the floor below which a value is noise, the
/// threshold that makes one worth looking at, and which categories count.
/// Its editor-only options are not here.
/// </remarks>
public sealed record LootSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Value floors, per bucket, in Exalted. The reference's numbers.
    /// </summary>
    /// <remarks>
    /// Separate floors because the value scales are different: five Exalted is
    /// an unremarkable unique and an extraordinary scroll. One global minimum
    /// was my simplification, and it is what made the feature look broken — at
    /// one Exalted a Scroll of Wisdom (0.013 ex) and every white base are both
    /// correctly hidden, so a normal levelling floor shows nothing at all.
    /// </remarks>
    public float UniqueMinimumExalted { get; init; } = 5f;

    public float CurrencyMinimumExalted { get; init; } = 1f;

    public float OtherMinimumExalted { get; init; } = 1f;

    /// <summary>At or above this the value is drawn in the attention colour.</summary>
    public float HighlightExalted { get; init; } = 10f;

    /// <summary>
    /// Size multiplier for the value chip.
    /// </summary>
    /// <remarks>
    /// Above one because the chip competes with the game's own loot tags, which
    /// are drawn at the game's UI scale rather than ImGui's default 13px — at
    /// 1.0 the price is legible only if you already know where to look. 1.6 was
    /// too far the other way: it out-shouted the tag it belongs to.
    /// </remarks>
    public float TextScale { get; init; } = 1.2f;

    /// <summary>
    /// Draw the value on the game's own loot tag rather than over the drop.
    /// </summary>
    /// <remarks>
    /// The reference's default, and for a reason worth keeping: the game lays
    /// its tags out in a column so they never overlap, so a chip at the item's
    /// projected position lands near the item and nowhere near its name. The
    /// tag's rectangle is the game's own arithmetic — no projection, no jitter.
    ///
    /// Unidentified uniques always take the projected route regardless: the game
    /// hides their name, so there is no tag text to match.
    /// </remarks>
    public bool AnchorValuesToTags { get; init; } = true;

    /// <summary>Price the item under the cursor in inventory, stash and vendor.</summary>
    public bool ShowHoverPrice { get; init; } = true;

    /// <summary>
    /// Outline the slots worth something, like a loot filter for panels.
    /// </summary>
    /// <remarks>
    /// The hover chip answers "what is this one worth". This answers the
    /// question a full ritual window or stash tab actually poses — which of
    /// these forty do I care about — without hovering forty squares.
    /// </remarks>
    public bool HighlightSlots { get; init; } = true;

    /// <summary>Print the value inside the outlined slot as well as outlining it.</summary>
    public bool ShowSlotValues { get; init; } = true;

    /// <summary>
    /// Below this many listings, a price is shown with a "?" rather than hidden.
    /// </summary>
    /// <remarks>
    /// The reference's confidence threshold, which I had not ported. A price
    /// backed by two listings and a price backed by two hundred are printed the
    /// same way and are not the same claim. Flagged rather than hidden, because
    /// a thin market is still information — and a reported volume of zero means
    /// "no volume data", which many legitimate fungibles have, so it is never
    /// flagged.
    /// </remarks>
    public int MinQuantity { get; init; } = 2;

    /// <summary>
    /// Name a unique on its chip, not just its price.
    /// </summary>
    /// <remarks>
    /// Off by default. It was added because a bare "1 div" is unattributable —
    /// establishing which item that number described took a memory dump — but
    /// on screen the name doubles the chip's width over a panel that is already
    /// dense, and the game names the item on hover anyway. Worth having when a
    /// number looks wrong; not worth having all the time.
    /// </remarks>
    public bool NameUniques { get; init; } = false;

    /// <summary>Where a slot's value is drawn, relative to the slot.</summary>
    public ChipCorner SlotValueCorner { get; init; } = ChipCorner.BottomLeft;

    /// <summary>Where the hovered item's chip is drawn, relative to its slot.</summary>
    public ChipCorner HoverCorner { get; init; } = ChipCorner.BottomLeft;

    /// <summary>
    /// Show each rolled affix's tier on the item under the cursor.
    /// </summary>
    /// <remarks>
    /// The question a price cannot answer. poe.ninja prices what the market
    /// trades, which during a campaign is almost nothing, while a rare with a
    /// top-tier roll is worth keeping long before anyone has listed one.
    /// </remarks>
    public bool ShowModTiers { get; init; } = true;

    /// <summary>
    /// The best tier that still earns a shout. 3 means T1, T2 and T3.
    /// </summary>
    /// <remarks>
    /// The point is to catch the eye, and something that marks every line marks
    /// nothing. Everything past this is drawn quietly or not at all.
    /// </remarks>
    public int ModTierAlert { get; init; } = 3;

    /// <summary>Which corner of the slot the mark sits in.</summary>
    public ChipCorner ModTierCorner { get; init; } = ChipCorner.TopLeft;

    public int ModTier1Colour { get; init; } = unchecked((int)0xFF6EE86E);

    public int ModTier2Colour { get; init; } = unchecked((int)0xFF5AC8F0);

    public int ModTier3Colour { get; init; } = unchecked((int)0xFF5AA0FF);

    public float ModTierScale { get; init; } = 1f;

    /// <summary>
    /// Put the tier at the start of the item's own line in the tooltip.
    /// </summary>
    /// <remarks>
    /// The mark in the corner of a cell says an item is worth a look; it cannot
    /// say which of its rolls earned that. Reading the tooltip is when the
    /// question becomes "which line", and the client leaves a small empty box at
    /// the start of every mod line — so the answer goes exactly where the eye
    /// already is, covering nothing.
    /// </remarks>
    public bool ModTierOnTooltip { get; init; } = true;

    // ---- skill supports ------------------------------------------------------

    /// <summary>
    /// Show the recommended supports for the skill under the cursor.
    /// </summary>
    /// <remarks>
    /// Costs more than the other hover features: the captions it needs are not
    /// collected unless this is on, and collecting them is two crossings per
    /// visible node. Which is why it is a switch and not a constant.
    /// </remarks>
    public bool ShowSkillSupports { get; init; } = true;

    /// <summary>How many to list. Five is what a gem has room for.</summary>
    public int SkillSupportCount { get; init; } = 5;

    /// <summary>
    /// Where the list is drawn.
    /// </summary>
    /// <remarks>
    /// Beside the skill is the obvious place and the worst one on a left-hand
    /// panel: the client draws its own skill tooltip there, so the two land on
    /// top of each other and the overlay's half reads as noise inside the game's
    /// half. A corner is further from the eye and always legible.
    /// </remarks>
    public SkillSupportAnchor SkillSupportAnchor { get; init; } = SkillSupportAnchor.Free;

    /// <summary>Where the free position sits, as a fraction of the window.</summary>
    /// <remarks>
    /// A fraction rather than a pixel so it stays put when the window changes
    /// size, which a pixel would not.
    /// </remarks>
    public float SkillSupportX { get; init; } = 0.62f;

    public float SkillSupportY { get; init; } = 0.10f;

    /// <summary>
    /// How far right of the skill's name the panel starts, beside the skill.
    /// </summary>
    /// <remarks>
    /// The caption's right edge is the end of the NAME, not of the window it
    /// sits in - so anchoring straight to it drops the panel onto the sockets.
    /// The window's own width is not something this can see, and it changes with
    /// resolution and UI scale, so the distance is a setting with a default that
    /// clears it rather than a constant pretending to know.
    /// </remarks>
    public float SkillSupportGap { get; init; } = 0.18f;

    /// <summary>
    /// Outline every caption the sweep found, and the one the cursor picked.
    /// </summary>
    /// <remarks>
    /// A debugging aid that exists because the alternative was worse. Whether a
    /// caption's rectangle lands where its text is drawn is a question about the
    /// screen, and asking it through a terminal probe means the panel has to be
    /// open at the same instant the probe runs — which cost most of a session
    /// before anyone noticed the mouse was on the other monitor.
    ///
    /// Drawn, it takes one screenshot to answer.
    /// </remarks>
    /// <remarks>
    /// It earned its keep: three screenshots of it answered what four terminal
    /// probes could not, and the rectangles turned out to be right all along
    /// while the rule using them was wrong. Off now that they are confirmed,
    /// and kept for the next time something is drawn somewhere unexpected.
    /// </remarks>
    public bool DebugSkillCaptions { get; init; }

    // ---- what this character is building -------------------------------------

    /// <summary>
    /// Which kinds of mod are worth a mark on the item that has them.
    /// </summary>
    /// <remarks>
    /// Stored as a number rather than as the flags' own names: the settings file
    /// is one key per line and a flags value writes itself as "Minion, Fire",
    /// whose comma the reader would have to learn about for no gain.
    /// </remarks>
    public int BuildTagMask { get; init; }

    /// <summary>Where the build mark sits, so it need not fight the tier mark.</summary>
    public ChipCorner BuildTagCorner { get; init; } = ChipCorner.BottomLeft;

    public int BuildTagColour { get; init; } = unchecked((int)0xFF7DFF7D);

    // ---- resistances still short of the cap ----------------------------------

    /// <summary>Mark items that fill a resistance gap this character has.</summary>
    /// <remarks>
    /// It says nothing at all once everything is capped, and nothing when the
    /// stats could not be read - a mark that is always on is not a mark, and a
    /// number that might be wrong is worse than none.
    /// </remarks>
    public bool ShowResistanceHelp { get; init; } = true;

    public ChipCorner ResistanceCorner { get; init; } = ChipCorner.BottomRight;

    public int ResistanceColour { get; init; } = unchecked((int)0xFF5AC8F0);


    // ---- colours ---------------------------------------------------------------
    //
    // Stored as packed ints because that is what the settings file can carry.
    // Three tiers rather than one, because they answer different questions: this
    // is worth a lot, this is worth something, this cannot be priced and might
    // be worth anything.

    /// <summary>
    /// Write the item's real name above the game's own ground tag.
    /// </summary>
    /// <remarks>
    /// The game names a drop by its base — "Hardwood Spear" — and the name that
    /// matters is often a different one. Drawn above the tag and in the tag's
    /// colour so it reads as replacing the generic line rather than arguing with
    /// it, and only when the two actually differ.
    /// </remarks>
    public bool RevealNames { get; init; } = true;

    /// <summary>
    /// The colour of that revealed name when the rarity is unknown.
    /// </summary>
    public int RevealColour { get; init; } = unchecked((int)0xFFC8DCE6);

    /// <summary>
    /// The colour a unique's revealed name is written in.
    /// </summary>
    /// <remarks>
    /// Its own setting because it is the one that matters. The replacement
    /// stands in for the client's own line, and the client writes a unique in
    /// orange — but "orange" is a range, and only the person looking at it over
    /// their own tileset knows which one reads.
    ///
    /// The other rarities follow the palette. They are rare on the ground and
    /// four pickers for one useful choice is clutter, not configurability.
    /// </remarks>
    public int UniqueNameColour { get; init; } = unchecked((int)0xFF28A0FF);

    /// <summary>At or above the highlight threshold.</summary>
    public int RichColour { get; init; } = unchecked((int)0xFF46E6FF);

    /// <summary>Priced, above its floor, below the highlight threshold.</summary>
    public int PricedColour { get; init; } = unchecked((int)0xFFFFC85A);

    /// <summary>A unique the price book has no row for.</summary>
    public int UnknownColour { get; init; } = unchecked((int)0xFF28A0FF);

    /// <summary>The floor that applies to a given category.</summary>
    public float MinimumFor(string? category, bool unique)
    {
        if (unique) return UniqueMinimumExalted;

        return category is not null &&
               (category.Contains("Currency", StringComparison.OrdinalIgnoreCase) ||
                category.Contains("Exchange", StringComparison.OrdinalIgnoreCase))
            ? CurrencyMinimumExalted
            : OtherMinimumExalted;
    }

    public bool ShowCurrency { get; init; } = true;
    public bool ShowUniques { get; init; } = true;
    public bool ShowGems { get; init; } = true;
    public bool ShowOther { get; init; } = true;

    /// <summary>
    /// Whether a poe.ninja category is drawn at all.
    /// </summary>
    /// <remarks>
    /// Matched loosely on the category string rather than against a fixed
    /// enumeration: poe.ninja adds types with content patches, and a rigid list
    /// would silently drop whatever it has not heard of. Anything unrecognised
    /// falls under "other", which stays on.
    /// </remarks>
    public bool ShowsCategory(string? category)
    {
        if (string.IsNullOrEmpty(category)) return ShowOther;

        if (category.Contains("Currency", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("Exchange", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("Essence", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("Rune", StringComparison.OrdinalIgnoreCase))
            return ShowCurrency;

        if (category.Contains("Unique", StringComparison.OrdinalIgnoreCase)) return ShowUniques;

        if (category.Contains("Gem", StringComparison.OrdinalIgnoreCase)) return ShowGems;

        return ShowOther;
    }
}
