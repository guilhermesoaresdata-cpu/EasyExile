using System.Diagnostics;

namespace EasyExile.Radar.Rendering;

/// <summary>
/// Instrumentation for the debug panel.
/// </summary>
/// <remarks>
/// None of this is printed to a console. At render rate a per-frame log costs
/// more than the thing it measures, and it scrolls past faster than anyone can
/// read; the numbers belong on screen, next to what they describe.
/// </remarks>
public sealed class RadarStats
{
    private readonly Stopwatch _frame = Stopwatch.StartNew();

    // Exponential moving averages. A raw per-frame reciprocal jitters far too
    // much to read, and an average over a fixed window would lag a real stall.
    private const double Smoothing = 0.08;

    public double RenderFps { get; private set; }
    public double FrameMilliseconds { get; private set; }
    public double ProjectionMicroseconds { get; private set; }

    public double CaptureHz { get; set; }
    public TimeSpan SnapshotAge { get; set; }

    public int EntitiesReceived { get; set; }
    public int EntitiesRendered { get; set; }

    public int OnScreen { get; set; }
    public int OffScreen { get; set; }
    public int BehindCamera { get; set; }
    public int InvalidProjection { get; set; }

    public long Frames { get; private set; }

    // The native map overlay keeps its own counters: it and the world HUD look
    // at the same entities and reject different ones.
    public int MapEntitiesReceived { get; set; }
    public int MapEntitiesDrawn { get; set; }
    public int MapEntitiesOutside { get; set; }
    public int MapEntitiesIgnored { get; set; }

    // Per-rank counts. Rarity is read from the client, so a rank that never
    // appears is a reading problem, not a quiet area — the panel has to be able
    // to show the difference.
    public int MapNormal { get; set; }
    public int MapMagic { get; set; }
    public int MapRare { get; set; }
    public int MapUnique { get; set; }

    /// <summary>Entities the client itself marked on its map.</summary>
    public int MapPois { get; set; }

    /// <summary>Of those, how many actually reached the canvas.</summary>
    public int MapPoisDrawn { get; set; }

    /// <summary>And how many had no world position to draw at.</summary>
    public int MapPoisUnplaced { get; set; }

    /// <summary>Named terrain features in the area, and how many were on screen.</summary>
    public int MapLandmarks { get; set; }

    public int MapLandmarksDrawn { get; set; }

    /// <summary>League mechanics drawn with their own icon.</summary>
    public int MapMechanics { get; set; }

    /// <summary>Monsters carrying an affix a threat rule watches for.</summary>
    public int MapThreats { get; set; }

    /// <summary>Exits drawn from memory rather than from the live entity list.</summary>
    public int MapRemembered { get; set; }

    /// <summary>Health bars drawn this frame, and how many carried a threat mark.</summary>
    public int HpBars { get; set; }

    public int HpBarThreats { get; set; }

    /// <summary>Drops that resolved to a price this frame.</summary>
    public int LootPriced { get; set; }

    /// <summary>
    /// What happened to the game's ground tags this frame, step by step.
    /// </summary>
    /// <remarks>
    /// "Nem esta aparecendo os precos" has several different causes that look
    /// identical on screen: there is no loot on the floor, or there is and no
    /// entity was paired with the tag, or there is and poe.ninja has no price
    /// for it, or there is and the price is below the floor you set. Without
    /// this you cannot tell them apart, and neither could I — the first thing I
    /// had to do was ask the client directly.
    ///
    /// So the counters follow the funnel: how many tags the game drew, how many
    /// found their item, how many got a price, and how many survived the
    /// filters to be drawn.
    /// </remarks>
    public int LootTags { get; set; }

    public int LootTagsMatched { get; set; }

    public int LootTagsUnpriced { get; set; }

    public int LootTagsBelowFloor { get; set; }

    public int LootTagsFiltered { get; set; }

    /// <summary>Slots outlined as worth something this frame.</summary>
    public int SlotsHighlighted { get; set; }

    /// <summary>Pictures saved this session.</summary>
    public int Screenshots { get; set; }

    /// <summary>Waypoints drawn on the ground this frame.</summary>
    public int WorldRouteWaypoints { get; set; }

    /// <summary>Guidance routes drawn this frame.</summary>
    public int MapRoutes { get; set; }

    /// <summary>Markers whose position came from interpolation this frame.</summary>
    public int MapInterpolated { get; set; }

    /// <summary>Markers drawn on the raw captured position this frame.</summary>
    public int MapSnapped { get; set; }

    /// <summary>Entities holding a movement track.</summary>
    public int MapTracked { get; set; }

    public int TerrainWidth { get; set; }
    public int TerrainHeight { get; set; }
    public float MapScale { get; set; }
    public EasyExile.Core.Spatial.Vector2 MapCentre { get; set; }

    public double MapFrameHz { get; private set; }
    public double MapFrameMilliseconds { get; private set; }
    public long MapFrameFailures { get; private set; }

    private readonly Stopwatch _sinceMapFrame = Stopwatch.StartNew();

    /// <summary>One fast-lane capture, timed.</summary>
    public void RecordMapFrame(double milliseconds, bool captured)
    {
        MapFrameMilliseconds = Blend(MapFrameMilliseconds, milliseconds);

        var elapsed = _sinceMapFrame.Elapsed.TotalSeconds;
        _sinceMapFrame.Restart();

        if (elapsed > 0) MapFrameHz = Blend(MapFrameHz, 1.0 / elapsed);

        if (!captured) MapFrameFailures++;
    }

    public void ResetMapCounters()
    {
        MapEntitiesReceived = 0;
        MapEntitiesDrawn = 0;
        MapEntitiesOutside = 0;
        MapEntitiesIgnored = 0;

        MapNormal = 0;
        MapMagic = 0;
        MapRare = 0;
        MapUnique = 0;
        MapPois = 0;
        MapPoisDrawn = 0;
        MapPoisUnplaced = 0;
        MapLandmarks = 0;
        MapLandmarksDrawn = 0;
        MapMechanics = 0;
        MapThreats = 0;
        MapRemembered = 0;
        HpBars = 0;
        HpBarThreats = 0;
        LootPriced = 0;
        LootTags = 0;
        LootTagsMatched = 0;
        LootTagsUnpriced = 0;
        LootTagsBelowFloor = 0;
        LootTagsFiltered = 0;
        SlotsHighlighted = 0;
        MapPois = 0;
        MapPoisDrawn = 0;
        MapPoisUnplaced = 0;
    }

    public void BeginFrame()
    {
        var elapsed = _frame.Elapsed.TotalMilliseconds;
        _frame.Restart();

        Frames++;

        if (elapsed <= 0) return;

        FrameMilliseconds = Blend(FrameMilliseconds, elapsed);
        RenderFps = Blend(RenderFps, 1000.0 / elapsed);
    }

    public void RecordProjection(double microseconds) =>
        ProjectionMicroseconds = Blend(ProjectionMicroseconds, microseconds);

    /// <summary>Zeroes the per-frame counters the entity pass fills in.</summary>
    public void ResetEntityCounters()
    {
        EntitiesReceived = 0;
        EntitiesRendered = 0;
        OnScreen = 0;
        OffScreen = 0;
        BehindCamera = 0;
        InvalidProjection = 0;
    }

    private static double Blend(double current, double sample) =>
        current <= 0 ? sample : (current * (1 - Smoothing)) + (sample * Smoothing);
}
