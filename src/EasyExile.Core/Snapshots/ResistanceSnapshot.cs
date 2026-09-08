namespace EasyExile.Core.Snapshots;

/// <summary>
/// The four resistances, as the character sheet shows them.
/// </summary>
/// <remarks>
/// Read from the client's own stat table by key. The keys were matched against a
/// sheet reading Fire 7, Cold 11, Lightning -2, Chaos 6 - and the chaos value is
/// what identified them: two other blocks in the table hold the three elemental
/// numbers and neither carries a fourth, so only one set can be the one the
/// sheet is drawn from.
/// </remarks>
public sealed record ResistanceSnapshot(int Fire, int Cold, int Lightning, int Chaos)
{
    /// <summary>The cap a levelling character is trying to reach.</summary>
    public const int Cap = 75;

    public static readonly ResistanceSnapshot Unknown = new(int.MinValue, 0, 0, 0);

    /// <summary>
    /// Whether these numbers were read at all.
    /// </summary>
    /// <remarks>
    /// Worth asking rather than assuming zero. A character with no resistance
    /// and a character whose stats could not be read look identical if the
    /// answer is a number either way, and only one of them wants every ring on
    /// screen marked.
    /// </remarks>
    public bool IsKnown => Fire != int.MinValue;

    /// <summary>How far this one is from the cap, never below zero.</summary>
    public int Missing(ResistanceKind kind) => Missing(kind, Cap);

    /// <summary>
    /// How far this one is from a target of the player's choosing.
    /// </summary>
    /// <remarks>
    /// The cap is the target almost always, and not while stacking against one
    /// map's elemental damage, nor early in the campaign where aiming at 75 on
    /// all four marks every ring and singles out none.
    /// </remarks>
    public int Missing(ResistanceKind kind, int target) => Math.Max(0, target - Of(kind));

    public int Of(ResistanceKind kind) => kind switch
    {
        ResistanceKind.Fire => Fire,
        ResistanceKind.Cold => Cold,
        ResistanceKind.Lightning => Lightning,
        _ => Chaos,
    };

    /// <summary>Whether anything at all is short of the cap.</summary>
    public bool AnyMissing => AnyBelow(Cap);

    /// <summary>Whether anything at all is short of a chosen target.</summary>
    public bool AnyBelow(int target) =>
        IsKnown && (Fire < target || Cold < target || Lightning < target || Chaos < target);
}

public enum ResistanceKind
{
    Fire,
    Cold,
    Lightning,
    Chaos,
}
