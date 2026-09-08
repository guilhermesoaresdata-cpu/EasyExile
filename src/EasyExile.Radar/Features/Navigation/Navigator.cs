using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using NumVec2 = System.Numerics.Vector2;

namespace EasyExile.Radar.Features.Navigation;

/// <summary>
/// One route per selected target, kept current.
/// </summary>
/// <remarks>
/// Ported from POE2Radar (MIT), <c>RadarApp.MaintainRoutes</c> and
/// <c>ReconcileTrackers</c>. The split is the reference's and it is the whole
/// design: a cheap per-frame <c>Maintain</c> that only advances a cursor, and a
/// replan that fires on a trigger and runs on a worker. A* never touches the
/// render thread.
/// </remarks>
public sealed class Navigator : IDisposable
{
    /// <summary>Cap on simultaneous routes. The reference's, and the palette's size.</summary>
    public const int MaxRoutes = 8;

    private readonly BackgroundReplanner _replanner = new();
    private readonly Dictionary<string, RouteTracker> _trackers = new(StringComparer.Ordinal);
    private readonly List<string> _selected = new();

    private long _epoch = -1;

    private bool _seeded;

    /// <summary>Ids currently routed, in selection order — which is colour order.</summary>
    public IReadOnlyList<string> Selected => _selected;

    public bool IsSelected(string id) => _selected.Contains(id);

    public int RouteCount => _selected.Count;

    /// <summary>Adds or removes a target. Returns false when the cap is reached.</summary>
    public bool Toggle(string id)
    {
        if (_selected.Remove(id))
        {
            _trackers.Remove(id);

            return true;
        }

        if (_selected.Count >= MaxRoutes) return false;

        _selected.Add(id);

        return true;
    }

    public void Clear()
    {
        _selected.Clear();
        _trackers.Clear();
        _labels.Clear();
    }

    /// <summary>
    /// One frame of route maintenance. Cheap by construction: it advances
    /// cursors, drains whatever the worker finished, and enqueues a replan only
    /// when a trigger fires.
    /// </summary>
    public void Maintain(WorldSnapshot snapshot, Vector2 playerGrid) =>
        Maintain(snapshot, playerGrid, null);

    /// <inheritdoc cref="Maintain(WorldSnapshot, Vector2)"/>
    /// <param name="autoRoute">
    /// Asked, once per area, which entities their display rule opted into
    /// auto-routing. Null disables the behaviour.
    /// </param>
    public void Maintain(WorldSnapshot snapshot, Vector2 playerGrid, Func<EntitySnapshot, bool>? autoRoute)
    {
        // A new area invalidates every route: the terrain is different, the
        // entity ids are reallocated, and a landmark key belongs to the old map.
        // The FIRST snapshot is not a change — adopting it silently is what lets
        // a target be selected before the navigator has ever seen a world.
        if (snapshot.Epoch != _epoch)
        {
            var known = _epoch != -1;

            _epoch = snapshot.Epoch;

            if (known) Clear();

            _seeded = false;
        }

        // First arrival in this area: seed the selection from the rules that
        // asked for it, as the reference does on a zone's first visit.
        if (!_seeded && autoRoute is not null)
        {
            _seeded = true;

            foreach (var entity in snapshot.Entities)
            {
                if (_selected.Count >= MaxRoutes) break;

                if (autoRoute(entity)) Toggle(NavTarget.IdFor(entity));
            }
        }

        var player = new NumVec2(playerGrid.X, playerGrid.Y);

        // Trackers follow the selection: one per selected id, none for anything
        // that was deselected.
        foreach (var id in _selected)
        {
            if (!_trackers.ContainsKey(id)) _trackers[id] = new RouteTracker();
        }

        if (_replanner.TryDrainResults(out var finished))
        {
            foreach (var result in finished)
            {
                if (!_trackers.TryGetValue(result.TargetId, out var tracker)) continue;

                tracker.ApplyResult(result.Waypoints, new NumVec2(result.Goal.X, result.Goal.Y));
            }
        }

        if (snapshot.Terrain is not { IsEmpty: false } terrain) return;

        var start = ((int)MathF.Round(playerGrid.X), (int)MathF.Round(playerGrid.Y));

        foreach (var id in _selected)
        {
            var tracker = _trackers[id];

            tracker.Maintain(player);

            if (NavTargets.Name(id, snapshot) is { } name) _labels[id] = name;

            if (NavTargets.Resolve(id, snapshot) is not { } goal) continue;

            var target = new NumVec2(goal.X, goal.Y);

            if (tracker.ReplanInFlight || !tracker.ShouldReplan(player, target)) continue;

            tracker.MarkReplanRequested(player);

            _replanner.Enqueue(new BackgroundReplanner.Request(
                id, terrain, start, ((int)MathF.Round(goal.X), (int)MathF.Round(goal.Y))));
        }
    }

    /// <summary>
    /// What to draw: the un-walked waypoints of each route, with the colour slot
    /// its selection order earns it.
    /// </summary>
    public IEnumerable<(int Slot, IReadOnlyList<(int X, int Y)> Points)> Routes()
    {
        for (var i = 0; i < _selected.Count; i++)
        {
            if (!_trackers.TryGetValue(_selected[i], out var tracker)) continue;

            var points = tracker.CurrentPoints;

            if (points.Count > 0) yield return (i, points);
        }
    }

    /// <summary>
    /// What the route in this slot is heading to. Null until a snapshot has
    /// named it.
    /// </summary>
    public string? LabelOf(int slot) =>
        slot >= 0 && slot < _selected.Count && _labels.TryGetValue(_selected[slot], out var label)
            ? label
            : null;

    private readonly Dictionary<string, string> _labels = new(StringComparer.Ordinal);

    public void Dispose() => _replanner.Dispose();
}
