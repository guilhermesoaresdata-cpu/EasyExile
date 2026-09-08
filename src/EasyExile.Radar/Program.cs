using EasyExile.Radar.Overlay;
using EasyExile.Radar.Runtime;
using EasyExile.Radar.Settings;

// Before any window exists, and therefore before anything else. On a scaled
// display an unaware process is fed virtualised coordinates, and the overlay
// lands somewhere near the game rather than on it.
var dpiMode = Native.EnableDpiAwareness();

var settings = new RadarSettings();

// Whatever was chosen last time, applied over the defaults.
SettingsStore.Load(settings);

if (!RadarApplication.TryStart(settings, out var started, out var failure, out var detail) || started is null)
{
    RadarText.Report(failure == RadarStartFailure.BuildMismatch
        ? $"{RadarText.BuildMismatch}\n\n{detail}\n\n{RadarText.Refusing}"
        : detail);

    return failure == RadarStartFailure.BuildMismatch ? 3 : 2;
}

using var radar = started;
using var stopping = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stopping.Cancel();
};

RadarText.Trace($"dpi awareness: {dpiMode}");
RadarText.Trace($"build: {radar.BuildFingerprint}");

await radar.RunAsync(stopping.Token).ConfigureAwait(false);

// On the way out, not on every change: the panel mutates settings dozens of
// times while a checkbox is being dragged, and writing a file each time would
// be work done for nobody.
SettingsStore.Save(settings);

return 0;
