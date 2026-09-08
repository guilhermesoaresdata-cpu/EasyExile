using EasyExile.Radar.Features.Levelling;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Getting out of a dead end.
/// </summary>
/// <remarks>
/// The campaign's zone list is a VISITING order, not a map, and reading it as
/// though consecutive entries shared a door was wrong in a way that only shows
/// up in the pockets. Live, in The Bone Pits: it connects to Mastodon Badlands
/// and to nothing else, while the zone listed after it — Valley of the Titans —
/// is reached from the town waypoint. The guide stood there waiting for a door
/// that was never going to appear, which is "estou em um local sem rota
/// nenhuma".
/// </remarks>
public class AreaGraphRoutingTests
{
    /// <summary>The corner of Act 2 this was found in, as the client reported it.</summary>
    private static AreaGraph Act2()
    {
        var path = Path.Combine(Path.GetTempPath(), $"easyexile-areas-{Guid.NewGuid():N}.txt");

        File.WriteAllText(path, """
            G2_5_2=The Bone Pits
            G2_5_1=Mastodon Badlands
            G2_town=The Ardura Caravan
            G2_6=Valley of the Titans
            G2_5_2>G2_5_1
            G2_5_1>G2_5_2
            G2_5_1>G2_town
            G2_town>G2_5_1
            G2_town>G2_6
            !G2_5_2
            !G2_5_1
            !G2_town
            """);

        try
        {
            return new AreaGraph(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_way_out_of_a_pocket_is_the_door_back()
    {
        // Two zones away, and the only thing the player can act on is the first
        // door. Answering with the destination instead would name a place with
        // no exit leading to it from here.
        Assert.Equal("G2_5_1", Act2().FirstHopTowards("G2_5_2", "G2_6"));
    }

    [Fact]
    public void An_adjacent_target_is_its_own_first_hop()
    {
        Assert.Equal("G2_6", Act2().FirstHopTowards("G2_town", "G2_6"));
    }

    [Fact]
    public void The_fewest_zone_changes_wins_over_the_first_branch_tried()
    {
        // Breadth-first, not depth-first: from town, Mastodon Badlands is one
        // hop even though the town's exits also reach it the long way round.
        Assert.Equal("G2_5_1", Act2().FirstHopTowards("G2_town", "G2_5_2"));
    }

    [Fact]
    public void A_target_the_graph_has_never_seen_has_no_path_rather_than_a_guess()
    {
        Assert.Null(Act2().FirstHopTowards("G2_5_2", "G3_1"));
    }

    [Fact]
    public void Standing_on_the_target_is_not_a_journey()
    {
        Assert.Null(Act2().FirstHopTowards("G2_5_2", "G2_5_2"));
    }

    [Fact]
    public void A_cycle_does_not_hang_the_walk()
    {
        // The graph is read from a file we did not write, so it can say anything.
        var path = Path.Combine(Path.GetTempPath(), $"easyexile-cycle-{Guid.NewGuid():N}.txt");

        File.WriteAllText(path, "A>B\nB>C\nC>A\n");

        try
        {
            Assert.Null(new AreaGraph(path).FirstHopTowards("A", "Z"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void A_zone_name_resolves_back_to_its_area_code()
    {
        // The join the whole guide rests on, in the direction the routing needs
        // it: the campaign speaks names, the graph plans in codes.
        var graph = Act2();

        Assert.Equal("G2_6", graph.CodeFor("Valley of the Titans"));
        Assert.Equal("G2_5_2", graph.CodeFor("the bone pits"));
        Assert.Null(graph.CodeFor("Somewhere Else"));
        Assert.Null(graph.CodeFor(null));
    }
}
