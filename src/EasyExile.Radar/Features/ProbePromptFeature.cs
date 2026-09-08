using EasyExile.Core.Diagnostics;
using EasyExile.Core.Spatial;
using EasyExile.Radar.Features.Levelling;
using EasyExile.Radar.Rendering;
using EasyExile.Radar.Settings;

namespace EasyExile.Radar.Features;

using static EasyExile.Radar.UI.Text;

/// <summary>
/// Draws what a running analysis needs the player to do, and how long it will wait.
/// </summary>
/// <remarks>
/// A whole session was lost this way. Probe after probe asked for the mouse to
/// rest on an item; each request went to a terminal behind the game; every
/// window closed on nothing. The runs were not failing, they were never being
/// answered — and an unanswered run reads exactly like a negative result, which
/// is how I came to report twice that the client did not have something it had
/// all along.
///
/// So it goes at the top of the screen, in the middle, big enough to catch the
/// eye of someone who is looking at their character. The countdown is the part
/// that makes it actionable: an instruction with no deadline is a suggestion.
/// </remarks>
public sealed class ProbePromptFeature : IRadarFeature
{
    private readonly RadarSettings _settings;
    private readonly Func<(string Message, TimeSpan Left)?> _prompt;

    public ProbePromptFeature(RadarSettings settings)
        : this(settings, ProbePrompt.Current)
    {
    }

    /// <summary>
    /// With the prompt source handed in.
    /// </summary>
    /// <remarks>
    /// The default reads a file, and a feature that can only be exercised by
    /// writing to the filesystem is a feature whose drawing goes untested.
    /// </remarks>
    public ProbePromptFeature(
        RadarSettings settings, Func<(string Message, TimeSpan Left)?> prompt)
    {
        _settings = settings;
        _prompt = prompt;
    }

    public string Name => T("Pedidos da analise");

    public bool Enabled => _settings.General.ShowProbePrompts;

    public void Draw(RenderFrame frame, IOverlayCanvas canvas)
    {
        if (!Enabled) return;
        if (_prompt() is not { } prompt) return;

        // Latin-1 is all the default font has, and an accented request rendered
        // as question marks is a request nobody can follow.
        var message = StepPanel.Ascii(prompt.Message);
        var clock = $"{Math.Ceiling(prompt.Left.TotalSeconds):0}s";

        var bounds = frame.ClientBounds;

        if (bounds.IsEmpty) return;

        const float scale = 1.35f;
        const float clockScale = 1.15f;
        const float padding = 14f;

        var messageSize = canvas.MeasureText(message, scale);
        var clockSize = canvas.MeasureText(clock, clockScale);

        var boxWidth = messageSize.X + clockSize.X + (padding * 3f);
        var boxHeight = Math.Max(messageSize.Y, clockSize.Y) + (padding * 1.4f);

        var left = (bounds.Width - boxWidth) / 2f;

        // Below the very top edge, which is where the client keeps its own
        // banners, and well above anything the player is reading.
        var top = bounds.Height * 0.06f;

        var min = new Vector2(left, top);
        var max = new Vector2(left + boxWidth, top + boxHeight);

        canvas.Rect(min, max, Palette.Rgba(10, 10, 14));
        canvas.Rect(min, max, Palette.Warning, filled: false);

        canvas.Text(
            new Vector2(left + padding, top + (boxHeight - messageSize.Y) / 2f),
            Palette.Rgba(255, 236, 168), message, scale);

        // The seconds run out on the right, where they do not push the words
        // around as the number narrows from two digits to one.
        canvas.Text(
            new Vector2(max.X - padding - clockSize.X, top + (boxHeight - clockSize.Y) / 2f),
            prompt.Left.TotalSeconds <= 5 ? Palette.Bad : Palette.Warning,
            clock, clockScale);
    }
}
