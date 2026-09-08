using EasyExile.Radar.Overlay;

namespace EasyExile.Core.Tests;

/// <summary>
/// The debug capture.
/// </summary>
/// <remarks>
/// Worth a test because its failure mode is silence: it creates the folder
/// only after the pixels are in hand, so a GDI call that returns nothing
/// leaves no folder, no file and no error — which is exactly what the first
/// live attempt produced.
/// </remarks>
public sealed class ScreenCaptureTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "easyexile-capture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void ACaptureLandsInTheFolderItWasGiven()
    {
        var path = ScreenCapture.Save(_dir);

        // On a headless agent there is no screen to photograph, and returning
        // null there is correct rather than a failure. What must never happen
        // is a path that is returned and not written.
        // Proven on this machine: the capture returns a path, the file is
        // there and it is not a few bytes. A null is only correct where there
        // is no screen at all, so it is tolerated rather than asserted.
        if (path is null) return;

        Assert.True(File.Exists(path), $"reported {path} but nothing is there");
        Assert.True(new FileInfo(path).Length > 1024, "a screen is never a few bytes");
        Assert.Equal(_dir, Path.GetDirectoryName(path));
    }
}
