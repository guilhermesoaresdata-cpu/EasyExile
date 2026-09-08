using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Levelling;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// What each zone actually contained, written down for correcting the guide.
/// </summary>
/// <remarks>
/// The campaign file is written from a community walkthrough, which describes a
/// tendency. This records what the CLIENT reported in the zone the player was
/// really standing in, so a whole playthrough can be read back afterwards and
/// the advice checked against it.
/// </remarks>
public class CampaignJournalTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"easyexile-journal-{Guid.NewGuid():N}.txt");

    [Fact]
    public void A_zone_is_written_with_its_code_its_name_and_what_was_in_it()
    {
        var journal = new CampaignJournal(_path);

        journal.Record(BonePits(), "The Bone Pits");

        var text = File.ReadAllText(_path);

        Assert.Contains("zone G2_5_2 The Bone Pits", text);
        Assert.Contains("landmark Blackrib Pit  (lugar)", text);
        Assert.Contains("landmark Mastodon Badlands  (saida)", text);
        Assert.Contains("exit G2_5_1  Mastodon Badlands", text);
        Assert.Contains("unique Iktab", text);
    }

    [Fact]
    public void One_visit_writes_one_entry_however_many_captures_it_takes()
    {
        // Thirty captures a second inside one visit would bury the zone it is
        // meant to describe.
        var journal = new CampaignJournal(_path);

        for (var i = 0; i < 50; i++) journal.Record(BonePits(), "The Bone Pits");

        Assert.Equal(1, journal.Entries);
    }

    [Fact]
    public void Coming_back_to_a_zone_writes_it_again()
    {
        // The order of visits is exactly the thing a route is made of, so a
        // fourth trip to town is a fourth entry rather than a duplicate.
        var journal = new CampaignJournal(_path);

        journal.Record(BonePits(), "The Bone Pits");
        journal.Record(BonePits() with { Epoch = 2 }, "The Bone Pits");

        Assert.Equal(2, journal.Entries);
    }

    [Fact]
    public void The_file_only_grows()
    {
        // Append-only, so a crash costs the last zone rather than the run.
        var journal = new CampaignJournal(_path);

        journal.Record(BonePits(), "The Bone Pits");

        var first = new FileInfo(_path).Length;

        journal.Record(BonePits() with { Epoch = 2 }, "The Bone Pits");

        Assert.True(new FileInfo(_path).Length > first);
    }

    [Fact]
    public void An_area_the_client_could_not_name_is_skipped_rather_than_written_blank()
    {
        var journal = new CampaignJournal(_path);

        journal.Record(BonePits() with { AreaCode = null }, null);

        Assert.Equal(0, journal.Entries);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void A_finished_encounter_is_recorded_as_finished()
    {
        // Which icons the client had already crossed off is half of what makes
        // the record worth reading back.
        var journal = new CampaignJournal(_path);

        journal.Record(BonePits(), "The Bone Pits");

        Assert.Contains("marked Waypoint  (concluido)", File.ReadAllText(_path));
    }

    private static WorldSnapshot BonePits() =>
        RadarFixture.World(entities: [Exit(), Unique(), Done()]) with
        {
            Epoch = 1,
            AreaCode = "G2_5_2",
            Landmarks = ImmutableArray.Create(
                new LandmarkSnapshot("Blackrib Pit", "Metadata/Terrain/Pit", new Vector2(2369, 713), 81),
                new LandmarkSnapshot(
                    "Mastodon Badlands", "Metadata/Terrain/Exit", new Vector2(368, 1403), 81,
                    IsWayOut: true)),
        };

    private static EntitySnapshot Exit() =>
        RadarFixture.Entity(1, new Vector3(1, 0, 1), "Metadata/MiscellaneousObjects/AreaTransition") with
        {
            Kind = EntityKind.Transition,
            DestinationCode = "G2_5_1",
            FriendlyName = "Mastodon Badlands",
        };

    private static EntitySnapshot Unique() =>
        RadarFixture.Entity(2, new Vector3(2, 0, 2), "Metadata/Monsters/Hyena/Iktab") with
        {
            Kind = EntityKind.Monster,
            Rarity = MonsterRarity.Unique,
            FriendlyName = "Iktab",
        };

    private static EntitySnapshot Done() =>
        RadarFixture.Entity(3, new Vector3(3, 0, 3), "Metadata/MiscellaneousObjects/Waypoint") with
        {
            IsPoi = true,
            IconComplete = true,
            FriendlyName = "Waypoint",
        };

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }
}
