using EasyExile.Radar.Features.Levelling;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Settings.Levelling;
using EasyExile.Radar.Settings.Loot;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// The steps, on the game.
/// </summary>
/// <remarks>
/// "Eu nao vou jogar com o F0 aparecendo" — nobody plays with a settings window
/// open, so a guide that only speaks inside one is a website. These hold the
/// panel to being readable, positionable, and quiet when it is switched off.
/// </remarks>
public class StepPanelTests
{
    private static readonly ScreenRect Client = new(0, 0, 1920, 1080);

    [Fact]
    public void The_zone_the_objective_and_the_steps_all_reach_the_screen()
    {
        var canvas = Draw(new LevellingSettings());

        Assert.Contains(canvas.Texts, t => t.Text.Contains("The Bone Pits"));
        Assert.Contains(canvas.Texts, t => t.Text.Contains("Mate o boss"));
        Assert.Contains(canvas.Texts, t => t.Text.Contains("Volte para a cidade"));
    }

    [Fact]
    public void The_zones_number_says_how_far_through_the_campaign_you_are()
    {
        Assert.Contains(Draw(new LevellingSettings()).Texts, t => t.Text.Contains("26/65"));
    }

    [Fact]
    public void A_hint_rides_with_the_step_it_explains()
    {
        // Only against the step being worked on: printed against every step it
        // made each line longer than the screen.
        Assert.Contains(
            Draw(new LevellingSettings(), current: true).Texts,
            t => t.Text.Contains("em linha reta com a entrada"));

        Assert.DoesNotContain(
            Draw(new LevellingSettings()).Texts,
            t => t.Text.Contains("em linha reta com a entrada"));
    }

    [Fact]
    public void Optional_steps_can_be_hidden_without_hiding_the_rest()
    {
        var without = Draw(new LevellingSettings { ShowOptional = false });

        Assert.DoesNotContain(without.Texts, t => t.Text.Contains("Shrine of Bones"));
        Assert.Contains(without.Texts, t => t.Text.Contains("Mate o boss"));
    }

    [Fact]
    public void The_step_list_can_be_capped_without_losing_the_objective()
    {
        // Zero steps is a legitimate choice: some people want the objective line
        // and nothing else on screen.
        var canvas = Draw(new LevellingSettings { VisibleSteps = 0 });

        Assert.Contains(canvas.Texts, t => t.Text.Contains("Ir para Valley of the Titans"));
        Assert.DoesNotContain(canvas.Texts, t => t.Text.Contains("Volte para a cidade"));
    }

    [Theory]
    [InlineData(ChipCorner.TopLeft)]
    [InlineData(ChipCorner.TopRight)]
    [InlineData(ChipCorner.BottomLeft)]
    [InlineData(ChipCorner.BottomRight)]
    public void Every_corner_keeps_the_panel_inside_the_client(ChipCorner corner)
    {
        var canvas = Draw(new LevellingSettings { Corner = corner });

        foreach (var (at, _) in canvas.Texts)
        {
            Assert.InRange(at.X, Client.X, Client.Right);
            Assert.InRange(at.Y, Client.Y, Client.Bottom);
        }
    }

    [Fact]
    public void The_two_right_hand_corners_are_not_the_two_left_hand_ones()
    {
        // A corner setting that quietly did nothing would look like the panel
        // being stuck, which is the complaint this whole panel answers.
        var left = Draw(new LevellingSettings { Corner = ChipCorner.TopLeft }).Texts[0].At;
        var right = Draw(new LevellingSettings { Corner = ChipCorner.TopRight }).Texts[0].At;
        var below = Draw(new LevellingSettings { Corner = ChipCorner.BottomLeft }).Texts[0].At;

        Assert.True(right.X > left.X);
        Assert.True(below.Y > left.Y);
    }

    [Fact]
    public void A_plate_is_drawn_behind_the_text_so_it_reads_over_a_bright_tileset()
    {
        Assert.True(Draw(new LevellingSettings()).Rects > 0);
    }

    [Fact]
    public void Nothing_the_overlay_font_cannot_draw_reaches_the_screen()
    {
        // The font is ImGui's default, which is Latin-1: an em dash or a middle
        // dot comes out as a literal question mark. The first screenshot of this
        // panel was full of them and read as corruption, not as a missing glyph.
        foreach (var (_, text) in Draw(new LevellingSettings(), current: true).Texts)
        {
            Assert.All(text, c => Assert.True(c < 128, $"caractere {(int)c:X4} em \"{text}\""));
        }
    }

    [Theory]
    [InlineData("a — b", "a - b")]
    [InlineData("· item", "- item")]
    [InlineData("caçador ação", "cacador acao")]
    [InlineData("plain ascii", "plain ascii")]
    public void Punctuation_is_substituted_rather_than_dropped(string input, string expected)
    {
        // The dash between a step and its hint is what separates them, so
        // deleting it would cost more than replacing it.
        Assert.Equal(expected, StepPanel.Ascii(input));
    }

    [Fact]
    public void The_step_being_done_is_not_printed_twice()
    {
        // The objective line already says it. The screenshot showed the same
        // sentence twice, once in orange and once in grey.
        // The objective the feature builds for a kill step names the thing it
        // routed to, and the step list must not then repeat the same step.
        var zone = Zone();
        var canvas = new RecordingCanvas();

        StepPanel.Draw(
            canvas, Client, new LevellingSettings(), zone, 26, 65,
            "Matar: Mastodon", routing: true, current: zone.Steps[0]);

        Assert.Equal(1, canvas.Texts.Count(t => t.Text.Contains("Matar")));
        Assert.DoesNotContain(canvas.Texts, t => t.Text.Contains("Mate o boss"));
    }

    private static RecordingCanvas Draw(LevellingSettings options, bool current = false)
    {
        var canvas = new RecordingCanvas();
        var zone = Zone();

        StepPanel.Draw(
            canvas, Client, options, zone, 26, 65, "Ir para Valley of the Titans", routing: true,
            current: current ? zone.Steps[0] : null);

        return canvas;
    }

    private static CampaignZone Zone()
    {
        var zone = new CampaignZone("G2_5_2", "The Bone Pits", "Act Two", Optional: false);

        zone.Steps.Add(new CampaignStep(
            StepAction.Kill, "o boss", "em linha reta com a entrada", Optional: false, Target: null));

        zone.Steps.Add(new CampaignStep(
            StepAction.Take, "Shrine of Bones", null, Optional: true, Target: null));

        zone.Steps.Add(new CampaignStep(
            StepAction.Waypoint, "", null, Optional: false, Target: null));

        return zone;
    }
}
