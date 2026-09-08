using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings.NativeMap;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Choosing what the map is coloured with.
/// </summary>
/// <remarks>
/// Rank is the marker on this map — at map scale a ring around a three-pixel dot
/// is mud — so the colour is the whole of what a dot says. Which makes it the
/// one thing worth being able to change: the palette that reads on a desert
/// tileset is not the one that reads in a crypt, and nobody else can judge that
/// from here.
/// </remarks>
public class MapColourTests
{
    [Fact]
    public void Each_rank_is_drawn_in_the_colour_it_was_given()
    {
        var options = new NativeMapSettings
        {
            MonsterNormalColour = unchecked((int)0xFF112233),
            MonsterMagicColour = unchecked((int)0xFF445566),
            MonsterRareColour = unchecked((int)0xFF778899),
            MonsterUniqueColour = unchecked((int)0xFFAABBCC),
        };

        Assert.Equal(0xFF112233u, ColourOf(MonsterRarity.Normal, options));
        Assert.Equal(0xFF445566u, ColourOf(MonsterRarity.Magic, options));
        Assert.Equal(0xFF778899u, ColourOf(MonsterRarity.Rare, options));
        Assert.Equal(0xFFAABBCCu, ColourOf(MonsterRarity.Unique, options));
    }

    [Fact]
    public void An_unranked_monster_borrows_the_ordinary_colour()
    {
        // It reads as normal rather than being promoted to a rank the client
        // never claimed, and that has to include the colour.
        var options = new NativeMapSettings { MonsterNormalColour = unchecked((int)0xFF010203) };

        Assert.Equal(0xFF010203u, ColourOf(MonsterRarity.Unknown, options));
    }

    [Fact]
    public void Minions_are_drawn_in_the_colour_they_were_given()
    {
        var options = new NativeMapSettings { AllyColour = unchecked((int)0xFF00FF7F) };

        var ally = RadarFixture.Entity(0, new Vector3(0, 0, 0)) with
        {
            Kind = EntityKind.Ally,
            IsAlive = true,
        };

        Assert.Equal(0xFF00FF7Fu, new DisplayRules(options).Resolve(ally)!.Colour);
    }

    [Fact]
    public void A_colour_change_rebuilds_the_ruleset()
    {
        // The subtle one. The ruleset is compiled once and rebuilt only when
        // this value changes, so a colour left out of the signature would be a
        // setting the HUD accepts and the map ignores until something else
        // happened to change.
        var before = DisplayRules.SignatureFor(new NativeMapSettings());

        Assert.NotEqual(before, DisplayRules.SignatureFor(
            new NativeMapSettings { MonsterUniqueColour = unchecked((int)0xFF123456) }));

        Assert.NotEqual(before, DisplayRules.SignatureFor(
            new NativeMapSettings { AllyColour = unchecked((int)0xFF123456) }));

        Assert.NotEqual(before, DisplayRules.SignatureFor(
            new NativeMapSettings { PlayerColour = unchecked((int)0xFF123456) }));
    }

    [Fact]
    public void Out_of_the_box_the_map_looks_exactly_as_it_did()
    {
        // Every default is the constant the map drew with before any of this
        // was configurable. A fresh install must not be restyled by the arrival
        // of the ability to restyle it.
        var options = new NativeMapSettings();

        Assert.Equal(Palette.Monster, ColourOf(MonsterRarity.Normal, options));
        Assert.Equal(Palette.Magic, ColourOf(MonsterRarity.Magic, options));
        Assert.Equal(Palette.Rare, ColourOf(MonsterRarity.Rare, options));
        Assert.Equal(Palette.Unique, ColourOf(MonsterRarity.Unique, options));

        Assert.Equal(Palette.Ally, unchecked((uint)options.AllyColour));
        Assert.Equal(Palette.Player, unchecked((uint)options.PlayerColour));
    }

    private static uint ColourOf(MonsterRarity rarity, NativeMapSettings options)
    {
        var monster = RadarFixture.Entity(0, new Vector3(0, 0, 0), "Metadata/Monsters/Abyss/Void") with
        {
            Kind = EntityKind.Monster,
            Rarity = rarity,
            IsAlive = true,
            Mods = ImmutableArray<string>.Empty,
        };

        return new DisplayRules(options).Resolve(monster)!.Colour;
    }
}
