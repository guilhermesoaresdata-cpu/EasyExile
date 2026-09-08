using System.Diagnostics;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Runtime;

/// <summary>
/// Asks the Core for a snapshot on a fixed cadence and publishes the newest one.
/// </summary>
/// <remarks>
/// This is the whole reason the Core stays pull-based and thread-free: the
/// policy about when to read lives here, in the consumer, where the cost is
/// visible. The renderer reads <see cref="LatestSnapshot"/> whenever it draws and
/// will usually draw the same snapshot several times over.
/// </remarks>
public sealed class RadarUpdateLoop : ISnapshotSource
{
    private readonly GameSession _session;

    // Reference assignment is atomic and this field is written by the loop and
    // read by the render thread, so a volatile field is the whole
    // synchronisation story. The snapshot itself is immutable, so a reader can
    // never observe one half-built and never needs a lock to walk it.
    private volatile WorldSnapshot? _latest;

    private readonly Stopwatch _sinceLastCapture = Stopwatch.StartNew();

    public RadarUpdateLoop(GameSession session, RadarSettings settings)
    {
        _session = session;
        Settings = settings;
    }

    public RadarSettings Settings { get; }

    /// <summary>The most recent consistent snapshot, or null before the first one.</summary>
    public WorldSnapshot? LatestSnapshot => _latest;

    /// <summary>Outcome of the last attempt, including the ones that produced nothing.</summary>
    public CaptureStatus LastStatus { get; private set; } = CaptureStatus.NoArea;

    /// <summary>Measured, not configured: what the loop actually achieved.</summary>
    public double CaptureHz { get; private set; }

    private Features.Navigation.Navigator? _navigator;

    /// <summary>Hands the loop the routes to keep current. Set once, at composition.</summary>
    public void UseNavigator(Features.Navigation.Navigator navigator) => _navigator = navigator;

    private Func<EasyExile.Core.Snapshots.EntitySnapshot, bool>? _autoRoute;

    /// <summary>Wires the rule flag that decides what gets auto-routed on arrival.</summary>
    public void UseAutoRoute(Func<EasyExile.Core.Snapshots.EntitySnapshot, bool> predicate) =>
        _autoRoute = predicate;

    /// <inheritdoc />
    public MapFrameSnapshot? CaptureMapFrame() => _session.CaptureMapFrame();

    /// <summary>
    /// The last item-UI sweep, published by the sweep loop.
    /// </summary>
    /// <remarks>
    /// A field read, not a capture. The render lane used to call the sweep
    /// itself and stopped drawing for the length of a UI-tree walk eight times
    /// a second — periodic hitches in the drawing are more visible than a slow
    /// world, so that was worse than where it started.
    /// </remarks>
    public UiSnapshot? CaptureUi() => Volatile.Read(ref _ui);

    /// <summary>
    /// The whole UI tree, written down. Not on <see cref="ISnapshotSource"/> on
    /// purpose: that contract promises nothing behind it walks the client.
    /// </summary>
    public string? DumpUiTree(string directory) => _session.DumpUiTree(directory);

    /// <inheritdoc cref="DumpUiTree(string)"/>
    public string? DumpUiTree(string directory, Core.Diagnostics.DumpOptions options) =>
        _session.DumpUiTree(directory, options);

    /// <summary>What is under this point, for the picker.</summary>
    /// <remarks>
    /// Same reason as the dump for not being on <see cref="ISnapshotSource"/>:
    /// it walks the client, and that contract promises nothing behind it does.
    /// </remarks>
    public Core.Diagnostics.ElementProbe? PickElement(float x, float y, float uiScale) =>
        _session.PickElement(x, y, uiScale);

    private UiSnapshot? _ui;

    /// <summary>
    /// Sweeps the item UI on its own thread and its own reader, forever.
    /// </summary>
    /// <remarks>
    /// A third cadence because it belongs to neither of the other two: the
    /// world walk pays for it in entity rate, the render lane pays for it in
    /// frames, and it needs to be neither fast nor synchronised with either.
    /// </remarks>
    public async Task SweepUiAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                // Only collect the client's captions while something reads
                // them. It is two more crossings on every visible node, and the
                // sweep already visits thousands.
                // Only collect the client's captions while something reads
                // them. It is two more crossings on every visible node, and the
                // sweep already visits thousands.
                var options = new UiSweepOptions(Settings.Loot.ShowSkillSupports);

                if (_session.CaptureUi(options) is { } swept) Volatile.Write(ref _ui, swept);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A failed sweep is a stale panel, not a dead overlay.
            }
        }
    }

    public long Captures { get; private set; }
    public long Attempts { get; private set; }

    /// <summary>
    /// Runs until cancelled. A <see cref="PeriodicTimer"/> rather than a sleep
    /// loop: it waits on a timer instead of spinning, and a slow capture makes
    /// the next tick late rather than queueing a backlog of missed ones.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Settings.General.UpdateInterval);

        var appliedInterval = Settings.General.UpdateInterval;

        while (await WaitAsync(timer, cancellationToken).ConfigureAwait(false))
        {
            // The rate is a live setting, so the timer follows it rather than
            // being fixed at whatever it was when the loop started.
            var interval = Settings.General.UpdateInterval;
            if (interval != appliedInterval)
            {
                timer.Period = interval;
                appliedInterval = interval;
            }

            if (!Settings.General.Enabled) continue;

            Attempts++;

            var result = _session.Capture(Settings.ToCaptureOptions());
            LastStatus = result.Status;

            // A failed capture leaves the previous snapshot in place, and the
            // renderer decides how long it is still worth drawing.
            if (!result.Success) continue;

            _latest = result.Snapshot;
            Captures++;

            // Route maintenance rides the world lane, as it does in the
            // reference: this is the thread that has the terrain and the entity
            // list, and it is the only thread allowed to enqueue an A* replan.
            // The route's head is anchored at the map centre when it is drawn,
            // so the cursor advancing at capture rate is never visible.
            if (_navigator is { } navigator && result.Snapshot!.Player.GridPosition is { } grid)
                navigator.Maintain(result.Snapshot, grid, _autoRoute);

            var elapsed = _sinceLastCapture.Elapsed.TotalSeconds;
            _sinceLastCapture.Restart();

            if (elapsed > 0)
                CaptureHz = CaptureHz <= 0 ? 1 / elapsed : (CaptureHz * 0.9) + (0.1 / elapsed);
        }
    }

    /// <summary>Cancellation ends the loop; it is not a failure.</summary>
    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
