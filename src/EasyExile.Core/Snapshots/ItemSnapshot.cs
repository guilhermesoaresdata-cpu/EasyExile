namespace EasyExile.Core.Snapshots;

/// <summary>
/// What a drop on the ground is, as far as pricing cares.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>Poe2Live.ReadIdentityFromItem</c>. Two keys,
/// because one is not enough: the art basename prices uniques, and the rendered
/// base name prices everything else, since currency tiers all draw a single icon
/// and only the name separates an Orb from a Greater Orb.
/// </remarks>
/// <param name="Art">The 2D art's basename, which is the price key for uniques.</param>
/// <param name="BaseName">The rendered base-type name, the key for everything else.</param>
/// <param name="Identified">
/// False only for an unidentified unique. The game hides its name, which is
/// exactly when knowing it is worth something.
/// </param>
public sealed record ItemSnapshot(
    string? Art, string? BaseName, MonsterRarity Rarity, bool Identified,

    /// <summary>
    /// The rolled affixes, as the internal ids the game names them by.
    /// </summary>
    /// <remarks>
    /// Kept as ids rather than as text because the id IS the answer: the game
    /// calls them Strength1..Strength8, so the trailing number is the tier.
    /// Turning that into "T1" needs only a table of how many tiers each family
    /// has, which is a renderer's concern and not this one's.
    /// </remarks>
    System.Collections.Immutable.ImmutableArray<string> Mods = default)
{
    /// <summary>Never default, so a caller need not check.</summary>
    public System.Collections.Immutable.ImmutableArray<string> Affixes =>
        Mods.IsDefault ? System.Collections.Immutable.ImmutableArray<string>.Empty : Mods;

    public bool IsUnique => Rarity == MonsterRarity.Unique;

    /// <summary>A unique whose name the game is hiding.</summary>
    public bool IsUnidentifiedUnique => IsUnique && !Identified;

    public bool HasIdentity => !string.IsNullOrEmpty(Art) || !string.IsNullOrEmpty(BaseName);
}
