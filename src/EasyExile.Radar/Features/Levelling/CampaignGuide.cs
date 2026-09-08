namespace EasyExile.Radar.Features.Levelling;

/// <summary>What a campaign step asks you to do.</summary>
public enum StepAction
{
    /// <summary>Anything the guide could only say in words.</summary>
    Note,

    /// <summary>Walk to the door leading to another area.</summary>
    Enter,

    /// <summary>Take the waypoint or a portal out. Not a door.</summary>
    Waypoint,

    /// <summary>Kill something in this zone.</summary>
    Kill,

    /// <summary>Pick something up in this zone.</summary>
    Take,

    /// <summary>Speak to someone in this zone.</summary>
    Talk,
}

/// <summary>
/// The campaign as a list of typed actions.
/// </summary>
/// <remarks>
/// The first version of this was a list of zones with prose attached, and it
/// was wrong in a way that only showed up in the pockets: it assumed the zone
/// listed after this one is reachable through a door from it. Often it is not.
/// The Bone Pits is a dead end off Mastodon Badlands and you leave it by
/// waypoint, so a guide that only knows zones stands there waiting for a door
/// that is never coming.
///
/// The shape here is exile-leveling's (MIT), which is what every good levelling
/// tool converged on: a linear sequence of ACTIONS, several per zone, with
/// entering and waypointing as different things. That difference is the fix.
///
/// Keyed by the client's internal area code rather than by the name it displays.
/// The code is the same in every language and this client is not in English —
/// matching on a displayed name was a bug waiting for the first localised
/// string.
///
/// What none of the tools this borrows from can do is say where the thing IS,
/// because layouts are generated per character. That stays ours: this decides
/// what to look for, and the route system finds it in your instance.
/// </remarks>
public sealed class CampaignGuide
{
    private readonly List<CampaignZone> _zones = new();

    private CampaignGuide()
    {
    }

    public static CampaignGuide Empty { get; } = new();

    public IReadOnlyList<CampaignZone> Zones => _zones;

    public int Count => _zones.Count;

    public bool IsLoaded => _zones.Count > 0;

    public static CampaignGuide Load(string path)
    {
        var guide = new CampaignGuide();

        if (!File.Exists(path)) return guide;

        try
        {
            var act = "";
            CampaignZone? zone = null;

            foreach (var raw in File.ReadLines(path))
            {
                var line = raw.Trim();

                if (line.Length == 0 || line[0] == '#') continue;

                var (verb, rest) = Split(line);

                switch (verb)
                {
                    case "act":
                        act = After(rest);
                        continue;

                    case "zone":
                    case "zone?":
                        var (zoneCode, zoneName) = CodeAndName(rest);
                        zone = new CampaignZone(zoneCode, zoneName, act, verb.EndsWith('?'));
                        guide._zones.Add(zone);
                        continue;
                }

                if (zone is null) continue;

                var optional = verb.EndsWith('?');
                var (text, hint) = Hint(rest);

                switch (verb.TrimEnd('?'))
                {
                    case "enter":
                        var (target, targetName) = CodeAndName(text);
                        zone.Steps.Add(new CampaignStep(
                            StepAction.Enter, targetName, hint, optional, target));
                        break;

                    case "waypoint":
                        zone.Steps.Add(new CampaignStep(StepAction.Waypoint, "", hint, optional, null));
                        break;

                    case "kill":
                        zone.Steps.Add(new CampaignStep(StepAction.Kill, text, hint, optional, null));
                        break;

                    case "take":
                        zone.Steps.Add(new CampaignStep(StepAction.Take, text, hint, optional, null));
                        break;

                    case "talk":
                        zone.Steps.Add(new CampaignStep(StepAction.Talk, text, hint, optional, null));
                        break;

                    case "note":
                        zone.Steps.Add(new CampaignStep(StepAction.Note, text, hint, optional, null));
                        break;
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A guide that cannot be read says nothing. It is not a reason to
            // take the overlay down.
            return new CampaignGuide();
        }

        return guide;
    }

    /// <summary>
    /// Which zone this area is, by the code the client uses for it.
    /// </summary>
    /// <remarks>
    /// A zone can appear more than once — Ogham Manor is three floors under one
    /// code — so the search runs forward from where the guide already was, and
    /// only falls back to the start when that finds nothing. Otherwise leaving
    /// the third floor would send you back to the first.
    /// </remarks>
    public int IndexOf(string? areaCode, int from = 0)
    {
        if (areaCode is not { Length: > 0 }) return -1;

        for (var i = Math.Max(0, from); i < _zones.Count; i++)
        {
            if (Same(_zones[i].Code, areaCode)) return i;
        }

        for (var i = 0; i < Math.Max(0, from) && i < _zones.Count; i++)
        {
            if (Same(_zones[i].Code, areaCode)) return i;
        }

        return -1;
    }

    /// <summary>
    /// The next zone on the main line after this one.
    /// </summary>
    /// <remarks>
    /// Optional zones are stepped over rather than pointed at. Mud Burrow is on
    /// the list because it exists and has a reward, not because the campaign
    /// needs it, and sending someone into it as though it were the way forward
    /// would be wrong. It is still there to be seen.
    /// </remarks>
    public CampaignZone? NextAfter(int index)
    {
        for (var i = index + 1; i < _zones.Count; i++)
        {
            if (!_zones[i].Optional) return _zones[i];
        }

        return null;
    }

    private static bool Same(string? a, string? b) =>
        a is { Length: > 0 } && b is { Length: > 0 } &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>"enter G1_4 The Grelwood" to ("enter", "G1_4 The Grelwood").</summary>
    private static (string Verb, string Tail) Split(string line)
    {
        var space = line.IndexOf(' ');

        return space < 0 ? (line, "") : (line[..space], line[(space + 1)..].Trim());
    }

    /// <summary>"G1_4 The Grelwood" to ("G1_4", "The Grelwood"); "-" means unknown.</summary>
    private static (string? Code, string Name) CodeAndName(string rest)
    {
        var (first, name) = Split(rest);

        return first == "-" ? (null, name) : (first, name);
    }

    /// <summary>Text before the pipe, hint after it.</summary>
    private static (string Text, string? Hint) Hint(string rest)
    {
        var bar = rest.IndexOf('|');

        return bar < 0
            ? (rest.Trim(), null)
            : (rest[..bar].Trim(), rest[(bar + 1)..].Trim() is { Length: > 0 } h ? h : null);
    }

    /// <summary>"1 Act One" to "Act One".</summary>
    private static string After(string rest)
    {
        var space = rest.IndexOf(' ');

        return space < 0 ? rest : rest[(space + 1)..].Trim();
    }
}

/// <summary>One zone of the campaign, in order.</summary>
public sealed record CampaignZone(string? Code, string Name, string Act, bool Optional)
{
    public List<CampaignStep> Steps { get; } = new();
}

/// <summary>One thing to do, and where it points.</summary>
/// <param name="Target">
/// For <see cref="StepAction.Enter"/>, the area code of the door's destination —
/// null when the guide names a zone this tool has no code for.
/// </param>
public sealed record CampaignStep(
    StepAction Action, string Subject, string? Hint, bool Optional, string? Target)
{
    /// <summary>The step as the panel prints it: one sentence, in one language.</summary>
    /// <remarks>
    /// It used to be a bracketed tag glued to a fragment of an English
    /// walkthrough — "[matar] hyena enemies until you found the Sun Clan relic"
    /// — which read as broken rather than as bilingual. The steps are written
    /// out in full now, so nothing has to be assembled here: only the names the
    /// game itself prints on screen stay in English, because that is what the
    /// player is reading.
    /// </remarks>
    public string Text => Action switch
    {
        StepAction.Enter => $"Va para {Subject}",
        StepAction.Waypoint => "Volte para a cidade pelo waypoint ou portal",
        StepAction.Kill => $"Mate {Subject}",
        StepAction.Take => $"Pegue {Subject}",
        StepAction.Talk => $"Fale com {Subject}",
        _ => Subject,
    };
}
