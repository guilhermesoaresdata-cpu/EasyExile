namespace EasyExile.Radar.Settings.Loot;

/// <summary>
/// How the resistance mark decides what is worth marking.
/// </summary>
/// <remarks>
/// The automatic rule is the better default and the wrong answer often enough
/// to need a way out. It marks what is short of a target and goes quiet when
/// nothing is - which is right while levelling and wrong the moment you are
/// deliberately shopping: over-capping for a map's elemental damage, building a
/// second set for a boss, or gearing a character whose stats this tool could
/// not read at all.
/// </remarks>
public enum ResistanceMode
{
    /// <summary>Mark what this character is short of, and nothing once capped.</summary>
    Missing,

    /// <summary>Mark the elements the player picked, whatever the character has.</summary>
    Chosen,
}

/// <summary>
/// Which resistances to mark, when the player is choosing rather than the tool.
/// </summary>
/// <remarks>
/// Flags rather than one element: an item that rolls two of the ones you want
/// is the item worth stopping for, and a single-choice setting could not say so.
/// </remarks>
[Flags]
public enum ResistanceWatch
{
    None = 0,
    Fire = 1,
    Cold = 1 << 1,
    Lightning = 1 << 2,
    Chaos = 1 << 3,

    /// <summary>The three the game groups together and most gear rolls.</summary>
    Elemental = Fire | Cold | Lightning,

    All = Elemental | Chaos,
}
