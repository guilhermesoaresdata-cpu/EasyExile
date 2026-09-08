using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Loot;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// The supports that go in the skill under the cursor.
/// </summary>
/// <remarks>
/// The skill panel hangs no entity off its entries, unlike a grid cell, so
/// nothing structural says which caption on screen names a skill — that was
/// measured on the live client, not assumed. The lookup settles it instead: a
/// caption whose text is a key in the table is a skill.
///
/// Which makes the table's own contents part of the feature's behaviour, so
/// these run against the file that actually ships.
/// </remarks>
public class SkillSupportTests
{
    private static readonly SupportAdvice Shipped =
        SupportAdvice.Load(Path.Combine(AppContext.BaseDirectory, "supports.json"));

    [Fact]
    public void The_table_that_ships_covers_the_skill_set()
    {
        Assert.True(Shipped.IsLoaded, "supports.json nao foi copiado para a saida");
        Assert.True(Shipped.Skills >= 150, $"apenas {Shipped.Skills} skills");
    }

    [Fact]
    public void The_first_entries_are_the_ones_ranked_highest()
    {
        // poe2db puts Pierce I, Prolonged Duration I and Zenith I at tier one
        // for Spark, and the scrape keeps tier order, so "the first five" really
        // is "the five most recommended".
        var spark = Shipped.Find("Spark");

        Assert.Equal("Pierce I", spark[0]);
        Assert.True(spark.Length >= 5, $"apenas {spark.Length} suportes");
    }

    [Fact]
    public void Hovering_a_skill_lists_its_supports()
    {
        var canvas = Draw("Spark");

        Assert.Contains(canvas.ColouredTexts, t => t.Text == "Spark");
        Assert.Contains(canvas.ColouredTexts, t => t.Text.Contains("Pierce I"));
    }

    [Fact]
    public void It_lists_as_many_as_asked_for_and_no_more()
    {
        var five = Numbered(Draw("Spark", new LootSettings()));
        var three = Numbered(Draw("Spark", new LootSettings { SkillSupportCount = 3 }));

        Assert.Equal(5, five);
        Assert.Equal(3, three);
    }

    [Fact]
    public void A_skill_granted_through_a_mercenary_finds_the_same_advice()
    {
        // The client draws "Command: Gas Arrow" for a skill given to a
        // mercenary. It is the same skill and the same supports.
        Assert.Equal(Shipped.Find("Gas Arrow"), Shipped.Find("Command: Gas Arrow"));
        Assert.NotEmpty(Shipped.Find("Command: Gas Arrow "));
    }

    [Fact]
    public void A_caption_that_is_not_a_skill_draws_nothing()
    {
        // Which is the whole reason the lookup decides rather than the geometry:
        // the panel is full of captions and only some of them are skills.
        Assert.Empty(Draw("Resistances").Texts);
        Assert.Empty(Draw("Guild Stash").Texts);
    }

    [Fact]
    public void The_innermost_caption_under_the_cursor_wins()
    {
        // Captions nest: a skill's name sits inside a row inside a panel, so
        // several rectangles hold the cursor at once and only the smallest is
        // the thing being pointed at.
        var ui = new UiSnapshot(
            ImmutableArray<LootLabelSnapshot>.Empty,
            ImmutableArray<ItemSlotSnapshot>.Empty,
            RadarFixture.Camera(),
            null,
            [
                new TextLabelSnapshot("Skills", 0f, 0f, 900f, 900f),
                new TextLabelSnapshot("Spark", 90f, 90f, 200f, 40f),
            ]);

        Assert.Equal("Spark", ui.CaptionAt(100f, 100f)!.Text);
    }

    [Fact]
    public void Pointing_at_the_icon_counts_as_pointing_at_the_skill()
    {
        // A skill's row is an icon and then its name. Only the name is a
        // caption, and the icon is what a person actually points at — a strict
        // test answers nothing for the whole left half of the row.
        var ui = Ui(new TextLabelSnapshot("Spark", 200f, 100f, 260f, 60f));

        Assert.Equal("Spark", ui.CaptionAt(150f, 130f)?.Text);
        Assert.Equal("Spark", ui.CaptionAt(300f, 130f)?.Text);
    }

    [Fact]
    public void The_clients_own_tooltip_does_not_steal_the_choice()
    {
        // Straight from a screenshot. Hovering a skill makes the game open its
        // tooltip over the row, and the tooltip's captions - "Minion Info",
        // "Command: Gas Arrow" - are SMALLER than a skill row, so they won the
        // hit test and then failed the lookup. The panel went quiet at the exact
        // moment someone was reading the skill.
        //
        // The lookup is part of the choice now, so a caption that says nothing
        // about supports cannot win.
        var ui = Ui(
            new TextLabelSnapshot("Spark", 200f, 100f, 260f, 60f),
            new TextLabelSnapshot("Minion Info", 210f, 110f, 90f, 24f));

        Assert.Equal("Minion Info", ui.CaptionAt(250f, 120f)?.Text);
        Assert.Equal("Spark", ui.CaptionAt(250f, 120f, c => Shipped.Find(c.Text).Length > 0)?.Text);
    }

    [Fact]
    public void The_region_covers_the_sockets_under_the_name()
    {
        // A skill is a block, not a line: the name, sometimes a "Command:" line,
        // and then the row of round sockets holding the gem and its supports.
        // The gem is what a person points at and it is BELOW the name, which is
        // why reaching only sideways answered nothing for the one spot that
        // matters.
        var ui = Ui(new TextLabelSnapshot("Spark", 200f, 100f, 260f, 60f));

        Assert.Equal("Spark", ui.CaptionAt(150f, 220f)?.Text);
    }

    [Fact]
    public void The_region_does_not_run_away()
    {
        // Above it, and far to the left of it, are not it.
        var ui = Ui(new TextLabelSnapshot("Spark", 200f, 100f, 260f, 60f));

        Assert.Null(ui.CaptionAt(150f, 40f));
        Assert.Null(ui.CaptionAt(20f, 130f));
        Assert.Null(ui.CaptionAt(150f, 400f));
    }

    [Fact]
    public void The_nearest_block_above_the_cursor_is_the_one_meant()
    {
        // Blocks reach down over their own sockets, so the one above reaches
        // into the one below. The caption the cursor is really under is the
        // lowest of the ones claiming it.
        var ui = Ui(
            new TextLabelSnapshot("Spark", 200f, 100f, 260f, 60f),
            new TextLabelSnapshot("Contagion", 200f, 260f, 260f, 60f));

        Assert.Equal("Spark", ui.CaptionAt(250f, 200f)?.Text);
        Assert.Equal("Contagion", ui.CaptionAt(250f, 300f)?.Text);
    }

    [Fact]
    public void The_corner_it_is_pinned_to_is_where_it_lands()
    {
        // Beside the skill is where the client puts its own skill tooltip, so
        // the two shared a rectangle and the overlay's half read as noise
        // inside the game's half.
        var right = Draw("Spark", new LootSettings
        {
            SkillSupportAnchor = SkillSupportAnchor.TopRight,
        });

        var beside = Draw("Spark", new LootSettings
        {
            SkillSupportAnchor = SkillSupportAnchor.BesideSkill,
        });

        Assert.All(right.ColouredTexts, t => Assert.True(t.At.X > 900f, $"{t.Text} em {t.At.X}"));
        Assert.All(beside.ColouredTexts, t => Assert.True(t.At.X < 900f, $"{t.Text} em {t.At.X}"));
    }

    [Fact]
    public void Switching_it_off_draws_nothing()
    {
        Assert.Empty(Draw("Spark", new LootSettings { ShowSkillSupports = false }).Texts);
    }

    [Fact]
    public void Without_a_table_it_stays_quiet_rather_than_guessing()
    {
        var canvas = new RecordingCanvas();

        new SkillSupportFeature(new RadarSettings { Loot = new LootSettings() },
                SupportAdvice.Empty, OnTheCaption)
            .Draw(Frame("Spark"), canvas);

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void A_missing_file_is_an_empty_table_and_not_a_crash()
    {
        var missing = SupportAdvice.Load(
            Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.json"));

        Assert.False(missing.IsLoaded);
        Assert.Empty(missing.Find("Spark"));
        Assert.Empty(missing.Find(null));
    }

    [Fact]
    public void Rubbish_in_the_file_is_an_empty_table_and_not_a_crash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lixo-{Guid.NewGuid():N}.json");

        File.WriteAllText(path, "{ nao e json");

        try
        {
            Assert.False(SupportAdvice.Load(path).IsLoaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Nothing_accented_ever_reaches_the_screen()
    {
        // The default font is Latin-1. A support named with anything outside it
        // would render as question marks, and the heading is Portuguese.
        Assert.All(Draw("Spark").ColouredTexts,
            t => Assert.All(t.Text, c => Assert.True(c < 128, t.Text)));
    }

    // ---- fixtures ---------------------------------------------------------

    private static Vector2 OnTheCaption() => new(100f, 100f);

    private static UiSnapshot Ui(params TextLabelSnapshot[] captions) =>
        new(ImmutableArray<LootLabelSnapshot>.Empty,
            ImmutableArray<ItemSlotSnapshot>.Empty,
            RadarFixture.Camera(),
            null,
            captions.ToImmutableArray());

    private static int Numbered(RecordingCanvas canvas) =>
        canvas.ColouredTexts.Count(t => t.Text.Length > 2 && char.IsDigit(t.Text[0]) && t.Text[1] == '.');

    private static RecordingCanvas Draw(string caption) => Draw(caption, new LootSettings());

    private static RecordingCanvas Draw(string caption, LootSettings options)
    {
        var canvas = new RecordingCanvas();

        new SkillSupportFeature(new RadarSettings { Loot = options }, Shipped, OnTheCaption)
            .Draw(Frame(caption), canvas);

        return canvas;
    }

    private static RenderFrame Frame(string caption)
    {
        var world = RadarFixture.World() with { Camera = RadarFixture.Camera() };

        return RadarFixture.Frame(world) with
        {
            Ui = new UiSnapshot(
                ImmutableArray<LootLabelSnapshot>.Empty,
                ImmutableArray<ItemSlotSnapshot>.Empty,
                RadarFixture.Camera(),
                null,
                [new TextLabelSnapshot(caption, 60f, 80f, 200f, 60f)]),
        };
    }
}
