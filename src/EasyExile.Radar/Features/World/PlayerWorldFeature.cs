using EasyExile.Core.Spatial;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Player;

namespace EasyExile.Radar.Features.World;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// World HUD: draws the local character where the client renders it, with its
/// name, level and vitals.
/// </summary>
/// <remarks>
/// This is a world-space overlay and not the radar. It uses the camera, so it
/// answers "where is this on screen"; the map radar answers "where is this in
/// the area", which is a different question with a different projection.
///
/// The marker is the strongest alignment check available. The character is the
/// one entity whose true screen position is unambiguous to a human — if the
/// marker sits on it while walking and while the camera moves, the client rect,
/// the DPI handling and the projection are all correct at once.
/// </remarks>
public sealed class PlayerWorldFeature : IRadarFeature
{
    private readonly RadarSettings _settings;

    public PlayerWorldFeature(RadarSettings settings) => _settings = settings;

    public string Name => T("Player (HUD)");

    private PlayerSettings Options => _settings.Player;

    public bool Enabled => Options.Enabled;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled || !frame.HasWorld) return;

        var options = Options;
        var player = frame.Snapshot!.Player;

        var lines = Describe(player, options);

        if (options.Anchor == PlayerAnchor.FixedHud)
        {
            DrawFixed(frame, canvas, lines, options.MarkerColour);
            return;
        }

        if (player.WorldPosition is not { } world) return;
        if (frame.Project(world) is not { } point) return;

        // Off-screen and behind-camera are reported, never folded onto an edge.
        // A character marker pinned to the screen border while the character is
        // somewhere else is a lie that looks like a feature.
        if (point.Status != ScreenStatus.OnScreen) return;

        var at = point.Screen;

        if (options.ShowMarker)
        {
            canvas.Circle(at, options.MarkerRadius + 1.5f, Palette.Shadow);
            canvas.Circle(at, options.MarkerRadius, unchecked((uint)options.MarkerColour));
        }

        var y = at.Y + options.MarkerRadius + 4f;

        foreach (var line in lines)
        {
            canvas.TextCentred(new Vector2(at.X, y), unchecked((uint)options.MarkerColour), line);
            y += 14f;
        }
    }

    private static void DrawFixed(
        RenderFrame frame, IOverlayCanvas canvas, IReadOnlyList<string> lines, int colour)
    {
        var y = frame.ClientBounds.Height - 24f - (lines.Count * 14f);

        foreach (var line in lines)
        {
            canvas.Text(new Vector2(18f, y), unchecked((uint)colour), line);
            y += 14f;
        }
    }

    private static List<string> Describe(Core.Snapshots.PlayerSnapshot player, PlayerSettings options)
    {
        var lines = new List<string>(3);

        if (options.ShowName || options.ShowLevel)
        {
            var name = options.ShowName ? player.Name ?? "-" : string.Empty;
            var level = options.ShowLevel ? $"lvl {player.Level}" : string.Empty;

            var heading = string.Join("  ", new[] { name, level }.Where(s => s.Length > 0));
            if (heading.Length > 0) lines.Add(heading);
        }

        if (options.ShowVitals)
        {
            lines.Add($"{player.Health}/{player.MaxHealth}  {player.Mana}/{player.MaxMana}");

            // Energy shield is only interesting when the character has any.
            if (player.MaxEnergyShield > 0)
                lines.Add($"es {player.EnergyShield}/{player.MaxEnergyShield}");
        }

        return lines;
    }
}
