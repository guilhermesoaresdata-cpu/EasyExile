# Offset contract workflow

## The contract is compiled, not loaded

Everything in `GameOffsets.dll` is declared as `const`. The C# compiler embeds each constant value into its caller. The consequence is verified by `ContractIsolationTests.The_consumer_carries_no_non_framework_dependency_at_all`:

```text
EasyExile.Core.dll references: System.Runtime, System.Collections, ...
                               and no GameOffsets assembly
```

The contract does not exist as a runtime dependency. There is no assembly to swap, no `AssemblyLoadContext`, and no hot reload. This is intentional.

## Required update cycle

```text
1. The separate Analyzer proves an offset against the live client.
       --mapnode-probe / --scenario <block> / --smoke

2. The Analyzer exports the reviewed contract.
       --export-offsets --validate-live
       -> src/OffsetContract/GeneratedOffsets.cs
       -> GameOffsets.dll
       -> exports/<fingerprint>/GameOffsets.manifest.json

3. Copy the DLL to this repository:
       libs/GameOffsets.dll

4. Rebuild EasyExile:
       dotnet build EasyExile.slnx

5. The compiler embeds the new constants into EasyExile.Core.

6. The build gate validates the client against the embedded fingerprint.
```

Step 4 is mandatory. Replacing `libs/GameOffsets.dll` without rebuilding does not change consumer behavior because the old values remain embedded in the existing binary.

## Why the compiled boundary fails safely

The build fingerprint is also a constant. It is embedded with the offsets during the same compilation from the same export.

| Situation | Result |
| --- | --- |
| New contract and rebuilt consumer | New offsets and new fingerprint remain consistent. |
| New contract without rebuilding | The existing consumer still contains its old offsets and matching old fingerprint. |
| Updated game client | Fingerprint mismatch causes `BuildMismatchException`; memory-backed operation is refused. |

The dangerous state—offsets from one build used against another build—is prevented because offsets and fingerprint form one atomic compiled pair.

## Do not add hot swapping

Loading offsets at runtime from JSON, `Assembly.LoadFrom`, or another dynamic source would break this atomic relationship. Offsets and fingerprint could then come from different exports, recreating the exact failure mode the gate prevents.

## Where the contract is named

Only [GameLayout.cs](src/EasyExile.Core/Contract/GameLayout.cs) names the Analyzer contract namespace. No other consumer file may reference `Poe2Analyzer.OffsetContract`; an architecture test enforces this rule.
