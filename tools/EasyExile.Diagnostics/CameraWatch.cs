using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Diagnostics;

/// <summary>
/// Watches the camera viewport and one projected world point while the user
/// opens and closes a UI panel.
/// </summary>
/// <remarks>
/// A price chip landed roughly 360px right of the item it belonged to, but only
/// with the inventory open. Two explanations fit that screenshot and they need
/// opposite fixes: either the game shrinks the world viewport when a panel
/// covers part of the window — in which case the viewport we read changes and
/// our projection is doing the right arithmetic over the wrong rectangle — or
/// the viewport is constant and the error is somewhere else entirely.
///
/// Reading the viewport twice a second while the panel is toggled separates
/// those in one run. The player is the reference point because the player is
/// the one thing the user can see and locate on screen without any tag.
/// </remarks>
public static class CameraWatch
{
    public static int Run(GameSession session, int width, int height)
    {
        // Also to a file. A watch that only prints is useless the moment the
        // person running it and the person reading it are not the same.
        var log = Path.Combine(AppContext.BaseDirectory, "camera-watch.txt");
        var lines = new List<string>();

        void Say(string line)
        {
            Console.WriteLine(line);
            lines.Add(line);
        }

        Console.WriteLine();
        Say("== CAMERA ==================================================");
        Say($" janela {width}x{height}");
        Say(" abra e feche o inventario algumas vezes. 45 segundos.");
        Console.WriteLine();
        Say(" viewport      player na tela      matriz[12..15]");

        var deadline = DateTime.UtcNow.AddSeconds(45);
        var previous = string.Empty;
        var samples = 0;
        var changes = 0;

        while (DateTime.UtcNow < deadline)
        {
            var frame = session.CaptureMapFrame();
            var camera = frame?.Camera;

            if (camera is null)
            {
                Console.WriteLine(" sem camera");
                Thread.Sleep(500);
                continue;
            }

            var at = camera.Project(frame!.PlayerWorld);

            var line =
                $" {camera.Width,5}x{camera.Height,-5}  " +
                $"{at.Screen.X,7:0}, {at.Screen.Y,-7:0} {at.Status,-12}" +
                $"  {camera.ViewProjection[12]:0.000} {camera.ViewProjection[13]:0.000} " +
                $"{camera.ViewProjection[14]:0.000} {camera.ViewProjection[15]:0.000}";

            // Only the changes matter, and a still line every 500ms buries
            // them — but a run that prints ONE line is ambiguous between "the
            // panel changed nothing" and "the panel was never opened", so the
            // sample count is printed too.
            if (line != previous)
            {
                Say($" [{samples,3}] {line}");
                previous = line;
                changes++;
            }

            samples++;

            Thread.Sleep(500);
        }

        Console.WriteLine();
        Say($" {samples} amostras, {changes} mudancas.");
        Say(changes <= 1
            ? " NADA MUDOU: ou o painel nao foi aberto, ou abrir o painel nao mexe na camera."
            : " a camera mudou durante o teste — compare as linhas acima.");

        File.WriteAllLines(log, lines);
        Console.WriteLine($" gravado em {log}");

        return 0;
    }
}
