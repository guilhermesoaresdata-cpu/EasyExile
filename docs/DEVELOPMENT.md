# Development guide

## Build verification

Run this before committing:

```powershell
dotnet restore EasyExile.slnx
dotnet build EasyExile.slnx -c Release --no-restore
dotnet test EasyExile.slnx -c Release --no-build
dotnet list EasyExile.slnx package --vulnerable --include-transitive
```

## Architectural rules

- Only `EasyExile.Core` may reference `GameOffsets.dll`.
- Raw readers and memory interfaces remain internal to Core.
- Radar features consume snapshots; they do not read process memory.
- The runtime must fail closed when the contract fingerprint mismatches.
- Navigation and diagnostics never generate input.
- New collections and tree walkers require explicit size/depth bounds.

Architecture tests inspect real assemblies and should be updated only when a boundary change is deliberate.

## Testing

Use `FakeMemory` for deterministic reader and layout tests. Cover success, unreadable memory, invalid pointers, unreasonable counts, area changes, default immutable arrays, and partial-data behavior. A live test is supporting evidence, not a replacement for a deterministic unit test.

## Adding a feature

1. Define the minimum snapshot data the feature needs.
2. Add or extend a bounded Core reader and snapshot capture only if that data is not already available.
3. Add fake-memory tests for the reader and ordinary unit tests for feature decisions.
4. Implement the radar feature over snapshots and configuration.
5. Verify behavior when every optional input is absent.
6. Document user-facing settings and update `docs/FEATURES.md`.

## Contract updates

Do not edit offsets inside EasyExile. Generate and validate a new contract in the separate Analyzer repository, verify its fingerprint, copy the reviewed DLL to `libs/GameOffsets.dll`, and run all tests. See `CONTRACT_WORKFLOW.md`.

## Language and privacy

Write new identifiers, comments, messages, documentation, commits, and pull requests in English. Do not commit process dumps, log files, local price data, screenshots containing account details, credentials, or locally cloned research repositories.
