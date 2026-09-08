using System.Collections.Immutable;
using EasyExile.Core.World;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// Everything a capture remembers between passes, for one area.
/// </summary>
/// <remarks>
/// These were static, and static was wrong for the same reason it was wrong for
/// the map reader: they key on addresses, so they belong to one reader looking
/// at one client. Two callers sharing them read each other's answers.
///
/// The suite caught it as a FLAKE rather than a failure — test classes run in
/// parallel, and a second class taking a capture would occasionally invalidate
/// the first one's area mid-test. A flaky suite is worse than a failing one: it
/// teaches you to re-run instead of to look.
/// </remarks>
internal sealed class CaptureCaches
{
    /// <summary>Which <c>/Terrain/</c> paths the client marks. By path, not by address.</summary>
    public Dictionary<string, bool> SceneryVerdict { get; } = new(StringComparer.Ordinal);

    /// <summary>A monster's affixes, which are rolled at spawn and never change.</summary>
    public Dictionary<nint, ImmutableArray<string>> Mods { get; } = new();

    /// <summary>
    /// A drop's identity. Fixed the moment it lands, so it is read once.
    /// </summary>
    /// <remarks>
    /// Successes only. A drop's wrapper is populated a beat after its entity
    /// appears, so the capture that arrives during that beat reads nothing —
    /// and remembering that nothing left the item nameless for as long as it
    /// lay on the floor. The nullable value is kept for the reader's benefit
    /// rather than to store a miss.
    /// </remarks>
    public Dictionary<nint, ItemSnapshot?> Items { get; } = new();

    /// <summary>Metadata paths and component tables, per entity type.</summary>
    public EntityTypeCache Types { get; } = new();

    /// <summary>
    /// The last UI sweep, and when it was taken.
    /// </summary>
    /// <remarks>
    /// The loot tags and the item slots each cost a walk of the whole visible UI
    /// tree — twenty and forty thousand nodes. Doing both on every capture took
    /// the world walk from thirty ticks a second to twelve, and a world that
    /// slow is visible as stutter no matter what the renderer does with it. The
    /// reference says in as many words that these run throttled.
    ///
    /// Panels do not change between one tick and the next, so re-using the last
    /// sweep for a fraction of a second costs nothing anyone can see.
    /// </remarks>
    public DateTime UiSweptAt { get; set; } = DateTime.MinValue;

    public ImmutableArray<LootLabelSnapshot> Labels { get; set; } = ImmutableArray<LootLabelSnapshot>.Empty;

    public ImmutableArray<ItemSlotSnapshot> Slots { get; set; } = ImmutableArray<ItemSlotSnapshot>.Empty;

    /// <summary>
    /// The camera as it was when those rectangles were measured.
    /// </summary>
    /// <remarks>
    /// Kept WITH the sweep, not taken from the tick that publishes it. The
    /// renderer corrects a stale rectangle by how far the view has travelled
    /// since, and once the sweep is throttled the rectangle and the tick's
    /// camera are no longer from the same instant — correcting against the
    /// wrong "before" overshoots, which is worse than not correcting at all.
    /// </remarks>
    public CameraSnapshot? UiCamera { get; set; }

    /// <summary>The league name. It cannot change without a new session.</summary>
    public string? League { get; set; }

    public string TerrainKey { get; set; } = string.Empty;

    public TerrainSnapshot? Terrain { get; set; }

    public string LandmarkKey { get; set; } = string.Empty;

    public ImmutableArray<LandmarkSnapshot> Landmarks { get; set; } = ImmutableArray<LandmarkSnapshot>.Empty;

    /// <summary>
    /// Exits and marked points already seen in this area.
    /// </summary>
    /// <remarks>
    /// The client only loads entities near the player, so walking away from an
    /// exit deletes it from the entity list. It has not gone anywhere: a
    /// transition is fixed terrain furniture. Remembering it for the area's
    /// lifetime is the difference between a map that fills in as you explore and
    /// one that forgets behind you.
    /// </remarks>
    public Dictionary<EntityId, EntitySnapshot> Seen { get; } = new();

    /// <summary>Called when the area changes, which is when all of it stops meaning anything.</summary>
    public void Clear()
    {
        SceneryVerdict.Clear();
        Mods.Clear();
        Types.Clear();
        Seen.Clear();
        Items.Clear();
    }
}
