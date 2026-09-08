# EasyExile

EasyExile is a modular Path of Exile 2 companion written in C# for Windows. It reads selected game state, converts raw memory into immutable domain snapshots, and presents map, navigation, entity, loot, progression, and combat information through an external overlay.

This is a private experimental project and is not affiliated with or endorsed by Grinding Gear Games.

## Project purpose

The project separates memory interpretation from presentation. Low-level code is restricted to `EasyExile.Core`; visual features receive immutable snapshots and do not know addresses, offsets, process handles, or native memory layouts.

```text
Path of Exile 2 process
          |
          | bounded reads
          v
 Memory + Contract + World readers
          |
          v
      GameSession
          |
          v
  Immutable snapshots
          |
          v
 Radar features and UI
```

## Main modules

### EasyExile.Core

The data and safety boundary of the application.

- **Memory** — owns read-only Windows process access, typed reads, native strings, and read accounting.
- **Contract** — provides the single named boundary to the build-specific `GameOffsets.dll` contract.
- **Runtime** — validates the client build, manages the game session, area epochs, caches, and snapshot lifecycle.
- **World** — interprets entities, components, terrain, map UI, loot labels, item slots, and landmarks.
- **Camera** — reads the game camera and prepares view-projection data.
- **Snapshots** — defines the immutable data transferred from Core to all consumers.
- **Spatial** — contains vector types and world-to-screen projection logic.
- **Navigation** — provides terrain-cell access, A* search, route planning, and path smoothing.
- **Diagnostics** — defines bounded UI inspection, captions, dump options, and guided probe primitives.

### EasyExile.Radar

The presentation and feature layer.

- **Runtime** — coordinates snapshot updates separately from rendering and constructs each render frame.
- **Overlay** — manages the external transparent window, game-window tracking, screen capture, and native window integration.
- **Rendering** — provides the canvas abstraction, ImGui implementation, icons, SVG paths, frame data, and visual caches.
- **Native Map** — draws terrain, exploration, entities, labels, landmarks, and icons using map-specific projection and display rules.
- **Navigation** — manages destinations, background replanning, route progress, and visual route guidance.
- **Levelling** — models the campaign graph, current journal state, objectives, step selection, and progression panels.
- **Loot** — presents ground values, hovered-item prices, mod tiers, support suggestions, and item-slot highlighting.
- **HP Bars** — renders monster health and threat information from entity snapshots.
- **World** — renders player/world diagnostics independently of the native map.
- **AutoPotion** — optionally sends configured flask key presses after explicit enablement and multiple safety gates.
- **Settings and UI** — stores feature configuration and exposes the settings window.
- **Pricing** — resolves cached prices, mod tiers, and support recommendations.
- **Input** — contains the isolated Win32 keyboard-input implementation used only by AutoPotion.

### EasyExile.Diagnostics

A separate live-research executable. It contains focused tools for inspecting the game-state chain, areas, transitions, camera, UI panels, text, tooltips, map alignment, item slots, and loot labels. Diagnostics are not part of the normal rendering pipeline.

### EasyExile.Core.Tests

The deterministic verification project. It uses fake memory and ordinary snapshot fixtures to test native reads, build mismatch behavior, area transitions, snapshot rules, navigation, map rendering contracts, loot, campaign guidance, AutoPotion gates, and architecture boundaries.

## Feature groups

| Group | Responsibility |
| --- | --- |
| Process safety | Read-only access, bounded reads, client fingerprint validation, and fail-closed behavior |
| Entity model | Classification and snapshots for players, monsters, NPCs, chests, transitions, items, and objects |
| Native map | Terrain texture, exploration history, map projection, icons, labels, and display filtering |
| Navigation | A* pathfinding, route smoothing, destinations, replanning, and progress tracking |
| Campaign | Area graph, route definitions, progression journal, objective selection, and step presentation |
| Loot | Ground labels, item slots, price lookup, mod tiers, support advice, and highlighting |
| Combat display | Player state, monster health bars, rarity, relation, and threat presentation |
| Diagnostics | Live structural inspection and evidence gathering for supported client builds |
| AutoPotion | Optional flask-key automation, disabled by default |

## Important boundaries

- `EasyExile.Core` is the only project allowed to reference `GameOffsets.dll`.
- Radar features consume snapshots and cannot access memory readers.
- Memory layouts are accepted only for the client fingerprint compiled with the contract.
- Invalid or unavailable data disables the affected behavior instead of being guessed.
- Area-scoped caches are invalidated when the area epoch changes.
- Native containers, trees, and collections must always have explicit traversal limits.
- Navigation provides visual guidance and never controls movement.

## AutoPotion warning

AutoPotion is the only module that generates input. It uses Windows `SendInput` for configured flask keys when explicitly enabled. It ships disabled and checks the foreground window, in-area state, plausible vitals, thresholds, cooldowns, dry-run mode, and the F8 kill switch.

Automation may violate game rules or account policies. Anyone with access to this private repository is responsible for reviewing the current Path of Exile terms before enabling it.

## Offset contract

`libs/GameOffsets.dll` is produced and validated by a separate Analyzer project. Its constants and build fingerprint are compiled into `EasyExile.Core`, making them one atomic versioned unit. Replacing the DLL alone does not update an already compiled consumer.

The complete update design is documented in [Offset contract workflow](CONTRACT_WORKFLOW.md).

## Detailed documentation

- [Complete module reference](docs/MODULES.md)
- [Architecture and data flow](docs/ARCHITECTURE.md)
- [Feature behavior](docs/FEATURES.md)
- [Development and testing rules](docs/DEVELOPMENT.md)
- [Overlay backend](src/EasyExile.Radar/Overlay/BACKEND.md)

## Repository structure

| Path | Contents |
| --- | --- |
| `src/EasyExile.Core` | Memory boundary, game-domain readers, snapshots, and navigation algorithms |
| `src/EasyExile.Radar` | Overlay, rendering, settings, and feature implementations |
| `tools/EasyExile.Diagnostics` | Live inspection and validation tools |
| `tools/support-advice` | Generator and curated support recommendation data |
| `tools/devtree` | Utility for comparing diagnostic tree captures |
| `tests/EasyExile.Core.Tests` | Deterministic unit and architecture tests |
| `libs/GameOffsets.dll` | Build-specific offset contract |
| `docs` | Architecture, modules, features, and development documentation |

## Repository status

The codebase is experimental and tied to specific Path of Exile 2 builds. Passing deterministic tests proves internal behavior; it does not prove that a historical memory layout remains valid after a client update.
