using System.Text;
using EasyExile.Core.Snapshots;

namespace EasyExile.Radar.Features.Levelling;

/// <summary>
/// A running record of what each zone actually contained.
/// </summary>
/// <remarks>
/// The campaign guide is written from a community walkthrough, which describes
/// a tendency. This writes down what the CLIENT reported in the zone you were
/// actually in — the exits and where they lead, the named tile clusters, the
/// uniques, the marked encounters — so a real playthrough can be read back
/// afterwards and the guide corrected against it.
///
/// Append-only and one line at a time, so a crash costs the last zone rather
/// than the run. Written once per area rather than per capture: the interesting
/// facts are per-zone and re-writing them thirty times a second would bury them.
///
/// Off by default. It is a file that grows for as long as it is on, and nobody
/// should discover that by finding it.
/// </remarks>
public sealed class CampaignJournal
{
    private readonly string _path;
    private readonly HashSet<string> _written = new(StringComparer.Ordinal);

    public CampaignJournal(string path) => _path = path;

    /// <summary>How many zones this session has written.</summary>
    public int Entries { get; private set; }

    public string Path => _path;

    /// <summary>
    /// Records a zone, once.
    /// </summary>
    /// <remarks>
    /// Keyed by area code AND epoch, so returning to a town for the fourth time
    /// writes a fourth entry — the order of visits is exactly the thing a route
    /// is made of — while the thirty captures a second inside one visit write
    /// one.
    /// </remarks>
    public void Record(WorldSnapshot snapshot, string? zoneName)
    {
        if (snapshot.AreaCode is not { Length: > 0 } area) return;

        var key = $"{snapshot.Epoch}/{area}";

        if (!_written.Add(key)) return;

        try
        {
            File.AppendAllText(_path, Describe(snapshot, area, zoneName), Encoding.UTF8);

            Entries++;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A journal that cannot be written is not a reason to stop playing.
        }
    }

    /// <summary>
    /// One zone, in the same shape the campaign file uses.
    /// </summary>
    /// <remarks>
    /// Deliberately close to campaign.txt so the two can be read side by side.
    /// Everything here is what the client said, marked as observed rather than
    /// as advice — the point of collecting it is to check the advice.
    /// </remarks>
    private static string Describe(WorldSnapshot snapshot, string area, string? zoneName)
    {
        var text = new StringBuilder();

        text.Append("\nzone ").Append(area);

        if (zoneName is { Length: > 0 } && !string.Equals(zoneName, area, StringComparison.Ordinal))
            text.Append(' ').Append(zoneName);

        text.Append('\n');
        text.Append("  at ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append('\n');
        text.Append("  level ").Append(snapshot.Player.Level).Append('\n');

        foreach (var mark in snapshot.Marks)
        {
            text.Append("  landmark ").Append(mark.Name)
                .Append(mark.IsWayOut ? "  (saida)" : "  (lugar)")
                .Append("  em ").Append((int)mark.Centre.X).Append(',').Append((int)mark.Centre.Y)
                .Append('\n');
        }

        var exits = new SortedSet<string>(StringComparer.Ordinal);
        var uniques = new SortedSet<string>(StringComparer.Ordinal);
        var marked = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var entity in snapshot.Entities)
        {
            if (entity.DestinationCode is { Length: > 0 } code)
                exits.Add($"{code}  {entity.FriendlyName ?? "?"}");

            if (entity is { Kind: EntityKind.Monster, Rarity: MonsterRarity.Unique })
                uniques.Add(entity.FriendlyName ?? Tail(entity.Metadata));

            if (entity is { IsPoi: true })
                marked.Add($"{entity.FriendlyName ?? Tail(entity.Metadata)}{(entity.IconComplete ? "  (concluido)" : "")}");
        }

        foreach (var exit in exits) text.Append("  exit ").Append(exit).Append('\n');
        foreach (var unique in uniques) text.Append("  unique ").Append(unique).Append('\n');
        foreach (var poi in marked) text.Append("  marked ").Append(poi).Append('\n');

        return text.ToString();
    }

    private static string Tail(string path)
    {
        var cut = path.LastIndexOf('/');

        return cut > 0 ? path[(cut + 1)..] : path;
    }
}
