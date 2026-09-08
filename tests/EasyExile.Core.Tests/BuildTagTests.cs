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
/// Marking the drops that help what this character is building.
/// </summary>
/// <remarks>
/// Levelling loot is a firehose and almost none of it matters, but which part
/// matters depends entirely on the build — and the game has no idea what that
/// is. The player says once; every drop answers without being hovered.
///
/// The classification is read off the mod's own family name, which the game
/// writes in full, so these use real ids rather than invented ones.
/// </remarks>
public class BuildTagTests
{
    [Theory]
    [InlineData("MinionLife3", BuildTag.Minion)]
    [InlineData("DamageWithBowSkills2", BuildTag.Projectile)]
    [InlineData("ProjectileSpeed1", BuildTag.Projectile)]
    [InlineData("IncreasedAccuracy3", BuildTag.Attack)]
    [InlineData("BaseSpirit2", BuildTag.Spirit)]
    public void A_mod_is_classified_by_the_name_the_game_gave_it(string id, BuildTag expected)
    {
        Assert.Equal(expected, BuildTags.Of(id) & expected);
    }

    [Fact]
    public void A_mod_can_help_more_than_one_thing()
    {
        // LocalIncreasedSpiritAndMana is exactly what it says, and an item with
        // it is worth a look from two different builds.
        var tags = BuildTags.Of("LocalIncreasedSpiritAndMana1");

        Assert.True(tags.HasFlag(BuildTag.Spirit));
    }

    [Fact]
    public void A_resistance_is_not_a_damage_type()
    {
        // ColdResistance contains "Cold" and helps no cold build. Letting it
        // through would put a mark on nearly every ring in the game, which is
        // the same as putting one on none of them.
        Assert.False(BuildTags.Of("ColdResistance4").HasFlag(BuildTag.Cold));
        Assert.False(BuildTags.Of("FireResist8").HasFlag(BuildTag.Fire));
        Assert.False(BuildTags.Of("ChaosResistance2").HasFlag(BuildTag.Chaos));
    }

    [Fact]
    public void A_mod_the_build_does_not_care_about_is_not_marked()
    {
        Assert.Equal(BuildTag.None, BuildTags.Of(["FireResist8"], BuildTag.Minion));
        Assert.Equal(BuildTag.None, BuildTags.Of("SomethingWithNoTier"));
        Assert.Equal(BuildTag.None, BuildTags.Of((string?)null));
    }

    [Fact]
    public void An_item_that_helps_gets_a_mark_on_its_slot()
    {
        var canvas = Draw(BuildTag.Minion, "MinionLife3", "FireResist8");

        Assert.Equal("Mi", Assert.Single(canvas.ColouredTexts).Text);
    }

    [Fact]
    public void An_item_that_helps_two_ways_says_so()
    {
        // The item worth opening hardest, so it gets the loudest mark rather
        // than an arbitrary one of the two.
        var canvas = Draw(BuildTag.Minion | BuildTag.Life, "MinionLife3", "IncreasedLife5");

        Assert.Equal("**", Assert.Single(canvas.ColouredTexts).Text);
    }

    [Fact]
    public void Choosing_nothing_marks_nothing()
    {
        Assert.Empty(Draw(BuildTag.None, "MinionLife3").Texts);
    }

    [Fact]
    public void A_mark_never_lands_on_the_item_being_read()
    {
        // The same rule the tier mark follows. The overlay paints last, so a
        // mark on a covered slot lands on the client's own description of it.
        var canvas = new RecordingCanvas();

        var slot = Slot("MinionLife3");

        new BuildTagFeature(Settings(BuildTag.Minion), OnTheItem)
            .Draw(Frame([slot], new TooltipSnapshot(0f, 0f, 400f, 400f,
                ImmutableArray<ModRowSnapshot>.Empty)), canvas);

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void Nothing_accented_ever_reaches_the_screen()
    {
        // The default font is Latin-1 and the marks are Portuguese
        // abbreviations - "Fo" for fogo, "Ra" for raio.
        foreach (var tag in Enum.GetValues<BuildTag>())
            Assert.All(BuildTags.Mark(tag), c => Assert.True(c < 128));
    }

    // ---- fixtures ---------------------------------------------------------

    private static Vector2 OnTheItem() => new(100f, 100f);

    private static Vector2 Away() => new(900f, 900f);

    private static RadarSettings Settings(BuildTag wanted) =>
        new() { Loot = new LootSettings { BuildTagMask = (int)wanted } };

    private static ItemSlotSnapshot Slot(params string[] affixes) =>
        new(new ItemSnapshot("Art", "Beaded Circlet", MonsterRarity.Rare, true,
                affixes.ToImmutableArray()),
            1, 80f, 80f, 60f, 60f);

    private static RecordingCanvas Draw(BuildTag wanted, params string[] affixes)
    {
        var canvas = new RecordingCanvas();

        new BuildTagFeature(Settings(wanted), Away).Draw(Frame([Slot(affixes)], null), canvas);

        return canvas;
    }

    private static RenderFrame Frame(ItemSlotSnapshot[] slots, TooltipSnapshot? tooltip)
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
}
