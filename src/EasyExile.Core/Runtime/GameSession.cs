using System.Diagnostics;
using System.Runtime.InteropServices;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Snapshots;
using EasyExile.Core.World;

namespace EasyExile.Core.Runtime;

public sealed record SessionState(
    nint GameStateRoot,
    nint InGameState,
    nint AreaInstance,
    nint LocalPlayer);

/// <summary>
/// Attaches to the client and resolves the root chain using contract offsets
/// only. Every address it hands out is re-resolved on demand rather than kept,
/// because the client reallocates entities constantly and freed memory keeps
/// reading as whatever it last held.
/// </summary>
public sealed class GameSession : IDisposable
{
    private readonly AreaEpoch _epoch = new();

    // The fast lane keeps its own epoch counter and map-element cache, because
    // it runs on a different thread from the world capture.
    private readonly AreaEpoch _fastEpoch = new();
    private readonly World.MapUiReader _fastMapUi = new();
    private readonly Snapshots.UiSweepState _uiSweep = new();

    /// <summary>
    /// The world lane's own map reader, for the same reason the fast lane has
    /// one: the element cache is rebuilt on an area change, and two threads
    /// walking one list while it is cleared is a crash, not a stale read.
    /// </summary>
    private readonly World.MapUiReader _worldMapUi = new();

    /// <summary>
    /// What the world lane remembers between captures. Per session, because
    /// every key in it is an address in THIS client.
    /// </summary>
    private readonly CaptureCaches _caches = new();

    private GameSession(
        Process process, IMemoryReader memory, IMemoryReader fastMemory,
        IMemoryReader uiMemory, ClientBuild build)
    {
        Process = process;
        Memory = memory;
        FastMemory = fastMemory;
        UiMemory = uiMemory;
        Build = build;
    }

    /// <summary>
    /// The client process. Public because an overlay has to align itself to the
    /// game window, which needs the handle. It grants no read access.
    /// </summary>
    public Process Process { get; }

    /// <summary>
    /// Internal, and the reason the whole Memory namespace is internal. A visual
    /// module receives a <see cref="WorldSnapshot"/>; handing it a reader would
    /// let it read the client on its own render thread, at which point the
    /// snapshot boundary would exist only as a convention.
    /// </summary>
    internal IMemoryReader Memory { get; }

    /// <summary>
    /// A second reader for the fast lane.
    /// </summary>
    /// <remarks>
    /// Not an optimisation — a correctness requirement. The reader caches memory
    /// regions in a plain list with no locking, so two threads sharing one reader
    /// would corrupt it. POE2Radar keeps separate reader stacks for the same
    /// reason. Same process, separate caches.
    /// </remarks>
    internal IMemoryReader FastMemory { get; }

    /// <summary>The UI lane's own reader, so a sweep never waits behind a frame.</summary>
    internal IMemoryReader UiMemory { get; }

    public ClientBuild Build { get; }

    /// <summary>
    /// Finds the client the contract describes.
    /// </summary>
    /// <remarks>
    /// Path of Exile 1 and Path of Exile 2 ship the same process name, so on a
    /// machine with both installed picking the first match is a coin flip. The
    /// build gate refuses the wrong one either way, but it reports it as a
    /// patched client rather than as the wrong game, which sends whoever reads
    /// it looking for an offset problem that does not exist. Preferring the
    /// process whose executable the contract was proven on costs one PE hash and
    /// removes the whole class of confusion.
    ///
    /// When nothing matches, the first candidate is still returned so the gate
    /// produces its refusal with a real fingerprint to compare against.
    /// </remarks>
    public static Process? FindClient()
    {
        var candidates = Process.GetProcessesByName("PathOfExile")
            .Concat(Process.GetProcessesByName("PathOfExileSteam"))
            .ToArray();

        return candidates.FirstOrDefault(DescribedByContract) ?? candidates.FirstOrDefault();
    }

    private static bool DescribedByContract(Process process)
    {
        try
        {
            var executable = process.MainModule?.FileName;

            return executable is not null && BuildGate.Check(executable).Matches;
        }
        // ExternalException rather than Win32Exception: the latter is the type
        // MainModule actually throws, but naming it drags in
        // Microsoft.Win32.Primitives, and the consumer is asserted to carry no
        // assembly reference outside the core framework. Win32Exception derives
        // from ExternalException, so this catches the same failures.
        catch (Exception error) when (
            error is InvalidOperationException or ExternalException or IOException or UnauthorizedAccessException)
        {
            // A process that cannot be inspected is not the one being looked for.
            return false;
        }
    }

    /// <summary>
    /// Refuses to attach unless the client is the build the contract was proven
    /// against. There is no override: reading build A with build B's offsets
    /// produces plausible values that are simply wrong.
    /// </summary>
    public static GameSession Attach(Process process)
    {
        var executable = process.MainModule?.FileName
                         ?? throw new InvalidOperationException(CoreText.ClientModuleUnreadable);

        var build = RequireMatchingBuild(executable);

        // Three readers, three cadences. A reader is a handle and a buffer, not
        // a connection, so a third costs nothing — and sharing one across
        // threads is what turns an independent cadence back into a queue.
        return new GameSession(
            process,
            new ProcessMemory(process),
            new ProcessMemory(process),
            new ProcessMemory(process),
            build);
    }

    /// <summary>
    /// The gate itself, throwing rather than reporting. There is deliberately no
    /// flag that turns this into a warning: a mismatched build does not read
    /// slightly wrong, it reads entirely different fields and says nothing.
    /// </summary>
    public static ClientBuild RequireMatchingBuild(string executablePath)
    {
        var check = BuildGate.Check(executablePath);

        if (!check.Matches) throw new BuildMismatchException(check);

        return check.Client;
    }

    /// <summary>Re-walks the chain. Nothing it returns is cached across calls.</summary>
    internal SessionState? Resolve() => ResolveChain(Memory, Memory.ModuleBase);

    /// <summary>
    /// The product-facing entry point: the live client turned into immutable
    /// state. Pull-based and synchronous — the Core runs no thread of its own,
    /// so the caller decides the rate and owns the result.
    /// </summary>
    public CaptureResult Capture() => Capture(CaptureOptions.Default);

    /// <inheritdoc cref="Capture()"/>
    public CaptureResult Capture(CaptureOptions options) =>
        SnapshotCapture.Capture(Memory, Memory.ModuleBase, _epoch, options, _worldMapUi, _caches);

    /// <summary>
    /// The fast half: player position and map state, cheap enough to take once
    /// per rendered frame. Uses its own reader so it can run while a capture is
    /// in flight on another thread.
    /// </summary>
    /// <summary>
    /// Every map-UI candidate and its live state. Diagnostics only: the overlay
    /// uses the single aggregate answer, which is the point of the reader.
    /// </summary>
    internal IEnumerable<(nint Element, MapSnapshot State, bool Toggler)> MapCandidates()
    {
        var state = ResolveChain(Memory, Memory.ModuleBase);

        return state is null
            ? Array.Empty<(nint, MapSnapshot, bool)>()
            : _worldMapUi.Candidates(Memory, state.InGameState, state.AreaInstance);
    }

    /// <summary>
    /// Every visible UI element carrying text, with its position in client
    /// pixels. Diagnostics only.
    /// </summary>
    /// <remarks>
    /// The game writes its own names on its own map, and a UI element knows
    /// where it is. That makes alignment measurable instead of estimated.
    /// </remarks>
    internal IEnumerable<(string Text, float X, float Y)> VisibleLabels(
        nint under = 0, int maxElements = 30000)
    {
        var state = ResolveChain(Memory, Memory.ModuleBase);

        if (state is null) yield break;

        var root = under;

        // Restricted to a subtree when asked. Walking the whole UI returns every
        // panel's text — a trade window's buttons outnumber the map's names and
        // drown the thing being measured.
        if (root == 0 &&
            (!Memory.TryReadPointer(state.InGameState + Contract.GameLayout.Roots.UiRoot, out root) || root == 0))
            yield break;

        // The UI is laid out in a fixed virtual space and scaled by height.
        var camera = Camera.GameCamera.Resolve(Memory, state.InGameState);
        var height = camera?.Viewport()?.Height ?? 1080;
        var scale = height / 1600f;

        var seen = new HashSet<nint>();
        var queue = new Queue<nint>();

        queue.Enqueue(root);

        while (queue.Count > 0 && seen.Count < maxElements)
        {
            var element = queue.Dequeue();

            if (element == 0 || !seen.Add(element)) continue;

            if (Memory.TryReadPointer(element + Contract.GameLayout.Ui.Children, out var first) && first != 0 &&
                Memory.TryReadPointer(element + Contract.GameLayout.Ui.Children + 8, out var last))
            {
                var count = ((long)last - first) / 8;

                if (count is > 0 and <= 8192)
                {
                    for (long i = 0; i < count; i++)
                    {
                        if (Memory.TryReadPointer(first + (nint)(i * 8), out var child)) queue.Enqueue(child);
                    }
                }
            }

            if (!Memory.TryRead<uint>(element + Contract.GameLayout.Ui.Flags, out var flags)) continue;
            if ((flags & (1u << Contract.GameLayout.Ui.VisibleBit)) == 0) continue;

            var text = Memory.TryReadWideString(
                element + Contract.GameLayout.Ui.Text,
                Contract.GameLayout.Native.StringBuffer,
                Contract.GameLayout.Native.StringSize,
                Contract.GameLayout.Native.StringCapacity);

            if (!Readable(text)) continue;

            var (x, y, _, _) = World.MapUiReader.AbsoluteRect(Memory, element);

            yield return (text!, x * scale, y * scale);
        }
    }

    /// <summary>The UI root's own origin, for diagnostics.</summary>
    internal (float X, float Y, float W, float H) UiRootRect()
    {
        var state = ResolveChain(Memory, Memory.ModuleBase);

        if (state is null) return default;

        return Memory.TryReadPointer(state.InGameState + Contract.GameLayout.Roots.UiRoot, out var root) && root != 0
            ? World.MapUiReader.AbsoluteRect(Memory, root)
            : default;
    }

    /// <summary>
    /// Any readable text reachable from an entity, by offset. Diagnostics only.
    /// </summary>
    /// <remarks>
    /// Answers "does this thing carry its own name anywhere" without committing
    /// to a field. Every qword in the entity's head is treated as a candidate
    /// pointer and the target is decoded both ways a game stores a string.
    /// </remarks>
    internal IEnumerable<(int Offset, string Text)> StringsNear(Snapshots.EntityId id) =>
        StringsAt(id.Address, 0x400);

    /// <summary>The components an entity carries, by name. Diagnostics only.</summary>
    internal IReadOnlyDictionary<string, nint> ComponentsOf(Snapshots.EntityId id) =>
        new World.GameEntity(Memory, id.Address).Components();

    /// <summary>
    /// Readable text reachable from an address, directly or one dereference in.
    /// </summary>
    internal IEnumerable<(int Offset, string Text)> StringsAt(nint address, int span)
    {
        if (address == 0) yield break;

        for (var offset = 0; offset < span; offset += 8)
        {
            if (!Memory.TryReadPointer(address + offset, out var target) || target == 0) continue;

            var direct = Memory.TryReadUtf16(target, 64);

            if (Readable(direct)) { yield return (offset, direct!); continue; }

            var wide = Memory.TryReadWideString(
                address + offset,
                Contract.GameLayout.Native.StringBuffer,
                Contract.GameLayout.Native.StringSize,
                Contract.GameLayout.Native.StringCapacity);

            if (Readable(wide)) { yield return (offset, wide!); continue; }

            // One more hop. A destination is usually a reference to a data row,
            // and the row holds the name — the entity only holds the reference.
            for (var inner = 0; inner < 0x60; inner += 8)
            {
                if (!Memory.TryReadPointer(target + inner, out var row) || row == 0) continue;

                var nested = Memory.TryReadUtf16(row, 64);

                if (Readable(nested)) yield return ((offset * 0x1000) + inner, nested!);
            }
        }
    }

    /// <summary>Text a person would recognise: letters and spaces, not mojibake.</summary>
    private static bool Readable(string? text)
    {
        if (text is not { Length: >= 4 and <= 64 }) return false;

        var letters = 0;

        foreach (var c in text)
        {
            if (char.IsLetter(c)) letters++;
            else if (c is not (' ' or '\'' or '-' or '_' or ',' or '.')) return false;
        }

        return letters >= 4;
    }

    /// <summary>Where a UI element sits, for diagnostics.</summary>
    internal (float X, float Y, float W, float H) RectOf(nint element) =>
        World.MapUiReader.AbsoluteRect(Memory, element);

    public MapFrameSnapshot? CaptureMapFrame() =>
        SnapshotCapture.CaptureMapFrame(FastMemory, FastMemory.ModuleBase, _fastEpoch, _fastMapUi, _uiSweep);

    /// <summary>
    /// The item UI, on the render lane and on its own throttle.
    /// </summary>
    /// <remarks>
    /// Its OWN reader and, in the radar, its own thread. Putting this on the
    /// render lane was worse than leaving it on the world walk: a sweep visits
    /// tens of thousands of nodes, so the thread that draws stopped to do it
    /// eight times a second, and periodic hitches in the drawing are more
    /// visible than a slow world.
    /// </remarks>
    /// <summary>
    /// Write the whole UI tree to a file in this directory, and say where.
    /// </summary>
    /// <remarks>
    /// Public because it is the answer to what cost most of a session: a probe
    /// only sees what is on screen while it runs, so every question about a
    /// panel needed the panel open at that instant, and each miss read exactly
    /// like a real negative result. A key press hands over the whole tree
    /// instead, at a moment somebody chose.
    /// </remarks>
    public string? DumpUiTree(string directory) =>
        DumpUiTree(directory, Diagnostics.DumpOptions.Drawn);

    /// <inheritdoc cref="DumpUiTree(string)"/>
    public string? DumpUiTree(string directory, Diagnostics.DumpOptions options) =>
        Diagnostics.UiTreeDump.Save(UiMemory, UiMemory.ModuleBase, directory, options);

    /// <summary>
    /// What is under this point on screen, in client pixels.
    /// </summary>
    /// <remarks>
    /// Walks from the root, so it belongs on a key rather than on a frame. Its
    /// own reader, because a picker runs while the world walk is in flight.
    /// </remarks>
    public Diagnostics.ElementProbe? PickElement(float x, float y, float uiScale) =>
        Diagnostics.UiTreeDump.Probe(UiMemory, UiMemory.ModuleBase, x, y, uiScale);

    public UiSnapshot? CaptureUi() => CaptureUi(UiSweepOptions.Default);

    /// <inheritdoc cref="CaptureUi()"/>
    /// <remarks>
    /// The options arrive as a record rather than as a flag, which is not
    /// decoration: nothing on this type may take a bool, because that is the
    /// shape a "continue past the build mismatch" escape hatch would have, and a
    /// test holds the whole surface to it. The world capture already reads its
    /// options the same way.
    /// </remarks>
    public UiSnapshot? CaptureUi(UiSweepOptions options)
    {
        _uiSweep.ReadLabels = options.ReadLabels;

        return SnapshotCapture.CaptureUi(
            UiMemory, UiMemory.ModuleBase, _uiSweep, UiSweepInterval);
    }

    /// <summary>
    /// Eight sweeps a second. A panel does not change faster than a person can
    /// see, and a ground tag's position is corrected against the camera anyway.
    /// </summary>
    private static readonly TimeSpan UiSweepInterval = TimeSpan.FromMilliseconds(125);

    /// <summary>
    /// Walks the chain from the module base. The state stack can hold several
    /// states at once and the in-game one is not always first, so the one that
    /// carries a usable area is chosen rather than assumed. Re-running this is
    /// the whole area-change story: after a transition the previous area and
    /// every entity in it are gone, and only a fresh walk is trustworthy.
    /// </summary>
    internal static SessionState? ResolveChain(IMemoryReader memory, nint moduleBase)
    {
        var slot = moduleBase + GameLayout.Roots.GlobalSlotRva;

        if (!memory.TryReadPointer(slot, out var root) || !GameEntity.IsPlausibleAddress(root))
            return null;

        foreach (var state in States(memory, root))
        {
            if (!GameEntity.IsPlausibleAddress(state)) continue;

            if (!memory.TryReadPointer(state + GameLayout.Roots.AreaInstance, out var area)) continue;
            // The span comes from the fields actually read, not from a round
            // number; see GameLayout.World.RequiredReadableSpan.
            if (!GameEntity.IsPlausibleAddress(area) ||
                !memory.IsReadable(area, GameLayout.World.RequiredReadableSpan)) continue;

            if (!memory.TryReadPointer(area + GameLayout.World.LocalPlayer, out var player)) continue;

            var entity = new GameEntity(memory, player);
            if (!entity.IsValid) continue;

            return new SessionState(root, state, area, player);
        }

        return null;
    }

    /// <summary>
    /// Every state the client might have put the in-game one in.
    /// </summary>
    /// <remarks>
    /// The vector was not the only place. GameState also carries an inline array
    /// of slots, and on the 2026-09-05 build the vector is empty and the state
    /// lives there — so reading only the vector turned a correct root into a
    /// dead chain, which is indistinguishable from a moved offset until you look.
    /// </remarks>
    private static IEnumerable<nint> States(IMemoryReader memory, nint root)
    {
        if (memory.TryReadPointer(root + GameLayout.Roots.CurrentStateVector + GameLayout.Native.VectorFirst, out var first) &&
            memory.TryReadPointer(root + GameLayout.Roots.CurrentStateVector + GameLayout.Native.VectorLast, out var last) &&
            first != 0 && last >= first)
        {
            var count = (int)((last - first) / 8);

            if (count is > 0 and <= 64)
            {
                for (int i = 0; i < count; i++)
                {
                    if (memory.TryReadPointer(first + (i * 8), out var state)) yield return state;
                }
            }
        }

        for (int i = 0; i < GameLayout.Roots.StateSlotCount; i++)
        {
            if (memory.TryReadPointer(
                    root + GameLayout.Roots.StatesArray + (i * GameLayout.Roots.StateSlotStride), out var slot))
                yield return slot;
        }
    }

    public void Dispose()
    {
        Memory.Dispose();
        FastMemory.Dispose();
        UiMemory.Dispose();
    }
}

public sealed class BuildMismatchException : Exception
{
    public BuildMismatchException(BuildCheck check)
        : base(CoreText.BuildMismatchRefusal(check.Detail))
        => Check = check;

    public BuildCheck Check { get; }
}
