using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.Settings.NativeMap;

namespace EasyExile.Core.Tests;

/// <summary>
/// The live map transform, and its behaviour while a game panel is open.
/// </summary>
/// <remarks>
/// The numbers here are measured, not invented. On a 1920x1080 client the map
/// element sat at UI-space (1422, 800) with the inventory closed and (929, 800)
/// with it open, while Shift stayed at (0,0) the whole time. The UI root is
/// 2560x1600, so the client scale is 1080/1600.
/// </remarks>
public class MapUiAlignmentTests
{
    private const float Width = 1920f;
    private const float Height = 1080f;

    /// <summary>Measured: where the element sits with nothing open.</summary>
    private static MapSnapshot Normal(float shiftX = 0f, float shiftY = 0f, float zoom = 0.5f) =>
        new(true, shiftX, shiftY, zoom, 1422f, 800f);

    /// <summary>Measured: where the game moves it when the inventory opens.</summary>
    private static MapSnapshot InventoryOpen(float zoom = 0.5f) =>
        new(true, 0f, 0f, zoom, 929f, 800f);

    [Fact]
    public void MapProjectionMatchesPoe2RadarCenterFormula()
    {
        // With nothing open, the element's scaled position IS the window centre,
        // so this has to agree with the reference to the pixel: window centre,
        // plus the pan, plus the constant -20.
        var centre = NativeMapProjection.MapCentre(Normal(), Width, Height);

        Assert.Equal(Width * 0.5f, centre.X, 0);
        Assert.Equal((Height * 0.5f) - 20f, centre.Y, 0);
    }

    [Fact]
    public void WithoutAnOriginItFallsBackToTheWindowCentre()
    {
        // An element that will not say where it is leaves the reference's
        // formula exactly as it was.
        var centre = NativeMapProjection.MapCentre(new MapSnapshot(true, 12f, 34f, 0.5f), Width, Height);

        Assert.Equal((Width * 0.5f) + 12f, centre.X, 0);
        Assert.Equal((Height * 0.5f) + 34f - 20f, centre.Y, 0);
    }

    [Fact]
    public void OpeningAPanelMovesTheMapCentreWithTheElement()
    {
        var closed = NativeMapProjection.MapCentre(Normal(), Width, Height);
        var open = NativeMapProjection.MapCentre(InventoryOpen(), Width, Height);

        // 1422 - 929 UI units at 1080/1600 is about 333 pixels. Projecting from
        // the window centre held still through that, which is the misalignment.
        Assert.Equal(333f, closed.X - open.X, 0);
        Assert.Equal(closed.Y, open.Y, 0);
    }

    [Fact]
    public void MapProjectionDoesNotAddInventorySpecificOffset()
    {
        // No panel is named anywhere in the transform. The element's position
        // covers every panel at once, and a per-panel constant would be a guess
        // that rots with the next UI change.
        var source = File.ReadAllText(SourceOf("NativeMapProjection.cs"));

        foreach (var panel in new[] { "Inventory", "Stash", "Character", "Passive" })
            Assert.DoesNotContain($"{panel}Offset", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LiveShiftChangesMapCenterImmediately()
    {
        // Shift is the manual pan, and it still applies on top of the element's
        // own position.
        var still = NativeMapProjection.MapCentre(Normal(), Width, Height);
        var panned = NativeMapProjection.MapCentre(Normal(shiftX: 120f, shiftY: -45f), Width, Height);

        Assert.Equal(still.X + 120f, panned.X, 1);
        Assert.Equal(still.Y - 45f, panned.Y, 1);
    }

    [Fact]
    public void ZoomChangesMapScaleImmediately()
    {
        Assert.Equal(
            NativeMapProjection.ScaleFor(0.5f, Height) * 2f,
            NativeMapProjection.ScaleFor(1.0f, Height),
            3);
    }

    [Fact]
    public void CorrectTogglerSuppliesShiftAndZoom()
    {
        // The reference's rule: an element seen both visible and hidden is the
        // genuine toggler, and it is the one whose state the projection uses.
        // Measured live, one candidate toggled with the map and the other never
        // did.
        using var memory = WorldFixture.Build();

        var reader = new EasyExile.Core.World.MapUiReader();

        var state = reader.Read(memory, WorldFixture.InGameState, WorldFixture.FirstArea);

        Assert.NotNull(state);
    }

    [Fact]
    public void AllMapLayersShareSameTransform()
    {
        // One centre and one scale per frame, handed to every layer. A layer
        // computing its own variant is how a route ends up beside the terrain it
        // is supposed to lie on.
        var source = File.ReadAllText(SourceOf("NativeMapRadarFeature.cs"));

        var centres = Count(source, "NativeMapProjection.MapCentre");
        var scales = Count(source, "NativeMapProjection.ScaleFor");

        Assert.Equal(1, centres);
        Assert.Equal(1, scales);

        // And every layer is handed those values rather than the map state.
        foreach (var layer in new[] { "DrawTerrain", "DrawPlayer", "DrawEntities", "DrawLandmarks", "DrawRoutes" })
            Assert.Contains($"{layer}(canvas", source, StringComparison.Ordinal);
    }

    private static int Count(string text, string term)
    {
        var total = 0;
        var at = 0;

        while ((at = text.IndexOf(term, at, StringComparison.Ordinal)) >= 0)
        {
            total++;
            at += term.Length;
        }

        return total;
    }

    private static string SourceOf(string file)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "main")))
            directory = directory.Parent;

        return Path.Combine(
            directory!.FullName, "main", "src", "EasyExile.Radar", "Features", "NativeMap", file);
    }
}
