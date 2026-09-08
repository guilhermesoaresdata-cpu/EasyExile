using EasyExile.Radar.Pricing;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Turning a mod id into "T1".
/// </summary>
/// <remarks>
/// The tier is in the mod's own name — the game calls them Strength1..Strength8
/// — so reading it needs no table. The table supplies only the ceiling, and the
/// ceiling is per item class: IncreasedLife has 8 tiers on a ring, 10 on a belt
/// and 13 on a body armour. That is the whole reason this is not a flat lookup.
/// </remarks>
public class ModTierTableTests
{
    private static ModTierTable Shipped => Table.Value;

    private static readonly Lazy<ModTierTable> Table =
        new(() => ModTierTable.Load(Path.Combine(AppContext.BaseDirectory, "modtiers.json")));

    // ---- the name alone ---------------------------------------------------

    [Theory]
    [InlineData("Strength1", 1)]
    [InlineData("IncreasedAccuracy3", 3)]
    [InlineData("UniqueManaRegeneration15", 15)]
    [InlineData("IncreasedMana10", 10)]
    [InlineData("ItemFoundRarityIncreasePrefix4_", 4)]
    [InlineData("WeaponElementalDamageOnTwohandWeapon2____", 2)]
    [InlineData("SomethingWithNoTier", 0)]
    [InlineData("", 0)]
    public void The_tier_is_the_number_the_game_put_in_the_name(string id, int tier)
    {
        Assert.Equal(tier, ModTierTable.TierOf(id));
    }

    [Theory]
    [InlineData("Strength8", "Strength")]
    [InlineData("LocalAddedFireDamageTwoHand8_", "LocalAddedFireDamageTwoHand")]
    [InlineData("IncreasedMana13", "IncreasedMana")]
    public void The_family_is_the_name_without_its_tier(string id, string family)
    {
        Assert.Equal(family, ModTierTable.FamilyOf(id));
    }

    // ---- the shipped table ------------------------------------------------

    [Fact]
    public void The_table_that_ships_covers_the_whole_item_set()
    {
        Assert.True(Shipped.IsLoaded, "modtiers.json nao foi copiado para a saida");
        Assert.True(Shipped.Classes >= 25, $"apenas {Shipped.Classes} classes");
        Assert.True(Shipped.Mods >= 500, $"apenas {Shipped.Mods} mods");
    }

    [Fact]
    public void A_ceiling_is_read_per_item_class()
    {
        // The reason the class matters at all, in one assertion.
        Assert.Equal(8, Shipped.Find("IncreasedLife8", "Ring")!.Value.Top);
        Assert.Equal(10, Shipped.Find("IncreasedLife8", "Belt")!.Value.Top);
        Assert.Equal(13, Shipped.Find("IncreasedLife8", "Body Armour")!.Value.Top);
    }

    [Fact]
    public void The_top_tier_of_a_family_reads_as_the_best_roll()
    {
        var best = Shipped.Find("IncreasedLife8", "Ring")!.Value;

        Assert.True(best.IsBest);
        Assert.True(best.IsExact);
        Assert.True(best.IsPrefix);
        Assert.Equal("T1 (8/8)", best.Label);
    }

    [Fact]
    public void A_middling_roll_says_how_far_off_it_is()
    {
        var middling = Shipped.Find("IncreasedLife4", "Body Armour")!.Value;

        Assert.False(middling.IsBest);
        Assert.Equal("T10 (4/13)", middling.Label);
    }

    [Fact]
    public void Prefix_and_suffix_come_from_the_table_rather_than_the_name()
    {
        Assert.True(Shipped.Find("IncreasedLife8", "Ring")!.Value.IsPrefix);
        Assert.False(Shipped.Find("FireResist8", "Ring")!.Value.IsPrefix);
    }

    [Fact]
    public void Without_the_class_a_family_that_agrees_everywhere_is_still_exact()
    {
        // FireResist is eight tiers on everything that has it, so not knowing
        // the item class costs nothing.
        var anywhere = Shipped.Find("FireResist8")!.Value;

        Assert.True(anywhere.IsExact);
        Assert.Equal(8, anywhere.Top);
        Assert.True(anywhere.IsBest);
    }

    [Fact]
    public void Without_the_class_a_family_that_disagrees_leans_on_the_higher_ceiling()
    {
        // IncreasedLife is 8 on a ring and 13 on a body armour. Not knowing
        // which, the answer takes 13: it under-claims rather than announcing a
        // T1 that might not be one. A missed alert costs a second look; a false
        // one costs trust.
        var unsure = Shipped.Find("IncreasedLife8")!.Value;

        Assert.False(unsure.IsExact);
        Assert.Equal(13, unsure.Top);
        Assert.False(unsure.IsBest);
    }

    [Fact]
    public void The_ids_read_off_the_live_client_resolve()
    {
        // Exactly what --item-mods printed from the open inventory. If the
        // table and the client ever stop agreeing on how a mod is named, this
        // is where it shows.
        foreach (var id in new[]
                 {
                     "IncreasedAccuracy3", "FireResist1", "ItemFoundRarityIncreasePrefix1",
                     "Intelligence1", "IncreasedMana2", "LightningResist2",
                     "ManaRegeneration1", "AddedColdDamage1",
                 })
        {
            Assert.True(Shipped.Find(id) is not null, $"{id} nao esta na tabela");
        }
    }

    [Fact]
    public void A_mod_the_table_never_heard_of_is_a_miss_rather_than_a_guess()
    {
        // Uniques carry their own families and a new league adds more. The tier
        // still reads off the name; only the ceiling is unavailable.
        Assert.Null(Shipped.Find("UniqueManaRegeneration15"));
        Assert.Equal(15, ModTierTable.TierOf("UniqueManaRegeneration15"));

        Assert.Null(Shipped.Find(null));
        Assert.Null(Shipped.Find(""));
    }

    [Fact]
    public void An_unknown_item_class_falls_back_rather_than_failing()
    {
        var guessed = Shipped.Find("FireResist8", "Nao Existe");

        Assert.NotNull(guessed);
        Assert.Equal(8, guessed!.Value.Top);
    }

    [Fact]
    public void A_missing_file_is_an_empty_table_and_not_a_crash()
    {
        var missing = ModTierTable.Load(
            Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.json"));

        Assert.False(missing.IsLoaded);
        Assert.Equal(0, missing.Mods);
        Assert.Null(missing.Find("Strength1"));
    }

    [Fact]
    public void Rubbish_in_the_file_is_an_empty_table_and_not_a_crash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"lixo-{Guid.NewGuid():N}.json");

        File.WriteAllText(path, "{ nao e json");

        try
        {
            Assert.False(ModTierTable.Load(path).IsLoaded);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
