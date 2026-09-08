using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.UI;

namespace EasyExile.Radar.Runtime;

/// <summary>
/// Drives one rendered frame.
/// </summary>
/// <remarks>
/// This never calls Capture. It reads whatever snapshot the update loop last
/// published and draws it, which means the same snapshot is normally drawn
/// several times over — at 144 Hz against a 20 Hz capture, roughly seven times.
/// That is correct: reading another process once per displayed frame would cost
/// far more than it could possibly reveal.
/// </remarks>
public sealed class RadarRenderLoop
{
    private readonly ISnapshotSource _snapshots;
    private readonly RadarSettings _settings;

    /// <summary>Auto potion, exposed so the panel can show its state.</summary>
    public Features.AutoPotion.AutoPotion Potion { get; }
    private readonly RadarStats _stats;
    private readonly IReadOnlyList<IRadarFeature> _features;
    private readonly GameWindowTracker _tracker;
    private readonly SettingsWindow _panel;

    private readonly ImGuiCanvas _canvas = new();

    private bool _hotkeyWasDown;
    private bool _screenshotWasDown;
    private nint _windowHandle;
    private DateTimeOffset _shotAt = DateTimeOffset.MinValue;
    private string? _shotName;

    private bool _uiTreeWasDown;

    private bool _shotOk = true;

    private readonly Func<float, float, float, Core.Diagnostics.ElementProbe?>? _pickElement;

    private bool _inspectWasDown;

    private bool _inspecting;

    private Core.Diagnostics.ElementProbe? _picked;

    private DateTimeOffset _pickedAt;

    private readonly Func<string, Core.Diagnostics.DumpOptions, string?>? _dumpUiTree;

    internal RadarRenderLoop(
        ISnapshotSource snapshots,
        RadarSettings settings,
        RadarStats stats,
        IReadOnlyList<IRadarFeature> features,
        GameWindowTracker tracker,
        SettingsWindow panel,
        Func<string, Core.Diagnostics.DumpOptions, string?>? dumpUiTree = null,
        Func<float, float, float, Core.Diagnostics.ElementProbe?>? pickElement = null)
    {
        // A delegate rather than a member of ISnapshotSource. Walking the tree
        // is expensive and that contract promises nothing behind it walks, so
        // the expensive thing stays outside the shape that promises cheap.
        _dumpUiTree = dumpUiTree;
        _pickElement = pickElement;

        _snapshots = snapshots;
        _settings = settings;
        Potion = new Features.AutoPotion.AutoPotion(settings);
        _stats = stats;
        _features = features;
        _tracker = tracker;
        _panel = panel;
    }

    public bool Interactive { get; private set; }

    private readonly System.Diagnostics.Stopwatch _sinceSettingsCheck = System.Diagnostics.Stopwatch.StartNew();

    public GameWindowState GameWindow { get; private set; } = GameWindowState.Gone;

    public OverlayPlacement Placement { get; private set; }

    /// <summary>Set once the client is gone, so the host can shut down cleanly.</summary>
    public bool Disconnected { get; private set; }

    /// <summary>
    /// Client area versus the viewport the snapshot's camera reports. They should
    /// agree; when they do not, every marker is scaled wrong and the cause is
    /// worth finding rather than papering over.
    /// </summary>
    public string? ViewportMismatch { get; private set; }

    internal void DrawFrame(OverlayWindow window)
    {
        _stats.BeginFrame();

        GameWindow = _tracker.Poll();

        if (GameWindow.Status == GameWindowStatus.Gone)
        {
            Disconnected = true;
            Placement = new OverlayPlacement(false, ScreenRect.Empty, "cliente encerrado");
            window.Place(Placement);
            return;
        }

        UpdateInputMode(window);

        Placement = OverlayPlacementPolicy.Decide(
            GameWindow, _settings.General.ShowWhenGameUnfocused, Interactive);

        window.Place(Placement);

        if (!Placement.Visible) return;

        var frame = BuildFrame(Placement.Bounds);

        CheckViewport(frame);

        _canvas.Begin();

        // Before anything is drawn: the decision is about the frame's numbers,
        // not about what ends up on screen.
        TickAutoPotion(frame);

        DrawStatus(frame);

        foreach (var feature in _features) feature.Draw(frame, _canvas);

        DrawInspector();
        DrawShotConfirmation();

        // The destination list follows the world, not the frame: it is rebuilt
        // only when the area or the entity set actually changes.
        if (frame.Snapshot is { } world) _panel.Observe(world);

        if (Interactive) _panel.Draw(this, _stats); else _panel.Forget();

        // The panel owns the mouse only where it actually is. Everywhere else
        // the click belongs to the game, which is the point of an overlay.
        window.CursorOverPanel = Interactive && CursorIsOverPanel();

        // Settings are written when they change, not when the overlay closes.
        // Throttled because the panel mutates them on every frame a slider is
        // being dragged, and a file write per frame is work done for nobody.
        if (_sinceSettingsCheck.Elapsed.TotalSeconds >= 1)
        {
            _sinceSettingsCheck.Restart();
            Settings.SettingsStore.SaveIfChanged(_settings);
        }

        // Again, after the frame is built. The library sets click-through from
        // what ImGui wanted this frame, so the last word has to be ours or the
        // panel stops accepting clicks the moment the cursor leaves it.
        window.ApplyInputMode();
    }

    /// <summary>
    /// Whether the cursor sits inside the panel, in the overlay's own pixels.
    /// </summary>
    /// <remarks>
    /// The overlay is positioned over the game's client area, so subtracting the
    /// client origin from the screen cursor gives the coordinates ImGui laid the
    /// panel out in.
    /// </remarks>
    private bool CursorIsOverPanel()
    {
        if (_panel.Bounds is null) return false;
        if (Native.CursorPosition() is not { } cursor) return false;

        var bounds = GameWindow.ClientBounds;

        return _panel.Contains(cursor.X - bounds.X, cursor.Y - bounds.Y);
    }

    /// <summary>
    /// Auto potion, on the fast frame.
    /// </summary>
    /// <remarks>
    /// No thread and no polling of its own: this loop already has the vitals and
    /// already knows whether the game is in front, and the whole decision is a
    /// few comparisons and a timestamp.
    ///
    /// The kill switch is polled OUTSIDE the enabled check on purpose — a switch
    /// that only works while automation is armed is not a kill switch.
    /// </remarks>
    private void TickAutoPotion(RenderFrame frame)
    {
        var now = DateTimeOffset.UtcNow;

        // Only while the game or the overlay is in front, so F8 does not fire
        // into whatever else the player is doing.
        if (GameWindow.IsForeground) Potion.PollKillSwitch(Native.IsKeyDown, now);

        Potion.Tick(frame.MapFrame, GameWindow.IsForeground, now);

        PollScreenshot(now);
        PollUiTree(now);
        PollInspector(now);
    }

    /// <summary>
    /// Says a picture was saved, and which one, for a couple of seconds.
    /// </summary>
    /// <remarks>
    /// A capture that gives no sign it happened is a capture you press twice.
    /// The filename is on screen rather than only in the folder because the
    /// point of the picture is to be pointed at afterwards.
    ///
    /// Drawn AFTER the features and before the panel, so it is never the thing
    /// buried by a chip — and it deliberately appears in the NEXT frame's
    /// capture, not this one, so pressing twice does not photograph the notice.
    /// </remarks>
    private void DrawShotConfirmation()
    {
        if (_shotName is null) return;

        var age = DateTimeOffset.UtcNow - _shotAt;

        if (age > TimeSpan.FromSeconds(3))
        {
            _shotName = null;
            return;
        }

        var text = _shotOk ? _shotName : $"FALHOU: {_shotName}";
        var size = _canvas.MeasureText(text);
        var at = new Vector2(16f, 40f);

        _canvas.Rect(
            new Vector2(at.X - 4f, at.Y - 2f),
            new Vector2(at.X + size.X + 4f, at.Y + size.Y + 2f),
            Palette.Shadow);

        _canvas.Text(at, _shotOk ? Palette.Good : Palette.Bad, text);
    }

    /// <summary>
    /// A picture of the screen, on a rising edge, for reporting what happened.
    /// </summary>
    /// <remarks>
    /// Rising edge for the same reason the HUD hotkey uses one: the overlay is
    /// click-through in HUD mode and never receives key messages, so polling is
    /// the only signal there is, and a held key would otherwise fill the disk at
    /// frame rate.
    /// </remarks>
    private void PollScreenshot(DateTimeOffset now)
    {
        var owns = GameWindow.IsForeground || Native.Foreground() == _windowHandle;
        var down = owns && Native.IsKeyDown(_settings.General.ScreenshotHotkey);

        if (down && !_screenshotWasDown)
        {
            var path = Overlay.ScreenCapture.Save(
                Path.Combine(AppContext.BaseDirectory, "prints"));

            if (path is not null)
            {
                _shotAt = now;
                _shotName = $"print salvo: {Path.GetFileName(path)}";
                _shotOk = true;
                _stats.Screenshots++;
            }
        }

        _screenshotWasDown = down;
    }

    /// <summary>
    /// Writes the whole UI tree when the key goes down.
    /// </summary>
    /// <remarks>
    /// It takes a moment - the walk visits every element, not only the drawn
    /// ones - and that is the trade it exists to make. A pause the player chose
    /// beats a probe that had to be running at the same instant, which is how
    /// most of a session was spent.
    /// </remarks>
    private void PollUiTree(DateTimeOffset now)
    {
        var owns = GameWindow.IsForeground || Native.Foreground() == _windowHandle;

        var whole = owns && Native.IsKeyDown(_settings.General.FullTreeHotkey);
        var down = whole || (owns && Native.IsKeyDown(_settings.General.UiTreeHotkey));

        if (down && !_uiTreeWasDown && _dumpUiTree is not null)
        {
            // A key each, and no modifiers. Shift and control belong to the
            // game - shift is held to read an item's prefixes - so putting a
            // meaning on them here means doing two things at once, one of them
            // unwanted.
            //
            // Three earlier shapes of this were worse: the picker's key
            // doubling as save, the meaning of save depending on whether the
            // picker was on, and modifiers that collided with the game.
            var at = ImGuiNET.ImGui.GetMousePos();
            var ui = Placement.Bounds.Height > 0 ? Placement.Bounds.Height / 1600f : 1f;

            var options = whole
                ? Core.Diagnostics.DumpOptions.Everything
                : Core.Diagnostics.DumpOptions.Complete(at.X, at.Y, ui);

            var path = _dumpUiTree(Path.Combine(AppContext.BaseDirectory, "trees"), options);

            _shotAt = now;
            _shotOk = path is not null;

            LastDump = path is null ? null : Path.GetFileName(path);

            _shotName = path is not null
                ? $"salvo: {Path.GetFileName(path)}"
                : "nao salvou (o personagem esta numa area?)";
        }

        _uiTreeWasDown = down;
    }

    /// <summary>
    /// Point at something and be told exactly what it is.
    /// </summary>
    /// <remarks>
    /// The DevTree half of what was asked for. A fifty-thousand line dump
    /// answers everything and finds nothing; this answers one thing and finds it
    /// instantly, and between them there is no question about a panel that needs
    /// somebody holding a mouse still while a probe runs.
    ///
    /// Each pick walks the tree from the root, so it happens a few times a
    /// second and only while the mode is on.
    /// </remarks>
    private void PollInspector(DateTimeOffset now)
    {
        var owns = GameWindow.IsForeground || Native.Foreground() == _windowHandle;
        var down = owns && Native.IsKeyDown(_settings.General.InspectHotkey);

        if (down && !_inspectWasDown)
        {
            _inspecting = !_inspecting;
            _picked = null;
        }

        _inspectWasDown = down;

        if (!_inspecting || _pickElement is null) return;
        if (now - _pickedAt < TimeSpan.FromMilliseconds(120)) return;

        _pickedAt = now;

        var cursor = ImGuiNET.ImGui.GetMousePos();

        // The client lays its UI out against a 1600-high design and scales to
        // the window, so the picker is told the same conversion every rectangle
        // in the snapshot already went through.
        var scale = Placement.Bounds.Height > 0 ? Placement.Bounds.Height / 1600f : 1f;

        _picked = _pickElement(cursor.X, cursor.Y, scale);
    }

    /// <summary>Draws the outline and the description of whatever is picked.</summary>
    private void DrawInspector()
    {
        if (!_inspecting) return;

        _canvas.Text(new Vector2(16f, 60f), Palette.Warning,
            "APONTADOR ligado - F8 desliga, detalhes em F0 > Debug");

        if (_picked is not { } probe) return;

        _canvas.Rect(
            new Vector2(probe.X, probe.Y),
            new Vector2(probe.Right, probe.Bottom),
            Palette.Good, filled: false);

        // The detail is read in the panel now. Over the game it would be a
        // paragraph on top of what somebody is trying to look at, and the
        // outline alone answers the only question the screen can: which one.
        var caption = probe.LineText ?? probe.WideText ?? $"0x{probe.Address:X}";

        _canvas.Text(
            new Vector2(probe.X, Math.Max(0f, probe.Y - 15f)),
            Palette.Good,
            caption.Length <= 48 ? caption : caption[..48]);
    }

    /// <summary>Whether the element picker is aiming.</summary>
    /// <remarks>
    /// Settable so the HUD can drive it as well as the key. A tool that lives
    /// only on a hotkey lives only in whoever was told about the hotkey.
    /// </remarks>
    public bool Inspecting
    {
        get => _inspecting;
        set
        {
            _inspecting = value;

            if (!value) _picked = null;
        }
    }

    /// <summary>What the cursor is on, while the picker is aiming.</summary>
    public Core.Diagnostics.ElementProbe? Picked => _picked;

    /// <summary>The last dump written, for the panel to report.</summary>
    public string? LastDump { get; private set; }

    /// <summary>
    /// Write a dump now, asked for by the panel rather than by a key.
    /// </summary>
    /// <remarks>
    /// An aimed dump uses where the picker last looked, not where the mouse is:
    /// by the time a button has been clicked the mouse is on the button.
    /// </remarks>
    public string? SaveDump(Core.Diagnostics.DumpOptions options)
    {
        if (_dumpUiTree is null) return null;

        var path = _dumpUiTree(Path.Combine(AppContext.BaseDirectory, "trees"), options);

        LastDump = path is null ? null : Path.GetFileName(path);

        return path;
    }

    /// <summary>Where the picker last looked, in client pixels.</summary>
    public Vector2 AimedAt =>
        _picked is { } probe ? new Vector2(probe.X + 2f, probe.Y + 2f) : new Vector2(0f, 0f);

    /// <summary>UI units to client pixels, for an aimed dump.</summary>
    public float UiScale => Placement.Bounds.Height > 0 ? Placement.Bounds.Height / 1600f : 1f;

    private void UpdateInputMode(OverlayWindow window)
    {
        // Polled on a rising edge. The overlay is click-through in HUD mode and
        // therefore never receives key messages, so there is no ImGui event to
        // listen for; and only while the game or the overlay is in front, so the
        // hotkey does not fire into whatever else the player is doing.
        var owns = GameWindow.IsForeground || Native.Foreground() == window.Handle;
        var down = owns && Native.IsKeyDown(_settings.General.HudInteractiveHotkey);

        if (down && !_hotkeyWasDown) Interactive = !Interactive;

        _hotkeyWasDown = down;

        _windowHandle = window.Handle;

        window.Interactive = Interactive;
        window.ApplyInputMode();
    }

    private RenderFrame BuildFrame(ScreenRect bounds)
    {
        // Taken every frame. This is the whole fluidity story: the entity list is
        // 30 Hz, but what moves under it is current.
        var mapClock = System.Diagnostics.Stopwatch.StartNew();
        var mapFrame = _snapshots.CaptureMapFrame();

        // Asked every frame; the source returns the previous answer until a
        // sweep is due, so this costs eight sweeps a second, not 144.
        var ui = _snapshots.CaptureUi();
        _stats.RecordMapFrame(mapClock.Elapsed.TotalMilliseconds, mapFrame is not null);

        var snapshot = _snapshots.LatestSnapshot;
        var status = _snapshots.LastStatus;

        _stats.CaptureHz = _snapshots.CaptureHz;

        var age = snapshot is null
            ? TimeSpan.MaxValue
            : DateTimeOffset.UtcNow - snapshot.Timestamp;

        _stats.SnapshotAge = age;

        // A snapshot that has aged out stops driving world markers. During an
        // area load the last one describes a world the player has left, and a
        // marker on a monster that no longer exists is worse than an empty
        // screen. The status line still says what is happening.
        var stale = snapshot is null || age > _settings.General.StaleAfter;

        return new RenderFrame(snapshot, status, age, stale, bounds, Interactive, mapFrame, ui);
    }

    private void CheckViewport(RenderFrame frame)
    {
        if (frame.Snapshot?.Camera is not { } camera)
        {
            ViewportMismatch = null;
            return;
        }

        var bounds = frame.ClientBounds;

        ViewportMismatch = camera.Width == bounds.Width && camera.Height == bounds.Height
            ? null
            : $"client {bounds.Width}x{bounds.Height} != viewport {camera.Width}x{camera.Height}";
    }

    private void DrawStatus(RenderFrame frame)
    {
        var (colour, text) = frame switch
        {
            { Snapshot: null } => (Palette.Warning, Describe(frame.Status)),
            { SnapshotIsStale: true } => (Palette.Warning, Describe(frame.Status)),
            _ => (Palette.Good, "conectado"),
        };

        _canvas.Circle(new Vector2(16f, 18f), 4f, colour);
        _canvas.Text(new Vector2(26f, 11f), Palette.Muted, $"EasyExile  {text}");

        if (frame.Snapshot is not { } snapshot) return;

        _canvas.Text(
            new Vector2(26f, 25f),
            Palette.Muted,
            $"entities {snapshot.Entities.Length}   poi {_stats.MapPois}   epoch {snapshot.Epoch}   " +
            $"{_stats.RenderFps:0} fps   {_stats.CaptureHz:0} Hz");

        if (ViewportMismatch is { } mismatch)
            _canvas.Text(new Vector2(26f, 39f), Palette.Bad, mismatch);
    }

    private static string Describe(CaptureStatus status) => status switch
    {
        CaptureStatus.NoArea => "carregando / fora de area",
        CaptureStatus.InvalidatedByTransition => "trocando de area",
        _ => "aguardando snapshot",
    };
}
