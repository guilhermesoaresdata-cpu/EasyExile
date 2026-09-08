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

    private static RecordingCanvas Draw(ResistanceSnapshot resists, params string[] affixes)
    {
        var canvas = new RecordingCanvas();

        new ResistanceFeature(new RadarSettings { Loot = new LootSettings() }, Away)
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
