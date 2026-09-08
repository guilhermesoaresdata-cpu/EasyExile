using EasyExile.Core.Runtime;
using EasyExile.Radar.Features;
using EasyExile.Radar.Features.NativeMap;
using EasyExile.Radar.Features.World;
using EasyExile.Radar.Overlay;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;
using EasyExile.Radar.UI;

namespace EasyExile.Radar.Runtime;

public enum RadarStartFailure
{
    ClientNotRunning,
    BuildMismatch,
    AttachRefused,
}

/// <summary>
/// Owns the session, the features and the overlay for as long as the radar runs.
/// </summary>
public sealed class RadarApplication : IDisposable
{
    private readonly GameSession _session;
    private readonly OverlayHost _host;

    private bool _disposed;

    private RadarApplication(GameSession session, RadarSettings settings)
    {
        _session = session;
        Settings = settings;
        Stats = new RadarStats();

        var updates = new RadarUpdateLoop(session, settings);

        var worldDebug = new DebugWorldFeature(settings, Stats);

        // The texture upload is the overlay's, so the feature is handed the call
        // rather than the window: it stays testable against a stub.
        NativeMap = new NativeMapRadarFeature(settings, Stats, UploadTexture);

        // Navigation owns a worker thread, so it is created once here and given
        // to both the loop that maintains it and the feature that draws it.
        Navigator = new Features.Navigation.Navigator();

        // Beside the executable, like the settings: a learned graph and a
        // recorded route belong to the installation, not to the repository.
        var beside = AppContext.BaseDirectory;

        Areas = new Features.Levelling.AreaGraph(Path.Combine(beside, "areas.txt"));
        Route = Features.Levelling.LevelRoute.Load(Path.Combine(beside, "route.txt"));

        // One price index for the session, cached to disk beside the executable.
        Prices = new Pricing.PriceBook(
            System.IO.Path.Combine(AppContext.BaseDirectory, "prices.json"));

        // How many tiers each mod family has. Read once: it is four hundred
        // kilobytes that never change while the game does not.
        ModTiers = Pricing.ModTierTable.Load(Path.Combine(beside, "modtiers.json"));
        Supports = Pricing.SupportAdvice.Load(Path.Combine(beside, "supports.json"));

        NativeMap.UseNavigator(Navigator);
        updates.UseNavigator(Navigator);
        updates.UseAutoRoute(NativeMap.IsNavigable);

        // The native map first, then the world HUD over it.
        var features = new IRadarFeature[]
        {
            NativeMap,

            // The other half of the same navigation state: the map draws the
            // route while it is open, this draws it on the ground while it is
            // not. Neither ever draws both.
            new Features.Navigation.WorldRouteFeature(settings, Stats, Navigator),

            // A combat overlay: it draws whether the game's map is open or not.
            new Features.HpBars.MonsterHpBars(settings, Stats),

            new Features.Loot.LootValuesFeature(settings, Stats, Prices),
            // The cursor is handed in as a function rather than a value: it
            // moves every frame and the feature draws every frame, while the
            // slots it hit-tests are refreshed at world rate.
            Guide = new Features.Levelling.RouteGuideFeature(
                settings, Navigator, Areas, Route,
                Path.Combine(AppContext.BaseDirectory, "route.txt"),
                Features.Levelling.CampaignGuide.Load(
                    Path.Combine(AppContext.BaseDirectory, "campaign.txt")),
                new Features.Levelling.CampaignJournal(
                    Path.Combine(AppContext.BaseDirectory, "campaign-journal.txt")),
                Features.Levelling.CampaignGuide.Load(
                    Path.Combine(AppContext.BaseDirectory, "campaign.en.txt"))),
            new Features.Loot.SlotHighlightFeature(settings, Stats, Prices, Cursor),
            new Features.Loot.HoverPriceFeature(settings, Prices, Cursor),
            new Features.Loot.ModTierFeature(settings, ModTiers, Cursor),
            new Features.Loot.SkillSupportFeature(settings, Supports, Cursor),
            new Features.Loot.BuildTagFeature(settings, Cursor),
            new Features.Loot.ResistanceFeature(settings, Cursor),

            worldDebug,
            new PlayerWorldFeature(settings),

            // Last, so it draws over everything. What it says is only ever
            // needed while something else is being measured, and a request
            // hidden behind a health bar is a request nobody answers.
            new Features.ProbePromptFeature(settings),
        };

        // The window is followed by process, never by name. Path of Exile 1 and 2
        // share a process name and the Core already settled which one this is by
        // fingerprint; searching again from here could undo that.
        var tracker = new GameWindowTracker(session.Process);

        var panel = new SettingsWindow(settings, worldDebug, NativeMap, Navigator, Guide, Areas, Route);

        panel.UsePrices(Prices);

        Render = new RadarRenderLoop(
            updates, settings, Stats, features, tracker, panel,
            updates.DumpUiTree, updates.PickElement);

        _host = new OverlayHost(updates, Render);
    }

    public RadarSettings Settings { get; }

    public RadarStats Stats { get; }

    /// <summary>poe.ninja prices, refreshed on a TTL.</summary>
    public Pricing.PriceBook Prices { get; }

    public Pricing.ModTierTable ModTiers { get; }

    /// <summary>Which supports go in a skill, best first.</summary>
    public Pricing.SupportAdvice Supports { get; }

    /// <summary>Routes to the selected destinations.</summary>
    public Features.Navigation.Navigator Navigator { get; }

    /// <summary>
    /// Where the pointer is, in client pixels.
    /// </summary>
    /// <remarks>
    /// Handed to features as a function rather than a value: it moves every
    /// frame, and the slots it is tested against are refreshed at world rate.
    /// </remarks>
    private static EasyExile.Core.Spatial.Vector2 Cursor()
    {
        var at = ImGuiNET.ImGui.GetMousePos();

        return new EasyExile.Core.Spatial.Vector2(at.X, at.Y);
    }

    /// <summary>What the player has learned about the world.</summary>
    public Features.Levelling.AreaGraph Areas { get; }

    /// <summary>The campaign route being followed, or recorded.</summary>
    public Features.Levelling.LevelRoute Route { get; }

    /// <summary>The guide, exposed so the panel can show which step is current.</summary>
    public Features.Levelling.RouteGuideFeature Guide { get; }

    /// <summary>Drawing on the game's own map. The main feature.</summary>
    public NativeMapRadarFeature NativeMap { get; }

    public RadarRenderLoop Render { get; }

    public string BuildFingerprint => _session.Build.Fingerprint;

    /// <summary>
    /// Attaches, or explains why not. The build gate is not bypassable here or
    /// anywhere else: against the wrong client every offset reads a different
    /// field and reports it with confidence.
    /// </summary>
    public static bool TryStart(
        RadarSettings settings, out RadarApplication? application, out RadarStartFailure failure, out string detail)
    {
        application = null;
        detail = string.Empty;

        var process = GameSession.FindClient();
        if (process is null)
        {
            failure = RadarStartFailure.ClientNotRunning;
            detail = RadarText.ClientNotRunning;
            return false;
        }

        try
        {
            application = new RadarApplication(GameSession.Attach(process), settings);
            failure = default;
            return true;
        }
        catch (BuildMismatchException mismatch)
        {
            failure = RadarStartFailure.BuildMismatch;
            detail = mismatch.Check.Detail;
            return false;
        }
        catch (Exception error) when (error is InvalidOperationException or FileNotFoundException)
        {
            failure = RadarStartFailure.AttachRefused;
            detail = error.Message;
            return false;
        }
    }

    public Task RunAsync(CancellationToken cancellationToken) => _host.RunAsync(cancellationToken);

    /// <summary>Hands a texture to the overlay and returns its ImGui handle.</summary>
    private nint UploadTexture(string name, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image, bool srgb) =>
        _host.UploadTexture(name, image, srgb);

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        _host.Dispose();
        Navigator.Dispose();
        _session.Dispose();
    }
}
