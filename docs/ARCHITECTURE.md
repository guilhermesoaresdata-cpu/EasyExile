# Architecture

## Dependency direction

EasyExile has one production dependency direction:

```text
EasyExile.Radar -> EasyExile.Core -> GameOffsets.dll
```

The diagnostics executable also references Core, but it is not part of the overlay runtime. Tests reference Core and Radar only to validate behavior and assembly boundaries.

## Core responsibilities

`EasyExile.Core` owns everything that can access raw game state:

- `Memory/` centralizes process reads behind `IMemoryReader`.
- `Runtime/BuildGate` validates the current executable against the contract fingerprint.
- `Runtime/GameSession` owns attachment, caches, area epochs, and capture lifetime.
- `World/` contains bounded readers for entities, components, terrain, UI, loot, and item slots.
- `Camera/` resolves projection data.
- `Snapshots/` defines immutable values passed to consumers.
- `Navigation/` performs pathfinding over captured terrain and never controls the player.
- `Diagnostics/` contains reusable inspection primitives, not rendering features.

## Snapshot boundary

The overlay receives immutable snapshot records rather than a process handle or memory reader. This ensures that:

- rendering cannot issue arbitrary reads;
- one frame observes a coherent capture;
- feature tests can use ordinary values without a running game;
- stale or missing data is represented explicitly;
- expensive capture work is shared among features.

`WorldSnapshot` is the primary frame-level domain object. It includes player, entity, camera, map, terrain, landmark, loot-label, and item-slot data. Convenience properties normalize default immutable arrays to empty collections.

## Build compatibility

Memory layouts are not portable across arbitrary game builds. `BuildGate` compares the live executable identity with the fingerprint embedded in the contract. `GameSession` must not continue with raw reads after a mismatch.

The contract is intentionally a binary reference rather than a project dependency. EasyExile contains no scanners, offset recovery, or discovery recipes.

## Rendering

`EasyExile.Radar` owns the transparent Win32 overlay and rendering loop. `IOverlayCanvas` provides the drawing abstraction used by features. A `RenderFrame` combines the latest snapshot with presentation state, allowing features to remain independent of process memory.

Features implement `IRadarFeature` and should:

- consume only snapshot/configuration data;
- degrade safely when optional capture data is unavailable;
- avoid blocking the render loop;
- keep persistent state bounded and scoped to the current area epoch;
- place shared drawing behavior in rendering utilities rather than duplicating backend calls.

The concrete backend and package rationale are documented in [Overlay backend](../src/EasyExile.Radar/Overlay/BACKEND.md).

## Input boundary

Navigation, radar, and diagnostics are observational. AutoPotion is the explicit exception: when enabled, it may call Win32 `SendInput` to press configured flask keys. It ships disabled and applies foreground, in-area, plausible-vitals, cooldown, dry-run, and kill-switch gates. New input-producing features must never be added implicitly or disguised as rendering behavior.

## Navigation

Navigation operates on a terrain snapshot. `TerrainCellReader` adapts captured cells to `ICellReader`; `AStar` searches the grid; `PathPlanner` coordinates route creation; and `PathSmoother` removes unnecessary intermediate cells. The result is visual guidance only.

## Failure behavior

The preferred response to uncertainty is absence:

- fingerprint mismatch: stop memory-backed operation;
- unreadable pointer or invalid container: omit the affected data;
- missing camera: retain world data but disable projection;
- missing terrain/map frame: disable native-map rendering or routing;
- area transition: advance the epoch and invalidate area-scoped caches.
