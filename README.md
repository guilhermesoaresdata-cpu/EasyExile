<div align="center">

# EasyExile

### See more. Navigate smarter. Stay focused on the fight.

**A complete Path of Exile 2 companion that brings your map, enemies, loot, progression, and essential combat information together in one clean overlay.**

![Platform](https://img.shields.io/badge/platform-Windows-0078D4?style=for-the-badge)
![Game](https://img.shields.io/badge/game-Path%20of%20Exile%202-C88A3D?style=for-the-badge)
![Status](https://img.shields.io/badge/status-Private%20Development-6C5CE7?style=for-the-badge)

</div>

---

## Your adventure, made clearer

Path of Exile 2 is full of information: unexplored paths, dangerous enemies, hidden objectives, valuable drops, complex items, and long campaign routes.

EasyExile brings the most useful information into a single companion experience. It helps you understand what is happening around your character without constantly switching screens, memorizing routes, or stopping to inspect every detail.

Whether you are progressing through the campaign, exploring a large area, hunting important enemies, or deciding which item deserves your attention, EasyExile keeps the information visible and organized.

## What EasyExile does

### 🗺️ Advanced map and exploration

EasyExile transforms the game world into a clear, informative map built for exploration.

- Displays walkable terrain and explored areas
- Shows your current position and movement
- Marks enemies, NPCs, chests, exits, transitions, and important objects
- Keeps useful landmarks visible as you explore
- Supports the game's map view with accurate positioning and alignment
- Uses configurable icons, labels, colors, and visibility rules

### 🧭 Intelligent navigation

Choose where you want to go and let EasyExile turn the terrain into a practical route.

- Calculates paths through available terrain
- Updates the route when your position or destination changes
- Removes unnecessary turns for cleaner guidance
- Tracks your progress along the selected route
- Provides visual direction without controlling character movement

### 📖 Campaign guidance

Progress through the campaign with less uncertainty.

- Recognizes the current area
- Tracks campaign progress
- Suggests the next relevant objective
- Shows the current and upcoming steps
- Connects areas through a structured campaign route
- Helps reduce backtracking and missed objectives

### 👹 Enemy awareness

Understand nearby threats before they disappear into visual clutter.

- Identifies monsters and other important entities
- Distinguishes entity types and monster rarity
- Displays monster health information
- Highlights dangerous or relevant targets
- Keeps labels and markers stable while entities move

### 💎 Loot intelligence

Spend less time checking everything and more time collecting what matters.

- Reads visible ground-item labels
- Displays known item values
- Shows price information for inspected items
- Highlights relevant inventory slots
- Identifies modifier tiers
- Provides curated support-gem suggestions

### ❤️ Player and combat information

Keep essential character information easy to understand during combat.

- Tracks life, mana, and energy shield
- Presents player position and status
- Connects combat data with map and enemy information
- Safely hides information when the current data cannot be validated

### 🧪 AutoPotion

EasyExile includes an optional automatic flask assistant.

- Supports life, mana, and energy-shield conditions
- Allows custom thresholds, keys, and cooldowns
- Works only while Path of Exile 2 is the active window
- Pauses outside playable areas or when character data is invalid
- Includes a dry-run mode for testing without pressing keys
- Includes an F8 emergency switch
- Is disabled by default

> **Important:** AutoPotion sends configured keyboard input when enabled. Automation may conflict with game rules or account policies. Review the current Path of Exile terms before using this feature.

## Designed to stay out of your way

EasyExile is built around a simple idea: useful information should be available when you need it and invisible when you do not.

- **Clean presentation** — information is separated into focused visual layers.
- **Customizable experience** — map, loot, player, health-bar, campaign, and debug options can be configured independently.
- **Responsive updates** — game information and visual rendering run independently for a smoother experience.
- **Area-aware behavior** — exploration, entities, routes, and temporary data are refreshed correctly when changing zones.
- **Graceful failure** — unavailable information disables only the affected feature instead of displaying unreliable results.

## Built with safety in mind

EasyExile reads selected information from the game process but does not inject code into it and does not modify game memory.

The application validates the running game version before using its memory layout. If the version is not compatible, memory-based functionality is stopped instead of attempting to use outdated information.

AutoPotion is the only feature capable of sending keyboard input. It is isolated from the rest of the application, disabled by default, and protected by multiple checks.

## Feature overview

| Category | Included capabilities |
| --- | --- |
| Map | Terrain, exploration, icons, labels, landmarks, map alignment |
| Navigation | Route calculation, smoothing, destination tracking, automatic replanning |
| Campaign | Area recognition, objective guidance, progression history, next-step panel |
| Enemies | Classification, rarity, health bars, threat visibility |
| Loot | Ground labels, price information, slot highlighting, modifier tiers |
| Build support | Skill support suggestions and item information |
| Player | Position, life, mana, energy shield, combat status |
| Customization | Individual settings for every major feature group |
| Diagnostics | Specialized tools for validating supported game information |
| Optional automation | Configurable AutoPotion with safety gates and kill switch |

## Who EasyExile is for

EasyExile is designed for players who want:

- clearer exploration;
- faster campaign progression;
- better awareness during combat;
- less time spent evaluating low-value loot;
- useful information without an overloaded interface;
- one companion instead of several disconnected tools.

## Current development status

EasyExile is in private active development. Its main systems are implemented and covered by an extensive automated test suite, but Path of Exile 2 updates may require compatibility updates before live information becomes available again.

The priority is always to show trustworthy information. When EasyExile cannot validate something, it prefers to show nothing rather than show a convincing but incorrect result.

---

## Technical information

This section is intended for developers and maintainers. Players do not need to understand it to know what EasyExile offers.

EasyExile is divided into three main projects:

- **EasyExile.Core** reads and validates game information, manages sessions, and creates safe snapshots.
- **EasyExile.Radar** turns those snapshots into the map, navigation, loot, campaign, combat, and settings experience.
- **EasyExile.Diagnostics** contains focused tools used to verify compatibility and investigate game structures.

The Radar never receives memory addresses or process readers. It receives completed, immutable snapshots containing only the information a feature is allowed to use.

For maintainers:

- [Complete module reference](docs/MODULES.md)
- [Architecture and data flow](docs/ARCHITECTURE.md)
- [Feature implementation guide](docs/FEATURES.md)
- [Development and testing rules](docs/DEVELOPMENT.md)
- [Offset contract workflow](CONTRACT_WORKFLOW.md)
- [Overlay backend](src/EasyExile.Radar/Overlay/BACKEND.md)

---

<div align="center">

**EasyExile — more clarity between you and the next objective.**

Private project. Not affiliated with Grinding Gear Games.

</div>
