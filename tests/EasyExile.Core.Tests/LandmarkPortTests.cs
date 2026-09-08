using System.Collections.Immutable;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// Tile landmarks, ported from POE2Radar's <c>ScanLandmarks</c>.
/// </summary>
public class LandmarkPortTests
{
    private static WorldSnapshot World(params LandmarkSnapshot[] marks) =>
        RadarFixture.World() with
        {
            Terrain = new TerrainSnapshot(new byte[16 * 16], 16, 16),
            Landmarks = marks.ToImmutableArray(),
        };

    private static RecordingCanvas Draw(WorldSnapshot world, NativeMapSettings? options = null)
    {
        var settings = new RadarSettings { NativeMap = options ?? new NativeMapSettings() };
        var feature = new NativeMapRadarFeature(settings, new RadarStats(), (_, _, _) => 0x1234);
        var canvas = new RecordingCanvas();

        feature.Draw(RadarFixture.Frame(world) with { MapFrame = RadarFixture.MapFrame(world) }, canvas);

        return canvas;
    }

    [Fact]
    public void TheCuratedDataLoads()
    {
        var data = CuratedLandmarks.Load();

        // Embedded as a resource. If the build stops embedding it the lookup
        // silently returns nothing and every landmark quietly disappears.
        Assert.NotEmpty(data);
        Assert.True(data.ContainsKey("G2_5_1"), "the probed area must be in the curated data");
    }

    [Theory]
    [InlineData("G2_5_1", "Metadata/Terrain/Desert/Badlands/Features/AbyssHole.tdt", "Lightless Passage")]
    [InlineData("G2_5_1", "Metadata/Terrain/Desert/Badlands/Features/Badlands_Entrance_01.tdt", "The Ardura Caravan")]
    public void CuratedLabelsResolve(string area, string path, string expected)
    {
        // The reference's keys carry a ":x-y:" qualifier and use .tdtx; the live
        // client hands back .tdt with no qualifier. The loader normalises both,
        // and getting that wrong means every lookup misses.
        Assert.Equal(expected, CuratedLandmarks.Match(area, path));
    }

    [Fact]
    public void AnUncuratedTileIsNotALandmark()
    {
        // Only curated tiles surface. The reference dropped its keyword sweep
        // because it turned every decorative vault door into a marker.
        Assert.Null(CuratedLandmarks.Match("G2_5_1", "Metadata/Terrain/Desert/Badlands/BoneEdge_Cv_01.tdt"));
    }

    [Fact]
    public void ALandmarkIsDrawnWithItsLabel()
    {
        var canvas = Draw(World(new LandmarkSnapshot(
            "The Ardura Caravan", "Metadata/Terrain/x.tdt", new Vector2(0, 0), 12)));

        Assert.Contains(canvas.Polygons, p => p.Colour == Palette.Landmark);
        Assert.Contains(canvas.Texts, t => t.Text == "The Ardura Caravan");
    }

    [Fact]
    public void LandmarksCanBeTurnedOff()
    {
        var canvas = Draw(
            World(new LandmarkSnapshot("Boss", "Metadata/Terrain/x.tdt", new Vector2(0, 0), 3)),
            new NativeMapSettings { ShowLandmarks = false });

        Assert.DoesNotContain(canvas.Polygons, p => p.Colour == Palette.Landmark);
    }

    /// <summary>
    /// Two clusters of the same tile path stay two landmarks. Averaging them
    /// would drop one marker in the empty space between, pointing at nothing.
    /// </summary>
    [Fact]
    public void SeparateClustersOfOnePathStaySeparate()
    {
        var canvas = Draw(World(
            new LandmarkSnapshot("Stairs", "Metadata/Terrain/s.tdt", new Vector2(0, 0), 4),
            new LandmarkSnapshot("Stairs", "Metadata/Terrain/s.tdt", new Vector2(40, 40), 4)));

        Assert.Equal(2, canvas.Texts.Count(t => t.Text == "Stairs"));
    }

    /// <summary>
    /// Two of the same name on one pixel is one label.
    /// </summary>
    /// <remarks>
    /// The opposite of the test above, and both matter. Clusters far apart are
    /// two places and get two markers; clusters that land on top of each other
    /// are one word written over itself. Measured on a live map, where
    /// Freythorn, Entrance, Checkpoint and Dryadic Ritual were each printed
    /// twice and all four were unreadable for it.
    /// </remarks>
    [Fact]
    public void TwoOfTheSameNameOnOnePixelIsOneLabel()
    {
        var canvas = Draw(World(
            new LandmarkSnapshot("Freythorn", "Metadata/Terrain/f.tdt", new Vector2(0, 0), 4),
            new LandmarkSnapshot("Freythorn", "Metadata/Terrain/f.tdt", new Vector2(1, 1), 4)));

        Assert.Equal(1, canvas.Texts.Count(t => t.Text == "Freythorn"));
    }

    [Fact]
    public void ALandmarkKeyIsPerClusterNotPerPath()
    {
        var a = new LandmarkSnapshot("Stairs", "Metadata/Terrain/s.tdt", new Vector2(0, 0), 4);
        var b = new LandmarkSnapshot("Stairs", "Metadata/Terrain/s.tdt", new Vector2(40, 40), 4);

        Assert.NotEqual(a.Key, b.Key);
    }

    [Fact]
    public void AnAreaWithoutLandmarksDrawsNone()
    {
        Assert.Empty(Draw(World()).Polygons);
    }
}
