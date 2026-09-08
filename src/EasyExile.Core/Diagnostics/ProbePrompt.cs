namespace EasyExile.Core.Diagnostics;

/// <summary>
/// What a running probe needs the player to do, put where the player is looking.
/// </summary>
/// <remarks>
/// A whole session went this way: probe after probe asked for the mouse to rest
/// on an item, each one printed the request into a terminal behind the game, and
/// every window closed on nothing. The runs were not failing — they were never
/// being answered, and twice I read the empty result as a finding about the
/// client rather than about where the question had been asked.
///
/// So the request goes on the screen the player is actually watching. The probe
/// writes a line and a deadline; the overlay reads it and draws it. Nothing is
/// injected and nothing is sent to the game — it is one small file, written by
/// one process and read by another, which is the only channel the overlay's
/// rules leave open and all this needs.
///
/// The deadline is part of the message rather than a courtesy. Knowing an
/// analysis has eleven seconds left is what makes it worth walking back to the
/// keyboard for.
/// </remarks>
public static class ProbePrompt
{
    /// <summary>Where both sides agree to look.</summary>
    /// <remarks>
    /// Under the user's temp directory rather than beside either binary: the
    /// probe and the overlay run from different output folders, and a path
    /// relative to one of them is a path the other cannot guess.
    /// </remarks>
    public static string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "easyexile", "probe-prompt.txt");

    /// <summary>
    /// Ask for something, for as long as the probe will wait for it.
    /// </summary>
    public static void Show(string message, TimeSpan window)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (directory is { Length: > 0 }) Directory.CreateDirectory(directory);

            var until = DateTimeOffset.UtcNow.Add(window).ToUnixTimeMilliseconds();

            File.WriteAllText(Path, $"{until}{(char)10}{message}");
        }
        catch (IOException)
        {
            // A prompt that cannot be written must not take the probe down with
            // it. The probe still runs; it just runs unannounced, which is where
            // this started.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Take it down, whether the probe finished or gave up.</summary>
    public static void Clear()
    {
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// The request on the screen right now, and how long is left of it.
    /// </summary>
    /// <remarks>
    /// A prompt past its deadline is not shown. That covers the case the file
    /// outlives its probe — a crash, a kill, a machine that went to sleep — so a
    /// stale instruction cannot sit on the screen telling the player to do
    /// something nobody is waiting for.
    /// </remarks>
    public static (string Message, TimeSpan Left)? Current()
    {
        try
        {
            if (!File.Exists(Path)) return null;

            var lines = File.ReadAllLines(Path);

            if (lines.Length < 2) return null;
            if (!long.TryParse(lines[0], out var until)) return null;

            var left = DateTimeOffset.FromUnixTimeMilliseconds(until) - DateTimeOffset.UtcNow;

            if (left <= TimeSpan.Zero) return null;

            var message = string.Join(" ", lines.Skip(1)).Trim();

            return message.Length == 0 ? null : (message, left);
        }
        catch (IOException)
        {
            // Read while the probe is mid-write. The next frame gets it.
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
