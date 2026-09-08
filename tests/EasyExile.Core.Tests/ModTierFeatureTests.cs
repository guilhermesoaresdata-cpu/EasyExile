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
/// The mark on a slot holding a top-tier roll.
/// </summary>
/// <remarks>
/// This started as a panel beside the hovered item listing every affix by its
/// internal name, and the screenshots showed three faults at once: it landed on
/// the client's own tooltip, it printed developer strings that appear nowhere in
/// the game, and it spoke about rolls nobody cares about.
///
/// So it stopped being a reader and became a marker: one number, on the slot,
/// only when the item is worth a second look.
/// </remarks>
public class ModTierFeatureTests
{
    private static readonly ModTierTable Tiers =
        ModTierTable.Load(Path.Combine(AppContext.BaseDirectory, "modtiers.json"));

    [Fact]
    public void A_top_roll_marks_its_slot_and_says_which_half_it_came_from()
    {
        // FireResist8 is the best of eight, which the player calls tier 1, and
        // it is a suffix. The leading letter used to be T on every mark ever
        // drawn and so said nothing; spending it on prefix-or-suffix answers
        // the question a good roll actually raises, which is whether the other
        // half of the item is still open.
        Assert.Equal("S1", Assert.Single(Draw(Slot("FireResist8")).ColouredTexts).Text);
    }

    [Fact]
    public void A_prefix_says_so_too()
    {
        // Eight tiers on every class that has it, so the ceiling is known
        // without knowing the item class — which keeps this test about the
        // letter rather than about the lookup.
        Assert.Equal(
            "P1",
            Assert.Single(Draw(Slot("LocalIncreasedPhysicalDamagePercent8")).ColouredTexts).Text);
    }

    [Fact]
    public void One_of_each_at_the_same_tier_names_both()
    {
        // Two top prefixes and one of each are not the same item, and a single
        // letter that quietly picked one of them would say they were.
        Assert.Equal(
            "PS1x2",
            Assert.Single(Draw(
                Slot("FireResist8", "LocalIncreasedPhysicalDamagePercent8")).ColouredTexts).Text);
    }

    [Fact]
    public void T1_is_the_best_roll_and_not_the_games_lowest_number()
    {
        // The game counts up and players count down, so FireResist1 is the
        // worst of eight and earns no mark at all.
        Assert.Empty(Draw(Slot("FireResist1")).Texts);
    }

    [Fact]
    public void Nothing_worth_a_second_look_means_no_mark()
    {
        // The whole point: a mark on every slot marks nothing.
        Assert.Empty(Draw(Slot("FireResist4", "IncreasedMana3")).Texts);
    }

    [Fact]
    public void The_best_roll_on_the_item_is_the_one_that_shows()
    {
        // One mark per item, not one per mod. A slot is sixty pixels.
        Assert.Equal(
            "S1",
            Assert.Single(Draw(Slot("FireResist8", "ColdResist7")).ColouredTexts).Text);
    }

    [Fact]
    public void Two_rolls_at_the_same_tier_say_so()
    {
        Assert.Equal(
            "S1x2",
            Assert.Single(Draw(Slot("FireResist8", "ColdResist8")).ColouredTexts).Text);
    }

    [Fact]
    public void Each_tier_gets_its_own_colour()
    {
        var options = new LootSettings();

        Assert.Equal(
            unchecked((uint)options.ModTier1Colour),
            Assert.Single(Draw(Slot("FireResist8"), options).ColouredTexts).Colour);

        Assert.Equal(
            unchecked((uint)options.ModTier2Colour),
            Assert.Single(Draw(Slot("FireResist7"), options).ColouredTexts).Colour);

        Assert.Equal(
            unchecked((uint)options.ModTier3Colour),
            Assert.Single(Draw(Slot("FireResist6"), options).ColouredTexts).Colour);
    }

    [Fact]
    public void The_threshold_decides_what_is_worth_a_mark()
    {
        var strict = new LootSettings { ModTierAlert = 1 };

        Assert.NotEmpty(Draw(Slot("FireResist8"), strict).Texts);
        Assert.Empty(Draw(Slot("FireResist7"), strict).Texts);

        Assert.NotEmpty(Draw(Slot("FireResist1"), new LootSettings { ModTierAlert = 8 }).Texts);
    }

    [Fact]
    public void Every_slot_is_marked_and_not_only_the_one_under_the_cursor()
    {
        // Scanning a stash is a glance now rather than thirty hovers, which is
        // why this stopped needing a cursor at all.
        var canvas = Draw(Slot("FireResist8"), Slot("ColdResist8"), Slot("Intelligence1"));

        Assert.Equal(2, canvas.Texts.Count);
    }

    [Fact]
    public void No_internal_mod_name_ever_reaches_the_screen()
    {
        // The complaint in one assertion: LocalIncreasedEnergyShield and
        // NearbyAlliesAllDamage are developer strings and appear nowhere in the
        // game the player is reading.
        var canvas = Draw(Slot("LocalIncreasedEnergyShield8", "FireResist8"));

        Assert.All(canvas.Texts, t => Assert.DoesNotContain("Local", t.Text, StringComparison.Ordinal));
        Assert.All(canvas.Texts, t => Assert.True(t.Text.Length <= 6, t.Text));
    }

    [Fact]
    public void A_mod_the_table_does_not_know_is_never_marked()
    {
        // Without a ceiling there is no way to know a roll is good, and a guess
        // dressed as a mark is worse than no mark.
        Assert.Empty(Draw(Slot("UniqueManaRegeneration15")).Texts);
    }

    [Fact]
    public void An_item_with_no_rolls_is_never_marked()
    {
        Assert.Empty(Draw(Slot()).Texts);
    }

    [Fact]
    public void Switching_it_off_draws_nothing()
    {
        Assert.Empty(Draw(Slot("FireResist8"), new LootSettings { ShowModTiers = false }).Texts);
    }

    [Fact]
    public void Without_a_table_it_stays_quiet_rather_than_guessing()
    {
        var canvas = new RecordingCanvas();

        new ModTierFeature(new RadarSettings { Loot = new LootSettings() }, ModTierTable.Empty, Away)
            .Draw(Frame(Slot("FireResist8")), canvas);

        Assert.Empty(canvas.Texts);
    }

    [Theory]
    [InlineData(ChipCorner.TopLeft)]
    [InlineData(ChipCorner.TopRight)]
    [InlineData(ChipCorner.BottomLeft)]
    [InlineData(ChipCorner.BottomRight)]
    public void Every_corner_keeps_the_mark_on_its_slot(ChipCorner corner)
    {
        var canvas = Draw(Slot("FireResist8"), new LootSettings { ModTierCorner = corner });

        var at = Assert.Single(canvas.ColouredTexts).At;

        Assert.InRange(at.X, 70f, 150f);
        Assert.InRange(at.Y, 70f, 150f);
    }

    [Fact]
    public void A_tooltip_takes_away_the_marks_it_is_sitting_on()
    {
        // The overlay has no z-order with the game, so a mark belonging to a
        // covered slot lands on top of the item's own lines — which is exactly
        // what the screenshot showed, twice.
        var canvas = Draw(Frame([Slot("FireResist8")], Tooltip(0f, 0f, 400f, 400f)));

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void A_tooltip_beside_a_slot_leaves_that_slot_marked()
    {
        // Standing every mark down while a tooltip was up fixed the overlap by
        // giving up the feature at the moment it is most useful. Only the marks
        // underneath it need to go.
        var canvas = Draw(Frame([Slot("FireResist8")], Tooltip(900f, 900f, 400f, 400f)));

        Assert.Equal("S1", Assert.Single(canvas.ColouredTexts).Text);
    }

    [Fact]
    public void An_undetected_tooltip_still_takes_the_marks_off_the_item()
    {
        // The screenshot: the panel was not being found, so every slot answered
        // "not covered" and two marks belonging to cells behind the tooltip
        // landed on the item's own name and one of its lines.
        //
        // Without a rectangle there is no telling which slots are underneath,
        // and the cursor resting on an item is the only remaining evidence a
        // tooltip is up. So everything stands down — losing the marks while one
        // item is being read costs nothing next to stamping them across it.
        var canvas = Draw(Frame(Slot("FireResist8")));

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void With_no_tooltip_at_all_the_marks_stay()
    {
        Assert.NotEmpty(Draw(Slot("FireResist8")).Texts);
    }

    // ---- the tier at the start of the item's own line ----------------------

    [Fact]
    public void The_tier_lands_in_the_box_the_game_left_at_the_start_of_the_line()
    {
        // One badge per mod line, inside the small box the client pins to the
        // left edge of every row that carries a mod.
        var canvas = Draw(Frame(
            [Slot(100f, 100f, "FireResist8", "ColdResist1")],
            Tooltip(400f, 100f, 500f, 300f, (410f, 120f), (410f, 150f))));

        var badge = Assert.Single(InTooltip(canvas));

        Assert.Equal("S1", badge.Text);
        Assert.InRange(badge.At.X, 410f, 434f);
        Assert.InRange(badge.At.Y, 120f, 146f);
    }

    [Fact]
    public void A_line_whose_roll_misses_the_threshold_gets_nothing()
    {
        // Two lines, one good roll: one badge, on the line that earned it.
        var canvas = Draw(Frame(
            [Slot(100f, 100f, "ColdResist1", "FireResist8")],
            Tooltip(400f, 100f, 500f, 300f, (410f, 120f), (410f, 150f))));

        var badge = Assert.Single(InTooltip(canvas));

        Assert.InRange(badge.At.Y, 150f, 176f);
    }

    [Fact]
    public void When_the_line_count_disagrees_with_the_rolls_nothing_is_claimed()
    {
        // The lines are matched to the rolls by position, and position only
        // means anything while the two agree. A confident T1 against the wrong
        // line is worse than no badge: it would be read, believed, and wrong.
        var canvas = Draw(Frame(
            [Slot(100f, 100f, "FireResist8")],
            Tooltip(400f, 100f, 500f, 300f, (410f, 120f), (410f, 150f))));

        Assert.Empty(InTooltip(canvas));
    }

    [Fact]
    public void A_cursor_on_no_item_puts_no_tier_on_any_line()
    {
        var canvas = Draw(Frame(
            [Slot(700f, 700f, "FireResist8")],
            Tooltip(400f, 100f, 500f, 300f, (410f, 120f))));

        Assert.Empty(InTooltip(canvas));
    }

    [Fact]
    public void Switching_the_line_badge_off_leaves_the_corner_marks_alone()
    {
        var canvas = Draw(
            Frame([Slot(100f, 100f, "FireResist8")],
                Tooltip(400f, 100f, 500f, 300f, (410f, 120f))),
            new LootSettings { ModTierOnTooltip = false });

        var mark = Assert.Single(canvas.ColouredTexts);

        Assert.InRange(mark.At.X, 70f, 150f);
        Assert.Empty(InTooltip(canvas));
    }

    // ---- fixtures --------------------------------------------------------

    /// <summary>Inside the fixture's slot, so the client would be drawing a tooltip.</summary>
    private static Vector2 OnTheItem() => new(100f, 100f);

    /// <summary>Well clear of it.</summary>
    private static Vector2 Away() => new(900f, 900f);

    /// <summary>
    /// Only what was drawn on the panel.
    /// </summary>
    /// <remarks>
    /// The hovered slot keeps its own corner mark — the tooltip is drawn beside
    /// a cell, not over it — so every one of these fixtures records two kinds of
    /// text at once. The badges are the ones inside the panel, which every test
    /// below puts at the same place.
    ///
    /// Both axes, because one is not enough: a slot parked at (700,700) to be
    /// clear of the cursor is also clear to the RIGHT of the panel, and its
    /// corner mark walked straight through a filter that only asked about x.
    /// </remarks>
    private static IReadOnlyList<(Vector2 At, uint Colour, string Text)> InTooltip(
        RecordingCanvas canvas) =>
        canvas.ColouredTexts
            .Where(t => t.At.X is >= 400f and <= 900f && t.At.Y is >= 100f and <= 400f)
            .ToList();

    /// <summary>A panel, with a marker box for each mod line it shows.</summary>
    private static TooltipSnapshot Tooltip(
        float x, float y, float width, float height, params (float X, float Y)[] rows) =>
        new(x, y, width, height,
            rows.Select(r => new ModRowSnapshot(r.X, r.Y, 24f, 26f)).ToImmutableArray());

    private static RecordingCanvas Draw(RenderFrame frame) => Draw(frame, new LootSettings());

    private static RecordingCanvas Draw(RenderFrame frame, LootSettings options)
    {
        var canvas = new RecordingCanvas();

        new ModTierFeature(new RadarSettings { Loot = options }, Tiers, OnTheItem)
            .Draw(frame, canvas);

        return canvas;
    }

    private static RecordingCanvas Draw(params ItemSlotSnapshot[] slots) =>
        Draw(slots, new LootSettings());

    private static RecordingCanvas Draw(ItemSlotSnapshot slot, LootSettings options) =>
        Draw([slot], options);

    private static RecordingCanvas Draw(ItemSlotSnapshot[] slots, LootSettings options)
    {
        var canvas = new RecordingCanvas();

        new ModTierFeature(new RadarSettings { Loot = options }, Tiers, Away)
            .Draw(Frame(slots), canvas);

        return canvas;
    }

    /// <summary>A sixty-pixel slot at (80,80), which is a real one's size.</summary>
    private static ItemSlotSnapshot Slot(params string[] affixes) => Slot(80f, 80f, affixes);

    private static ItemSlotSnapshot Slot(float x, float y, params string[] affixes) =>
        new(new ItemSnapshot("Art", "Beaded Circlet", MonsterRarity.Rare, true,
                affixes.ToImmutableArray()),
            1, x, y, 60f, 60f);

    private static RenderFrame Frame(ItemSlotSnapshot[] slots, TooltipSnapshot tooltip)
    {
        var world = RadarFixture.World() with { Camera = RadarFixture.Camera() };

        return RadarFixture.Frame(world) with
        {
            Ui = new UiSnapshot(
                ImmutableArray<LootLabelSnapshot>.Empty,
                slots.ToImmutableArray(),
                RadarFixture.Camera(),
                tooltip),
        };
    }

    private static RenderFrame Frame(params ItemSlotSnapshot[] slots)
    {
        var world = RadarFixture.World() with { Camera = RadarFixture.Camera() };

        return RadarFixture.Frame(world) with
        {
            Ui = new UiSnapshot(
                ImmutableArray<LootLabelSnapshot>.Empty,
                slots.ToImmutableArray(),
                RadarFixture.Camera()),
        };
    }
}
