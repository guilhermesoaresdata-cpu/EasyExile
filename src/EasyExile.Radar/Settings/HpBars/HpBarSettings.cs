using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Settings.HpBars;

/// <summary>
/// Health bars over monsters.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT) <c>HpBarSettings</c> and the per-rarity toggles
/// beside it. Every number here is the reference's.
///
/// There is no maximum distance, because the reference has none: its only cull
/// is the screen, and a monster you cannot see has no bar by construction.
/// Adding a radius would be inventing a limit the port does not have.
/// </remarks>
public sealed record HpBarSettings
{
    public bool Enabled { get; init; } = true;

    /// <summary>Off: a bar over every trash mob is the screen, not information.</summary>
    public bool ShowNormal { get; init; }

    public bool ShowMagic { get; init; } = true;
    public bool ShowRare { get; init; } = true;
    public bool ShowUnique { get; init; } = true;

    public float Height { get; init; } = 5f;

    /// <summary>Pixels from the monster's own screen position. Negative is up.</summary>
    public float OffsetY { get; init; } = -30f;

    public float OffsetX { get; init; }

    public float WidthNormal { get; init; } = 30f;
    public float WidthMagic { get; init; } = 38f;
    public float WidthRare { get; init; } = 50f;
    public float WidthUnique { get; init; } = 64f;

    public float BorderNormal { get; init; }
    public float BorderMagic { get; init; } = 1f;
    public float BorderRare { get; init; } = 2f;
    public float BorderUnique { get; init; } = 2f;

    /// <summary>Whether a rank gets a bar at all.</summary>
    public bool ShowsRank(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => ShowMagic,
        MonsterRarity.Rare => ShowRare,
        MonsterRarity.Unique => ShowUnique,
        _ => ShowNormal,
    };

    public float WidthFor(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => WidthMagic,
        MonsterRarity.Rare => WidthRare,
        MonsterRarity.Unique => WidthUnique,
        _ => WidthNormal,
    };

    public float BorderFor(MonsterRarity rarity) => rarity switch
    {
        MonsterRarity.Magic => BorderMagic,
        MonsterRarity.Rare => BorderRare,
        MonsterRarity.Unique => BorderUnique,
        _ => BorderNormal,
    };
}
