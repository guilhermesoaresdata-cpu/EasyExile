using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;
using EasyExile.Core.Spatial;

namespace EasyExile.Core.World;

/// <summary>
/// An entity in the running client. Players, monsters, chests and items are all
/// entities and all resolve their components the same way, so nothing here is
/// specialised per kind.
/// </summary>
internal sealed class GameEntity
{
    private readonly IMemoryReader _memory;
    private readonly EntityTypeCache? _types;

    /// <param name="types">
    /// Per-area memory for facts that belong to an entity's TYPE rather than to
    /// the entity — its metadata path and its component table. Optional: without
    /// one every read goes to the client, which is correct but is what made a
    /// capture cost 180 ms. It is deliberately NOT static: the addresses it keys
    /// on are only meaningful for one area seen through one reader.
    /// </param>
    public GameEntity(IMemoryReader memory, nint address, EntityTypeCache? types = null)
    {
        _memory = memory;
        Address = address;
        _types = types;
    }

    public nint Address { get; }

    public static bool IsPlausibleAddress(nint address) =>
        (long)address is >= 0x10000 and <= 0x00007FFFFFFFFFFF && (long)address % 8 == 0;

    /// <summary>
    /// The entity's type descriptor, read once per instance.
    /// </summary>
    /// <remarks>
    /// Every component a capture resolves went through here, and each visit was
    /// a crossing into the client for a pointer that cannot move while the
    /// entity is being described. Six or seven per entity, five hundred
    /// entities, thirty times a second.
    /// </remarks>
    public nint Details
    {
        get
        {
            if (_details is { } cached) return cached;

            var details = _memory.TryReadPointer(
                Address + GameLayout.Components.EntityDetails, out var read) ? read : 0;

            _details = details;

            return details;
        }
    }

    private nint? _details;

    /// <summary>
    /// The entity's metadata path, cached per TYPE for the area's lifetime.
    /// </summary>
    /// <remarks>
    /// The path hangs off <c>details</c> — the type descriptor — not off the
    /// entity, so every skeleton in the area shares one string. Read per entity
    /// it was a chunked UTF-16 read on all five hundred of them, every capture,
    /// for an answer that cannot change.
    /// </remarks>
    public string? Metadata
    {
        get
        {
            var details = Details;
            if (details == 0) return null;

            if (_types is not null && _types.Paths.TryGetValue(details, out var cached)) return cached;

            var text = _memory.TryReadWideString(
                details + GameLayout.Components.Metadata,
                GameLayout.Native.StringBuffer,
                GameLayout.Native.StringSize,
                GameLayout.Native.StringCapacity);

            var path = text?.StartsWith("Metadata/", StringComparison.Ordinal) == true ? text : null;

            // Cached even when null: an unreadable descriptor stays unreadable,
            // and retrying it every capture is the cost this exists to avoid.
            if (_types is not null) _types.Paths[details] = path;

            return path;
        }
    }



    public bool IsValid => IsPlausibleAddress(Address) && Metadata is not null;

    /// <summary>
    /// Name to index to address, exactly as the contract describes. The index is
    /// read fresh every time because it is assigned per entity at runtime and
    /// caching it would survive the entity it belonged to.
    /// </summary>
    public IReadOnlyDictionary<string, nint> Components()
    {
        var map = new Dictionary<string, nint>(StringComparer.Ordinal);

        var lookup = Lookup;
        if (lookup == 0) return map;

        foreach (var (name, index) in LayoutOf(lookup).Entries)
        {
            var component = Slot(index);

            if (component != 0) map[name] = component;
        }

        return map;
    }

    /// <summary>
    /// The name-to-slot table for one entity TYPE, read once per area.
    /// </summary>
    /// <remarks>
    /// This is the single biggest cost in a capture, and it is entirely
    /// avoidable. The table hangs off the entity's <c>details</c> pointer — its
    /// type descriptor — so every rhoa in the area shares one. Read per entity
    /// it meant a UTF-8 string read for each of ~15 components on each of ~500
    /// entities, several thousand chunked reads per capture, and the whole world
    /// lane crawling at 5 Hz. Read per type it is a few dozen, once.
    ///
    /// POE2Radar caches the resolved component ADDRESSES per entity for the same
    /// reason. Keying on the type is strictly better: it also covers entities
    /// the walk is seeing for the first time.
    /// </remarks>
    private ComponentLayout LayoutOf(nint lookup)
    {
        if (_types is not null && _types.Layouts.TryGetValue(lookup, out var cached)) return cached;

        var empty = ComponentLayout.Empty;

        if (!TryReadVector(lookup + GameLayout.Components.NameBucket,
                GameLayout.Components.EntryStride, out var bucketFirst, out var entries))
            return empty;

        if (entries is <= 0 or > 256) return empty;

        var layout = new List<(string Name, int Index)>(entries);

        for (var i = 0; i < entries; i++)
        {
            var entry = bucketFirst + (i * GameLayout.Components.EntryStride);

            if (!_memory.TryReadPointer(entry + GameLayout.Components.EntryName, out var namePtr)) continue;
            if (!_memory.TryRead<int>(entry + GameLayout.Components.EntryIndex, out var index)) continue;

            var name = _memory.TryReadUtf8(namePtr, 64);
            if (string.IsNullOrEmpty(name)) continue;

            layout.Add((name, index));
        }

        var result = new ComponentLayout(layout.ToArray());

        // Cached even when the type yielded nothing: re-walking a type that has
        // no readable table is the same waste as re-walking one that does.
        if (_types is not null) _types.Layouts[lookup] = result;

        return result;
    }



    /// <summary>
    /// The names this entity's type declares. No reads at all: the table is
    /// already cached per type.
    /// </summary>
    /// <remarks>
    /// Declared by the type rather than resolved per entity, which is the point
    /// — building a name list per entity per capture was allocating a dictionary
    /// and sorting fifteen strings five hundred times a tick for an answer that
    /// belongs to the type.
    /// </remarks>
    public ImmutableArray<string> ComponentNames()
    {
        var lookup = Lookup;
        if (lookup == 0) return ImmutableArray<string>.Empty;

        return LayoutOf(lookup).Names;
    }

    /// <summary>
    /// One component's address, or 0. A single read: the slot index comes from
    /// the type's cached table, so nothing is walked and nothing is allocated.
    /// </summary>
    public nint Component(string name)
    {
        var lookup = Lookup;
        if (lookup == 0) return 0;

        return LayoutOf(lookup).Slots.TryGetValue(name, out var index) ? Slot(index) : 0;
    }

    /// <summary>One component address out of the block read for this entity.</summary>
    private nint Slot(int index)
    {
        var slots = SlotBlock;

        if (index < 0 || slots.Length < (index + 1) * 8) return 0;

        return (nint)BitConverter.ToInt64(slots, index * 8);
    }

    /// <summary>
    /// Every component address this entity has, fetched in one crossing.
    /// </summary>
    /// <remarks>
    /// A capture asks for five or six components on the same entity — life,
    /// render, positioned, magic properties, the map icon — and each one used to
    /// be its own read of one pointer out of the middle of an array. The array
    /// is a hundred bytes: fetching all of it costs the same as fetching eight
    /// of them, because the price is the crossing and not the payload.
    /// </remarks>
    private byte[] SlotBlock
    {
        get
        {
            if (_slotBlock is { } cached) return cached;

            _slotBlock = Array.Empty<byte>();

            if (!TryReadVector(Address + GameLayout.Components.ComponentList, 8, out var first, out var count))
                return _slotBlock;

            if (first == 0 || count is <= 0 or > 256) return _slotBlock;

            if (_memory.TryReadBytes(first, count * 8, out var block)) _slotBlock = block;

            return _slotBlock;
        }
    }

    private byte[]? _slotBlock;

    /// <summary>The type's component table, one read in.</summary>
    private nint Lookup
    {
        get
        {
            if (_lookup is { } cachedInstance) return cachedInstance;

            var details = Details;
            if (details == 0) return 0;

            // The table belongs to the TYPE, so the first rhoa in the area pays
            // for it and the next four hundred do not.
            if (_types is not null && _types.Lookups.TryGetValue(details, out var cachedType))
            {
                _lookup = cachedType;
                return cachedType;
            }

            var lookup = _memory.TryReadPointer(details + GameLayout.Components.ComponentLookup, out var read)
                ? read
                : 0;

            if (_types is not null) _types.Lookups[details] = lookup;

            _lookup = lookup;

            return lookup;
        }
    }

    private nint? _lookup;

    /// <summary>The component points back at the entity that owns it.</summary>
    public nint OwnerOf(nint component) =>
        _memory.TryReadPointer(component + GameLayout.Components.Owner, out var owner) ? owner : 0;

    // ---- typed reads ------------------------------------------------------

    /// <summary>
    /// Health, mana and energy shield, in one crossing rather than nine.
    /// </summary>
    /// <remarks>
    /// The three blocks sit within a couple of hundred bytes of each other, and
    /// each held three fields we wanted. Read one field at a time that was nine
    /// syscalls on every living thing in the area; the span between the first
    /// field and the last is small enough to fetch whole.
    ///
    /// The span is measured from the contract, not assumed: if a future layout
    /// scatters the blocks, the fallback below reads them the old way rather
    /// than fetching a megabyte or reporting nonsense.
    /// </remarks>
    public Vital[] Vitals()
    {
        var life = Component(GameLayout.Names.Life);
        if (life == 0) return Array.Empty<Vital>();

        var blocks = new[]
        {
            (GameLayout.Vitals.Health, "Health"),
            (GameLayout.Vitals.Mana, "Mana"),
            (GameLayout.Vitals.EnergyShield, "EnergyShield"),
        };

        var lowest = int.MaxValue;
        var highest = int.MinValue;

        foreach (var (block, _) in blocks)
        {
            lowest = Math.Min(lowest, block);
            highest = Math.Max(highest, block);
        }

        var from = lowest + Math.Min(
            GameLayout.Vitals.BlockId, Math.Min(GameLayout.Vitals.BlockMax, GameLayout.Vitals.BlockCurrent));

        var to = highest + Math.Max(
            GameLayout.Vitals.BlockId, Math.Max(GameLayout.Vitals.BlockMax, GameLayout.Vitals.BlockCurrent)) + 4;

        if (to - from is > 0 and <= 4096 && _memory.TryReadBytes(life + from, to - from, out var bytes))
        {
            var vitals = new Vital[blocks.Length];

            for (var i = 0; i < blocks.Length; i++)
            {
                var (block, name) = blocks[i];
                var at = block - from;

                vitals[i] = new Vital(
                    name,
                    BitConverter.ToInt32(bytes, at + GameLayout.Vitals.BlockId),
                    BitConverter.ToInt32(bytes, at + GameLayout.Vitals.BlockCurrent),
                    BitConverter.ToInt32(bytes, at + GameLayout.Vitals.BlockMax));
            }

            return vitals;
        }

        return new[]
        {
            ReadVital(life, GameLayout.Vitals.Health, "Health"),
            ReadVital(life, GameLayout.Vitals.Mana, "Mana"),
            ReadVital(life, GameLayout.Vitals.EnergyShield, "EnergyShield"),
        };
    }

    private Vital ReadVital(nint life, int block, string name)
    {
        _memory.TryRead<int>(life + block + GameLayout.Vitals.BlockId, out var id);
        _memory.TryRead<int>(life + block + GameLayout.Vitals.BlockMax, out var max);
        _memory.TryRead<int>(life + block + GameLayout.Vitals.BlockCurrent, out var current);

        return new Vital(name, id, current, max);
    }

    public Vector3? WorldPosition()
    {
        var render = Component(GameLayout.Names.Render);
        if (render == 0) return null;

        if (!_memory.TryReadBytes(render + GameLayout.Spatial.WorldPosition,
                GameLayout.Spatial.WorldPositionSize, out var bytes))
            return null;

        var x = BitConverter.ToSingle(bytes, 0);
        var y = BitConverter.ToSingle(bytes, 4);
        var z = BitConverter.ToSingle(bytes, 8);

        return float.IsFinite(x) && float.IsFinite(y) && float.IsFinite(z) ? new Vector3(x, y, z) : null;
    }

    /// <summary>
    /// Derived from the world position, not read from memory. The offset that
    /// used to supply this was withdrawn from the contract: it read (0,0) while
    /// the world position was valid, and only ever matched half the pair.
    /// </summary>
    public Vector2? GridPosition() =>
        Projection.ToGrid(WorldPosition(), GameLayout.Spatial.WorldToGrid);

    /// <summary>
    /// Friend or foe, from the Positioned component. Null when the entity has
    /// no Positioned — which is most non-combatants.
    /// </summary>
    /// <remarks>
    /// The only thing that separates your companion from something hunting you:
    /// both are filed under Metadata/Monsters and their component lists are
    /// nearly identical.
    /// </remarks>
    public bool? IsFriendly()
    {
        var positioned = Component(GameLayout.Names.Positioned);
        if (positioned == 0) return null;

        if (!_memory.TryRead<byte>(positioned + GameLayout.Spatial.Reaction, out var reaction)) return null;

        return (reaction & 0x7F) == 1;
    }

    /// <summary>
    /// The monster's rank, or Unknown when the component is absent — most
    /// things in the world are not monsters. Unknown is drawn as ordinary
    /// rather than promoted to a rank the client never claimed.
    /// </summary>
    public MonsterRarity Rarity()
    {
        var properties = Component(GameLayout.Rank.ComponentName);
        if (properties == 0) return MonsterRarity.Unknown;

        if (!_memory.TryRead<int>(properties + GameLayout.Rank.Rarity, out var value)) return MonsterRarity.Unknown;

        return value is >= 0 and <= 3 ? (MonsterRarity)value : MonsterRarity.Unknown;
    }

    /// <summary>
    /// Whether the client itself marks this entity on the map, and whether it
    /// has faded the icon because the encounter behind it is finished.
    /// </summary>
    /// <remarks>
    /// Port of POE2Radar (MIT) <c>Poe2Live.ReadIcon</c>. The component's mere
    /// presence is the answer to the first question: the client does not put a
    /// MinimapIcon on scenery. Live, 80 of 1792 entities carried one, and all 80
    /// were checkpoints, portals, an expedition device and a quest switch.
    ///
    /// The flag is read every time rather than cached. A completed encounter is
    /// the one thing here that changes while the entity stands still — the
    /// client flips it and fades the icon rather than removing it.
    /// </remarks>
    public (bool IsPoi, bool Complete) Icon()
    {
        var icon = Component(GameLayout.MinimapIcon.ComponentName);
        if (icon == 0) return (false, false);

        var complete = _memory.TryRead<int>(icon + GameLayout.MinimapIcon.CompletedState, out var state) &&
                       state != 0;

        return (true, complete);
    }

    /// <summary>
    /// The monster's rolled affix mod ids — auras, resistances, extra damage.
    /// Empty for anything that is not a rolled monster.
    /// </summary>
    /// <remarks>
    /// Port of POE2Radar (MIT) <c>Poe2Live.ReadMods</c>. Reads only the rolled
    /// affix vector; the neighbouring placeholder vector that holds
    /// MonsterRare1/MonsterMagic2 filler is deliberately untouched, because it
    /// reads exactly like affixes and is not.
    ///
    /// The record's field is a POINTER to the id, not the id. The reference
    /// records the consequence of forgetting that: the record pointer's own
    /// bytes get read as text, every string fails to parse, and every monster
    /// looks unmodded.
    /// </remarks>
    public ImmutableArray<string> Mods()
    {
        var properties = Component(GameLayout.Rank.ComponentName);
        if (properties == 0) return ImmutableArray<string>.Empty;

        if (!_memory.TryReadPointer(properties + GameLayout.Rank.Mods, out var first) || first == 0)
            return ImmutableArray<string>.Empty;

        if (!_memory.TryReadPointer(properties + GameLayout.Rank.Mods + 8, out var last))
            return ImmutableArray<string>.Empty;

        var stride = GameLayout.Rank.ModStride;
        var length = (long)last - first;

        // Shape first. A vector that does not divide by its own stride is not
        // this vector, and following it would read arbitrary memory as text.
        if (length <= 0 || length > 0x4000 || length % stride != 0) return ImmutableArray<string>.Empty;

        var count = (int)(length / stride);

        if (count > 100) return ImmutableArray<string>.Empty;

        var found = ImmutableArray.CreateBuilder<string>(count);

        for (var i = 0; i < count; i++)
        {
            if (!_memory.TryReadPointer(first + (nint)((i * stride) + GameLayout.Rank.ModRecord), out var record) ||
                record == 0)
                continue;

            if (!_memory.TryReadPointer(record + GameLayout.Rank.ModId, out var id) || id == 0) continue;

            var text = _memory.TryReadUtf16(id, 64);

            if (text is null || !LooksLikeModId(text) || found.Contains(text)) continue;

            found.Add(text);
        }

        return found.ToImmutable();
    }

    /// <summary>
    /// Letters, digits and underscores, with at least one letter. The guard that
    /// keeps a misread pointer from entering the snapshot as a mod id.
    /// </summary>
    private static bool LooksLikeModId(string text)
    {
        if (text.Length is < 3 or > 64) return false;

        var letter = false;

        foreach (var c in text)
        {
            if (char.IsAsciiLetter(c)) { letter = true; continue; }
            if (char.IsAsciiDigit(c) || c == '_') continue;

            return false;
        }

        return letter;
    }

    /// <summary>
    /// The friendly name the client itself would print for this thing, or null.
    /// </summary>
    /// <remarks>
    /// Two sources, in the order they are worth having:
    ///
    /// An exit's DESTINATION, through its AreaTransition component. This is the
    /// one that matters — every exit in the game shares the metadata path
    /// <c>AreaTransition_Animate</c>, so the path can only ever say "Area
    /// Transition", while the destination says "The Well of Souls".
    ///
    /// Failing that, the name on its map-icon definition: "Checkpoint",
    /// "Entrance", "Waypoint". Not a destination, but still the client's own
    /// word for the marker rather than ours.
    /// </remarks>
    public string? FriendlyName()
    {
        var transition = Component(GameLayout.Transition.ComponentName);

        if (transition != 0 &&
            _memory.TryReadPointer(transition + GameLayout.Transition.Destination, out var area) && area != 0 &&
            Indirect(area + GameLayout.Transition.AreaName) is { } destination)
            return destination;

        var icon = Component(GameLayout.MinimapIcon.ComponentName);

        if (icon != 0 &&
            _memory.TryReadPointer(icon + GameLayout.MinimapIcon.Definition, out var definition) && definition != 0)
            return Indirect(definition);

        return null;
    }

    /// <summary>
    /// Where this transition leads, as a code rather than a display name.
    /// </summary>
    /// <remarks>
    /// A levelling route has to join on something the client spells the same way
    /// in every language. The display name does not qualify — this client is in
    /// Portuguese — and the code does.
    /// </remarks>
    public string? DestinationCode()
    {
        var transition = Component(GameLayout.Transition.ComponentName);

        if (transition == 0) return null;

        if (!_memory.TryReadPointer(transition + GameLayout.Transition.Destination, out var area) || area == 0)
            return null;

        return Indirect(area + GameLayout.Transition.AreaId);
    }

    /// <summary>A pointer to a NUL-terminated UTF-16 string, validated as text.</summary>
    private string? Indirect(nint address)
    {
        if (!_memory.TryReadPointer(address, out var text) || text == 0) return null;

        var value = _memory.TryReadUtf16(text, 64);

        if (value is not { Length: >= 2 and <= 64 }) return null;

        // A pointer into arbitrary memory decodes as mojibake; a name does not.
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c is not (' ' or '_' or '\'' or '-' or ',')) return null;
        }

        return value;
    }

    /// <summary>
    /// A ground drop's identity, or null when this is not a drop.
    /// </summary>
    /// <remarks>
    /// Port of POE2Radar (MIT) <c>Poe2Live.ReadIdentityFromItem</c>, including
    /// its default: identified is TRUE unless the item says otherwise, so a
    /// plain base with no Mods component is never mistaken for something whose
    /// name is being hidden.
    /// </remarks>
    /// <summary>
    /// How many are in this pile. One when the item does not stack.
    /// </summary>
    /// <remarks>
    /// A chip that prices a stack of forty Exalted Orbs as one is not a rounding
    /// error, it is the wrong answer by a factor of forty — so the count is part
    /// of the identity, not a decoration.
    /// </remarks>
    public int StackCount()
    {
        var stack = Component(GameLayout.Item.StackComponent);

        if (stack == 0) return 1;

        return _memory.TryRead<int>(stack + GameLayout.Item.StackCount, out var count) && count > 0
            ? count
            : 1;
    }

    /// <summary>
    /// A dropped item's identity, read through its ground wrapper.
    /// </summary>
    /// <remarks>
    /// A drop on the floor is a WorldItem entity holding the real item, so this
    /// unwraps first. An item sitting in a UI slot has no wrapper — the slot
    /// points straight at it — which is why the unwrapping and the reading are
    /// two methods rather than one.
    /// </remarks>
    public ItemSnapshot? Item()
    {
        var wrapper = Component(GameLayout.Item.WorldItemComponent);
        if (wrapper == 0) return null;

        if (!_memory.TryReadPointer(wrapper + GameLayout.Item.Inner, out var item) || item == 0) return null;

        return new GameEntity(_memory, item, _types).ItemIdentity();
    }

    /// <summary>This entity's own item identity, with no unwrapping.</summary>
    public ItemSnapshot? ItemIdentity()
    {
        var mods = Component(GameLayout.Item.ModsComponent);

        var rarity = MonsterRarity.Unknown;
        var identified = true;

        if (mods != 0)
        {
            if (_memory.TryRead<int>(mods + GameLayout.Item.Rarity, out var value) && value is >= 0 and <= 3)
                rarity = (MonsterRarity)value;

            if (_memory.TryRead<int>(mods + GameLayout.Item.Identified, out var flag)) identified = flag != 0;
        }

        var identity = new ItemSnapshot(
            ArtBasename(), BaseName(), rarity, identified, ItemMods(mods));

        return identity.HasIdentity ? identity : null;
    }

    /// <summary>
    /// The item's rolled affixes, as the internal ids the game names them by.
    /// </summary>
    /// <remarks>
    /// The id carries its own tier: the game calls them Strength1..Strength8, so
    /// reading the name is reading the tier — no stat ranges, no item level
    /// arithmetic. What the name cannot say is how many tiers the family HAS,
    /// which is the one thing a table is needed for.
    ///
    /// Read only for items that are worth it. An entry is a pointer to a record
    /// and the record's id is a pointer to text, so a mod costs three crossings;
    /// six mods on every item in a full stash would be worth avoiding, and a
    /// normal item has none at all.
    /// </remarks>
    private ImmutableArray<string> ItemMods(nint mods)
    {
        if (mods == 0) return ImmutableArray<string>.Empty;

        if (!TryReadVector(mods + GameLayout.Item.ExplicitMods,
                GameLayout.Item.ModStride, out var first, out var count))
            return ImmutableArray<string>.Empty;

        if (count is <= 0 or > 16) return ImmutableArray<string>.Empty;

        var ids = ImmutableArray.CreateBuilder<string>(count);

        for (var i = 0; i < count; i++)
        {
            var entry = first + (i * GameLayout.Item.ModStride);

            if (!_memory.TryReadPointer(entry + GameLayout.Item.ModRecord, out var record) || record == 0)
                continue;

            if (!_memory.TryReadPointer(record + GameLayout.Item.ModId, out var text) || text == 0)
                continue;

            if (_memory.TryReadUtf16(text, 96) is { Length: > 1 } id) ids.Add(id);
        }

        return ids.ToImmutable();
    }

    /// <summary>The 2D art's basename — the price key poe.ninja's icons agree with.</summary>
    private string? ArtBasename()
    {
        var render = Component(GameLayout.Item.RenderComponent);
        if (render == 0) return null;

        if (!_memory.TryReadPointer(render + GameLayout.Item.ArtPath, out var path) || path == 0) return null;

        var full = _memory.TryReadUtf16(path, 128);

        if (full is null || !full.Contains("Art/", StringComparison.OrdinalIgnoreCase)) return null;

        var slash = full.LastIndexOf('/');
        var name = slash >= 0 ? full[(slash + 1)..] : full;

        return name.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? name[..^4] : null;
    }

    /// <summary>The rendered base-type name, one dereference past the Base component.</summary>
    private string? BaseName()
    {
        var basic = Component(GameLayout.Item.BaseComponent);
        if (basic == 0) return null;

        if (!_memory.TryReadPointer(basic + GameLayout.Item.NameRow, out var row) || row == 0) return null;

        var name = _memory.TryReadWideString(
            row + GameLayout.Item.RowDisplayName,
            GameLayout.Native.StringBuffer,
            GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity);

        return name is { Length: >= 2 } && name.Any(char.IsLetter) ? name : null;
    }

    public (string? Name, int Level) Identity()
    {
        var player = Component(GameLayout.Names.Player);
        if (player == 0) return (null, 0);

        var name = _memory.TryReadWideString(
            player + GameLayout.Identity.Name,
            GameLayout.Native.StringBuffer,
            GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity);

        _memory.TryRead<byte>(player + GameLayout.Identity.Level, out var level);

        return (name, level);
    }

    /// <summary>Reads an inline first/last/end triple and its element count.</summary>
    private bool TryReadVector(nint address, int stride, out nint first, out int count)
    {
        first = 0;
        count = 0;

        if (!_memory.TryReadPointer(address + GameLayout.Native.VectorFirst, out first) ||
            !_memory.TryReadPointer(address + GameLayout.Native.VectorLast, out var last))
            return false;

        if (first == 0 || last < first) return false;

        var span = (long)(last - first);
        if (stride <= 0 || span % stride != 0) return false;

        count = (int)(span / stride);
        return true;
    }
}
