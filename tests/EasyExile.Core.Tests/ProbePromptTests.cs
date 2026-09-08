using EasyExile.Core.Diagnostics;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.General;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Asking for something where the player will actually see it.
/// </summary>
/// <remarks>
/// This exists because of a session that lost probe after probe: each one asked
/// for the mouse to rest on an item, each request went to a terminal behind the
/// game, and every window closed on nothing. The runs were not failing — they
/// were never being answered, and an unanswered run reads exactly like a
/// negative result.
/// </remarks>
public class ProbePromptTests
{
    [Fact]
    public void The_request_and_the_seconds_left_are_both_drawn()
    {
        var canvas = Draw("passe o mouse sobre um item", TimeSpan.FromSeconds(24));

        Assert.Contains(canvas.ColouredTexts, t => t.Text.Contains("passe o mouse"));

        // The deadline is the part that makes it worth walking back for.
        Assert.Contains(canvas.ColouredTexts, t => t.Text == "24s");
    }

    [Fact]
    public void The_last_seconds_change_colour()
    {
        var late = Assert.Single(Draw("x", TimeSpan.FromSeconds(3)).ColouredTexts, t => t.Text == "3s");
        var early = Assert.Single(Draw("x", TimeSpan.FromSeconds(20)).ColouredTexts, t => t.Text == "20s");

        Assert.NotEqual(early.Colour, late.Colour);
    }

    [Fact]
    public void Nothing_is_drawn_when_nothing_is_being_asked()
    {
        var canvas = new RecordingCanvas();

        new ProbePromptFeature(new RadarSettings(), () => null).Draw(RadarFixture.Frame(RadarFixture.World()), canvas);

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void Switching_it_off_draws_nothing()
    {
        var canvas = Draw(
            "passe o mouse", TimeSpan.FromSeconds(10),
            new RadarSettings { General = new GeneralSettings { ShowProbePrompts = false } });

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void An_accented_request_never_reaches_the_screen_as_question_marks()
    {
        // The default font is Latin-1 and the probes write Portuguese. An
        // instruction rendered as gibberish is an instruction nobody follows,
        // which is the whole failure this feature exists to fix.
        var canvas = Draw("ANALISE: nao mexa o mouse, aguarde a contagem", TimeSpan.FromSeconds(9));

        Assert.All(canvas.ColouredTexts, t => Assert.All(t.Text, c => Assert.True(c < 128, t.Text)));
    }

    // ---- the file the two processes share ---------------------------------

    [Fact]
    public void What_one_process_writes_the_other_reads()
    {
        try
        {
            ProbePrompt.Show("segure o mouse no item", TimeSpan.FromSeconds(30));

            var prompt = ProbePrompt.Current();

            Assert.NotNull(prompt);
            Assert.Equal("segure o mouse no item", prompt!.Value.Message);
            Assert.InRange(prompt.Value.Left.TotalSeconds, 25d, 30d);
        }
        finally
        {
            ProbePrompt.Clear();
        }
    }

    [Fact]
    public void A_prompt_nobody_is_waiting_on_stops_being_shown()
    {
        // The file can outlive its probe — a crash, a kill, a machine that
        // slept. A stale instruction telling the player to hold still for an
        // analysis that ended is worse than none.
        try
        {
            ProbePrompt.Show("ja passou", TimeSpan.FromSeconds(-1));

            Assert.Null(ProbePrompt.Current());
        }
        finally
        {
            ProbePrompt.Clear();
        }
    }

    [Fact]
    public void Clearing_takes_it_off_the_screen()
    {
        ProbePrompt.Show("temporario", TimeSpan.FromSeconds(30));
        ProbePrompt.Clear();

        Assert.Null(ProbePrompt.Current());
    }

    [Fact]
    public void No_prompt_file_at_all_is_quiet_rather_than_a_crash()
    {
        ProbePrompt.Clear();

        Assert.Null(ProbePrompt.Current());
    }

    // ---- fixtures ---------------------------------------------------------

    private static RecordingCanvas Draw(string message, TimeSpan left) =>
        Draw(message, left, new RadarSettings());

    private static RecordingCanvas Draw(string message, TimeSpan left, RadarSettings settings)
    {
        var canvas = new RecordingCanvas();

        new ProbePromptFeature(settings, () => (message, left)).Draw(RadarFixture.Frame(RadarFixture.World()), canvas);

        return canvas;
    }
}
