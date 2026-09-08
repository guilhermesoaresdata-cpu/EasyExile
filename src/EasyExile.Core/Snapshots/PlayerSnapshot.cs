using EasyExile.Core.Spatial;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// The local character. Deliberately limited to what the foundation reads with
/// confidence today; the contract carries more, and it will be surfaced when a
/// feature actually needs it rather than because it exists.
/// </summary>
public sealed record PlayerSnapshot(
    string? Name,
    int Level,
    int Health,
    int MaxHealth,
    int Mana,
    int MaxMana,
    int EnergyShield,
    int MaxEnergyShield,
    Vector3? WorldPosition,
    Vector2? GridPosition,
    ResistanceSnapshot? Resistances = null)
{
    /// <summary>The four resistances, never null so a feature need not check.</summary>
    public ResistanceSnapshot Resists => Resistances ?? ResistanceSnapshot.Unknown;
}
