using EasyExile.Radar.Runtime;

namespace EasyExile.Radar.Overlay;

/// <summary>
/// Owns the overlay window's lifetime and the two loops that feed it.
/// </summary>
/// <remarks>
/// Two independent rates meet here and nowhere else: the capture loop runs on
/// the thread pool at the configured Hz, the render loop runs on the thread the
/// overlay owns at whatever the display allows. They share exactly one field,
/// and it holds an immutable snapshot.
/// </remarks>
internal sealed class OverlayHost : IDisposable
{
    private readonly RadarUpdateLoop _updates;
    private readonly RadarRenderLoop _render;

    private OverlayWindow? _window;
    private CancellationTokenSource? _stopping;
    private Task _capturing = Task.CompletedTask;
    private Task _sweeping = Task.CompletedTask;

    public OverlayHost(RadarUpdateLoop updates, RadarRenderLoop render)
    {
        _updates = updates;
        _render = render;
    }

    /// <summary>Runs until the client goes away or the caller cancels.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _window = new OverlayWindow(DrawFrame, StartCapturing, _updates.Settings.General.RenderFps);

        try
        {
            // Returns when the overlay closes, which is either the client
            // disappearing or the caller cancelling.
            await _window.Run().ConfigureAwait(false);
        }
        finally
        {
            _stopping.Cancel();

            // The capture loop only ends on cancellation, so this is a join and
            // not a wait for work.
            try
            {
                await _capturing.ConfigureAwait(false);
                await _sweeping.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    private Task StartCapturing()
    {
        _capturing = Task.Run(() => _updates.RunAsync(_stopping!.Token), CancellationToken.None);

        // Its own thread, so a UI-tree walk never stops the world walk or the
        // drawing. It has its own memory reader too: sharing one would turn
        // three independent cadences back into a queue.
        _sweeping = Task.Run(() => _updates.SweepUiAsync(_stopping!.Token), CancellationToken.None);

        return Task.CompletedTask;
    }

    private void DrawFrame(OverlayWindow window)
    {
        if (_stopping!.IsCancellationRequested)
        {
            window.Close();
            return;
        }

        // A feature throwing must not take the overlay down with it. The first
        // failure is reported in full and the rest are counted, because a broken
        // feature at render rate would otherwise produce thousands of identical
        // lines a second.
        try
        {
            _render.DrawFrame(window);
        }
        catch (Exception error)
        {
            FrameFailures++;

            if (FirstFrameFailure is null)
            {
                FirstFrameFailure = error.ToString();
                Runtime.RadarText.Trace($"frame failure: {error}");
            }

            return;
        }

        // The client exiting is a normal end, not a failure: the radar has
        // nothing left to describe, so it stops instead of drawing a frozen
        // world over an empty desktop.
        if (_render.Disconnected) window.Close();
    }

    /// <summary>
    /// Uploads a texture through the overlay window. Returns 0 before the window
    /// exists, which the caller treats as "not ready yet" rather than an error.
    /// </summary>
    public long FrameFailures { get; private set; }

    public string? FirstFrameFailure { get; private set; }

    public nint UploadTexture(string name, SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32> image, bool srgb)
    {
        if (_window is null) return 0;

        // AddOrGet, emphasis on Get: an existing entry under this name is
        // returned as-is and the new image is dropped on the floor. That is what
        // kept the map showing the first area's terrain after a zone change, so
        // the old entry goes first and the name is the caller's problem to keep
        // unique.
        _window.RemoveImage(name);

        _window.AddOrGetImagePointer(name, image, srgb, out var handle);

        return handle;
    }

    public void Dispose()
    {
        _stopping?.Cancel();
        _window?.Dispose();
        _stopping?.Dispose();
    }
}
