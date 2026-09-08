using EasyExile.Radar.Features.Levelling;
using Xunit;

namespace EasyExile.Core.Tests;

/// <summary>
/// The campaign as typed actions, keyed by the client's internal area code.
/// </summary>
/// <remarks>
/// Two things had to change before the guide could send anyone anywhere.
///
/// It followed a route recorded from the player's own walking, so the route
/// always ended exactly where the player was standing and there was never a
/// next step. And when a campaign order was added, it was a list of ZONES —
/// which assumes the zone listed after this one is reachable through a door
/// from it. Often it is not: The Bone Pits is a pocket off Mastodon Badlands
/// and you leave it by waypoint, so the guide stood in the pocket waiting.
///
/// The shape here is exile-leveling's (MIT): a linear sequence of actions,
/// several per zone, with entering and waypointing as different things.
/// </remarks>
public class CampaignGuideTests
{
    private static CampaignGuide Guide()
    {
        var path = Path.Combine(Path.GetTempPath(), $"easyexile-campaign-{Guid.NewGuid():N}.txt");

        File.WriteAllText(path, """
            # a comment, and a blank line follow

            act 1 Act One
            zone G1_2 Clearfell
            kill Beira of the Rotten Pack | Boss is north of the waypoint
            enter G1_4 The Grelwood
            take? Mysterious Campsite
            zone? G1_3 Mud Burrow
            kill? The Devourer | speedrunners skip this
            zone G1_4 The Grelwood
            talk the quest NPC
            note Return to town when done
            act 2 Act Two
            zone G2_5_2 The Bone Pits
            kill the zone boss and loot the horn
            waypoint | Complete quest turn-ins
            zone - Somewhere Unmapped
            note Nothing is known about this place
            """);

        try
        {
            return CampaignGuide.Load(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Zones_keep_their_order_their_act_and_their_code()
    {
        var guide = Guide();

        Assert.True(guide.IsLoaded);
        Assert.Equal(5, guide.Count);
        Assert.Equal(["G1_2", "G1_3", "G1_4", "G2_5_2", null], guide.Zones.Select(z => z.Code));
        Assert.Equal("Act One", guide.Zones[0].Act);
        Assert.Equal("Act Two", guide.Zones[3].Act);
    }

    [Theory]
    [InlineData(0, 0, StepAction.Kill, "Beira of the Rotten Pack")]
    [InlineData(0, 1, StepAction.Enter, "The Grelwood")]
    [InlineData(0, 2, StepAction.Take, "Mysterious Campsite")]
    [InlineData(2, 0, StepAction.Talk, "the quest NPC")]
    [InlineData(2, 1, StepAction.Note, "Return to town when done")]
    public void Each_verb_becomes_the_action_it_names(int zone, int step, StepAction action, string subject)
    {
        var read = Guide().Zones[zone].Steps[step];

        Assert.Equal(action, read.Action);
        Assert.Equal(subject, read.Subject);
    }

    [Fact]
    public void Entering_carries_the_area_code_of_the_door_it_wants()
    {
        // The whole point of keying on codes: the routing plans in codes, and
        // the client reports the same code whatever language it is running in.
        var step = Guide().Zones[0].Steps[1];

        Assert.Equal(StepAction.Enter, step.Action);
        Assert.Equal("G1_4", step.Target);
    }

    [Fact]
    public void Waypointing_is_not_entering()
    {
        // The distinction the zone-list version did not have, and the reason it
        // stood in a dead end: you leave The Bone Pits by waypoint, not by door.
        var step = Guide().Zones[3].Steps[1];

        Assert.Equal(StepAction.Waypoint, step.Action);
        Assert.Null(step.Target);
        Assert.Equal("Complete quest turn-ins", step.Hint);
    }

    [Fact]
    public void A_trailing_question_mark_marks_a_step_optional()
    {
        var zone = Guide().Zones[0];

        Assert.False(zone.Steps[0].Optional);
        Assert.True(zone.Steps[2].Optional);
    }

    [Fact]
    public void A_hint_is_kept_apart_from_the_step_it_explains()
    {
        var step = Guide().Zones[0].Steps[0];

        Assert.Equal("Beira of the Rotten Pack", step.Subject);
        Assert.Equal("Boss is north of the waypoint", step.Hint);
    }

    [Fact]
    public void An_area_is_found_by_its_code()
    {
        var guide = Guide();

        Assert.Equal(0, guide.IndexOf("G1_2"));
        Assert.Equal(3, guide.IndexOf("g2_5_2"));
        Assert.Equal(-1, guide.IndexOf("HideoutLimestone"));
        Assert.Equal(-1, guide.IndexOf(null));
        Assert.Equal(-1, guide.IndexOf(""));
    }

    [Fact]
    public void The_search_runs_forward_so_a_revisit_does_not_snap_backwards()
    {
        // Ogham Manor is three floors under one area code. Searching from the
        // start every time would send someone leaving the third floor back to
        // the first.
        var guide = Guide();

        Assert.Equal(0, guide.IndexOf("G1_2", from: 0));
        Assert.Equal(0, guide.IndexOf("G1_2", from: 3));
    }

    [Fact]
    public void An_optional_zone_is_never_the_next_place_to_go()
    {
        var guide = Guide();

        Assert.True(guide.Zones[1].Optional);
        Assert.Equal("The Grelwood", guide.NextAfter(0)?.Name);
    }

    [Fact]
    public void A_zone_with_no_known_code_still_loads_rather_than_being_dropped()
    {
        // Kingsmarch has no code in any table we have. Losing the zone would
        // lose its steps too, and the steps are still worth reading.
        var last = Guide().Zones[^1];

        Assert.Null(last.Code);
        Assert.Equal("Somewhere Unmapped", last.Name);
        Assert.Single(last.Steps);
    }

    [Fact]
    public void A_missing_file_is_an_empty_guide_and_not_a_crash()
    {
        var guide = CampaignGuide.Load(
            Path.Combine(Path.GetTempPath(), $"nao-existe-{Guid.NewGuid():N}.txt"));

        Assert.False(guide.IsLoaded);
        Assert.Equal(0, guide.Count);
        Assert.Equal(-1, guide.IndexOf("G1_2"));
        Assert.Null(guide.NextAfter(-1));
    }

    [Fact]
    public void The_campaign_that_ships_covers_the_whole_game()
    {
        // The real file, as it will be read at run time. A guide that silently
        // shipped empty would look exactly like the bug it was written to fix.
        var shipped = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.txt"));

        Assert.True(shipped.IsLoaded, "campaign.txt nao foi copiado para a saida");
        Assert.True(shipped.Count >= 60, $"apenas {shipped.Count} zonas");

        // Almost every zone resolved to a code. The few that did not are named
        // in the generator's output and still carry their steps.
        var coded = shipped.Zones.Count(z => z.Code is { Length: > 0 });

        Assert.True(coded >= 60, $"apenas {coded} zonas com codigo");

        Assert.Equal(0, shipped.IndexOf("G1_2"));

        // The zone the player was standing in when this was diagnosed, and the
        // step that answers "estou em um local sem rota nenhuma": you leave by
        // waypoint, which the zone-list version had no way to say.
        var bonePits = shipped.Zones[shipped.IndexOf("G2_5_2")];

        Assert.Equal("The Bone Pits", bonePits.Name);
        Assert.Contains(bonePits.Steps, s => s.Action == StepAction.Waypoint);
        Assert.Contains(bonePits.Steps, s => s.Action == StepAction.Kill);
    }

    [Fact]
    public void The_english_campaign_is_the_same_guide_in_another_language()
    {
        // The overlay picks the file by language but keeps the zone and step
        // indices it already had, because switching language mid-act must not
        // move the player's place in the guide. That only holds while the two
        // files describe the same journey, so this is where it is made to hold:
        // same zones in the same order, same codes, same number of steps under
        // each, and the same action on each step.
        var ptBr = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.txt"));
        var english = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.en.txt"));

        Assert.True(english.IsLoaded, "campaign.en.txt nao foi copiado para a saida");
        Assert.Equal(ptBr.Count, english.Count);

        for (var i = 0; i < ptBr.Count; i++)
        {
            var (a, b) = (ptBr.Zones[i], english.Zones[i]);

            Assert.Equal(a.Code, b.Code);
            Assert.Equal(a.Steps.Count, b.Steps.Count);

            for (var s = 0; s < a.Steps.Count; s++)
            {
                Assert.Equal(a.Steps[s].Action, b.Steps[s].Action);
                Assert.Equal(a.Steps[s].Optional, b.Steps[s].Optional);
                Assert.Equal(a.Steps[s].Target, b.Steps[s].Target);
            }
        }
    }

    [Fact]
    public void The_english_campaign_actually_says_something_in_english()
    {
        // Copying the Portuguese across would satisfy every structural check
        // above and leave the player reading Portuguese with English selected.
        var ptBr = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.txt"));
        var english = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.en.txt"));

        var identical = 0;
        var total = 0;

        foreach (var (a, b) in ptBr.Zones.Zip(english.Zones))
            foreach (var (x, y) in a.Steps.Zip(b.Steps))
            {
                var left = x.Subject + " | " + x.Hint;
                var right = y.Subject + " | " + y.Hint;

                if (left.Trim() is not { Length: > 3 }) continue;

                total++;

                if (string.Equals(left, right, StringComparison.Ordinal)) identical++;
            }

        // Some steps are legitimately identical - proper names, "waypoint" -
        // but most of two hundred being so would mean nobody translated them.
        Assert.True(
            identical * 4 < total,
            $"{identical} de {total} passos iguais nos dois idiomas");
    }

    [Fact]
    public void Every_shipped_enter_step_that_names_a_code_names_a_zone_that_exists()
    {
        // A door pointing at a code no zone declares would route someone at a
        // place the guide cannot then recognise them as having reached.
        var shipped = CampaignGuide.Load(Path.Combine(AppContext.BaseDirectory, "campaign.txt"));

        var known = shipped.Zones
            .Where(z => z.Code is { Length: > 0 })
            .Select(z => z.Code!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var dangling = shipped.Zones
            .SelectMany(z => z.Steps)
            .Where(s => s.Action == StepAction.Enter && s.Target is { Length: > 0 })
            .Select(s => s.Target!)
            .Where(t => !known.Contains(t))
            .Distinct()
            .ToArray();

        Assert.True(dangling.Length == 0, $"portas para zonas que a campanha nao declara: {string.Join(", ", dangling)}");
    }
}
