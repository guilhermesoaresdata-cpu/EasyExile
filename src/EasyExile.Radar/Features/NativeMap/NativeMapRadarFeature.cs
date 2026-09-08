using EasyExile.Core.Spatial;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace EasyExile.Radar.Features.NativeMap;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// Draws on the game's own map: terrain first, then the player, then entities.
/// </summary>
/// <remarks>
/// Port of the map section of <c>POE2Radar.Overlay/Overlay/OverlayRenderer.cs</c>
/// (MIT), method <c>DrawMap</c>. The terrain goes down as a single quad built
/// from three projected corners, which is what makes several million cells cost
/// one draw call. Everything shares the one projection, so a marker and the
/// ground under it cannot disagree.
///
/// There is no panel of our own. If the native map is closed, nothing is drawn.
/// </remarks>
public sealed class NativeMapRadarFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly RadarStats _stats;
    private readonly TerrainTexture _terrain;
    private readonly ExplorationMap _explored = new();
    private readonly ExplorationOverlay _unexplored;
    private readonly EntityMotion _motion = new();

    private DisplayRules? _rules;

    /// <summary>How many transitions share each cell, and which cells are done.</summary>
    private readonly Dictionary<(int X, int Y), int> _sharing = new();

    private readonly HashSet<(int X, int Y)> _labelled = new();

    /// <summary>What has already been written this frame, and roughly where.</summary>
    private readonly HashSet<(string Text, int X, int Y)> _said = new();

    /// <summary>
    /// The route source, when navigation is wired up. Null in tests that only
    /// exercise markers.
    /// </summary>
    private Navigation.Navigator? _navigator;

    /// <summary>Hands the feature the routes to draw. Set once, at composition.</summary>
    public void UseNavigator(Navigation.Navigator navigator) => _navigator = navigator;

    /// <summary>Whether this entity's display rule opted into auto-routing.</summary>
    public bool IsNavigable(Core.Snapshots.EntitySnapshot entity)
    {
        _rules ??= new DisplayRules(_settings.NativeMap);

        return _rules.IsNavigable(entity);
    }

    public NativeMapRadarFeature(
        RadarSettings settings, RadarStats stats, Func<string, Image<Rgba32>, bool, nint> uploadTexture)
    {
        _settings = settings;
        _stats = stats;
        _terrain = new TerrainTexture(uploadTexture);
        _unexplored = new ExplorationOverlay(uploadTexture);
    }

    public string Name => T("Mapa");

    public bool Enabled => _settings.NativeMap.Enabled;

    /// <summary>Why nothing was drawn, for the diagnostics panel.</summary>
    public string Status { get; private set; } = "-";

    public int TerrainBuilds => _terrain.Builds;

    public double TerrainBuildMilliseconds => _terrain.LastBuildMilliseconds;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        _stats.ResetMapCounters();

        if (!Enabled) { Status = "desligado"; return; }
        if (!frame.HasWorld) { Status = T("sem mundo"); return; }

        var snapshot = frame.Snapshot!;

        // Everything positional comes from the fast frame, taken this frame. The
        // world snapshot only supplies WHICH entities exist, never where they are
        // on screen — that is recomputed here, every frame, which is what makes
        // the map track movement instead of stepping at capture rate.
        if (frame.MapFrame is not { } live) { Status = T("sem frame de mapa"); return; }

        // Different threads, different rates: a portal taken between the two
        // captures would draw the old area's entities on the new area's map.
        if (!frame.AreasAgree) { Status = "trocando de area"; return; }

        if (!live.Map.IsUsable) { Status = T("mapa nativo fechado"); return; }
        if (snapshot.Terrain is not { IsEmpty: false } terrain) { Status = T("sem terrain"); return; }

        var options = _settings.NativeMap;

        var width = frame.ClientBounds.Width;
        var height = frame.ClientBounds.Height;

        var centre = NativeMapProjection.MapCentre(live.Map, width, height);
        var scale = NativeMapProjection.ScaleFor(live.Map.Zoom, height);
        var playerGrid = live.PlayerGrid;

        _stats.MapScale = scale;
        _stats.MapCentre = centre;

        // The map covers the client area, so that is the clip. There is no
        // rectangle of our own to bound it by.
        canvas.PushClip(new Vector2(0, 0), new Vector2(width, height));

        if (options.ShowTerrain) DrawTerrain(canvas, frame, terrain, playerGrid, centre, scale);
        if (options.ShowPlayer) DrawPlayer(canvas, playerGrid, centre, scale, options.PlayerColour);
        if (options.ShowEntities) DrawEntities(canvas, frame, playerGrid, centre, scale);
        if (options.ShowLandmarks) DrawLandmarks(canvas, frame, playerGrid, centre, scale);
        if (options.ShowRoutes) DrawRoutes(canvas, playerGrid, centre, scale);

        canvas.PopClip();

        Status = "desenhando";
    }

    /// <summary>
    /// One quad from three projected corners, exactly as the reference builds
    /// its affine transform: (0,0), (width,0) and (0,height) determine the
    /// parallelogram, and the fourth corner follows.
    /// </summary>
    private void DrawTerrain(
        IOverlayCanvas canvas, RenderFrame frame, Core.Snapshots.TerrainSnapshot terrain,
        Vector2 playerGrid, Vector2 centre, float scale)
    {
        var options = _settings.NativeMap;

        // Recorded before the texture is asked for, so a rebuild this frame
        // already includes the ground under the player's feet.
        if (options.ShowUnvisited)
            _explored.Observe(frame.Epoch, terrain, playerGrid, options.VisitRadius);

        // The terrain is built once per area, in one colour. It used to be
        // repainted whenever exploration grew, and that can never be smooth:
        // the cost is the size of the MAP, not the size of the change.
        var paint = new TerrainPaint(options.VisitedColour, options.UnvisitedColour, null);

        if (!_terrain.Ensure(frame.Epoch, terrain, paint)) { Status = T("terrain sem textura"); return; }

        var p00 = NativeMapProjection.Project(new Vector2(0, 0), playerGrid, centre, scale);
        var p10 = NativeMapProjection.Project(new Vector2(terrain.Width, 0), playerGrid, centre, scale);
        var p11 = NativeMapProjection.Project(new Vector2(terrain.Width, terrain.Height), playerGrid, centre, scale);
        var p01 = NativeMapProjection.Project(new Vector2(0, terrain.Height), playerGrid, centre, scale);

        canvas.ImageQuad(_terrain.Handle, p00, p10, p11, p01, _settings.NativeMap.TerrainOpacity);

        // The unexplored tint on the same quad. Its own image at a hundredth of
        // the pixels, so it can follow you instead of catching up.
        if (options.ShowUnvisited &&
            _unexplored.Ensure(frame.Epoch, terrain, _explored, options.UnvisitedColour, options.UnvisitedStrength))
            canvas.ImageQuad(_unexplored.Handle, p00, p10, p11, p01, _settings.NativeMap.TerrainOpacity);

        _stats.TerrainWidth = terrain.Width;
        _stats.TerrainHeight = terrain.Height;
    }

    private static void DrawPlayer(
        IOverlayCanvas canvas, Vector2 playerGrid, Vector2 centre, float scale, int colour)
    {
        // Projects to the centre by construction; drawn through the same call as
        // everything else so a projection error shows up here first.
        var at = NativeMapProjection.Project(playerGrid, playerGrid, centre, scale);

        canvas.Circle(at, 5f, Palette.Shadow);
        canvas.Circle(at, 3.5f, unchecked((uint)colour));
    }

    private void DrawEntities(
        IOverlayCanvas canvas, RenderFrame frame, Vector2 playerGrid, Vector2 centre, float scale)
    {
        var snapshot = frame.Snapshot!;
        var options = _settings.NativeMap;

        // Rebuilt only when a toggle actually changed. Compiling a dozen rules
        // on every frame would undo the point of precompiling them.
        if (_rules is null || _rules.Signature != DisplayRules.SignatureFor(options))
            _rules = new DisplayRules(options);

        _stats.MapEntitiesReceived = snapshot.Entities.Length;

        foreach (var entity in snapshot.Entities)
        {
            if (entity.IsPoi) _stats.MapPois++;
        }

        var bounds = frame.ClientBounds;

        // The two clocks the smoothing runs on. Both come off the snapshot, so
        // the interpolation is a function of the frame's data rather than of a
        // wall clock the tests cannot control.
        var capturedAt = snapshot.Timestamp;
        var now = capturedAt + frame.SnapshotAge;

        _motion.BeginFrame();

        // A town waypoint is one AreaTransition entity PER DESTINATION, all on
        // the same cell — eight of them measured live. Drawing a label for each
        // piles eight names on one pixel, which reads as markers scattered off
        // their targets. Counted first so the one marker drawn can say how many
        // ways out it is.
        _sharing.Clear();
        _labelled.Clear();
        _said.Clear();

        foreach (var entity in snapshot.Entities)
        {
            if (entity.Kind != Core.Snapshots.EntityKind.Transition ||
                entity.GridPosition is not { } cell) continue;

            var key = ((int)cell.X, (int)cell.Y);

            _sharing[key] = _sharing.GetValueOrDefault(key) + 1;
        }

        // Rank order, lowest first, so a unique is never buried under the pack
        // it is standing in the middle of.
        for (var rank = 0; rank <= 3; rank++)
        {
            foreach (var entity in snapshot.Entities)
            {
                if (DrawOrder(entity) != rank) continue;

                // The client files destructible scenery under /Chests too — the
                // urns and crates come through as "EzomyteChest_01", which no
                // keyword list catches, so a town fills with chest markers.
                // Its own minimap knows the difference: it marks a chest worth
                // opening and ignores a pot. Trusting that beats guessing at
                // names, and the switch is there for when it is too strict.
                if (options.OnlyMarkedChests &&
                    entity.Kind == Core.Snapshots.EntityKind.Chest &&
                    !entity.IsPoi)
                {
                    _stats.MapEntitiesIgnored++;
                    continue;
                }

                if (_rules.Resolve(entity) is not { } rule)
                {
                    _stats.MapEntitiesIgnored++;
                    continue;
                }

                if (entity.WorldPosition is not { } captured)
                {
                    if (entity.IsPoi) _stats.MapPoisUnplaced++;
                    continue;
                }

                Count(entity);

                // Smoothed in world space, then projected. Projecting first and
                // smoothing the pixels would drag the marker whenever the player
                // moved, because the whole map moves under it.
                var world = _motion.Position(
                    frame.Epoch, entity.Id, captured, capturedAt, now, options.SmoothMovement);

                // Projected here, through the player and the map read on THIS
                // frame. Nothing positional is cached, so smoothing an entity
                // cannot delay the pan or the zoom.
                var at = NativeMapProjection.Project(
                    NativeMapProjection.ToGrid(world), playerGrid, centre, scale);

                if (at.X < bounds.X || at.X > bounds.Right || at.Y < bounds.Y || at.Y > bounds.Bottom)
                {
                    _stats.MapEntitiesOutside++;
                    continue;
                }

                MapIcons.Draw(canvas, rule.Shape, at, rule.Size, rule.Colour);

                // The client's own name first. The metadata path is the last
                // resort, not the first choice: every exit in the game shares
                // one, so it can only ever say "Area Transition".
                var label = rule.LabelFromMetadata
                    ? entity.FriendlyName ?? Sanitized(entity.Metadata)
                    : rule.Label;

                // A landmark already naming this spot has said it. Two copies of
                // "The Well of Souls" on top of each other is worse than one.
                if (label is { Length: > 0 } && NamedByALandmark(frame, label, entity)) label = null;

                // One label per cell. The rest of a waypoint's destinations are
                // the same marker in the same place, and stacking their names
                // buries all of them.
                if (entity.Kind == Core.Snapshots.EntityKind.Transition &&
                    entity.GridPosition is { } at2)
                {
                    var key = ((int)at2.X, (int)at2.Y);

                    if (!_labelled.Add(key)) continue;

                    if (_sharing.GetValueOrDefault(key) > 1)
                        label = $"{_sharing[key]} saidas";
                }

                // The same words twice in the same place is never information.
                // The per-cell collapse above only covers transitions, so a
                // mechanic marked twice — two Dryadic Ritual entities a grid
                // apart — printed its name over itself and both copies became
                // unreadable. Screen position, not grid: at low zoom two cells
                // land on one pixel.
                if (label is { Length: > 0 } && Said(label, at))
                    canvas.Text(new Vector2(at.X + 7f, at.Y - 7f), rule.Colour, label);

                // The threat mark rides ON TOP of the rank marker rather than
                // replacing it, so a dangerous rare still reads as a rare.
                if (_rules.ResolveOverlay(entity) is { } threat)
                {
                    MapIcons.Draw(
                        canvas, threat.Shape,
                        new Vector2(at.X, at.Y - rule.Size - threat.Size), threat.Size, threat.Colour);

                    if (threat.Label is { Length: > 0 } mark)
                        canvas.Text(new Vector2(at.X + 7f, at.Y + 2f), threat.Colour, mark);

                    _stats.MapThreats++;
                }

                if (entity.IsPoi) _stats.MapPoisDrawn++;

                _stats.MapEntitiesDrawn++;
            }
        }

        DrawRemembered(canvas, frame, playerGrid, centre, scale);

        _motion.Forget(now);

        _stats.MapInterpolated = _motion.Interpolated;
        _stats.MapSnapped = _motion.Snapped;
        _stats.MapTracked = _motion.Tracked;
    }

    /// <summary>
    /// Exits seen earlier in this area but no longer loaded by the client.
    /// </summary>
    /// <remarks>
    /// Dimmed, because they are remembered rather than observed — but drawn,
    /// because an exit does not move and a map that forgets behind you is worth
    /// less than one that fills in. Never smoothed and never counted as present:
    /// there is nothing there to interpolate.
    /// </remarks>
    private void DrawRemembered(
        IOverlayCanvas canvas, RenderFrame frame, Vector2 playerGrid, Vector2 centre, float scale)
    {
        var recalled = frame.Snapshot!.Recalled;

        if (recalled.Length == 0 || _rules is null) return;

        var bounds = frame.ClientBounds;

        foreach (var entity in recalled)
        {
            if (_rules.Resolve(entity) is not { } rule) continue;
            if (entity.WorldPosition is not { } world) continue;

            var at = NativeMapProjection.Project(
                NativeMapProjection.ToGrid(world), playerGrid, centre, scale);

            if (at.X < bounds.X || at.X > bounds.Right || at.Y < bounds.Y || at.Y > bounds.Bottom) continue;

            MapIcons.Draw(canvas, rule.Shape, at, rule.Size, Palette.Dim(rule.Colour));

            var label = rule.LabelFromMetadata
                ? entity.FriendlyName ?? EntityLabels.Pretty(entity.Metadata)
                : rule.Label;

            // Remembered exits go through the same filter: a live marker and
            // the memory of one are two drawings of a single door.
            if (label is { Length: > 0 } && !NamedByALandmark(frame, label, entity) && Said(label, at))
                canvas.Text(new Vector2(at.X + 7f, at.Y - 7f), Palette.Dim(rule.Colour), label);

            _stats.MapRemembered++;
        }
    }

    /// <summary>
    /// Whether a landmark already prints this name at this spot.
    /// </summary>
    /// <remarks>
    /// The landmark layer draws a tile cluster's curated name, and a transition
    /// standing on that cluster resolves to the same destination. Both are
    /// right; printing both is not.
    /// </remarks>
    /// <summary>
    /// A name derived from the metadata path, unless it is the generic one.
    /// </summary>
    /// <remarks>
    /// Every exit in the game shares one metadata path, so the sanitized version
    /// reads "Area Transition" — which names nothing, and which the user asked
    /// not to see. The fallback earns its keep on the paths that DO say
    /// something; it just must not fire for the ones that do not.
    /// </remarks>
    /// <summary>
    /// Whether this text is new here, and remembers it if so.
    /// </summary>
    /// <remarks>
    /// The bucket is deliberately coarse. A door is several entities — the
    /// transition, its animated prop, the tile landmark under it — and on a town
    /// map those land tens of pixels apart, which is one door and four copies of
    /// its name. Measured: "Clearfell" printed four times inside 150px.
    ///
    /// Wide enough to merge one door's worth of entities, narrow enough that two
    /// exits a screen apart stay two labels.
    /// </remarks>
    private bool Said(string label, Vector2 at) =>
        _said.Add((label, (int)(at.X / 56f), (int)(at.Y / 28f)));

    private static string? Sanitized(string metadata)
    {
        var pretty = EntityLabels.Pretty(metadata);

        if (pretty is not { Length: > 0 }) return null;

        return pretty.StartsWith("Area Transition", StringComparison.OrdinalIgnoreCase)
            ? null
            : pretty;
    }

    private static bool NamedByALandmark(RenderFrame frame, string label, Core.Snapshots.EntitySnapshot entity)
    {
        if (entity.GridPosition is not { } at) return false;

        foreach (var mark in frame.Snapshot!.Marks)
        {
            if (!string.Equals(mark.Name, label, StringComparison.OrdinalIgnoreCase)) continue;

            var dx = mark.Centre.X - at.X;
            var dy = mark.Centre.Y - at.Y;

            if ((dx * dx) + (dy * dy) <= SameSpotGrid * SameSpotGrid) return true;
        }

        return false;
    }

    /// <summary>
    /// How close two labels have to be to count as the same place. A tile
    /// cluster's centroid and the entity standing in it are never the same
    /// cell, so this cannot be zero.
    /// </summary>
    private const float SameSpotGrid = 25f;

    /// <summary>
    /// Which pass an entity is drawn in. Everything unranked goes down first;
    /// the ranks follow in ascending order so the rarest marker is on top.
    /// </summary>
    private static int DrawOrder(Core.Snapshots.EntitySnapshot entity) =>
        entity.Kind != Core.Snapshots.EntityKind.Monster
            ? 0
            : entity.Rarity switch
            {
                Core.Snapshots.MonsterRarity.Magic => 1,
                Core.Snapshots.MonsterRarity.Rare => 2,
                Core.Snapshots.MonsterRarity.Unique => 3,
                _ => 0,
            };

    private void Count(Core.Snapshots.EntitySnapshot entity)
    {
        if (entity.Kind != Core.Snapshots.EntityKind.Monster) return;

        switch (entity.Rarity)
        {
            case Core.Snapshots.MonsterRarity.Magic: _stats.MapMagic++; break;
            case Core.Snapshots.MonsterRarity.Rare: _stats.MapRare++; break;
            case Core.Snapshots.MonsterRarity.Unique: _stats.MapUnique++; break;
            default: _stats.MapNormal++; break;
        }
    }

    /// <summary>
    /// The guidance routes, one polyline per selected target.
    /// </summary>
    /// <remarks>
    /// Port of POE2Radar's <c>OverlayRenderer.DrawPaths</c>, including the detail
    /// that matters most: the line's head is anchored at the map CENTRE — the
    /// live player marker — rather than at the first waypoint. The waypoints
    /// update at world rate; the centre is current every frame, so the route
    /// stays attached to the player between replans instead of trailing behind.
    ///
    /// Every point goes through the same projection as the terrain and the
    /// markers, so a route cannot disagree with the ground under it: pan and
    /// zoom move it by construction.
    /// </remarks>
    private void DrawRoutes(IOverlayCanvas canvas, Vector2 playerGrid, Vector2 centre, float scale)
    {
        if (_navigator is null) return;

        _stats.MapRoutes = 0;

        foreach (var (slot, points) in _navigator.Routes())
        {
            var colour = Palette.RouteColour(slot);

            var previous = centre;

            foreach (var (gx, gy) in points)
            {
                var at = NativeMapProjection.Project(new Vector2(gx, gy), playerGrid, centre, scale);

                canvas.Line(previous, at, colour, RouteThickness);

                previous = at;
            }

            _stats.MapRoutes++;
        }
    }

    /// <summary>POE2Radar's route stroke width.</summary>
    private const float RouteThickness = 2.4f;

    /// <summary>
    /// Named terrain features, each a diamond with its label beside it.
    /// </summary>
    /// <remarks>
    /// Port of the landmark block of POE2Radar's <c>OverlayRenderer.DrawMap</c>:
    /// same default style, same label placement to the marker's upper right.
    /// Drawn last so a label is never buried under a monster dot.
    /// </remarks>
    private void DrawLandmarks(
        IOverlayCanvas canvas, RenderFrame frame, Vector2 playerGrid, Vector2 centre, float scale)
    {
        var marks = frame.Snapshot!.Marks;

        _stats.MapLandmarks = marks.Length;

        if (marks.Length == 0) return;

        var bounds = frame.ClientBounds;

        foreach (var mark in marks)
        {
            var at = NativeMapProjection.Project(mark.Centre, playerGrid, centre, scale);

            if (at.X < bounds.X || at.X > bounds.Right || at.Y < bounds.Y || at.Y > bounds.Bottom) continue;

            // An exit tile is an exit: same icon and colour as the entity
            // standing on it, so the two layers never look like two things.
            var colour = mark.IsWayOut ? Palette.TransitionGreen : Palette.Landmark;

            MapIcons.Draw(canvas, mark.IsWayOut ? "Stairs" : "Diamond", at, 5f, colour);

            if (Said(mark.Name, at))
                canvas.Text(new Vector2(at.X + 7f, at.Y - 7f), colour, mark.Name);

            _stats.MapLandmarksDrawn++;
        }
    }

}
