using System.Collections.Immutable;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;
using CameraReader = EasyExile.Core.Camera.GameCamera;

namespace EasyExile.Core.Snapshots;

/// <summary>
/// Turns the live client into an immutable <see cref="WorldSnapshot"/>. This is
/// the only place where reading memory becomes captured state, and therefore the
/// only place the boundary can be broken.
/// </summary>
internal static class SnapshotCapture
{
    /// <summary>
    /// Captures once and, if the client changed area underneath the reading,
    /// retries exactly once. A transition takes a fraction of a second, so a
    /// second collision means something else is wrong and reporting it beats
    /// spinning.
    /// </summary>
    /// <summary>
    /// Per-path memory of which <c>/Terrain/</c> paths the client marks. Cleared
    /// with the area, like every other per-area cache here.
    /// </summary>

    private static bool SceneryWithoutIcon(CaptureCaches caches, string metadata) =>
        caches.SceneryVerdict.TryGetValue(metadata, out var isPoi) && !isPoi;

    /// <summary>
    /// A monster's affix mods, remembered per entity for as long as the area
    /// lives.
    /// </summary>
    /// <remarks>
    /// Mods are rolled at spawn and never change, so reading them more than once
    /// per monster is pure waste â€” and it is expensive waste: a vector walk plus
    /// a string read per affix. Ported from POE2Radar's <c>_mods</c> cache,
    /// which exists for exactly this reason.
    /// </remarks>

    /// <summary>Per-type facts for the area being walked.</summary>

    /// <summary>
    /// New mod reads allowed per capture. POE2Radar's
    /// <c>ModReadBudgetPerPass</c>.
    /// </summary>
    /// <remarks>
    /// Without it, walking into a fresh pack pays for every monster's affixes in
    /// one capture and the whole pass stalls â€” which is visible as the markers
    /// stepping instead of moving. Budgeted, a new pack fills in over a few
    /// ticks and no single capture is ever the expensive one.
    /// </remarks>
    private const int ModReadBudget = 16;


    /// <summary>Identity of one area instance: where it lives and which zone it is.</summary>
    private static string Key(nint areaInstance, string? code) => $"{areaInstance:X}/{code ?? "?"}";

    /// <param name="mapUi">
    /// The caller's own reader. It caches the area's map elements across calls,
    /// and its caches are not thread-safe: the world lane and the fast lane each
    /// hold one, so neither can walk the other's list while it is being rebuilt.
    /// Passing null makes a throwaway, which is correct but re-discovers the UI
    /// tree on every call.
    /// </param>
    internal static CaptureResult Capture(
        IMemoryReader memory, nint moduleBase, AreaEpoch epoch, CaptureOptions options,
        MapUiReader? mapUi = null, CaptureCaches? caches = null)
    {
        mapUi ??= new MapUiReader();
        caches ??= new CaptureCaches();

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var result = CaptureOnce(memory, moduleBase, epoch, options, mapUi, caches);

            if (result.Status != CaptureStatus.InvalidatedByTransition) return result;
        }

        return CaptureResult.Failed(CaptureStatus.InvalidatedByTransition);
    }

    private static CaptureResult CaptureOnce(
        IMemoryReader memory, nint moduleBase, AreaEpoch epoch, CaptureOptions options,
        MapUiReader mapUi, CaptureCaches caches)
    {
        // Re-walked from the module base every time. Nothing is carried across
        // captures, because after a transition everything carried is a pointer
        // into memory the client has already reused.
        var before = GameSession.ResolveChain(memory, moduleBase);
        if (before is null) return CaptureResult.Failed(CaptureStatus.NoArea);

        var player = CapturePlayer(memory, before.LocalPlayer);
        var camera = CaptureCamera(memory, before.InGameState);
        var entities = CaptureEntities(memory, before.AreaInstance, before.LocalPlayer, options, caches);

        // The chain is walked again afterwards. If the area or the local player
        // moved, the reading spans two areas and is thrown away rather than
        // returned: mixed state looks entirely plausible and is entirely wrong.
        var after = GameSession.ResolveChain(memory, moduleBase);

        if (after is null ||
            after.AreaInstance != before.AreaInstance ||
            after.LocalPlayer != before.LocalPlayer)
            return CaptureResult.Failed(CaptureStatus.InvalidatedByTransition);

        // Read once and used for everything keyed on the area: the epoch, the
        // caches, and the curated landmark lookup.
        var areaCode = AreaCode(memory, before.AreaInstance);

        var snapshot = new WorldSnapshot(
            DateTimeOffset.UtcNow,
            epoch.Observe(before.AreaInstance, areaCode),
            new AreaId(before.AreaInstance),
            player,
            entities,
            camera,
            mapUi.Read(memory, before.InGameState, before.AreaInstance),
            Terrain(caches, memory, before.AreaInstance, areaCode),
            Landmarks(caches, memory, before.AreaInstance, areaCode),
            areaCode,
            Recall(caches, entities),
            League(caches, memory, before.AreaInstance),
            // The game's own loot tags. Read here, on the world walk, because
            // it is a UI tree walk and the render thread cannot afford one.
            ImmutableArray<LootLabelSnapshot>.Empty,
            ImmutableArray<ItemSlotSnapshot>.Empty,
            null);

        return new CaptureResult(CaptureStatus.Captured, snapshot);
    }


    /// <summary>
    /// Tile landmarks, scanned once per area. The scan walks every tile in the
    /// area â€” thousands â€” so doing it at capture rate would cost more than
    /// everything else here put together, and the answer cannot change without
    /// the area changing.
    /// </summary>
    private static ImmutableArray<LandmarkSnapshot> Landmarks(
        CaptureCaches caches, IMemoryReader memory, nint areaInstance, string? code)
    {
        var key = Key(areaInstance, code);

        if (key == caches.LandmarkKey) return caches.Landmarks;

        caches.LandmarkKey = key;

        caches.Landmarks = code is null
            ? ImmutableArray<LandmarkSnapshot>.Empty
            : LandmarkReader.Read(memory, areaInstance, code).ToImmutableArray();

        return caches.Landmarks;
    }

    /// <summary>
    /// The league the character is in, spelled as the price sources spell it.
    /// </summary>
    /// <remarks>
    /// Cached for the area's lifetime: it cannot change without a new character
    /// or a new session, and it is a chunked string read.
    /// </remarks>
    private static string? League(CaptureCaches caches, IMemoryReader memory, nint areaInstance)
    {
        if (caches.League is { Length: > 0 }) return caches.League;

        if (!memory.TryReadPointer(areaInstance + GameLayout.Server.Root, out var server) || server == 0)
            return null;

        var league = memory.TryReadWideString(
            server + GameLayout.Server.League,
            GameLayout.Native.StringBuffer,
            GameLayout.Native.StringSize,
            GameLayout.Native.StringCapacity);

        if (league is { Length: >= 3 }) caches.League = league;

        return league;
    }

    /// <summary>The area's own code, two dereferences in.</summary>
    private static string? AreaCode(IMemoryReader memory, nint areaInstance)
    {
        if (!memory.TryReadPointer(areaInstance + GameLayout.Area.Info, out var info) || info == 0) return null;
        if (!memory.TryReadPointer(info, out var text) || text == 0) return null;

        // A plain UTF-16 buffer, not a std::wstring: the area code is stored
        // inline at the front of AreaInfo and runs to its terminator.
        if (!memory.TryReadBytes(text, 64, out var bytes)) return null;

        var code = System.Text.Encoding.Unicode.GetString(bytes);
        var end = code.IndexOf(' ');

        if (end >= 0) code = code[..end];

        return string.IsNullOrEmpty(code) ? null : code;
    }

    /// <summary>
    /// New item identities per capture. POE2Radar's <c>ItemReadBudgetPerPass</c>.
    /// </summary>
    /// <remarks>
    /// The reference's twelve is a budget for a walk that runs many times a
    /// second. Ours runs at about eighteen and only reads an identity ONCE per
    /// address â€” after that the answer is cached for the area's lifetime â€” so
    /// twelve made a pack's worth of drops trickle in over several seconds, and
    /// the price arriving late is exactly what a user reports as "demora a
    /// aparecer". A whole floor of loot is a few dozen reads, once.
    /// </remarks>
    private const int ItemReadBudget = 64;

    /// <summary>
    /// The item UI, on its own cadence and off the world walk.
    /// </summary>
    /// <remarks>
    /// Each sweep visits the whole visible tree, and doing both on every world
    /// capture cost more than half the world rate. Everything drawn is
    /// interpolated between world ticks, so that slowdown was visible in every
    /// marker on screen, not just in the panels these serve.
    ///
    /// Throttled here rather than by the caller so a render loop can ask every
    /// frame and get the last answer for free.
    /// </remarks>
    internal static UiSnapshot? CaptureUi(
        IMemoryReader memory, nint moduleBase, UiSweepState state, TimeSpan interval)
    {
        if (DateTime.UtcNow - state.SweptAt < interval) return state.Last;

        var chain = GameSession.ResolveChain(memory, moduleBase);

        if (chain is null) return state.Last;

        var camera = CaptureCamera(memory, chain.InGameState);
        var scale = UiScale(camera);

        state.SweptAt = DateTime.UtcNow;

        var labels = LootLabelReader.Read(memory, chain.InGameState, scale);

        var tags = labels.Select(l => l.Label).ToImmutableArray();

        state.Targets = new LabelTargets(tags, labels.Select(l => l.Element).ToImmutableArray(), scale);

        var (slots, tooltip, captions) = ItemSlotReader.Read(
            memory, chain.InGameState, scale, state.Caches, state.ReadLabels);

        state.Last = new UiSnapshot(tags, slots, camera, tooltip, captions);

        return state.Last;
    }

    /// <summary>
    /// UI units to client pixels.
    /// </summary>
    /// <remarks>
    /// The client lays its UI out against a 1600-high design and scales to the
    /// real window, the same conversion the map centre needed. Without the
    /// camera we cannot know the window, and an unscaled rect would put the chip
    /// somewhere confidently wrong â€” so it is 1.0 and the caller sees tags in UI
    /// units, which the renderer then ignores for lack of a camera anyway.
    /// </remarks>
    private static float UiScale(CameraSnapshot? camera) =>
        camera is { Height: > 0 } ? camera.Height / 1600f : 1f;

    /// <summary>An item's identity never changes once dropped, so it is read once.</summary>
    private static ItemSnapshot? Item(
        CaptureCaches caches, GameEntity entity, nint address, ref int budget)
    {
        if (caches.Items.TryGetValue(address, out var cached)) return cached;

        if (budget <= 0) return null;

        budget--;

        var item = entity.Item();

        caches.Items[address] = item;

        return item;
    }

    /// <summary>
    /// Remembers this pass's exits and marked points, and hands back the ones
    /// that are no longer loaded.
    /// </summary>
    private static ImmutableArray<EntitySnapshot> Recall(
        CaptureCaches caches, ImmutableArray<EntitySnapshot> present)
    {
        foreach (var entity in present)
        {
            if (entity.Kind is EntityKind.Transition || entity.IsPoi) caches.Seen[entity.Id] = entity;
        }

        if (caches.Seen.Count == 0) return ImmutableArray<EntitySnapshot>.Empty;

        var here = new HashSet<EntityId>();

        foreach (var entity in present) here.Add(entity.Id);

        var recalled = ImmutableArray.CreateBuilder<EntitySnapshot>();

        foreach (var (id, entity) in caches.Seen)
        {
            if (!here.Contains(id)) recalled.Add(entity);
        }

        return recalled.ToImmutable();
    }

    /// <summary>
    /// The monster's affixes: cached forever within the area, and read at most
    /// <see cref="ModReadBudget"/> times per capture.
    /// </summary>
    private static ImmutableArray<string> Mods(
        CaptureCaches caches, GameEntity entity, nint address, ref int budget)
    {
        if (caches.Mods.TryGetValue(address, out var cached)) return cached;

        // Out of budget: leave it uncached so the next capture tries again. The
        // monster is simply unmodded for a tick or two, which no rule minds.
        if (budget <= 0) return ImmutableArray<string>.Empty;

        budget--;

        var mods = entity.Mods();

        caches.Mods[address] = mods;

        return mods;
    }

    /// <summary>
    /// The walkable grid, read once per area. Re-reading several megabytes at
    /// capture rate would cost more than everything else put together.
    /// </summary>
    private static TerrainSnapshot? Terrain(
        CaptureCaches caches, IMemoryReader memory, nint areaInstance, string? code)
    {
        // Keyed on the address AND the area's own code. The address alone let a
        // zone change go unnoticed when the client reused the allocation, and
        // the map kept showing the terrain of the zone that had been left.
        var key = Key(areaInstance, code);

        if (key == caches.TerrainKey) return caches.Terrain;

        caches.TerrainKey = key;

        // A new area means new scenery, and the same path can be marked in one
        // area and not in another. The mod cache goes with it: every entity is
        // reallocated, so an address there describes something else now.
        caches.Clear();

        caches.Terrain = TerrainReader.Read(memory, areaInstance);

        return caches.Terrain;
    }

    /// <summary>
    /// The fast lane: resolve the chain, read the player's world position and the
    /// map state. No entity walk, no terrain â€” a handful of reads, so it can run
    /// at render rate.
    /// </summary>
    internal static MapFrameSnapshot? CaptureMapFrame(
        IMemoryReader memory, nint moduleBase, AreaEpoch epoch, MapUiReader mapUi, UiSweepState sweep)
    {
        var state = GameSession.ResolveChain(memory, moduleBase);
        if (state is null) return null;

        var player = new GameEntity(memory, state.LocalPlayer);

        if (player.WorldPosition() is not { } world) return null;

        return new MapFrameSnapshot(
            DateTimeOffset.UtcNow,
            epoch.Observe(state.AreaInstance),
            new AreaId(state.AreaInstance),
            world,
            Projection.ToGrid(world, GameLayout.Spatial.WorldToGrid) ?? default,
            mapUi.Read(memory, state.InGameState, state.AreaInstance),
            CaptureCamera(memory, state.InGameState),
            Vitals(player),
            RefreshLabels(memory, sweep.Targets));
    }

    /// <summary>
    /// Where the tags the sweep found are RIGHT NOW.
    /// </summary>
    /// <remarks>
    /// The reason a price chip stuttered while a minion did not. A minion's
    /// marker is its world position projected through this frame's camera, so it
    /// moves with the view. A chip was pinned to a rectangle measured by the
    /// sweep eight times a second, so it moved eight times a second — the game's
    /// own label glided and ours hopped along behind it.
    ///
    /// Nothing is searched for here. The sweep already decided which elements
    /// are ground tags and what they say; this asks each of them one question,
    /// and one question is one read per level of the UI tree above it. Measured
    /// live: ten tags for 66 reads a frame, under a tenth of a millisecond.
    ///
    /// An element the sweep found can be gone by the time this asks — the item
    /// was picked up, the area changed. The address stays readable and answers
    /// with whatever now occupies it, which is how a tag ended up reading
    /// (-87140, 616085): not the tag moving, the tag no longer being there.
    ///
    /// The tell is the SIZE. A tag's box is its text, and its text does not
    /// change while it exists — measured live, zero changes in 258 readings of
    /// a tag that was really there. So a size that disagrees with the sweep's
    /// means the element is not that tag any more, and the honest answer is the
    /// sweep's own rectangle: an eighth of a second stale, in the right place,
    /// and dropped entirely by the next sweep.
    ///
    /// Keeping it rather than discarding it is deliberate. This refresh replaced
    /// nothing — before it, every tag was drawn at the sweep's rectangle — so
    /// falling back to exactly that is the floor. A refresh that can hide a tag
    /// the sweep found would be worse than no refresh at all.
    /// </remarks>
    internal static ImmutableArray<LootLabelSnapshot> RefreshLabels(
        IMemoryReader memory, LabelTargets targets)
    {
        if (targets.Labels.IsEmpty || targets.Labels.Length != targets.Elements.Length) return default;

        var fresh = ImmutableArray.CreateBuilder<LootLabelSnapshot>(targets.Labels.Length);

        foreach (var (label, element) in targets.Labels.Zip(targets.Elements))
        {
            var (x, y, w, h) = MapUiReader.AbsoluteRect(memory, element);

            var width = w * targets.Scale;
            var height = h * targets.Scale;

            if (Math.Abs(width - label.Width) > 1f || Math.Abs(height - label.Height) > 1f)
            {
                fresh.Add(label);
                continue;
            }

            fresh.Add(label with { X = x * targets.Scale, Y = y * targets.Scale });
        }

        return fresh.ToImmutable();
    }

    /// <summary>
    /// The three pools, read straight off the Life component.
    /// </summary>
    /// <remarks>
    /// On the fast lane because anything deciding on health has to be looking at
    /// health as it is now. One component lookup and nine reads.
    /// </remarks>
    private static VitalsSnapshot Vitals(GameEntity player)
    {
        var vitals = player.Vitals();

        var health = Find(vitals, "Health");
        var mana = Find(vitals, "Mana");
        var shield = Find(vitals, "EnergyShield");

        return new VitalsSnapshot(
            health.Current, health.Max, mana.Current, mana.Max, shield.Current, shield.Max);
    }

    private static Vital Find(Vital[] vitals, string name)
    {
        foreach (var vital in vitals)
        {
            if (vital.Name == name) return vital;
        }

        return new Vital(name, 0, 0, 0);
    }

    private static PlayerSnapshot CapturePlayer(IMemoryReader memory, nint address)
    {
        var entity = new GameEntity(memory, address);

        var (name, level) = entity.Identity();
        var world = entity.WorldPosition();

        var vitals = entity.Vitals();

        return new PlayerSnapshot(
            name,
            level,
            Current(vitals, "Health"), Max(vitals, "Health"),
            Current(vitals, "Mana"), Max(vitals, "Mana"),
            Current(vitals, "EnergyShield"), Max(vitals, "EnergyShield"),
            world,
            Projection.ToGrid(world, GameLayout.Spatial.WorldToGrid),
            entity.Resistances());

        static int Current(Vital[] vitals, string name) =>
            vitals.FirstOrDefault(v => v.Name == name)?.Current ?? 0;

        static int Max(Vital[] vitals, string name) =>
            vitals.FirstOrDefault(v => v.Name == name)?.Max ?? 0;
    }

    private static CameraSnapshot? CaptureCamera(IMemoryReader memory, nint inGameState)
    {
        var camera = CameraReader.Resolve(memory, inGameState);
        if (camera is null) return null;

        var matrix = camera.ViewProjection();
        var viewport = camera.Viewport();

        if (matrix is null || viewport is not { } size) return null;

        return new CameraSnapshot(ImmutableArray.Create(matrix), size.Width, size.Height);
    }

    private static ImmutableArray<EntitySnapshot> CaptureEntities(
        IMemoryReader memory, nint areaInstance, nint localPlayer, CaptureOptions options,
        CaptureCaches caches)
    {
        var addresses = EntityWorld.Enumerate(memory, areaInstance, GameLayout.World.AwakeEntities, caches.Types);

        if (options.IncludeSleepingEntities)
        {
            addresses = addresses
                .Concat(EntityWorld.Enumerate(memory, areaInstance, GameLayout.World.SleepingEntities, caches.Types))
                .ToArray();
        }

        var captured = ImmutableArray.CreateBuilder<EntitySnapshot>();

        var modBudget = ModReadBudget;
        var itemBudget = ItemReadBudget;

        foreach (var address in addresses)
        {
            if (captured.Count >= options.MaxEntities) break;

            var entity = new GameEntity(memory, address, caches.Types);

            // No metadata means the address stopped describing an entity between
            // enumeration and description, which is ordinary in a live client.
            var metadata = entity.Metadata;
            if (metadata is null) continue;

            // Cheap pass first: the path alone settles most of it, and building a
            // component catalogue is the expensive part of describing an entity.
            // An area holds hundreds of scenery tiles under /Terrain/, and paying
            // a catalogue build for each of them slowed the whole walk enough to
            // be visible in how often monsters moved.
            if (SceneryWithoutIcon(caches, metadata)) continue;

            var componentNames = entity.ComponentNames();

            var kind = EntityClassifier.Classify(
                metadata, address == localPlayer, componentNames, entity.IsFriendly());

            // Junk never reaches a snapshot at all. Attachments, effect nodes and
            // invisible daemons outnumber everything the map has a use for, and
            // describing them costs a component walk each.
            if (kind == EntityKind.Junk) continue;

            // The client's own map marker. Asked of every surviving entity, not
            // just of a category we guessed at: the whole point of the component
            // is that the game has already decided what belongs on the map.
            var (isPoi, iconComplete) = entity.Icon();

            // Remember the verdict for this path so the next rock sharing it is
            // rejected by the cheap pass above. Scenery paths repeat by the
            // hundred; POI paths agree with each other too.
            if (kind == EntityKind.Object)
            {
                caches.SceneryVerdict[metadata] = isPoi;

                if (!isPoi) continue;
            }

            var world = entity.WorldPosition();
            var vitals = entity.Vitals();

            // A monster with a Life component and no health left is a corpse.
            var alive = vitals.Length == 0 ||
                        vitals.All(v => v.Name != "Health" || v.Max <= 0 || v.Current > 0);

            var mods = kind == EntityKind.Monster
                ? Mods(caches, entity, address, ref modBudget)
                : ImmutableArray<string>.Empty;

            captured.Add(new EntitySnapshot(
                new EntityId(address),
                metadata,
                componentNames,
                world,
                Projection.ToGrid(world, GameLayout.Spatial.WorldToGrid),
                vitals.ToImmutableArray(),
                kind,
                alive,
                kind == EntityKind.Monster ? entity.Rarity() : MonsterRarity.Unknown,
                isPoi,
                iconComplete,
                mods,
                // Only asked of the things that can have one. A transition or a
                // marked point is a handful per area; a monster is five hundred.
                kind == EntityKind.Transition || isPoi ? entity.FriendlyName() : null,
                // Only asked of things that could be a drop, and budgeted the
                // way the reference budgets it: a floor covered in loot must not
                // make one capture the expensive one.
                // Gated on the COMPONENT, not on the metadata string. A drop's
                // path names the item, not its wrapper, so testing the path for
                // "WorldItem" matched nothing and the identity was never read.
                componentNames.Contains(GameLayout.Item.WorldItemComponent)
                    ? Item(caches, entity, address, ref itemBudget)
                    : null,
                // Where an exit leads, as a code. Asked only of transitions â€”
                // the one kind that has a destination â€” and it is the join key a
                // levelling route needs, because the name beside it is
                // localized and the code is not.
                kind == EntityKind.Transition ? entity.DestinationCode() : null));
        }

        return captured.ToImmutable();
    }
}
