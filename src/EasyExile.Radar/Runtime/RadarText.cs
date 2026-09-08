using System.Diagnostics;

namespace EasyExile.Radar.Runtime;

/// <summary>
/// User-facing strings for the radar, kept out of the code that produces them.
/// Same reasoning as the Core's equivalent: this is not a localization system,
/// it is the shape one would need. Default is pt-BR.
/// </summary>
internal static class RadarText
{
    public const string ClientNotRunning = "PathOfExile nao esta rodando";
    public const string BuildMismatch = "OFFSETS BUILD MISMATCH";
    public const string Refusing = "RECUSANDO EXECUTAR";
    public const string Waiting = "aguardando o primeiro snapshot";
    public const string Stopped = "encerrado";

    /// <summary>
    /// Tells the user why the radar will not start.
    /// </summary>
    /// <remarks>
    /// The radar is a windowed application with no console of its own, so a
    /// refusal printed to stdout would be seen by nobody. A message box is the
    /// only channel that reaches somebody who launched this from Explorer.
    /// </remarks>
    public static void Report(string message)
    {
        Trace(message);
        MessageBox(0, message, "EasyExile", MbOk | MbIconWarning | MbSetForeground);
    }

    /// <summary>
    /// Diagnostics for a developer, never for a frame. Goes to the debugger and
    /// to the console when one happens to be attached.
    /// </summary>
    public static void Trace(string message)
    {
        Debug.WriteLine(message);
        Console.WriteLine(message);
    }

    private const int MbOk = 0x00000000;
    private const int MbIconWarning = 0x00000030;
    private const int MbSetForeground = 0x00010000;

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int MessageBox(nint owner, string text, string caption, int type);
}
