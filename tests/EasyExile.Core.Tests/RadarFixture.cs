using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Rendering;

namespace EasyExile.Core.Tests;

/// <summary>
/// Records what a feature drew.
/// </summary>
/// <remarks>
/// This is what the <see cref="IOverlayCanvas"/> abstraction buys: a feature can
/// be driven through a whole frame with no Direct3D device, no window and no
/// immediate-mode context, and the result can be asserted on directly.
/// </remarks>
internal sealed class RecordingCanvas : IOverlayCanvas
{
    public List<(Vector2 At, float Radius, uint Colour)> Circles { get; } = new();
    public List<(Vector2 At, string Text)> Texts { get; } = new();
    public int Lines { get; private set; }
    public int Rects { get; private set; }

    /// <summary>Marks, not clip state: pushing a clip draws nothing.</summary>
    public int Drawn => Circles.Count + Outlines.Count + Texts.Count + Lines + Rects + Quads.Count + Polygons.Count;

    public void Circle(Vector2 at, float radius, uint colour, bool filled = true) =>
        Circles.Add((at, radius, colour));

    public List<(Vector2 At, float Radius, uint Colour, float Thickness)> Outlines { get; } = new();

    public void CircleOutline(Vector2 at, float radius, uint colour, float thickness) =>
        Outlines.Add((at, radius, colour, thickness));

    /// <summary>Every line's far endpoint, in order — a route's drawn shape.</summary>
    public List<Vector2> LinePoints { get; } = new();

    public void Line(Vector2 from, Vector2 to, uint colour, float thickness = 1f)
    {
        Lines++;
        LinePoints.Add(to);
    }

    public List<(Vector2[] Points, uint Colour)> Polygons { get; } = new();

    public void Polygon(ReadOnlySpan<Vector2> points, uint colour) =>
        Polygons.Add((points.ToArray(), colour));

    public List<uint> RectColours { get; } = new();

    public void Rect(Vector2 min, Vector2 max, uint colour, bool filled = true)
    {
        Rects++;
        RectColours.Add(colour);
    }

    /// <summary>Every drawn string with the colour it was drawn in.</summary>
    /// <remarks>
    /// Kept beside <see cref="Texts"/> rather than replacing it: colour matters
    /// to exactly one feature so far — a tier badge whose whole job is to catch
    /// the eye — and rewriting every assertion in the suite to prove that would
    /// be paying for it everywhere.
    /// </remarks>
    public List<(Vector2 At, uint Colour, string Text)> ColouredTexts { get; } = new();

    public void Text(Vector2 at, uint colour, string text)
    {
        Texts.Add((at, text));
        ColouredTexts.Add((at, colour, text));
    }

    public void TextCentred(Vector2 at, uint colour, string text)
    {
        Texts.Add((at, text));
        ColouredTexts.Add((at, colour, text));
    }

    // Roughly ImGui's default font metrics; the features only use this to centre
    // text, and no assertion here depends on the exact width.
    public Vector2 MeasureText(string text) => new(text.Length * 7f, 13f);

    public int ClipPushes { get; private set; }
    public int ClipPops { get; private set; }

    public void PushClip(Vector2 min, Vector2 max) => ClipPushes++;

    public void PopClip() => ClipPops++;

    public List<(nint Texture, Vector2 TopLeft, Vector2 TopRight, Vector2 BottomRight, Vector2 BottomLeft)> Quads { get; } = new();

    public void ImageQuad(nint texture, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight, Vector2 bottomLeft,
        float opacity = 1f) =>
        Quads.Add((texture, topLeft, topRight, bottomRight, bottomLeft));
}

/// <summary>
/// Builds snapshots by hand. Nothing here reads a client, which is the point:
/// the whole render path can be exercised from constructed state.
/// </summary>
internal static class RadarFixture
{
    public static readonly ScreenRect Client = new(0, 0, 1920, 1080);

    /// <summary>Projects (0,0,0) to the centre of the viewport.</summary>
    public static CameraSnapshot Camera() =>
        new(WorldFixture.Orthographic().ToImmutableArray(), 1920, 1080);

    /// <summary>Every point lands behind the camera.</summary>
    public static CameraSnapshot CameraFacingAway() =>
        new(new[]
        {
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, -1f,
        }.ToImmutableArray(), 1920, 1080);

    public static PlayerSnapshot Player(Vector3? at = null) =>
        new("gravataicity", 17, 359, 359, 120, 120, 157, 157,
            at ?? new Vector3(0, 0, 0),
            Projection.ToGrid(at ?? new Vector3(0, 0, 0), 250f / 23f));

    public static EntitySnapshot Entity(int id, Vector3? at, string metadata = "Metadata/Monsters/Test/Rhoa") =>
        new(EntityIdFor(id),
            metadata,
            ImmutableArray.Create("Render"),
            at,
            Projection.ToGrid(at, 250f / 23f),
            ImmutableArray<Vital>.Empty);

    public static WorldSnapshot World(
        long epoch = 1,
        CameraSnapshot? camera = null,
        PlayerSnapshot? player = null,
        params EntitySnapshot[] entities) =>
        new(DateTimeOffset.UtcNow,
            epoch,
            default,
            player ?? Player(),
            entities.ToImmutableArray(),
            camera ?? Camera(),
            MapSnapshot.Closed,
            null);

    /// <summary>A fast frame for the same area, so the area guard agrees.</summary>
    public static MapFrameSnapshot MapFrame(
        WorldSnapshot world, float zoom = 0.5f, float shiftX = 0f, float shiftY = 0f,
        Vector2? playerGrid = null) =>
        new(DateTimeOffset.UtcNow, world.Epoch, world.Area,
            world.Player.WorldPosition ?? default,
            playerGrid ?? world.Player.GridPosition ?? default,
            new MapSnapshot(true, shiftX, shiftY, zoom));

    public static RenderFrame Frame(
        WorldSnapshot? snapshot,
        CaptureStatus status = CaptureStatus.Captured,
        bool stale = false,
        bool interactive = false) =>
        new(snapshot, status, TimeSpan.Zero, stale, Client, interactive);

    /// <summary>
    /// EntityId has no public constructor on purpose — it is minted during
    /// capture. Tests reach it the same way any snapshot does, through a capture
    /// against a fake address space.
    /// </summary>
    private static EntityId EntityIdFor(int id)
    {
        var ids = CapturedIds.Value;

        return id < ids.Length ? ids[id] : default;
    }

    /// <summary>A real AreaId, minted the only way one can be: by a capture.</summary>
    public static AreaId RealArea => CapturedArea.Value;

    private static readonly Lazy<AreaId> CapturedArea = new(() =>
    {
        using var mem = WorldFixture.Build();

        var result = SnapshotCapture.Capture(
            mem, WorldFixture.ModuleBase, new AreaEpoch(), CaptureOptions.Default);

        return result.Snapshot!.Area;
    });

    private static readonly Lazy<EntityId[]> CapturedIds = new(() =>
    {
        using var mem = WorldFixture.Build(monsters: 8);

        var result = SnapshotCapture.Capture(
            mem, WorldFixture.ModuleBase, new AreaEpoch(), CaptureOptions.Default);

        return result.Snapshot!.Entities.Select(e => e.Id).ToArray();
    });
}
