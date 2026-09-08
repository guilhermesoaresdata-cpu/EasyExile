using EasyExile.Core.Snapshots;
using EasyExile.Radar.Settings.Debug;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.Settings.NativeMap;
using EasyExile.Radar.Settings.Player;

namespace EasyExile.Radar.Settings;

/// <summary>
/// The root of the settings tree. It composes one record per category and holds
/// no options of its own, so a category can grow without this file growing.
/// </summary>
public sealed class RadarSettings
{
    public GeneralSettings General { get; set; } = new();

    /// <summary>Drawing on the game's own map, which is the main feature.</summary>
    public NativeMapSettings NativeMap { get; set; } = new();
    public PlayerSettings Player { get; set; } = new();
    public DebugEntitySettings DebugEntities { get; set; } = new();

    /// <summary>
    /// The only feature that sends anything to the game. Its own category, and
    /// deliberately not folded into the map's.
    /// </summary>
    public AutoPotion.AutoPotionSettings AutoPotion { get; set; } = new();

    /// <summary>Health bars over monsters, in the world.</summary>
    public HpBars.HpBarSettings HpBars { get; set; } = new();

    /// <summary>Prices over drops on the ground.</summary>
    public Loot.LootSettings Loot { get; set; } = new();

    public Levelling.LevellingSettings Levelling { get; set; } = new();


    public CaptureOptions ToCaptureOptions() => new(MaxEntities: Math.Max(1, General.MaxEntities));
}
