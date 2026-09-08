using System.Text;

namespace EasyExile.Radar.Features.Levelling;

public enum StepKind
{
    /// <summary>Go to the exit that leads to this area.</summary>
    Enter,

    /// <summary>Go to the waypoint in this area.</summary>
    Waypoint,

    /// <summary>Something to do here that we cannot route to. Text only.</summary>
    Note,
}

/// <summary>One instruction. The target is an area code for Enter, otherwise free text.</summary>
public sealed record RouteStep(StepKind Kind, string Target, string Note)
{
    public bool IsRoutable => Kind is StepKind.Enter or StepKind.Waypoint;
}

/// <summary>
/// An ordered campaign route, authored in area codes.
/// </summary>
/// <remarks>
/// The format is deliberately the shape the community already writes in — one
/// instruction per line, an area code as the target — because the value of this
/// feature is not the file format, it is that the step becomes a drawn path
/// instead of a compass direction.
///
///   enter G1_2          # Clearfell
///   note  kill Beira    # north/northeast of the waypoint
///   enter G1_town
///
/// A route can also be RECORDED rather than written: walking the campaign once
/// emits exactly this, which is the only honest way to get one when no published
/// table of PoE2 area codes exists.
/// </remarks>
public sealed class LevelRoute
{
    private readonly List<RouteStep> _steps = new();

    public IReadOnlyList<RouteStep> Steps => _steps;

    public bool IsEmpty => _steps.Count == 0;

    public static LevelRoute Load(string path)
    {
        var route = new LevelRoute();

        if (!File.Exists(path)) return route;

        try
        {
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#') continue;

                var hash = line.IndexOf('#');
                var note = hash >= 0 ? line[(hash + 1)..].Trim() : string.Empty;

                if (hash >= 0) line = line[..hash].Trim();

                var space = line.IndexOf(' ');
                var verb = space > 0 ? line[..space] : line;
                var target = space > 0 ? line[(space + 1)..].Trim() : string.Empty;

                var kind = verb.ToLowerInvariant() switch
                {
                    "enter" => StepKind.Enter,
                    "waypoint" => StepKind.Waypoint,
                    _ => StepKind.Note,
                };

                if (kind == StepKind.Note && target.Length == 0 && note.Length == 0) continue;

                route._steps.Add(new RouteStep(
                    kind,
                    target,
                    note.Length > 0 ? note : (kind == StepKind.Note ? line : string.Empty)));
            }
        }
        catch (IOException)
        {
        }

        return route;
    }

    /// <summary>
    /// Appends an area to a recorded route, unless it is already the last one.
    /// </summary>
    /// <remarks>
    /// Re-entering the area you just left is normal play — going back for a
    /// waypoint, dying, a portal — and recording it would turn a route into a
    /// transcript of someone's mistakes.
    /// </remarks>
    public bool Record(string code)
    {
        if (code.Length == 0) return false;

        if (_steps.Count > 0 &&
            _steps[^1].Kind == StepKind.Enter &&
            string.Equals(_steps[^1].Target, code, StringComparison.OrdinalIgnoreCase))
            return false;

        _steps.Add(new RouteStep(StepKind.Enter, code, string.Empty));
        return true;
    }

    public void Save(string path, Func<string, string> name)
    {
        var text = new StringBuilder();

        text.AppendLine("# EasyExile levelling route. One step per line.");
        text.AppendLine("#   enter <area code>   walk to the exit that leads there");
        text.AppendLine("#   waypoint            walk to this area's waypoint");
        text.AppendLine("#   anything else       a note, shown but not routed");
        text.AppendLine();

        foreach (var step in _steps)
        {
            switch (step.Kind)
            {
                case StepKind.Enter:
                    text.AppendLine($"enter {step.Target}   # {name(step.Target)}");
                    break;

                case StepKind.Waypoint:
                    text.AppendLine("waypoint");
                    break;

                default:
                    text.AppendLine(step.Note);
                    break;
            }
        }

        try
        {
            File.WriteAllText(path, text.ToString());
        }
        catch (IOException)
        {
        }
    }
}
