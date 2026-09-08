namespace EasyExile.Core.Diagnostics;

/// <summary>
/// What the sweep found on screen, written down for reading later.
/// </summary>
/// <remarks>
/// This exists because catching the answer live kept failing. A probe can only
/// see what is on screen while it runs, so every question about a panel needed
/// the panel open at that instant — and across this session that coincidence
/// happened perhaps one time in five, while each miss came back looking exactly
/// like a real negative result.
///
/// Writing it down removes the coincidence entirely: the overlay is already
/// there, already sweeping, and already knows. Play normally, then read the
/// file.
///
/// Throttled, appended, and capped, because a debugging aid that fills a disk
/// during a campaign is not one.
/// </remarks>
public static class CaptionLog
{
    private static readonly object Gate = new();

    private static DateTime _lastWrite = DateTime.MinValue;

    /// <summary>Where it lands, beside the other thing the two processes share.</summary>
    public static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "easyexile", "captions.txt");

    /// <summary>How often a snapshot is worth keeping.</summary>
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>
    /// Record this pass, if enough time has gone by since the last one.
    /// </summary>
    public static void Write(IEnumerable<(string Text, float X, float Y, float W, float H)> captions)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;

            if (now - _lastWrite < Interval) return;

            _lastWrite = now;

            try
            {
                var directory = System.IO.Path.GetDirectoryName(Path);

                if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

                // Capped rather than rotated: the interesting pass is whichever
                // one the player was looking at, and they will say roughly when.
                if (File.Exists(Path) && new FileInfo(Path).Length > MaxBytes) return;

                var lines = captions
                    .Select(c => $"  ({c.X:0},{c.Y:0}) {c.W:0}x{c.H:0}  {c.Text}")
                    .ToList();

                if (lines.Count == 0) return;

                File.AppendAllLines(
                    Path,
                    new[] { $"== {now:HH:mm:ss}  {lines.Count} legendas" }.Concat(lines));
            }
            catch (IOException)
            {
                // A log that cannot be written must not take the overlay down.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
