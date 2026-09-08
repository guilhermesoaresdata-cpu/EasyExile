using System.Reflection;
using System.Text.RegularExpressions;
using EasyExile.Core.Contract;
using EasyExile.Core.Runtime;
using EasyExile.Core.Snapshots;

namespace EasyExile.Core.Tests;

/// <summary>
/// The boundary between the Core and everything that draws.
/// </summary>
/// <remarks>
/// The rule is that a visual module receives captured state and cannot read the
/// client. Written down, that is a convention; asserted here against the real
/// assemblies, it is a property of the build. These tests are the reason the
/// memory layer is internal rather than merely undocumented.
/// </remarks>
public class ArchitectureTests
{
    private static Assembly Radar => typeof(EasyExile.Radar.Runtime.RadarApplication).Assembly;
    private static Assembly Core => typeof(GameSession).Assembly;

    private static readonly string[] ForbiddenInRadar =
    {
        "IMemoryReader",
        "ProcessMemory",
        "NativeText",
        "GameLayout",
        "GameEntity",
        "EntityWorld",
        "GameCamera",
        "Poe2Analyzer",
        "GameOffsets",
    };

    private static IEnumerable<string> RadarSources() =>
        ContractIsolationTests.SourceFiles(Path.Combine("src", "EasyExile.Radar"));

    // ---- what the Radar is allowed to depend on -----------------------------

    /// <summary>
    /// EasyExile.Core for game state, and a rendering stack that was reviewed
    /// before it was added. See Overlay/BACKEND.md.
    /// </summary>
    private static readonly string[] AllowedRadarReferences =
    {
        "EasyExile.Core",
        "ClickableTransparentOverlay",
        "ImGui.NET",

        // The terrain mask is built as an image and handed to the overlay's own
        // texture path, which is ImageSharp. It arrives with the overlay stack
        // anyway; the radar now names it directly.
        "SixLabors.ImageSharp",
    };

    [Fact]
    public void The_radar_depends_on_the_core_and_a_vetted_rendering_stack()
    {
        // An allowlist rather than a ban on everything: the radar has to draw,
        // so it needs a graphics stack. What must not happen is a dependency
        // arriving without anyone deciding to add it, and an assertion on the
        // exact set is what makes that a failing build rather than a surprise.
        var referenced = Radar.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System", StringComparison.Ordinal)
                     && n is not ("netstandard" or "mscorlib" or "Microsoft.CSharp"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(AllowedRadarReferences.OrderBy(n => n, StringComparer.Ordinal).ToArray(), referenced);
    }

    [Fact]
    public void The_rendering_stack_stays_out_of_the_core()
    {
        // The Core must remain drawable-by-anything. If ImGui or Direct3D ever
        // reached it, the snapshot boundary would have been crossed from the
        // other side.
        var referenced = Core.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, n => n.Contains("ImGui", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Overlay", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Vortice", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Direct3D", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_radar_does_not_reference_the_offset_contract()
    {
        // GameOffsets is all consts, so it never survives as an assembly
        // reference anywhere. The reachable check below is the one with teeth.
        Assert.DoesNotContain(
            Radar.GetReferencedAssemblies(),
            a => a.Name!.Contains("GameOffsets", StringComparison.OrdinalIgnoreCase));

        foreach (var source in RadarSources().Select(File.ReadAllText))
        {
            Assert.DoesNotContain("Poe2Analyzer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("GameOffsets", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_radar_does_not_reference_the_analyzer()
    {
        var referenced = Radar.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

        Assert.DoesNotContain(referenced, n => n.StartsWith("Analyzer", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referenced, n => n.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));

        foreach (var source in RadarSources().Select(File.ReadAllText))
        {
            Assert.DoesNotContain("Analyzer", source, StringComparison.Ordinal);
            Assert.DoesNotContain("knowledge", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_radar_cannot_name_a_memory_type()
    {
        foreach (var file in RadarSources())
        {
            var source = File.ReadAllText(file);
            var name = Path.GetFileName(file);

            foreach (var forbidden in ForbiddenInRadar)
                Assert.False(source.Contains(forbidden, StringComparison.Ordinal),
                    $"{name} names {forbidden}; the radar consumes snapshots only");
        }
    }

    [Fact]
    public void No_radar_signature_mentions_a_memory_type()
    {
        // The source scan above catches intent; this catches a type arriving
        // through inference, where the name never appears in the file at all.
        foreach (var type in Radar.GetTypes())
        {
            foreach (var referenced in SignatureTypes(type))
                AssertNotAMemoryType(referenced, $"{type.Name} signature");
        }
    }

    [Fact]
    public void The_render_loop_reads_snapshots_and_never_captures()
    {
        // The rule the whole two-rate design rests on. The render loop is handed
        // an ISnapshotSource, which exposes three read-only members and no way to
        // ask for a capture — so drawing at 144 Hz cannot start reading another
        // process 144 times a second, whatever anyone writes inside a feature.
        var contract = typeof(EasyExile.Radar.Runtime.ISnapshotSource);

        // Read-only members plus the cheap captures. The rule is not "one
        // method" — it is that nothing here can ask for an ENTITY WALK, which is
        // the expensive thing and the thing the world lane exists to own.
        Assert.All(contract.GetProperties(), p => Assert.False(p.CanWrite, $"{p.Name} is settable"));

        var callable = contract.GetMethods()
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // CaptureMapFrame: player, map and camera, a handful of reads, per frame.
        // CaptureUi: the item panels, throttled inside to eight sweeps a second.
        // It lives here rather than on the world walk because paying for panels
        // out of the entity budget halved the world rate, and everything drawn
        // is interpolated between world ticks.
        Assert.Equal(["CaptureMapFrame", "CaptureUi"], callable);

        var renderLoop = typeof(EasyExile.Radar.Runtime.RadarRenderLoop);

        foreach (var field in renderLoop.GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
        {
            Assert.False(field.FieldType == typeof(GameSession), "the render loop holds a session");
            Assert.False(field.FieldType.Name.Contains("UpdateLoop", StringComparison.Ordinal),
                "the render loop is bound to the capture loop rather than to a snapshot source");
        }

        var file = ContractIsolationTests
            .SourceFiles(Path.Combine("src", "EasyExile.Radar"))
            .Single(f => f.EndsWith("RadarRenderLoop.cs", StringComparison.OrdinalIgnoreCase));

        var source = File.ReadAllText(file);

        // The heavy walk stays on its own loop. The render loop does take the
        // cheap map frame every frame — that is the point of the split, and the
        // reason the map moves at render rate instead of at capture rate — but
        // it must never trigger the entity walk.
        Assert.DoesNotContain(".Capture()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Capture(options", source, StringComparison.Ordinal);
        Assert.Contains("CaptureMapFrame()", source, StringComparison.Ordinal);
    }

    // ---- what a snapshot is allowed to carry --------------------------------

    [Fact]
    public void Nothing_reachable_from_a_world_snapshot_can_read_the_client()
    {
        var reachable = ReachableFrom(typeof(WorldSnapshot));

        Assert.Contains(typeof(PlayerSnapshot), reachable);
        Assert.Contains(typeof(EntitySnapshot), reachable);
        Assert.Contains(typeof(CameraSnapshot), reachable);

        foreach (var type in reachable)
            AssertNotAMemoryType(type, "reachable from WorldSnapshot");
    }

    [Fact]
    public void A_snapshot_carries_no_raw_address()
    {
        // Identity travels as an opaque struct. A bare nint would be a pointer a
        // consumer could be tempted to do something with, and would also survive
        // an area change looking perfectly valid.
        foreach (var type in ReachableFrom(typeof(WorldSnapshot)))
        {
            if (type == typeof(AreaId) || type == typeof(EntityId)) continue;

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.False(property.PropertyType == typeof(nint) || property.PropertyType == typeof(nint?),
                    $"{type.Name}.{property.Name} exposes a raw address");
        }
    }

    [Fact]
    public void The_memory_layer_and_the_contract_are_invisible_outside_the_core()
    {
        var exported = Core.GetExportedTypes()
            .Where(t => t.Namespace is "EasyExile.Core.Memory" or "EasyExile.Core.Contract")
            .ToArray();

        Assert.Empty(exported);
    }

    [Fact]
    public void The_session_hands_out_snapshots_and_not_a_reader()
    {
        foreach (var member in typeof(GameSession).GetMembers(BindingFlags.Public | BindingFlags.Instance |
                                                              BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            foreach (var type in MemberTypes(member))
                AssertNotAMemoryType(type, $"GameSession.{member.Name}");
        }
    }

    // ---- coupling that must stay visible ------------------------------------

    [Fact]
    public void Readable_spans_are_derived_from_the_contract_rather_than_written_as_numbers()
    {
        // A literal here would be a hidden dependency on the current layout: if a
        // patch moved SleepingEntities past it, the chain would stop resolving
        // with nothing to explain why, and the build gate would not catch it
        // because the contract would have been re-exported for that same patch.
        Assert.True(GameLayout.World.RequiredReadableSpan > GameLayout.World.SleepingEntities);
        Assert.True(GameLayout.World.RequiredReadableSpan > GameLayout.World.AwakeEntities);
        Assert.True(GameLayout.World.RequiredReadableSpan > GameLayout.World.LocalPlayer);

        Assert.True(GameLayout.View.RequiredReadableSpan >=
                    GameLayout.View.ViewProjection + GameLayout.View.ViewProjectionSize);
        Assert.True(GameLayout.View.RequiredReadableSpan >=
                    GameLayout.View.Viewport + GameLayout.View.ViewportSize);
    }

    [Fact]
    public void No_core_file_passes_a_literal_span_to_a_readability_check()
    {
        var literalSpan = new Regex(@"IsReadable\([^)]*,\s*0x[0-9A-Fa-f]+", RegexOptions.Compiled);

        foreach (var file in ContractIsolationTests.SourceFiles(Path.Combine("src", "EasyExile.Core")))
        {
            var source = File.ReadAllText(file);

            Assert.False(literalSpan.IsMatch(source),
                $"{Path.GetFileName(file)} checks readability against a magic number");
        }
    }

    [Fact]
    public void The_withdrawn_grid_offset_cannot_come_back()
    {
        // Positioned.GridPosition read (0,0) while the world position was valid.
        // It is quarantined in the analyzer and absent from the contract, and the
        // grid is derived instead. If an export ever reinstated it, this fails
        // before anyone builds a feature on it.
        var contract = typeof(GameSession).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .ToArray();

        // The contract is const-folded away, so it is inspected on disk.
        var offsets = ContractIsolationTests.LoadContract();

        // The Positioned struct itself is legitimate — it now carries Reaction,
        // which is what tells your own companion from something hunting you.
        // What stays quarantined is the GridPosition field, and only that.
        var gridFields = offsets.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.Name.Contains("GridPosition", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(gridFields);
        Assert.DoesNotContain(contract, n => n.Contains("GameOffsets", StringComparison.OrdinalIgnoreCase));
    }

    // ---- helpers -------------------------------------------------------------

    private static void AssertNotAMemoryType(Type type, string where)
    {
        var name = type.Name;

        Assert.False(type.Namespace is "EasyExile.Core.Memory", $"{where} exposes {name}");
        Assert.False(type.Namespace?.StartsWith("Poe2Analyzer", StringComparison.Ordinal) == true,
            $"{where} exposes the offset contract type {name}");

        foreach (var forbidden in new[] { "IMemoryReader", "ProcessMemory", "GameEntity", "EntityWorld", "GameCamera" })
            Assert.False(name == forbidden, $"{where} exposes {forbidden}");
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member) => member switch
    {
        PropertyInfo p => Unwrap(p.PropertyType),
        FieldInfo f => Unwrap(f.FieldType),
        MethodInfo m => Unwrap(m.ReturnType).Concat(m.GetParameters().SelectMany(x => Unwrap(x.ParameterType))),
        ConstructorInfo c => c.GetParameters().SelectMany(x => Unwrap(x.ParameterType)),
        _ => Array.Empty<Type>(),
    };

    private static IEnumerable<Type> SignatureTypes(Type type) =>
        type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.Static | BindingFlags.DeclaredOnly)
            .SelectMany(MemberTypes);

    /// <summary>A type plus its generic arguments and element type.</summary>
    private static IEnumerable<Type> Unwrap(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } element) yield return element;

        if (!type.IsGenericType) yield break;

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var inner in Unwrap(argument)) yield return inner;
        }
    }

    /// <summary>Every type a consumer can get to by following public members.</summary>
    private static HashSet<Type> ReachableFrom(Type root)
    {
        var found = new HashSet<Type>();
        var queue = new Queue<Type>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            var type = queue.Dequeue();

            if (type.Namespace?.StartsWith("System", StringComparison.Ordinal) == true) continue;
            if (!found.Add(type)) continue;

            var members = type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            foreach (var next in members.SelectMany(MemberTypes))
            {
                if (!found.Contains(next)) queue.Enqueue(next);
            }
        }

        return found;
    }
}
