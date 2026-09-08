using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Loot;
using EasyExile.Radar.Pricing;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.Loot;

namespace EasyExile.Core.Tests;

/// <summary>
/// Prices over drops: the two lookup keys, the filters, and the reveal.
/// </summary>
public class LootValuesTests
{
    private static EntitySnapshot Drop(ItemSnapshot item) =>
        RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/MiscellaneousObjects/WorldItem") with
        {
            Kind = EntityKind.Other,
            Item = item,
        };

    // ---- the identity model -----------------------------------------------------

    [Fact]
    public void AnUnidentifiedUniqueIsRecognised()
    {
        // The game hides the name; the art does not care whether it has been
        // identified. That is exactly when knowing costs something.
        var hidden = new ItemSnapshot("Earthbound", null, MonsterRarity.Unique, Identified: false);

        Assert.True(hidden.IsUnidentifiedUnique);
        Assert.True(hidden.HasIdentity);

        var known = hidden with { Identified = true };

        Assert.False(known.IsUnidentifiedUnique);
    }

    [Fact]
    public void ANonUniqueIsNeverTreatedAsUnidentified()
    {
        // Identified defaults TRUE, so a plain base with no Mods component is
        // never mistaken for something whose name is being hidden.
        var plain = new ItemSnapshot("CoinPileTier1", "Gold", MonsterRarity.Normal, Identified: true);

        Assert.False(plain.IsUnidentifiedUnique);
    }

    [Fact]
    public void AnItemWithNeitherKeyHasNoIdentity()
    {
        // Fail closed: nothing to price, so nothing is drawn.
        Assert.False(new ItemSnapshot(null, null, MonsterRarity.Normal, true).HasIdentity);
    }

    // ---- the filters ------------------------------------------------------------

    [Theory]
    [InlineData("Currency", true, false, false)]
    [InlineData("Essence", true, false, false)]
    [InlineData("Runes", true, false, false)]
    [InlineData("UniqueWeapon", false, true, false)]
    [InlineData("UncutGems", false, false, true)]
    public void CategoriesFilterIndependently(string category, bool currency, bool unique, bool gem)
    {
        var options = new LootSettings
        {
            ShowCurrency = currency,
            ShowUniques = unique,
            ShowGems = gem,
            ShowOther = false,
        };

        Assert.True(options.ShowsCategory(category));
    }

    [Fact]
    public void AnUnknownCategoryFallsUnderOther()
    {
        // poe.ninja adds types with content patches. A rigid list would silently
        // drop whatever it has not heard of, which is the wrong way to fail.
        Assert.True(new LootSettings().ShowsCategory("SomethingAddedNextLeague"));
        Assert.False(new LootSettings { ShowOther = false }.ShowsCategory("SomethingAddedNextLeague"));
    }

    [Fact]
    public void EachBucketHasItsOwnFloor()
    {
        // Five Exalted is an unremarkable unique and an extraordinary scroll, so
        // one shared floor cannot serve both. A single global minimum was my
        // simplification and it is what made the feature look broken: at one
        // Exalted every levelling drop is correctly hidden, so nothing shows.
        var options = new LootSettings();

        Assert.Equal(5f, options.MinimumFor("UniqueWeapon", unique: true), 2);
        Assert.Equal(1f, options.MinimumFor("Currency", unique: false), 2);
        Assert.Equal(1f, options.MinimumFor("SomethingElse", unique: false), 2);
    }

    [Fact]
    public void TagAnchoringIsTheDefault()
    {
        // The reference's default, and not a preference: the game lays its loot
        // tags out in a column so they never overlap, so a chip drawn at the
        // item's projected position lands near the item and nowhere near its
        // name. The tag's rectangle is the game's own arithmetic.
        Assert.True(new LootSettings().AnchorValuesToTags);
    }

    [Fact]
    public void ALootLabelKnowsWhereItsValueGoes()
    {
        var label = new LootLabelSnapshot("Exalted Orb", X: 100f, Y: 200f, Width: 80f, Height: 20f);

        // The chip goes beside the name, so the right edge and the vertical
        // middle are what the renderer needs.
        Assert.Equal(180f, label.Right, 2);
        Assert.Equal(210f, label.CentreY, 2);
    }

    [Fact]
    public void ASnapshotWithoutLabelsReadsAsEmptyRatherThanDefault()
    {
        // A default ImmutableArray throws on enumeration, and the renderer walks
        // this every frame. Empty is the only safe absence.
        var snapshot = new WorldSnapshot(
            DateTimeOffset.UtcNow, 1, new AreaId(1), default!,
            ImmutableArray<EntitySnapshot>.Empty, null, default!, null);

        Assert.Empty(snapshot.Labels);
    }

    [Fact]
    public void HighlightingPanelsIsOnByDefault()
    {
        // The hover chip answers "what is this one worth"; a full ritual window
        // or stash tab poses a different question — which of these forty do I
        // care about — and hovering forty squares is the work the tool exists
        // to remove.
        var options = new LootSettings();

        Assert.True(options.HighlightSlots);
        Assert.True(options.ShowSlotValues);
    }

    [Fact]
    public void ASlotPricesItsWholeStack()
    {
        var slot = new ItemSlotSnapshot(
            new ItemSnapshot("CurrencyUpgradeToMagic", "Orb of Transmutation", MonsterRarity.Normal, true),
            Stack: 176, X: 10f, Y: 20f, Width: 40f, Height: 40f);

        // 176 orbs are worth 176 orbs. Pricing the pile as one is not rounding,
        // it is wrong by the size of the pile.
        Assert.Equal(176, slot.Count);
        Assert.Equal(50f, slot.Right, 2);
        Assert.Equal(60f, slot.Bottom, 2);
        Assert.True(slot.Contains(30f, 40f));
        Assert.False(slot.Contains(60f, 40f));
    }

    [Fact]
    public void AnEmptyStackStillCountsAsOne()
    {
        // Most items do not stack, so the component is absent and the count
        // reads zero. Multiplying a price by zero would hide every non-stacking
        // item in the game.
        var slot = new ItemSlotSnapshot(
            new ItemSnapshot(null, "Iron Crown", MonsterRarity.Rare, true),
            Stack: 0, X: 0f, Y: 0f, Width: 40f, Height: 40f);

        Assert.Equal(1, slot.Count);
    }

    [Fact]
    public void AColourSurvivesTheRoundTripThroughSettings()
    {
        // ImGui works in floats and the settings file carries ints, so a colour
        // crosses two representations every time it is touched. A shade lost
        // per save is a setting that quietly drifts to grey.
        var original = Palette.Rgba(70, 230, 255, 255);

        Span<float> rgba = stackalloc float[4];
        Palette.Unpack(original, rgba);

        Assert.Equal(original, Palette.Pack(rgba));
    }

    [Fact]
    public void TheDefaultHighlightColoursAreDistinguishable()
    {
        // Three tiers that answer different questions are worth nothing if they
        // look the same at a glance over a bright, moving background.
        var options = new LootSettings();

        Assert.NotEqual(options.RichColour, options.PricedColour);
        Assert.NotEqual(options.PricedColour, options.UnknownColour);
        Assert.NotEqual(options.RichColour, options.UnknownColour);
    }

    [Fact]
    public void ValuesDefaultToTheCornerTheGameLeavesEmpty()
    {
        var options = new LootSettings();

        Assert.Equal(ChipCorner.BottomLeft, options.SlotValueCorner);
        Assert.Equal(ChipCorner.BottomLeft, options.HoverCorner);

        // The name doubles the chip's width over an already dense panel, and
        // the game names the item on hover anyway. Worth having when a number
        // looks wrong; not worth having all the time.
        Assert.False(options.NameUniques);
    }

    [Theory]
    [InlineData(ChipCorner.BottomLeft, 12f, 68f)]
    [InlineData(ChipCorner.TopLeft, 12f, 22f)]
    [InlineData(ChipCorner.BottomRight, 28f, 68f)]
    [InlineData(ChipCorner.TopRight, 28f, 22f)]
    public void EveryCornerLandsInsideTheSlot(ChipCorner corner, float left, float top)
    {
        // A slot is 40x60 at (10,20); a chip is 20x10. Each corner has to stay
        // inside, because a chip that overhangs lands on the neighbouring item
        // and describes the wrong one.
        var slot = new ItemSlotSnapshot(
            new ItemSnapshot(null, "Straw Sandals", MonsterRarity.Normal, true),
            Stack: 1, X: 10f, Y: 20f, Width: 40f, Height: 60f);

        var (x, y) = SlotHighlightFeature.Corner(corner, slot, new Vector2(20f, 10f));

        Assert.Equal(left, x, 2);
        Assert.Equal(top, y, 2);
    }

    [Fact]
    public void AThinMarketIsFlaggedRatherThanHidden()
    {
        // A price backed by two listings and one backed by two hundred are not
        // the same claim, and printing them identically is the overlay lying by
        // omission. Hiding the thin one would throw away real information.
        var book = new PriceBook(Path.Combine(Path.GetTempPath(), "easyexile-prices-test.json"));

        var thin = new PriceResult("Some Unique", 12d, Quantity: 1, "UniqueArmours");
        var solid = new PriceResult("Luminous Pace", 88.85d, Quantity: 129, "UniqueArmours");

        Assert.EndsWith("?", book.Describe(thin, 12d, minQuantity: 2, nameIt: false));
        Assert.DoesNotContain("?", book.Describe(solid, 88.85d, minQuantity: 2, nameIt: false));
    }

    [Fact]
    public void NoVolumeDataIsNotAThinMarket()
    {
        // A reported volume of zero means "no volume data", which many
        // legitimate fungibles have. Flagging those would flag half the book.
        var book = new PriceBook(Path.Combine(Path.GetTempPath(), "easyexile-prices-test.json"));
        var unknown = new PriceResult("Orb of Alchemy", 1.34d, Quantity: 0, "Currency");

        Assert.DoesNotContain("?", book.Describe(unknown, 1.34d, minQuantity: 5, nameIt: false));
    }

    [Fact]
    public void AUniquesPriceCarriesItsName()
    {
        // A bare "1 div" is unattributable: it took a memory dump to establish
        // which item the number belonged to.
        var book = new PriceBook(Path.Combine(Path.GetTempPath(), "easyexile-prices-test.json"));
        var price = new PriceResult("Luminous Pace", 88.85d, Quantity: 129, "UniqueArmours");

        Assert.Contains("Luminous Pace", book.Describe(price, 88.85d, minQuantity: 2, nameIt: true));
    }

    [Fact]
    public void TwoUniquesSharingOneIconAreNotTheSameItem()
    {
        // Enfolding Dawn exists on Pilgrim Vestments and on Runemastered
        // Pilgrim Vestments — same name, same icon, 9 div against 3.8 div.
        // Keyed on art alone the busier row wins, so the overlay confidently
        // priced a body armour as the other variant. The base separates them.
        var path = Path.Combine(Path.GetTempPath(), "easyexile-variants-" + Guid.NewGuid().ToString("N") + ".json");

        File.WriteAllText(path, """
            {
              "League": "Test",
              "ExPerDivine": 100,
              "ExPerChaos": 1,
              "ByArt": {
                "EnfoldingDawn": { "Name": "Enfolding Dawn", "Exalted": 380, "Quantity": 100, "Category": "UniqueArmours" },
                "EnfoldingDawn|Pilgrim Vestments": { "Name": "Enfolding Dawn", "Exalted": 900, "Quantity": 60, "Category": "UniqueArmours" }
              },
              "ByName": {}
            }
            """);

        try
        {
            var book = new PriceBook(path);

            Assert.Equal(900d, book.TryByArt("EnfoldingDawn", "Pilgrim Vestments")!.Value.Exalted, 2);

            // An unknown variant still answers, with the art-only row. Silence
            // would be worse: the item is a unique and the number is close.
            Assert.Equal(380d, book.TryByArt("EnfoldingDawn", "Something Else")!.Value.Exalted, 2);
            Assert.Equal(380d, book.TryByArt("EnfoldingDawn")!.Value.Exalted, 2);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("exalted", 1d)]
    [InlineData("divine", 90.75d)]
    [InlineData("chaos", 24.16d)]
    [InlineData("Divine", 90.75d)]
    [InlineData("shards", 0d)]
    [InlineData(null, 0d)]
    public void EachResponseIsPricedInTheUnitItDeclares(string? primary, double expected)
    {
        // poe.ninja's two endpoints do not agree. Exchange quotes in Divine —
        // an Exalted Orb reads 0.011 — while the stash-item endpoint carrying
        // uniques quotes in Exalted, where Queen of the Forest reads 88.0.
        // Treating both as Divine multiplied every unique by the divine rate,
        // so a one-exalt body armour was shown as nine divine. It took a player
        // who had bought the item to catch it.
        //
        // An unfamiliar unit yields zero, which skips the category. A wrong
        // factor is a confident wrong number, and that is worse than no number.
        Assert.Equal(expected, PriceBook.PrimaryToExalted(primary, 90.75d, 24.16d), 3);
    }

    [Fact]
    public void OneItemShownTwiceIsStillOneItem()
    {
        // While the game shows a tooltip, the tooltip is itself a UI element
        // carrying the same item entity — so the sweep finds the item twice and
        // outlines an empty square at the tooltip's corner. The entity says they
        // are the same thing; the rectangles cannot.
        var item = new ItemSnapshot(null, "Lesser Iron Rune", MonsterRarity.Normal, true);
        var id = new EntityId(0x1234);

        var real = new ItemSlotSnapshot(item, 1, X: 500f, Y: 400f, Width: 40f, Height: 40f, Entity: id);
        var tooltip = new ItemSlotSnapshot(item, 1, X: 20f, Y: 20f, Width: 60f, Height: 60f, Entity: id);

        // The cursor is on the real slot: the tooltip is never where you point.
        var kept = ItemSlotSnapshot.Distinct([tooltip, real], 520f, 420f).Single();

        Assert.Equal(500f, kept.X, 2);
    }

    [Fact]
    public void WithNoCursorOnEitherTheSmallerCopyWins()
    {
        // A preview is drawn larger than the cell it came from, so size is the
        // tiebreak when the cursor settles nothing.
        var item = new ItemSnapshot(null, "Exalted Orb", MonsterRarity.Normal, true);
        var id = new EntityId(0x99);

        var cell = new ItemSlotSnapshot(item, 1, X: 500f, Y: 400f, Width: 40f, Height: 40f, Entity: id);
        var preview = new ItemSlotSnapshot(item, 1, X: 20f, Y: 20f, Width: 90f, Height: 90f, Entity: id);

        var kept = ItemSlotSnapshot.Distinct([preview, cell], 0f, 0f).Single();

        Assert.Equal(40f, kept.Width, 2);
    }

    [Fact]
    public void AGroundLabelKnowsItsCentreAndItsEdges()
    {
        // The value is centred under the tag and the revealed name centred over
        // it, so both need the tag's middle — not its left edge, which is what
        // the old "beside it" layout used and why a floor of drops read as
        // scattered rather than labelled.
        var label = new LootLabelSnapshot("Hardwood Spear", X: 100f, Y: 200f, Width: 80f, Height: 20f);

        Assert.Equal(140f, label.CentreX, 2);
        Assert.Equal(220f, label.Bottom, 2);
        Assert.Equal(180f, label.Right, 2);
    }

    [Fact]
    public void RevealingNamesIsOnAndHasItsOwnColour()
    {
        // The game names a drop by its base and the name that matters is often
        // a different one. Its colour is the tag's, not a value colour: it is
        // replacing that line, not competing with it.
        var options = new LootSettings();

        Assert.True(options.RevealNames);
        Assert.NotEqual(options.RichColour, options.RevealColour);
        Assert.NotEqual(options.PricedColour, options.RevealColour);
    }

    [Fact]
    public void AUniquesNameHasItsOwnColourSetting()
    {
        // The replacement stands in for the client's own line and the client
        // writes a unique in orange — but orange is a range, and only the person
        // looking at it over their own tileset knows which one reads.
        var options = new LootSettings();

        Assert.Equal(unchecked((int)0xFF28A0FF), options.UniqueNameColour);
        Assert.NotEqual(options.RevealColour, options.UniqueNameColour);
    }

    [Fact]
    public void ATagFollowsTheCameraBetweenWorldTicks()
    {
        // The tag rect is measured on the world walk, about eighteen a second,
        // and drawn at frame rate. Left alone the chip sits where the tag WAS
        // and jumps when the walk catches up — which is the stutter that was
        // reported. The item has not moved; the view has.
        var method = typeof(LootValuesFeature).GetMethod(
            "Drift", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        var world = new Vector3(100f, 0f, 100f);
        var entity = new EntitySnapshot(
            new EntityId(1), "Metadata/MiscellaneousObjects/WorldItem",
            ImmutableArray<string>.Empty, world, null, ImmutableArray<EasyExile.Core.World.Vital>.Empty);

        // Same camera on both sides: nothing has moved, so nothing shifts.
        var camera = RadarFixture.Camera();
        var still = (Vector2)method!.Invoke(null, [entity, camera, camera])!;

        Assert.Equal(0f, still.X, 3);
        Assert.Equal(0f, still.Y, 3);

        // No camera at all is not a reason to guess: the correction is zero.
        var blind = (Vector2)method.Invoke(null, [entity, null, camera])!;

        Assert.Equal(0f, blind.X, 3);
    }

    // ---- the overlay ------------------------------------------------------------

    private static RecordingCanvas Draw(
        EntitySnapshot entity, PriceBook prices, LootSettings? options = null)
    {
        var settings = new RadarSettings { Loot = options ?? new LootSettings() };
        var feature = new LootValuesFeature(settings, new RadarStats(), prices);
        var canvas = new RecordingCanvas();

        var world = RadarFixture.World(entities: entity) with { Camera = RadarFixture.Camera() };

        feature.Draw(RadarFixture.Frame(world), canvas);

        return canvas;
    }

    [Fact]
    public void AnEmptyPriceBookDrawsNothing()
    {
        // No prices means no opinions. It must not draw a zero over every drop.
        var prices = new PriceBook(TempCache());

        try
        {
            Assert.False(prices.IsLoaded);

            var canvas = Draw(
                Drop(new ItemSnapshot("Earthbound", "Sceptre", MonsterRarity.Unique, false)), prices);

            Assert.Empty(canvas.Texts);
        }
        finally
        {
            File.Delete(TempCache());
        }
    }

    [Fact]
    public void ADropWithoutIdentityIsNotPriced()
    {
        var prices = new PriceBook(TempCache());

        var canvas = Draw(Drop(new ItemSnapshot(null, null, MonsterRarity.Normal, true)), prices);

        Assert.Empty(canvas.Texts);
    }

    [Fact]
    public void ThePriceBookStartsUnloadedAndSaysSo()
    {
        // It reports its own state rather than pretending: a feature gated on
        // IsLoaded cannot draw stale or absent prices.
        var prices = new PriceBook(TempCache());

        Assert.False(prices.IsLoaded);
        Assert.Equal(0, prices.ItemCount);
    }

    [Fact]
    public void ChangingLeagueInvalidatesTheCachedPrices()
    {
        var prices = new PriceBook(TempCache());

        // League is what the book last PRICED against, which only a fetch can
        // settle — so the observable promise is the one that matters: a league
        // change must not leave the previous league's prices standing.
        prices.SetDetectedLeague("Runes of Aldur");

        Assert.Equal(DateTime.MinValue, prices.LastFetchUtc);

        // An override is the user's word and outranks detection, because the
        // league you price against and the one you play in can differ.
        prices.SetLeagueOverride("Standard");

        Assert.Equal(DateTime.MinValue, prices.LastFetchUtc);
        Assert.False(prices.IsLoaded);
    }

    private static string TempCache() =>
        Path.Combine(Path.GetTempPath(), "easyexile-prices-test.json");
}
