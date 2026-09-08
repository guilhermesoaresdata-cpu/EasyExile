using System.Collections.Immutable;

namespace EasyExile.Core.World;

/// <summary>
/// One entity TYPE's component table, in the three shapes a capture asks for.
/// </summary>
/// <remarks>
/// The same table was being re-derived per entity in two different ways: a
/// fresh <c>ImmutableArray</c> of names built for the classifier, and a linear
/// scan with string comparison for each of the half-dozen components a capture
/// resolves. Neither depends on the entity — only on its type — so both are
/// built once here and shared by every skeleton in the area.
/// </remarks>
internal sealed class ComponentLayout
{
    public static readonly ComponentLayout Empty = new([]);

    public ComponentLayout((string Name, int Index)[] entries)
    {
        Entries = entries;

        var names = ImmutableArray.CreateBuilder<string>(entries.Length);
        var slots = new Dictionary<string, int>(entries.Length, StringComparer.Ordinal);

        foreach (var (name, index) in entries)
        {
            names.Add(name);

            // A type declaring the same name twice is the client's business, not
            // ours; the first slot is the one the old linear scan would have
            // found, so keeping it changes nothing.
            slots.TryAdd(name, index);
        }

        Names = names.ToImmutable();
        Slots = slots;
    }

    public (string Name, int Index)[] Entries { get; }

    /// <summary>The declared names, allocated once per type rather than per entity.</summary>
    public ImmutableArray<string> Names { get; }

    /// <summary>Name to slot, so resolving a component is a hash and not a scan.</summary>
    public Dictionary<string, int> Slots { get; }
}
