using EasyExile.Core.Snapshots;
using EasyExile.Core.World;
using EasyExile.Radar.Features.Navigation;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.General;
using EasyExile.Radar.UI;

namespace EasyExile.Radar.Features.Levelling;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// A levelling guide that routes instead of pointing.
/// </summary>
/// <remarks>
/// Every published guide for this game does the same thing: it tells you the
/// name of the next zone and roughly which way to walk — "exit is north-east",
/// "boss spawns left or right of the start". They have to, because they cannot
/// see your instance. The layouts are generated per character, so a guide can
/// only ever describe a tendency.
///
/// We can see the instance. The terrain grid, the exits and their destinations,
/// A* and a route renderer are already here, so "find the exit to Grelwood" does
/// not have to be a compass direction: it can be the actual path to the actual
/// door in the layout you actually got. That is the whole idea, and it is the
/// one thing none of the existing tools can do.
///
/// What this feature itself does is small on purpose. It reads which area you
/// are in, finds the step for it, picks the exit whose destination matches the
/// NEXT step, and hands that to the Navigator we already had. The drawing, the
/// pathfinding and the replanning are not re-implemented here.
/// </remarks>
public sealed class RouteGuideFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly Navigator _navigator;
    private readonly AreaGraph _graph;
    private readonly LevelRoute _route;
    private readonly string _recordedPath;
    private readonly CampaignGuide _campaign;
    private readonly CampaignGuide _english;
    private readonly CampaignJournal? _journal;

    private string? _area;
    private int _step = -1;
    private int _zone = -1;
    private string? _routedTo;
    private DateTimeOffset _savedAt = DateTimeOffset.MinValue;

    public RouteGuideFeature(
        RadarSettings settings, Navigator navigator, AreaGraph graph, LevelRoute route, string recordedPath,
        CampaignGuide? campaign = null, CampaignJournal? journal = null,
        CampaignGuide? english = null)
    {
        _settings = settings;
        _navigator = navigator;
        _graph = graph;
        _route = route;
        _recordedPath = recordedPath;
        _campaign = campaign ?? CampaignGuide.Empty;
        _english = english ?? CampaignGuide.Empty;
        _journal = journal;
    }

    /// <summary>The guide in the language the panel is speaking.</summary>
    /// <remarks>
    /// Chosen per read rather than at startup, because the language is a
    /// setting and the player can change it without restarting. The zone and
    /// step indices carry across because the two files are the same guide -
    /// same zones in the same order, same steps under each - which is a
    /// property a test enforces rather than one we are hoping for.
    /// </remarks>
    private CampaignGuide Campaign =>
        Text.Current == Language.English && _english.IsLoaded ? _english : _campaign;

    public string Name => T("Guia de leveling");

    public bool Enabled => _settings.Levelling.Enabled;

    /// <summary>Where the player is, named if the graph has learned it.</summary>
    public string Here => _area is { Length: > 0 } a ? _graph.Name(a) : "—";

    /// <summary>What to do next, and why we think so.</summary>
    public string Objective { get; private set; } = T("sem objetivo");

    /// <summary>Whether a route is actually being drawn to that objective.</summary>
    public bool Routing { get; private set; }

    /// <summary>The step being worked on, so the panel can mark it.</summary>
    public CampaignStep? Current { get; private set; }

    public int Step => _step + 1;

    public int Steps => _route.Steps.Count;

    /// <summary>Where the campaign guide thinks you are, if it recognises the zone.</summary>
    public CampaignZone? Zone => _zone >= 0 && _zone < Campaign.Count ? Campaign.Zones[_zone] : null;

    /// <summary>Which of the campaign's zones this is, one-based, and how many there are.</summary>
    public int ZoneNumber => _zone + 1;

    public int ZoneCount => Campaign.Count;

    /// <summary>Zones written to the journal this session.</summary>
    public int Journalled => _journal?.Entries ?? 0;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled || !frame.HasWorld) return;

        var snapshot = frame.Snapshot!;

        Learn(snapshot);

        if (snapshot.AreaCode is not { Length: > 0 } area) return;

        // The area changing is the only thing that advances a route. Nothing
        // else is reliable: a step is done when you are somewhere else.
        if (!string.Equals(area, _area, StringComparison.OrdinalIgnoreCase))
        {
            _area = area;
            _routedTo = null;

            if (_settings.Levelling.Recording && _route.Record(area))
                _route.Save(_recordedPath, _graph.Name);

            Advance(area);

            // By the client's INTERNAL code, never by the name it displays: the
            // code is the same in every language and this client is not in
            // English. Searched forward from where the guide already was, so a
            // three-floor manor under one code does not snap back to floor one.
            _zone = Campaign.IndexOf(area, _zone);

            // Written on the area change, once, because the interesting facts
            // are per-zone and re-writing them at capture rate would bury them.
            if (_settings.Levelling.Journal)
                _journal?.Record(snapshot, Zone?.Name ?? _graph.Name(area));
        }

        if (!_settings.Levelling.AutoRoute)
        {
            Objective = T("rota automatica desligada");
            Routing = false;
            Panel(frame, canvas);
            return;
        }

        // What to walk to, and why. An ENTITY, not an area code: a step that
        // says kill the zone boss has to draw a line to the boss, and only
        // looking for doors is how the guide did the easy half and stopped.
        var (id, why, step) = Aim(snapshot, area);

        Objective = why;
        Current = step;

        if (id is null)
        {
            Routing = false;
            Panel(frame, canvas);
            return;
        }

        // Re-selecting the same target every frame would clear and re-plan the
        // route continuously, which is a stutter, not navigation.
        if (!string.Equals(_routedTo, id, StringComparison.Ordinal))
        {
            _navigator.Clear();
            _navigator.Toggle(id);
            _routedTo = id;
        }

        Routing = true;

        Panel(frame, canvas);
    }

    /// <summary>
    /// The steps, on the game.
    /// </summary>
    /// <remarks>
    /// Drawn from every exit of Draw rather than only the successful one: the
    /// case that most needs a panel is the one where nothing is being routed and
    /// the player is looking for a reason why.
    /// </remarks>
    private void Panel(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!_settings.Levelling.ShowSteps || Zone is not { } zone) return;

        StepPanel.Draw(
            canvas, frame.ClientBounds, _settings.Levelling,
            zone, ZoneNumber, ZoneCount, Objective, Routing, Current);
    }

    /// <summary>
    /// What to walk to, why, and which step asked for it.
    /// </summary>
    /// <remarks>
    /// Driven by the campaign's steps in order, and it returns a THING rather
    /// than an area: the whole complaint was that a step saying "kill the zone
    /// boss" drew nothing, because only doors were ever looked for.
    ///
    /// A step is finished when the world says so, never by us ticking it off. An
    /// enter is done once that area has been visited; a kill, a pickup or a
    /// conversation is done once nothing in the room matches it any more — the
    /// rare is dead, the icon the client draws has faded. So the ladder simply
    /// walks the list and stops at the first step the world still answers.
    /// </remarks>
    private (string? To, string Why, CampaignStep? Step) Aim(WorldSnapshot snapshot, string area)
    {
        if (Zone is { } zone)
        {
            foreach (var step in zone.Steps)
            {
                if (step.Optional && !_settings.Levelling.ShowOptional) continue;

                if (step.Action == StepAction.Enter)
                {
                    if (step.Target is not { Length: > 0 } destination) continue;

                    // Been there: the step is behind us.
                    if (_graph.HasVisited(destination) &&
                        !string.Equals(destination, area, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (Door(snapshot, destination) is { } door)
                        return (NavTarget.IdFor(door), T("Ir para ") + step.Subject, step);

                    // The door may be zones back the way we came, so ask the
                    // graph we have actually walked for the first hop.
                    if (_graph.FirstHopTowards(area, destination) is { Length: > 0 } hop &&
                        Door(snapshot, hop) is { } back)
                        return (NavTarget.IdFor(back), T("Ir para ") + step.Subject + T(" - via ") + _graph.Name(hop), step);

                    // No door in sight at all. The map already knows where the
                    // way out of this zone is — it is a named tile cluster the
                    // client marks as a way out — so head for that.
                    if (StepAim.Exit(snapshot) is { } out_)
                        return (out_.Id, T("Ir para ") + step.Subject + T(" - saida: ") + out_.Label, step);

                    continue;
                }

                if (StepAim.For(step, snapshot) is { } thing)
                    return (thing.Id, Verb(step) + thing.Label, step);
            }

            // Every step here is answered. The next zone is the objective.
            if (Campaign.NextAfter(_zone) is { } next && next.Code is { Length: > 0 } code)
            {
                if (Door(snapshot, code) is { } onward)
                    return (NavTarget.IdFor(onward), T("Ir para ") + next.Name, null);

                if (_graph.FirstHopTowards(area, code) is { Length: > 0 } hop &&
                    Door(snapshot, hop) is { } through)
                    return (NavTarget.IdFor(through), T("Ir para ") + next.Name + T(" - via ") + _graph.Name(hop), null);

                if (StepAim.Exit(snapshot) is { } away)
                    return (away.Id, T("Ir para ") + next.Name + T(" - saida: ") + away.Label, null);

                return (null, T("Proximo: ") + next.Name + T(" - caminho ainda desconhecido"), null);
            }
        }

        // Outside the campaign — a hideout, a map, the endgame — the question
        // still has an answer: a door nobody has been through, or something the
        // client still marks as unfinished.
        var unvisited = snapshot.Entities.FirstOrDefault(e =>
            e.DestinationCode is { Length: > 0 } code && !_graph.HasVisited(code));

        if (unvisited is not null)
            return (NavTarget.IdFor(unvisited), T("Explorar: ") + _graph.Name(unvisited.DestinationCode!), null);

        var marked = snapshot.Entities.FirstOrDefault(e => e is { IsPoi: true, IconComplete: false });

        if (marked is not null) return (NavTarget.IdFor(marked), T("Aqui: ") + Label(marked), null);

        return (null, "nada marcado aqui", null);
    }

    /// <summary>A door here leading to that area, if one has spawned.</summary>
    private static EntitySnapshot? Door(WorldSnapshot snapshot, string destination) =>
        snapshot.Entities.FirstOrDefault(e =>
            string.Equals(e.DestinationCode, destination, StringComparison.OrdinalIgnoreCase));

    private static string Verb(CampaignStep step) => step.Action switch
    {
        StepAction.Kill => T("Matar: "),
        StepAction.Take => T("Pegar: "),
        StepAction.Talk => T("Falar: "),
        StepAction.Waypoint => "Waypoint: ",
        _ => T("Ir: "),
    };

    /// <summary>The same name the map prints, so the route and the marker agree.</summary>
    private static string Label(EntitySnapshot entity) =>
        entity.FriendlyName is { Length: > 0 } name
            ? name
            : NativeMap.EntityLabels.Pretty(entity.Metadata);

    /// <summary>
    /// Finds where in the route this area is.
    /// </summary>
    /// <remarks>
    /// Searched forward from the current position rather than from the start, so
    /// a route that visits a town three times resumes at the third visit rather
    /// than snapping back to the first. Falling back to a full search is what
    /// makes a portal or a wrong turn recoverable.
    /// </remarks>
    private void Advance(string area)
    {
        for (var i = _step + 1; i < _route.Steps.Count; i++)
        {
            if (Matches(_route.Steps[i], area))
            {
                _step = i;
                return;
            }
        }

        for (var i = 0; i < _route.Steps.Count; i++)
        {
            if (Matches(_route.Steps[i], area))
            {
                _step = i;
                return;
            }
        }
    }

    private static bool Matches(RouteStep step, string area) =>
        step.Kind == StepKind.Enter &&
        string.Equals(step.Target, area, StringComparison.OrdinalIgnoreCase);

    private void Learn(WorldSnapshot snapshot)
    {
        _graph.Observe(
            snapshot.AreaCode,
            snapshot.Entities
                .Where(e => e.DestinationCode is { Length: > 0 })
                .Select(e => (e.DestinationCode!, e.FriendlyName)));

        // The graph grows a handful of entries per area, so writing it on a
        // timer costs nothing and losing an act's worth of learning to a crash
        // would cost an act.
        if (DateTimeOffset.UtcNow - _savedAt < TimeSpan.FromSeconds(10)) return;

        _savedAt = DateTimeOffset.UtcNow;
        _graph.SaveIfChanged();
    }
}
