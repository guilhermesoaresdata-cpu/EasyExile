using System.Text;

namespace EasyExile.Radar.Features.Levelling;

/// <summary>
/// What the player has learned about the world: which code is which zone, and
/// which zone leads where.
/// </summary>
/// <remarks>
/// Every published levelling guide for this game is written against zone names
/// in English. Names are the wrong key for a tool: the client localizes them, so
/// a guide that matches on "Clearfell" is a guide that works in one language.
/// Area codes do not move and do not translate, which is why the route below is
/// authored in codes and this exists to put a readable name back on screen.
///
/// There is no published table of PoE2 area codes, and inventing one would be
/// inventing data. So the graph is LEARNED: every area entered contributes its
/// own code, and every exit standing in it contributes a code-to-name pair for
/// somewhere the player has not been yet. Walking the campaign once fills it in.
/// </remarks>
public sealed class AreaGraph
{
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SortedSet<string>> _exits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Areas actually stood in, as opposed to merely named by a door.</summary>
    private readonly HashSet<string> _visited = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _path;

    private bool _dirty;

    public AreaGraph(string path)
    {
        _path = path;
        Load();
    }

    public int Areas => _names.Count;

    public int Edges => _exits.Values.Sum(e => e.Count);

    /// <summary>The readable name for a code, or the code when nothing was learned.</summary>
    public string Name(string code) =>
        _names.TryGetValue(code, out var name) && name.Length > 0 ? name : code;

    public IReadOnlyCollection<string> ExitsFrom(string code) =>
        _exits.TryGetValue(code, out var exits) ? exits : Array.Empty<string>();

    /// <summary>The area code known by this name, or null.</summary>
    public string? CodeFor(string? name)
    {
        if (name is not { Length: > 0 }) return null;

        foreach (var (code, learned) in _names)
        {
            if (learned.Length > 0 && string.Equals(learned, name, StringComparison.OrdinalIgnoreCase))
                return code;
        }

        return null;
    }

    /// <summary>
    /// The first door to take from here to get to there, over doors we have
    /// actually seen.
    /// </summary>
    /// <remarks>
    /// The campaign's zone list is a VISITING order, not a map: The Bone Pits is
    /// a dead end off Mastodon Badlands, and the zone listed after it is reached
    /// from the town waypoint. Reading the list as though consecutive entries
    /// shared a door meant the guide stood in a dead end waiting for a door that
    /// was never going to appear.
    ///
    /// Breadth-first, so the answer is the fewest zone changes rather than the
    /// first branch tried, and bounded because a graph read from a file on disk
    /// is not a graph we wrote.
    /// </remarks>
    public string? FirstHopTowards(string from, string target, int budget = 512)
    {
        if (from.Length == 0 || target.Length == 0) return null;
        if (string.Equals(from, target, StringComparison.OrdinalIgnoreCase)) return null;

        // Each entry remembers the door out of `from` that started its branch,
        // which is the only part of the path the caller can act on.
        var queue = new Queue<(string Area, string FirstHop)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { from };

        foreach (var exit in ExitsFrom(from))
        {
            if (string.Equals(exit, target, StringComparison.OrdinalIgnoreCase)) return exit;

            if (seen.Add(exit)) queue.Enqueue((exit, exit));
        }

        while (queue.Count > 0 && budget-- > 0)
        {
            var (area, first) = queue.Dequeue();

            foreach (var exit in ExitsFrom(area))
            {
                if (string.Equals(exit, target, StringComparison.OrdinalIgnoreCase)) return first;

                if (seen.Add(exit)) queue.Enqueue((exit, first));
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the player has been inside this area, not merely seen its door.
    /// </summary>
    /// <remarks>
    /// The two are different and the difference is the whole point of an
    /// exploration hint: an area you have only seen named from the other side
    /// of a doorway is exactly the area worth walking to.
    /// </remarks>
    public bool HasVisited(string code) => _visited.Contains(code);

    public int VisitedCount => _visited.Count;

    /// <summary>
    /// Records what this area knows about itself and its neighbours.
    /// </summary>
    public void Observe(string? area, IEnumerable<(string Code, string? Name)> exits)
    {
        if (area is not { Length: > 0 }) return;

        // An area does not name itself — the client only names it on the far
        // side of a door — so a code first seen underfoot has no name until some
        // other area's exit supplies one. Never overwrite a real name with a
        // placeholder.
        _names.TryAdd(area, string.Empty);
        _dirty |= _visited.Add(area);

        foreach (var (code, name) in exits)
        {
            if (code.Length == 0) continue;

            if (name is { Length: > 0 } &&
                (!_names.TryGetValue(code, out var known) || known.Length == 0))
            {
                _names[code] = name;
                _dirty = true;
            }
            else
            {
                _dirty |= _names.TryAdd(code, string.Empty);
            }

            if (!_exits.TryGetValue(area, out var set))
                _exits[area] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            _dirty |= set.Add(code);
        }
    }

    public void SaveIfChanged()
    {
        if (!_dirty) return;

        var text = new StringBuilder();

        text.AppendLine("# Learned area graph. code=name, code>exit, and !code for areas visited.");

        foreach (var code in _visited.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"!{code}");

        foreach (var (code, name) in _names.OrderBy(n => n.Key, StringComparer.OrdinalIgnoreCase))
            text.AppendLine($"{code}={name}");

        foreach (var (code, exits) in _exits.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            foreach (var exit in exits) text.AppendLine($"{code}>{exit}");
        }

        try
        {
            File.WriteAllText(_path, text.ToString());
            _dirty = false;
        }
        catch (IOException)
        {
            // A graph that cannot be written is a graph learned again next run.
            // Losing it is not worth taking the overlay down for.
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;

        try
        {
            foreach (var line in File.ReadAllLines(_path))
            {
                if (line.Length == 0 || line[0] == '#') continue;

                if (line[0] == '!')
                {
                    _visited.Add(line[1..]);
                    continue;
                }

                var equals = line.IndexOf('=');

                if (equals > 0)
                {
                    _names[line[..equals]] = line[(equals + 1)..];
                    continue;
                }

                var arrow = line.IndexOf('>');

                if (arrow <= 0) continue;

                var from = line[..arrow];
                var to = line[(arrow + 1)..];

                if (!_exits.TryGetValue(from, out var set))
                    _exits[from] = set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

                set.Add(to);
            }
        }
        catch (IOException)
        {
        }
    }
}
