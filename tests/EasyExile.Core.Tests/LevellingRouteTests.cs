using EasyExile.Radar.Features.Levelling;

namespace EasyExile.Core.Tests;

/// <summary>
/// The route file and the learned graph.
/// </summary>
public sealed class LevellingRouteTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "easyexile-route-" + Guid.NewGuid().ToString("N"));

    public LevellingRouteTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string File(string name) => Path.Combine(_dir, name);

    [Fact]
    public void AStepIsAVerbAndATarget()
    {
        var path = File("route.txt");
        System.IO.File.WriteAllLines(path, new[]
        {
            "# a comment",
            "enter G1_2   # Clearfell",
            "waypoint",
            "kill Beira",
        });

        var route = LevelRoute.Load(path);

        Assert.Equal(3, route.Steps.Count);
        Assert.Equal(StepKind.Enter, route.Steps[0].Kind);
        Assert.Equal("G1_2", route.Steps[0].Target);
        Assert.Equal("Clearfell", route.Steps[0].Note);
        Assert.Equal(StepKind.Waypoint, route.Steps[1].Kind);
        Assert.Equal(StepKind.Note, route.Steps[2].Kind);
    }

    [Fact]
    public void RecordingDoesNotRepeatTheAreaYouAreAlreadyIn()
    {
        // Going back for a waypoint, dying, taking a portal — all of these
        // re-enter the area you just left. Recording them would turn a route
        // into a transcript of someone's mistakes.
        var route = new LevelRoute();

        Assert.True(route.Record("G1_town"));
        Assert.False(route.Record("G1_town"));
        Assert.True(route.Record("G1_2"));
        Assert.True(route.Record("G1_town"));

        Assert.Equal(3, route.Steps.Count);
    }

    [Fact]
    public void TheGraphLearnsNamesFromTheExitsThatNameThem()
    {
        // An area never names itself: the client only spells a zone out on the
        // far side of a door. So a code first seen underfoot stays nameless
        // until some other area's exit supplies the name.
        var graph = new AreaGraph(File("areas.txt"));

        graph.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });

        Assert.Equal("Clearfell", graph.Name("G1_2"));
        Assert.Equal("G1_town", graph.Name("G1_town"));
        Assert.Contains("G1_2", graph.ExitsFrom("G1_town"));
    }

    [Fact]
    public void ALearnedNameIsNeverOverwrittenByAnEmptyOne()
    {
        // The same code arrives both ways: named, from an exit that leads to it,
        // and nameless, from standing in it. Order of arrival must not decide
        // whether the user sees "Clearfell" or "G1_2".
        var graph = new AreaGraph(File("areas.txt"));

        graph.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });
        graph.Observe("G1_2", Array.Empty<(string, string?)>());
        graph.Observe("G1_2", new[] { ("G1_town", (string?)null) });

        Assert.Equal("Clearfell", graph.Name("G1_2"));
    }

    [Fact]
    public void TheGraphSurvivesARestart()
    {
        var path = File("areas.txt");

        var first = new AreaGraph(path);
        first.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });
        first.SaveIfChanged();

        var second = new AreaGraph(path);

        Assert.Equal("Clearfell", second.Name("G1_2"));
        Assert.Contains("G1_2", second.ExitsFrom("G1_town"));
    }

    [Fact]
    public void ARecordedRouteReloadsAsTheSameRoute()
    {
        var path = File("route.txt");
        var graph = new AreaGraph(File("areas.txt"));

        graph.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });

        var route = new LevelRoute();
        route.Record("G1_town");
        route.Record("G1_2");
        route.Save(path, graph.Name);

        var reloaded = LevelRoute.Load(path);

        Assert.Equal(2, reloaded.Steps.Count);
        Assert.Equal("G1_town", reloaded.Steps[0].Target);
        Assert.Equal("G1_2", reloaded.Steps[1].Target);

        // The name rides along as a comment so the file reads to a human, and
        // is re-derived on load rather than trusted.
        Assert.Equal("Clearfell", reloaded.Steps[1].Note);
    }

    [Fact]
    public void BeingNamedByADoorIsNotTheSameAsHavingBeenThere()
    {
        // The difference is the whole point of an exploration hint: an area you
        // have only seen named from the far side of a doorway is exactly the
        // area worth walking to.
        var graph = new AreaGraph(File("areas.txt"));

        graph.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });

        Assert.True(graph.HasVisited("G1_town"));
        Assert.False(graph.HasVisited("G1_2"));
        Assert.Equal("Clearfell", graph.Name("G1_2"));

        graph.Observe("G1_2", Array.Empty<(string, string?)>());

        Assert.True(graph.HasVisited("G1_2"));
    }

    [Fact]
    public void WhatHasBeenVisitedSurvivesARestart()
    {
        var path = File("areas.txt");

        var first = new AreaGraph(path);
        first.Observe("G1_town", new[] { ("G1_2", (string?)"Clearfell") });
        first.SaveIfChanged();

        var second = new AreaGraph(path);

        Assert.True(second.HasVisited("G1_town"));
        Assert.False(second.HasVisited("G1_2"));
        Assert.Equal(1, second.VisitedCount);
    }
}
