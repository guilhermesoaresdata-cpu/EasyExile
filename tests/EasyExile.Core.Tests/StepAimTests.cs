using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Levelling;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// Where a step points, when the thing it names has not spawned.
/// </summary>
/// <remarks>
/// "Eu sei que voce nao sabe exatamente onde esta o boss, mas temos no mapa
/// registrado onde ele provavelmente esta." The map already had it. Live, in The
/// Bone Pits, the tool had found two landmarks and only two:
///
///     Blackrib Pit         (2369,713)   saida=False
///     Mastodon Badlands    (368,1403)   saida=True
///
/// One is the arena and one is the exit, and the client says which by marking
/// the exit as a way out. So no walkthrough prose has to be parsed to know where
/// a boss probably is.
/// </remarks>
public class StepAimTests
{
    [Fact]
    public void A_kill_with_no_monster_in_sight_heads_for_the_pit()
    {
        var (_, label) = Aim(StepAction.Kill, BonePits())!.Value;

        Assert.Equal("Blackrib Pit", label);
    }

    [Fact]
    public void A_kill_never_aims_at_a_waypoint()
    {
        // The bug this was found by: no rare in sight, so it fell through to the
        // nearest unfinished icon, and that is the waypoint you walked in past.
        // The panel said "Matar: Waypoint".
        var world = BonePits() with { Landmarks = ImmutableArray<LandmarkSnapshot>.Empty };

        Assert.Null(Aim(StepAction.Kill, world));
    }

    [Fact]
    public void A_yellow_rare_is_never_the_boss()
    {
        // "Ele guia para monstros amarelos, isso nao pode nem tem pq isso" — a
        // rare is a pack leader and there are dozens per zone, so the line kept
        // being redrawn at whatever trash had just spawned.
        var world = BonePits() with
        {
            Entities = BonePits().Entities.Add(
                Rare(new Vector2(120, 120)) with { Rarity = MonsterRarity.Rare }),
        };

        Assert.Equal("Blackrib Pit", Aim(StepAction.Kill, world)!.Value.Label);
    }

    [Fact]
    public void A_live_unique_outranks_the_pit_it_is_standing_in()
    {
        var world = BonePits() with
        {
            Entities = BonePits().Entities.Add(Rare(new Vector2(120, 120))),
        };

        var (_, label) = Aim(StepAction.Kill, world)!.Value;

        Assert.Equal("Iktab", label);
    }

    [Fact]
    public void A_dead_rare_is_not_a_target()
    {
        // A corpse still has its rarity. Routing to one is how a finished fight
        // keeps a line drawn to it.
        var world = BonePits() with
        {
            Entities = BonePits().Entities.Add(Rare(new Vector2(120, 120)) with { IsAlive = false }),
        };

        Assert.Equal("Blackrib Pit", Aim(StepAction.Kill, world)!.Value.Label);
    }

    [Fact]
    public void The_nearest_of_two_rares_wins_so_the_route_does_not_flip()
    {
        // Enumeration order is the client's allocation order, so first-found
        // would swap between two equally valid targets as they load.
        var world = BonePits() with
        {
            Entities = BonePits().Entities
                .Add(Rare(new Vector2(900, 900)) with { FriendlyName = "Ekbab" })
                .Add(Rare(new Vector2(120, 120))),
        };

        Assert.Equal("Iktab", Aim(StepAction.Kill, world)!.Value.Label);
    }

    [Fact]
    public void A_waypoint_step_is_the_one_place_a_waypoint_is_right()
    {
        Assert.Equal("Waypoint", Aim(StepAction.Waypoint, BonePits())!.Value.Label);
    }

    [Fact]
    public void The_way_out_is_the_landmark_the_client_marks_as_one()
    {
        var (_, label) = StepAim.Exit(BonePits())!.Value;

        Assert.Equal("Mastodon Badlands", label);
    }

    [Fact]
    public void A_zone_with_no_landmarks_and_nothing_marked_aims_at_nothing()
    {
        // Silence beats a confident wrong line.
        var empty = BonePits() with
        {
            Landmarks = ImmutableArray<LandmarkSnapshot>.Empty,
            Entities = ImmutableArray<EntitySnapshot>.Empty,
        };

        Assert.Null(Aim(StepAction.Kill, empty));
        Assert.Null(StepAim.Exit(empty));
    }

    // ---- fixtures --------------------------------------------------------

    private static (string Id, string Label)? Aim(StepAction action, WorldSnapshot world) =>
        StepAim.For(new CampaignStep(action, "o boss", null, false, null), world);

    /// <summary>The zone as the client actually reported it.</summary>
    private static WorldSnapshot BonePits() =>
        RadarFixture.World(entities: Waypoint()) with
        {
            Landmarks = ImmutableArray.Create(
                new LandmarkSnapshot("Blackrib Pit", "Metadata/Terrain/Pit", new Vector2(2369, 713), 81),
                new LandmarkSnapshot(
                    "Mastodon Badlands", "Metadata/Terrain/Exit", new Vector2(368, 1403), 81,
                    IsWayOut: true)),
        };

    /// <summary>An unfinished icon that is furniture, not an objective.</summary>
    private static EntitySnapshot Waypoint() =>
        RadarFixture.Entity(7, new Vector3(10, 0, 10), "Metadata/MiscellaneousObjects/Waypoint") with
        {
            IsPoi = true,
            IconComplete = false,
            FriendlyName = "Waypoint",
        };

    private static EntitySnapshot Rare(Vector2 grid) =>
        RadarFixture.Entity(8, new Vector3(grid.X, 0, grid.Y), "Metadata/Monsters/Hyena/Iktab") with
        {
            Kind = EntityKind.Monster,
            Rarity = MonsterRarity.Unique,
            IsAlive = true,
            FriendlyName = "Iktab",
        };
}
