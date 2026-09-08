using System.Reflection;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Debug;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.Settings.Player;

namespace EasyExile.Core.Tests;

/// <summary>
/// Where the overlay goes, and when it refuses to be seen.
/// </summary>
/// <remarks>
/// The placement rules are a pure function of the window state, which is why
/// they can be tested at all: no display, no game, no Win32. Only the part that
/// asks Windows for the numbers is untestable here, and it is deliberately the
/// thinnest part.
/// </remarks>
public class OverlayPlacementTests
{
    private static readonly ScreenRect Client = new(120, 80, 1920, 1080);

    private static GameWindowState Tracking(bool foreground = true, int dpi = 96) =>
        new(GameWindowStatus.Tracking, Client, foreground, dpi);

    [Fact]
    public void The_overlay_matches_the_client_area_of_the_game()
    {
        var placement = OverlayPlacementPolicy.Decide(Tracking(), showWhenUnfocused: false, interactive: false);

        Assert.True(placement.Visible);
        Assert.Equal(Client, placement.Bounds);
    }

    [Fact]
    public void A_minimised_client_hides_the_overlay()
    {
        var state = new GameWindowState(GameWindowStatus.Minimised, ScreenRect.Empty, false, 96);

        Assert.False(OverlayPlacementPolicy.Decide(state, false, false).Visible);
    }

    [Fact]
    public void A_closed_client_hides_the_overlay()
    {
        Assert.False(OverlayPlacementPolicy.Decide(GameWindowState.Gone, true, true).Visible);
    }

    [Fact]
    public void A_hidden_window_hides_the_overlay()
    {
        var state = new GameWindowState(GameWindowStatus.Hidden, ScreenRect.Empty, false, 96);

        Assert.False(OverlayPlacementPolicy.Decide(state, true, true).Visible);
    }

    [Fact]
    public void An_empty_client_area_hides_the_overlay()
    {
        var state = new GameWindowState(GameWindowStatus.Tracking, ScreenRect.Empty, true, 96);

        Assert.False(OverlayPlacementPolicy.Decide(state, true, true).Visible);
    }

    [Fact]
    public void By_default_the_overlay_hides_when_the_game_loses_focus()
    {
        // An overlay floating over a browser reads as a bug, not a feature.
        Assert.False(OverlayPlacementPolicy.Decide(Tracking(foreground: false), false, false).Visible);
    }

    [Fact]
    public void The_unfocused_setting_keeps_it_visible()
    {
        Assert.True(OverlayPlacementPolicy.Decide(Tracking(foreground: false), true, false).Visible);
    }

    [Fact]
    public void Interactive_mode_stays_visible_even_though_the_game_lost_focus()
    {
        // Clicking the settings panel is what took focus from the game. Hiding on
        // that click would make the panel impossible to use.
        Assert.True(OverlayPlacementPolicy.Decide(Tracking(foreground: false), false, interactive: true).Visible);
    }

    [Fact]
    public void Every_refusal_says_why()
    {
        var refusals = new[]
        {
            OverlayPlacementPolicy.Decide(GameWindowState.Gone, false, false),
            OverlayPlacementPolicy.Decide(new GameWindowState(GameWindowStatus.Minimised, ScreenRect.Empty, false, 96), false, false),
            OverlayPlacementPolicy.Decide(Tracking(foreground: false), false, false),
        };

        Assert.All(refusals, r =>
        {
            Assert.False(r.Visible);
            Assert.NotEmpty(r.Reason);
        });
    }

    [Fact]
    public void Dpi_scale_is_derived_from_the_reported_dpi()
    {
        Assert.Equal(1f, Tracking(dpi: 96).DpiScale);
        Assert.Equal(1.5f, Tracking(dpi: 144).DpiScale);
        Assert.Equal(2f, Tracking(dpi: 192).DpiScale);
    }

    // ---- settings stay in their own categories -------------------------------

    [Fact]
    public void The_settings_root_holds_categories_and_no_options_of_its_own()
    {
        var scalars = typeof(RadarSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType.IsPrimitive || p.PropertyType == typeof(string))
            .ToArray();

        // The moment an option lands here instead of in a category, the root
        // starts growing towards the three-hundred-property settings object this
        // structure exists to avoid.
        Assert.Empty(scalars);

        var categories = typeof(RadarSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType)
            .ToArray();

        Assert.Contains(typeof(GeneralSettings), categories);
        Assert.Contains(typeof(PlayerSettings), categories);
        Assert.Contains(typeof(DebugEntitySettings), categories);
    }

    [Fact]
    public void A_feature_reads_only_its_own_settings_category()
    {
        // Categories share names like Enabled and ShowMarker on purpose — each
        // owns its own copy, which is what having categories means. What must not
        // happen is one feature reaching into another's settings, and that shows
        // up as a type reference rather than as a name collision.
        AssertReadsOnly(Path.Combine("Features", "World", "PlayerWorldFeature.cs"), "DebugEntitySettings");
        AssertReadsOnly(Path.Combine("Features", "World", "DebugWorldFeature.cs"), "PlayerSettings");

        static void AssertReadsOnly(string relativePath, string foreignCategory)
        {
            var file = ContractIsolationTests
                .SourceFiles(Path.Combine("src", "EasyExile.Radar"))
                .Single(f => f.EndsWith(relativePath, StringComparison.OrdinalIgnoreCase));

            Assert.DoesNotContain(foreignCategory, File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_category_reaches_into_another()
    {
        var categories = new[] { typeof(GeneralSettings), typeof(PlayerSettings), typeof(DebugEntitySettings) };

        foreach (var category in categories)
        {
            var others = categories.Where(c => c != category).ToArray();

            foreach (var property in category.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.DoesNotContain(others, other => other == property.PropertyType);
        }
    }

    [Fact]
    public void The_capture_ceiling_comes_from_the_general_category()
    {
        var settings = new RadarSettings
        {
            General = new GeneralSettings { MaxEntities = 64 },
        };

        Assert.Equal(64, settings.ToCaptureOptions().MaxEntities);

        // A nonsensical rate slows the loop down rather than pinning a core.
        var absurd = new GeneralSettings { UpdateRateHz = 0 };
        Assert.True(absurd.UpdateInterval > TimeSpan.Zero);
        Assert.True(absurd.UpdateInterval <= TimeSpan.FromSeconds(1));
    }
}
