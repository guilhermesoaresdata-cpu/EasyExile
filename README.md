<div align="center">

# EasyExile

### A focused Path of Exile 2 utility for mapping, navigation, progression, loot, and combat awareness.

![Windows](https://img.shields.io/badge/Windows-0078D4?style=flat-square&logo=windows&logoColor=white)
![Path of Exile 2](https://img.shields.io/badge/Path%20of%20Exile%202-C88A3D?style=flat-square)
![Private Development](https://img.shields.io/badge/Private%20Development-6C5CE7?style=flat-square)

</div>

EasyExile combines the information normally spread across the map, UI, item inspection, and progression tools into a configurable external overlay. It is designed to reduce friction without replacing normal gameplay.

## Features

### Map and exploration

- Native terrain map
- Walkable-area visualization
- Persistent exploration state
- Player and entity tracking
- Transitions, landmarks, chests, NPCs, and relevant world objects
- Configurable icons, labels, colors, and display rules
- Game-map alignment

### Navigation

- Terrain-aware route calculation
- Automatic route replanning
- Path smoothing
- Destination and progress tracking
- World and map route display

Navigation is visual only and does not control character movement.

### Campaign

- Current-area recognition
- Campaign route and area graph
- Progress journal
- Current and next objective
- Step and route guidance

### Entities and combat

- Entity classification
- Monster rarity and relation
- Monster health bars
- Threat-focused display
- Player life, mana, energy shield, and position
- Stable markers for moving entities

### Loot and items

- Ground-item labels
- Item value information
- Hovered-item pricing
- Inventory-slot highlighting
- Modifier tier display
- Curated support-gem recommendations

### AutoPotion

- Life, mana, and energy-shield conditions
- Configurable thresholds, keys, and cooldowns
- Foreground-window and valid-area checks
- Invalid-vitals protection
- Dry-run mode
- F8 kill switch
- Disabled by default

> AutoPotion is the only feature that sends keyboard input. Automation may conflict with current game rules or account policies; use it only after reviewing those rules.

## Key characteristics

- **External overlay** — no process injection or renderer hooking.
- **Read-only game data** — EasyExile does not write to game memory.
- **Build validation** — incompatible game builds are rejected instead of using stale layouts.
- **Fail-closed behavior** — invalid data disables the affected feature rather than producing guessed results.
- **Modular configuration** — map, navigation, campaign, loot, health bars, player display, and AutoPotion can be configured independently.
- **Area-aware state** — routes, entities, exploration, and temporary caches are refreshed on area changes.
- **Independent update and render loops** — data capture does not block presentation.

## Feature status

| Module | Capabilities |
| --- | --- |
| Native Map | Terrain, exploration, entities, icons, labels, landmarks, map alignment |
| Navigation | A* routing, smoothing, targets, route tracking, background replanning |
| Campaign | Area graph, journal, objectives, step panel, route guidance |
| Combat | Player vitals, entity rarity, health bars, threat information |
| Loot | Ground labels, pricing, item slots, mod tiers, support advice |
| AutoPotion | Optional flask input with thresholds, cooldowns, dry run, and kill switch |
| Diagnostics | Chain, area, transition, camera, UI, tooltip, map, slot, and loot inspection |

## Compatibility

EasyExile depends on a build-specific memory-layout contract. A Path of Exile 2 update can temporarily disable live features until a matching validated contract is available.

The application deliberately refuses incompatible layouts. Test coverage verifies internal behavior but does not make an old contract compatible with a new game build.

## Project status

EasyExile is under private active development. Features and layouts may change as the game client evolves.

## Technical documentation

Developer documentation is kept separate from the product overview:

- [Module reference](docs/MODULES.md)
- [Architecture and data flow](docs/ARCHITECTURE.md)
- [Feature implementation](docs/FEATURES.md)
- [Development and testing](docs/DEVELOPMENT.md)
- [Offset contract workflow](CONTRACT_WORKFLOW.md)
- [Overlay backend](src/EasyExile.Radar/Overlay/BACKEND.md)

---

<div align="center">

Private project. Not affiliated with or endorsed by Grinding Gear Games.

</div>
