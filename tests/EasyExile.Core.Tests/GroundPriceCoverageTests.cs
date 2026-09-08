using System.Collections.Immutable;
using System.Text.Json;
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
/// A drop the game has not put a label on still gets its price.
/// </summary>
/// <remarks>
/// The client only draws a ground label for loot within a short radius of the
/// character, so most of what is lying in an area has no tag at all — measured
/// live in Act 2, four items on the floor and not one tag among them.
///
/// With value-on-tags enabled, the projected route used to stand down for
/// everything that was not an unidentified unique, on the reasoning that the
/// tag route had it covered. It did not: it had nothing to draw on. Between the
/// two of them the floor went silent, which is what "não está aparecendo os
/// preços no chão" was.
/// </remarks>
public class GroundPriceCoverageTests : IDisposable
{
    private readonly string _cache = Path.Combine(
        Path.GetTempPath(), $"easyexile-coverage-{Guid.NewGuid():N}.json");

    [Fact]
    public void A_priced_drop_with_no_tag_is_still_shown()
    {
        var canvas = Draw(TagsOn, Drop("ExaltedOrbArt", "Exalted Orb"), NoTags);

        Assert.Contains(canvas.Texts, t => t.Text.Contains("ex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Anchoring_to_tags_does_not_silence_ordinary_drops()
    {
        // The old rule in one assertion: not a unique, anchoring on, therefore
        // skipped — and the tag route had no tag for it either.
        var ordinary = Drop("ExaltedOrbArt", "Exalted Orb");

        Assert.NotEmpty(Draw(TagsOn, ordinary, NoTags).Texts);
        Assert.NotEmpty(Draw(TagsOff, ordinary, NoTags).Texts);
    }

    [Fact]
    public void A_drop_the_tag_route_already_drew_is_not_drawn_twice()
    {
        // The point of standing down is still served: one chip, not two, when
        // the game IS labelling the drop.
        var canvas = Draw(TagsOn, Drop("ExaltedOrbArt", "Exalted Orb"), Tag("Exalted Orb"));

        Assert.Single(canvas.Texts, t => t.Text.Contains("ex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_drop_with_no_price_draws_nothing_whether_tagged_or_not()
    {
        // What was really on the floor in Act 2: magic bases and a charm, none
        // of them priced anywhere. Silence is the right answer here, and this
        // holds the fix to fixing only the other case.
        Assert.Empty(Draw(TagsOn, Drop("BodyInt03", "Hexer's Robe"), NoTags).Texts);
        Assert.Empty(Draw(TagsOff, Drop("BodyInt03", "Hexer's Robe"), NoTags).Texts);
    }

    // ---- fixtures --------------------------------------------------------

    private static LootSettings TagsOn => new() { AnchorValuesToTags = true };

    private static LootSettings TagsOff => new() { AnchorValuesToTags = false };

    private static ImmutableArray<LootLabelSnapshot> NoTags =>
        ImmutableArray<LootLabelSnapshot>.Empty;

    // ---- a unique the price book has never heard of --------------------------

    [Fact]
    public void A_unique_with_no_price_is_still_marked_on_the_ground()
    {
        // The bug this covers: a unique is priced by its ART, because the game
        // writes only the base type on the ground tag. A unique whose art the
        // book does not carry produced no price, and the whole tag was skipped
        // - so the one drop that always deserves a look was the one guaranteed
        // to be invisible.
        var canvas = Draw(
            new LootSettings(),
            Unique("Art/2DItems/Armours/BodyArmours/Nowhere", "Wayfarer Jacket"),
            Tag("Wayfarer Jacket"));

        Assert.Contains(canvas.Texts, t => t.Text.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_unpriced_unique_mark_says_nothing_about_value()
    {
        // Deliberately not a number. Inventing one for a unique nobody has
        // priced would be worse than the silence it replaces: the honest answer
        // is that the tool does not know and the item is worth taking anyway.
        var canvas = Draw(
            new LootSettings(),
            Unique("Art/2DItems/Armours/BodyArmours/Nowhere", "Wayfarer Jacket"),
            Tag("Wayfarer Jacket"));

        Assert.DoesNotContain(canvas.Texts, t => t.Text.Any(char.IsDigit));
    }

    [Fact]
    public void It_is_drawn_in_the_colour_kept_for_exactly_this()
    {
        // "Unique sem preco - pode valer qualquer coisa" was a colour in the
        // settings that nothing on the ground ever used.
        var options = new LootSettings();

        var canvas = Draw(
            options,
            Unique("Art/2DItems/Armours/BodyArmours/Nowhere", "Wayfarer Jacket"),
            Tag("Wayfarer Jacket"));

        Assert.Contains(canvas.ColouredTexts, w =>
            w.Text.Contains("unique", StringComparison.OrdinalIgnoreCase) &&
            w.Colour == unchecked((uint)options.UnknownColour));
    }

    [Fact]
    public void A_rare_with_no_price_stays_quiet()
    {
        // The rule is about uniques only. Everything else with no price is
        // genuinely not worth a line, and marking it would put a chip on most
        // of the floor while levelling.
        var canvas = Draw(
            new LootSettings(),
            Drop("Art/2DItems/Armours/BodyArmours/Nowhere", "Wayfarer Jacket"),
            Tag("Wayfarer Jacket"));

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void It_can_be_switched_off()
    {
        var canvas = Draw(
            new LootSettings { ShowUnpricedUniques = false },
            Unique("Art/2DItems/Armours/BodyArmours/Nowhere", "Wayfarer Jacket"),
            Tag("Wayfarer Jacket"));

        Assert.Empty(canvas.Texts);
    }

    private static EntitySnapshot Unique(string art, string baseName) =>
        RadarFixture.Entity(1, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/WorldItem") with
        {
            Kind = EntityKind.Other,
            Item = new ItemSnapshot(art, baseName, MonsterRarity.Unique, Identified: false),
        };

    private static ImmutableArray<LootLabelSnapshot> Tag(string text) =>
        ImmutableArray.Create(new LootLabelSnapshot(text, 940f, 520f, 160f, 33f));

    private static EntitySnapshot Drop(string art, string baseName) =>
        RadarFixture.Entity(1, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/WorldItem") with
        {
            Kind = EntityKind.Other,
            Item = new ItemSnapshot(art, baseName, MonsterRarity.Normal, Identified: true),
        };

    private RecordingCanvas Draw(
        LootSettings options, EntitySnapshot drop, ImmutableArray<LootLabelSnapshot> tags)
    {
        Seed();

        var prices = new PriceBook(_cache);

        Assert.True(prices.IsLoaded, "a fixture do price book nao carregou");

        var feature = new LootValuesFeature(
            new RadarSettings { Loot = options }, new RadarStats(), prices);

        var canvas = new RecordingCanvas();
        var camera = RadarFixture.Camera();

        var world = RadarFixture.World(entities: drop) with { Camera = camera };

        var frame = RadarFixture.Frame(world) with
        {
            Ui = new UiSnapshot(tags, ImmutableArray<ItemSlotSnapshot>.Empty, camera),
        };

        feature.Draw(frame, canvas);

        return canvas;
    }

    /// <summary>One priced item, written in the shape the book caches.</summary>
    private void Seed()
    {
        var row = new
        {
            Name = "Exalted Orb",
            Exalted = 1.0,
            Quantity = 5000,
            Category = "Currency",
        };

        File.WriteAllText(_cache, JsonSerializer.Serialize(new
        {
            League = "Test",
            FetchedUtc = DateTime.UtcNow,
            ExPerDivine = 100.0,
            ExPerChaos = 5.0,
            ByArt = new Dictionary<string, object> { ["ExaltedOrbArt"] = row },
            ByName = new Dictionary<string, object> { ["Exalted Orb"] = row },
        }));
    }

    public void Dispose()
    {
        if (File.Exists(_cache)) File.Delete(_cache);
    }
}
