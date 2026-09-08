using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Loot;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Marking the items that fill a resistance this character is short of.
/// </summary>
/// <remarks>
/// The word carrying the whole feature is "short of". Every ring in the game
/// rolls resistances, so marking items that grant one marks everything and says
/// nothing — the useful list is the one that shrinks as the character improves
/// and is empty once capped.
///
/// The keys these are read from were matched against a character sheet showing
/// Fire 7, Cold 11, Lightning -2, Chaos 6. The chaos value is what identified
/// them: two other blocks in the stat table hold the three elemental numbers
/// and neither carries a fourth.
/// </remarks>
public class ResistanceTests
{
    [Fact]
    public void An_item_that_fills_a_gap_is_marked_with_which_gap()
    {
        Assert.Equal("+F", Mark(Draw(Resists(fire: 7), "FireResist8")));
    }

    [Fact]
    public void A_resistance_already_at_the_cap_is_not_a_gap()
    {
        // The list has to shrink as the character improves, or it is just a
        // mark on every ring in the game.
        Assert.Empty(Draw(Resists(fire: 75), "FireResist8").Texts);
        Assert.Empty(Draw(Resists(fire: 90), "FireResist8").Texts);
    }

    [Fact]
    public void One_item_can_answer_two_gaps()
    {
        Assert.Equal(
            "+FC",
            Mark(Draw(Resists(fire: 7, cold: 11), "FireResist8", "ColdResistance4")));
    }

    [Fact]
    public void All_resistances_counts_for_every_gap_at_once()
    {
        Assert.Equal(
            "+FCLX",
            Mark(Draw(Resists(fire: 7, cold: 11, lightning: -2, chaos: 6), "AllResistances2")));
    }

    [Fact]
    public void A_capped_character_is_told_nothing_at_all()
    {
        Assert.Empty(Draw(Resists(75, 75, 75, 75), "AllResistances2").Texts);
    }

    [Fact]
    public void Stats_that_could_not_be_read_say_nothing_rather_than_zero()
    {
        // Unknown and "no resistance at all" look identical if the answer is a
        // number either way, and only one of them wants every ring marked.
        Assert.False(ResistanceSnapshot.Unknown.IsKnown);
        Assert.False(ResistanceSnapshot.Unknown.AnyMissing);

        Assert.Empty(Draw(ResistanceSnapshot.Unknown, "FireResist8").Texts);
    }

    [Fact]
    public void A_mod_that_is_not_a_resistance_is_not_marked()
    {
        Assert.Empty(Draw(Resists(fire: 7), "IncreasedLife5").Texts);
        Assert.Empty(Draw(Resists(fire: 7), "MinionLife3").Texts);
    }

    [Fact]
    public void The_gap_is_never_negative()
    {
        var capped = Resists(fire: 90);

        Assert.Equal(0, capped.Missing(ResistanceKind.Fire));
        // A negative resistance is further from the cap than a zero one, which
        // is the case a levelling character is usually in.
        Assert.Equal(77, Resists(lightning: -2).Missing(ResistanceKind.Lightning));
    }

    [Fact]
    public void Nothing_lands_on_the_item_being_read()
    {
        var canvas = new RecordingCanvas();

        new ResistanceFeature(new RadarSettings { Loot = new LootSettings() }, OnTheItem)
            .Draw(
                Frame(Resists(fire: 7), [Slot("FireResist8")],
                    new TooltipSnapshot(0f, 0f, 400f, 400f, ImmutableArray<ModRowSnapshot>.Empty)),
                canvas);

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void Switching_it_off_draws_nothing()
    {
        var canvas = new RecordingCanvas();

        new ResistanceFeature(
                new RadarSettings { Loot = new LootSettings { ShowResistanceHelp = false } }, Away)
            .Draw(Frame(Resists(fire: 7), [Slot("FireResist8")], null), canvas);

        Assert.Empty(canvas.Texts);
    }

    /// <summary>
    /// The mark as it reads on screen, letter by letter.
    /// </summary>
    /// <remarks>
    /// Each letter is drawn separately so it can carry its element's own colour
    /// - orange for fire, blue for cold - which is what saves anyone from
    /// having to remember that G means cold.
    /// </remarks>
    // ---- the player choosing, instead of the tool ---------------------------

    [Fact]
    public void Chosen_mode_marks_what_was_ticked_even_at_the_cap()
    {
        // The whole reason this mode exists. Automatic is right while levelling
        // and wrong for somebody deliberately over-capping for a map, who is
        // short of nothing and still shopping.
        var loot = new LootSettings
        {
            ResistanceMode = ResistanceMode.Chosen,
            ResistanceWatchMask = (int)ResistanceWatch.Fire,
        };

        Assert.Equal("+F", Mark(Draw(loot, Resists(fire: 90), "FireResist8")));
    }

    [Fact]
    public void Chosen_mode_ignores_elements_that_were_not_ticked()
    {
        var loot = new LootSettings
        {
            ResistanceMode = ResistanceMode.Chosen,
            ResistanceWatchMask = (int)ResistanceWatch.Cold,
        };

        // Fire is wide open and still not marked: in this mode the character's
        // own numbers are not consulted at all.
        Assert.Empty(Draw(loot, Resists(fire: 0), "FireResist8").Texts);
        Assert.Equal("+C", Mark(Draw(loot, Resists(cold: 70), "ColdResist8")));
    }

    [Fact]
    public void Chosen_mode_still_answers_when_the_stats_could_not_be_read()
    {
        // Automatic goes silent for ever here, because it cannot tell a
        // character with no resistance from one it failed to read. Choosing the
        // elements yourself is the way out of that.
        var loot = new LootSettings
        {
            ResistanceMode = ResistanceMode.Chosen,
            ResistanceWatchMask = (int)ResistanceWatch.All,
        };

        Assert.Empty(Draw(ResistanceSnapshot.Unknown, "FireResist8").Texts);
        Assert.Equal("+F", Mark(Draw(loot, ResistanceSnapshot.Unknown, "FireResist8")));
    }

    [Fact]
    public void Nothing_ticked_draws_nothing()
    {
        // Switching every element off is a way of turning the mark off, and it
        // must not fall back to marking everything.
        var loot = new LootSettings
        {
            ResistanceMode = ResistanceMode.Chosen,
            ResistanceWatchMask = (int)ResistanceWatch.None,
        };

        Assert.Empty(Draw(loot, Resists(fire: 0), "FireResist8").Texts);
    }

    // ---- a target other than the cap ---------------------------------------

    [Fact]
    public void A_lower_target_stops_marking_what_already_reached_it()
    {
        // Early in the campaign, aiming all four at 75 marks every ring and
        // singles out none. A reachable target is what makes the mark mean
        // something again.
        var loot = new LootSettings { ResistanceTarget = 30 };

        Assert.Empty(Draw(loot, Resists(fire: 35), "FireResist8").Texts);
        Assert.Equal("+F", Mark(Draw(loot, Resists(fire: 20), "FireResist8")));
    }

    [Fact]
    public void A_higher_target_keeps_marking_past_the_cap()
    {
        var loot = new LootSettings { ResistanceTarget = 80 };

        Assert.Equal("+F", Mark(Draw(loot, Resists(fire: 75), "FireResist8")));
    }

    [Fact]
    public void The_target_decides_whether_the_feature_says_anything_at_all()
    {
        // The early return that keeps a capped character from seeing a mark on
        // every ring has to move with the target too, or a raised target would
        // be silently ignored.
        var capped = Resists(fire: 75, cold: 75, lightning: 75, chaos: 75);

        Assert.Empty(Draw(new LootSettings(), capped, "FireResist8").Texts);
        Assert.Equal(
            "+F",
            Mark(Draw(new LootSettings { ResistanceTarget = 80 }, capped, "FireResist8")));
    }

    private static string Mark(RecordingCanvas canvas) =>
        string.Concat(canvas.ColouredTexts.Select(t => t.Text));

    [Fact]
    public void Each_letter_carries_its_own_element_colour()
    {
        var canvas = Draw(Resists(fire: 7, cold: 11), "FireResist8", "ColdResistance4");

        var fire = Assert.Single(canvas.ColouredTexts, t => t.Text == "F");
        var cold = Assert.Single(canvas.ColouredTexts, t => t.Text == "C");

        Assert.NotEqual(fire.Colour, cold.Colour);
    }

    // ---- fixtures ---------------------------------------------------------

    private static Vector2 OnTheItem() => new(100f, 100f);

    private static Vector2 Away() => new(900f, 900f);

    private static ResistanceSnapshot Resists(
        int fire = 75, int cold = 75, int lightning = 75, int chaos = 75) =>
        new(fire, cold, lightning, chaos);

    private static ItemSlotSnapshot Slot(params string[] affixes) =>
        new(new ItemSnapshot("Art", "Ring", MonsterRarity.Rare, true, affixes.ToImmutableArray()),
            1, 80f, 80f, 60f, 60f);

    private static RecordingCanvas Draw(ResistanceSnapshot resists, params string[] affixes) =>
        Draw(new LootSettings(), resists, affixes);

    private static RecordingCanvas Draw(
        LootSettings loot, ResistanceSnapshot resists, params string[] affixes)
    {
        var canvas = new RecordingCanvas();

        new ResistanceFeature(new RadarSettings { Loot = loot }, Away)
            .Draw(Frame(resists, [Slot(affixes)], null), canvas);

        return canvas;
    }

    private static RenderFrame Frame(
        ResistanceSnapshot resists, ItemSlotSnapshot[] slots, TooltipSnapshot? tooltip)
    {
        var world = RadarFixture.World() with { Camera = RadarFixture.Camera() };

        world = world with { Player = world.Player with { Resistances = resists } };

        return RadarFixture.Frame(world) with
        {
            Ui = new UiSnapshot(
                ImmutableArray<LootLabelSnapshot>.Empty,
                slots.ToImmutableArray(),
                RadarFixture.Camera(),
                tooltip),
        };
    }
}
