using System.Reflection;
using EasyExile.Core.Camera;
using EasyExile.Core.Contract;
using EasyExile.Core.Memory;
using EasyExile.Core.Runtime;
using EasyExile.Core.Spatial;
using EasyExile.Core.World;

namespace EasyExile.Core.Tests;

/// <summary>
/// The main project is allowed to know the analyzer produced a contract and
/// nothing else about it. If any analyzer type or the knowledge database ever
/// leaked in, the offsets would stop being a reviewed export and start being
/// whatever the analyzer happened to believe that day.
/// </summary>
public class ContractIsolationTests
{
    /// <summary>
    /// The assembly that consumes the offset contract, which is exactly one.
    /// EasyExile.Radar is deliberately not here: it cannot see the contract at
    /// all, and it carries a rendering stack that ArchitectureTests vets
    /// separately.
    /// </summary>
    private static readonly Assembly[] Consumer =
    {
        typeof(GameLayout).Assembly,
    };

    /// <summary>Finds GameOffsets.dll by walking up from the test output.</summary>
    internal static Assembly LoadContract()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "libs", "GameOffsets.dll");
            if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);

            directory = directory.Parent;
        }

        throw new FileNotFoundException("GameOffsets.dll nao encontrado a partir de " + AppContext.BaseDirectory);
    }

    private static string[] NonFrameworkReferences(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => !n.StartsWith("System.", StringComparison.Ordinal)
                     && !n.StartsWith("EasyExile.", StringComparison.Ordinal)
                     && n is not ("System" or "netstandard" or "mscorlib" or "Microsoft.CSharp"))
            .ToArray();

    [Fact]
    public void Nothing_references_the_analyzer()
    {
        foreach (var assembly in Consumer)
        {
            var referenced = assembly.GetReferencedAssemblies().Select(a => a.Name!).ToArray();

            Assert.DoesNotContain(referenced, n => n.StartsWith("Analyzer", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referenced, n => n.Contains("Sqlite", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(referenced, n => n.Contains("EntityFramework", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_consumer_carries_no_non_framework_dependency_at_all()
    {
        // The contract is nothing but compile-time constants, so the compiler
        // folds them in and no assembly reference survives. That is the strongest
        // possible form of the isolation rule: at runtime there is nothing to
        // substitute, and a swapped contract cannot silently change behaviour
        // without a rebuild.
        foreach (var assembly in Consumer)
            Assert.Empty(NonFrameworkReferences(assembly));
    }

    [Fact]
    public void A_swapped_contract_cannot_go_unnoticed()
    {
        // Because the values are baked in, the fingerprint is baked in with them.
        // A contract regenerated for another build without rebuilding the client
        // still refuses at the gate rather than reading the wrong fields.
        var onDisk = LoadContract()
            .GetType("Poe2Analyzer.OffsetContract.BuildInfo")!
            .GetField("Fingerprint", BindingFlags.Public | BindingFlags.Static)!
            .GetRawConstantValue() as string;

        Assert.Equal(onDisk, GameLayout.Build.Fingerprint);
    }

    [Fact]
    public void The_contract_carries_no_runtime_state()
    {
        foreach (var type in LoadContract().GetTypes())
        {
            // Everything exported is a compile-time constant. A settable field
            // would mean the contract holds something from a session.
            Assert.Empty(type.GetFields(BindingFlags.Public | BindingFlags.Instance));
            Assert.DoesNotContain(
                type.GetFields(BindingFlags.Public | BindingFlags.Static),
                f => !f.IsLiteral && !f.IsInitOnly);
        }
    }

    [Fact]
    public void Every_exported_offset_is_build_relative_rather_than_an_address()
    {
        var values = LoadContract().GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsLiteral && f.FieldType == typeof(int))
            .Select(f => (Name: $"{f.DeclaringType!.Name}.{f.Name}", Value: (int)f.GetRawConstantValue()!))
            .ToArray();

        Assert.NotEmpty(values);

        foreach (var (name, value) in values)
        {
            // A heap address would be far larger than any structure offset or RVA.
            Assert.True(value is >= 0 and <= 0x10000000, $"{name} = 0x{value:X} does not look build-relative");
        }
    }

    [Fact]
    public void ConsumerMustNotReferenceControlJson()
    {
        // The invariant, stated directly.
        //
        // This used to ban JSON parsing and file reading outright, as a proxy
        // for "nothing loads the contract". The proxy has now collected three
        // legitimate exceptions — curated landmark labels, the icon library, the
        // price book — and a rule that is mostly exceptions has stopped saying
        // anything. What actually matters is narrower and checkable: no shipped
        // source may name a contract artefact or the directory they live in.
        //
        // The offsets are const and compiled in. If a consumer could load them
        // instead, the offsets and the build fingerprint could come from
        // different places, which is the single failure the build gate exists to
        // prevent.
        // The LOADABLE artefacts, not the assembly name. GameLayout names the
        // contract assembly by design — that is its whole job, and a separate
        // test asserts it is the only file that does.
        string[] artefacts = { "control.json", "manifest.json", "exports/", "exports\\" };

        foreach (var project in ProductProjects)
        {
            foreach (var file in SourceFiles(project))
            {
                var source = File.ReadAllText(file);
                var name = Path.GetFileName(file);

                foreach (var artefact in artefacts)
                {
                    Assert.False(
                        source.Contains(artefact, StringComparison.OrdinalIgnoreCase),
                        $"{name} names {artefact}; the contract is compiled in, never loaded");
                }
            }
        }
    }

    [Fact]
    public void EverythingLoadedAtRuntimeComesFromAKnownPlace()
    {
        // The other half of the same invariant: files ARE read at runtime now,
        // so each reader has to be aimed somewhere harmless. Anchored on
        // AppContext.BaseDirectory or an embedded resource means a reader cannot
        // be pointed at the repository's export directory by a relative path.
        // Each reviewed reader, and the anchor that keeps it aimed somewhere
        // harmless. Two shapes qualify: it resolves its own path from the
        // executable's directory or an embedded resource, or it accepts the path
        // from its caller and never invents one.
        var readers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CuratedLandmarks.cs"] = "GetManifestResourceStream",
            ["IconLibrary.cs"] = "AppContext.BaseDirectory",
            ["SettingsStore.cs"] = "AppContext.BaseDirectory",
            ["PriceBook.cs"] = "cachePath",
            // The levelling graph and route. Both take their path from the
            // caller and never build one, which is the second shape this list
            // accepts: RadarApplication anchors them beside the executable.
            ["AreaGraph.cs"] = "_path",
            ["LevelRoute.cs"] = "path",
            // The campaign's zone order and its journal, and the mod tier
            // ceilings. All three take their path from the caller — the journal
            // writes, the other two only read — and RadarApplication anchors
            // every one of them beside the executable.
            ["CampaignGuide.cs"] = "path",
            ["CampaignJournal.cs"] = "_path",
            ["ModTierTable.cs"] = "path",
            // Which supports go in each skill. Same shape as the tier table: the
            // path comes from the caller and RadarApplication anchors it beside
            // the executable.
            ["SupportAdvice.cs"] = "path",
            // The debug capture. Writes only where the caller points it, and
            // RadarRenderLoop points it beside the executable.
            ["ScreenCapture.cs"] = "directory",
            // Opens the CLIENT executable to fingerprint it, at the path the
            // caller supplies. Never listed before because the old detection
            // only knew File.ReadAll — widening it found this immediately,
            // which is the point of widening it.
            ["BuildGate.cs"] = "executablePath",
            // A diagnostic, not a product path: it reads the Radar's OWN price
            // cache to explain why a drop did or did not get a label. It anchors
            // on the executable's directory like the rest.
            ["LootFunnelWatch.cs"] = "AppContext.BaseDirectory",
            // Also a diagnostic: it writes its own transcript beside the
            // executable so a measurement taken by one person can be read by
            // another.
            ["CameraWatch.cs"] = "AppContext.BaseDirectory",
            ["SlotWatch.cs"] = "AppContext.BaseDirectory",
            // What a running analysis needs the player to do: written by the
            // probe, read by the overlay. It builds its own path, so it is
            // anchored on the user's temp directory rather than on anything in
            // the repository - the two processes run from different output
            // folders and neither can guess a path relative to the other.
            ["ProbePrompt.cs"] = "Path.GetTempPath",
            // What the sweep saw, written down so a question about a panel does
            // not need the panel open at the instant a probe runs. Same anchor
            // as the prompt it sits beside.
            ["CaptionLog.cs"] = "Path.GetTempPath",
            // The whole UI tree on a key press. Writes only where the caller
            // points it, and the render loop points it beside the executable.
            ["UiTreeDump.cs"] = "directory",
        };

        var found = new List<string>();

        foreach (var project in ProductProjects)
        {
            foreach (var file in SourceFiles(project))
            {
                var source = File.ReadAllText(file);
                var name = Path.GetFileName(file);

                // Not just File.*. A screenshot written through ImageSharp
                // touches the disk exactly as much as File.WriteAllBytes does,
                // and the first version of this list did not see it — a guard
                // that only knows one spelling of "write" guards one spelling.
                var reads = source.Contains("File.ReadAll", StringComparison.Ordinal) ||
                            source.Contains("File.WriteAll", StringComparison.Ordinal) ||
                            // Streaming and appending touch the disk exactly as
                            // much. The list named a guide that reads by lines
                            // and a journal that appends, and the guard saw
                            // neither — same lesson as SaveAsPng, one spelling
                            // later.
                            source.Contains("File.ReadLines", StringComparison.Ordinal) ||
                            source.Contains("File.Append", StringComparison.Ordinal) ||
                            source.Contains("GetManifestResourceStream", StringComparison.Ordinal) ||
                            source.Contains("Directory.CreateDirectory", StringComparison.Ordinal) ||
                            source.Contains("SaveAsPng", StringComparison.Ordinal) ||
                            source.Contains("File.Open", StringComparison.Ordinal) ||
                            source.Contains("File.Create", StringComparison.Ordinal);

                if (!reads) continue;

                found.Add(name);

                // A reader nobody has vouched for is the thing this catches.
                Assert.True(
                    readers.ContainsKey(name),
                    $"{name} reads or writes files and is not on the list of readers that were reviewed");

                Assert.Contains(readers[name], source, StringComparison.Ordinal);
            }
        }

        // And the list must not rot into naming files that no longer read.
        foreach (var reader in readers.Keys) Assert.Contains(reader, found);
    }

    [Fact]
    public void The_consumer_has_no_runtime_offset_loader()
    {
        // A loader would show up as a mutable offset surface. Everything the
        // consumer exposes for layout is a computed property over constants.
        // GameLayout and its groups are internal, so the default binding flags
        // would return nothing and the assertions below would pass vacuously.
        var layout = typeof(GameLayout)
            .GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic);

        Assert.NotEmpty(layout);

        foreach (var group in layout)
        {
            Assert.Empty(group.GetFields(BindingFlags.Public | BindingFlags.Instance));

            Assert.DoesNotContain(
                group.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static),
                p => p.CanWrite);
        }
    }

    /// <summary>Every project that ships as part of the product.</summary>
    internal static readonly string[] ProductProjects =
    {
        Path.Combine("src", "EasyExile.Core"),
        Path.Combine("src", "EasyExile.Radar"),
        Path.Combine("tools", "EasyExile.Diagnostics"),
    };

    /// <summary>
    /// Source files of a project, found by walking up from the test output.
    /// Keyed on the path rather than the project name so a project that moves
    /// between src/ and tools/ fails loudly instead of silently scanning nothing.
    /// </summary>
    internal static IEnumerable<string> SourceFiles(string projectPath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, projectPath);

            if (Directory.Exists(candidate))
                return Directory.EnumerateFiles(candidate, "*.cs", SearchOption.AllDirectories)
                    .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                             && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"{projectPath} nao encontrado a partir de {AppContext.BaseDirectory}");
    }

    [Fact]
    public void The_layout_wrapper_is_the_only_place_that_names_the_contract()
    {
        var offenders = Consumer
            .SelectMany(a => a.GetTypes())
            .Where(t => t != typeof(GameLayout) && !t.Name.StartsWith("<", StringComparison.Ordinal))
            .SelectMany(t => t.GetNestedTypes().Append(t))
            .Where(t => t.Namespace?.StartsWith("Poe2Analyzer", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(offenders);
    }
}
