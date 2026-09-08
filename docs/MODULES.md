# Complete module reference

This document explains the responsibility and collaboration boundary of every EasyExile module.

## EasyExile.Core

### Memory

The Memory module is the only low-level process-access implementation.

| Type | Responsibility |
| --- | --- |
| `IMemoryReader` | Small internal contract for safe typed reads, pointers, byte ranges, and readability checks. It enables fake-memory tests without a live process. |
| `ProcessMemory` | Opens and owns the Windows process handle, maps readable regions, resolves the main module, and performs bounded reads. |
| `NativeText` | Decodes native UTF-8/UTF-16 and game string representations with maximum-length protection. |
| `ReadCounter` | Counts memory operations so capture cost and unexpected read growth can be observed. |

### Contract

| Type | Responsibility |
| --- | --- |
| `GameLayout` | The only EasyExile source file allowed to name the external offset-contract namespace. It exposes contract values to internal readers through EasyExile-owned names. |

The isolation prevents generated contract types from spreading across the domain and renderer.

### Runtime

| Type | Responsibility |
| --- | --- |
| `BuildGate` | Computes the live client identity and compares it with the embedded contract fingerprint. A mismatch produces a refusal result. |
| `GameSession` | Owns attachment, contract validation, current area identity, epoch changes, capture caches, and the lifetime of snapshot production. |
| `CoreText` | Centralizes Core-facing status and diagnostic text. |

### Camera

| Type | Responsibility |
| --- | --- |
| `GameCamera` | Reads camera state and produces a validated `CameraSnapshot` containing projection data and viewport dimensions. |

### World

| Type | Responsibility |
| --- | --- |
| `ComponentLayout` | Defines the component locations and structural relationships needed by entity readers. |
| `GameEntity` | Interprets one raw entity and extracts identity, metadata, components, position, vitals, rarity, and relation where available. |
| `EntityWorld` | Enumerates bounded awake/sleeping entity containers and turns valid entries into entity snapshots. |
| `EntityClassifier` | Maps metadata and component evidence to semantic categories such as monster, NPC, chest, transition, or item. |
| `EntityTypeCache` | Caches stable classification information while keeping it scoped to valid entity/area lifetimes. |
| `Vital` | Reads and validates health, mana, and energy-shield values. |
| `TerrainReader` | Reads walkability and dimensions and creates an immutable terrain snapshot. |
| `MapUiReader` | Reads the state and geometry of the game's map interface for alignment and projection. |
| `LootLabelReader` | Captures visible ground-item labels and their screen rectangles. |
| `ItemSlotReader` | Captures visible inventory/item-slot identity and geometry. |
| `LandmarkReader` | Resolves terrain or world landmarks used by map and navigation features. |
| `CuratedLandmarks` | Loads the embedded curated landmark catalog from `CustomLandmarks.json`. |

### Snapshots

Snapshots are immutable values and form the public data language between Core and presentation.

| Snapshot/type | Meaning |
| --- | --- |
| `WorldSnapshot` | Complete coherent capture: player, entities, camera, terrain, map, landmarks, loot labels, item slots, league, and area identity. |
| `MapFrameSnapshot` | Map-focused view used by native-map, navigation, vitals, and AutoPotion decisions. |
| `PlayerSnapshot` | Player identity and position information. |
| `EntitySnapshot` | Stable presentation data for one classified world entity. |
| `VitalsSnapshot` | Validated current/maximum life, mana, and energy shield plus percentage helpers. |
| `CameraSnapshot` | View-projection matrix and viewport dimensions. |
| `TerrainSnapshot` / `MapSnapshot` | Walkable grid and game-map state required for map rendering and routing. |
| `LandmarkSnapshot` | A named point of interest with world/grid identity. |
| `LootLabelSnapshot` | Ground-label text and screen rectangle. |
| `ItemSnapshot` / `ItemSlotSnapshot` | Item identity, attributes, and visible slot placement. |
| `UiSnapshot`, `TextLabelSnapshot`, `TooltipSnapshot` | Bounded UI, text, and tooltip observations. |
| `AreaId`, `EntityId`, `AreaEpoch` | Strong identities that prevent data from different areas or entity lifetimes from being mixed. |
| `CaptureCaches` | Area/session-scoped caches shared during snapshot construction. |
| `CaptureStatus`, `CaptureResult`, `CaptureOptions` | Explicit capture outcome and limits. |
| `EntityKind`, `MonsterRarity`, `LabelTargets` | Domain classifications used by feature rules. |

### Spatial

| Type | Responsibility |
| --- | --- |
| `VectorTypes` | Lightweight 2D/3D coordinate values used across snapshots and algorithms. |
| `Projection` | Applies validated view-projection data and viewport dimensions to convert world positions into screen positions. |

### Navigation

| Type | Responsibility |
| --- | --- |
| `ICellReader` | Grid-access abstraction consumed by pathfinding. |
| `TerrainCellReader` | Adapts an immutable terrain snapshot to `ICellReader`. |
| `AStar` | Finds a lowest-cost route through walkable cells using bounded A* search. |
| `PathPlanner` | Coordinates path requests, grid/world conversion, and route construction. |
| `PathSmoother` | Removes redundant waypoints while preserving traversability. |

Navigation returns data for visual guidance only.

### Diagnostics

| Type | Responsibility |
| --- | --- |
| `ElementProbe` | Represents a bounded UI element inspection request/result. |
| `ProbePrompt` | Describes operator actions required during a diagnostic sample. |
| `UiTreeDump` | Walks and serializes UI trees with depth/node bounds. |
| `DumpOptions` / `DumpScope` | Controls diagnostic scope and safety limits. |
| `CaptionLog` | Records observed captions/text for comparison without coupling diagnostics to the radar. |

## EasyExile.Radar

### Runtime

| Type | Responsibility |
| --- | --- |
| `RadarApplication` | Top-level composition of session, overlay, settings, update loop, and render loop. |
| `RadarUpdateLoop` | Captures fresh snapshots independently of drawing and publishes the latest coherent state. |
| `RadarRenderLoop` | Builds render frames, evaluates enabled features, coordinates AutoPotion, and draws UI/overlay content. |
| `ISnapshotSource` | Decouples rendering from the concrete session and enables fixture-driven tests. |
| `RadarText` | Centralizes user-facing radar labels and state messages. |

### Overlay

| Type | Responsibility |
| --- | --- |
| `OverlayHost` | Owns the overlay lifecycle and integrates the rendering backend. |
| `OverlayWindow` | Configures the external transparent window and its interaction behavior. |
| `GameWindowTracker` | Tracks the game window position, size, focus, and visibility. |
| `ScreenCapture` | Captures bounded screen regions needed by visual alignment/inspection features. |
| `Native` | Contains the minimal Win32 declarations used by the overlay layer. |

### Rendering

| Type | Responsibility |
| --- | --- |
| `IOverlayCanvas` | Backend-independent primitives for text, lines, shapes, images, and clipping. |
| `ImGuiCanvas` | ImGui implementation of the canvas contract. |
| `RenderFrame` | Carries one snapshot plus current rendering context to every feature. |
| `Palette` | Shared semantic colors. |
| `IconLibrary` / `IconCache` | Defines and caches reusable vector icons. |
| `SvgPath` | Parses the supported SVG path subset used by icons. |
| `EntityVisualCache<T>` | Bounded cache for stable per-entity visual state. |
| `RadarStats` | Measures frame/capture behavior for diagnostics and performance display. |

### Feature contract

| Type | Responsibility |
| --- | --- |
| `IRadarFeature` | Common feature lifecycle and drawing contract over `RenderFrame`. |
| `ProbePromptFeature` | Presents active guided-probe instructions in the overlay. |

### Native Map

| Type | Responsibility |
| --- | --- |
| `NativeMapRadarFeature` | Main coordinator for terrain, exploration, route, entity, icon, and label layers. |
| `NativeMapProjection` | Converts terrain/grid/world values to native-map coordinates. |
| `TerrainTexture` | Converts walkability data into a reusable map texture. |
| `TerrainPaint` | Defines visited/unvisited terrain presentation. |
| `ExplorationMap` | Stores area-scoped exploration state. |
| `ExplorationOverlay` | Draws explored and unexplored regions. |
| `MapIcons` | Selects semantic icons for map objects. |
| `EntityLabels` | Produces labels for classified entities. |
| `EntityMotion` | Stabilizes or predicts short-lived entity movement for presentation. |
| `DisplayRule` / `DisplayRules` | Controls which objects appear and how they are styled. |

### Navigation features

| Type | Responsibility |
| --- | --- |
| `NavTarget` | Represents the current navigation destination and its identity. |
| `Navigator` | Coordinates destination selection and route state. |
| `BackgroundReplanner` | Recalculates routes away from the render thread when meaningful inputs change. |
| `RouteTracker` | Tracks progress and determines when route segments are reached or stale. |
| `WorldRouteFeature` | Renders the planned route as world/map guidance. |

### Levelling features

| Type | Responsibility |
| --- | --- |
| `AreaGraph` | Represents campaign areas and their connections. |
| `LevelRoute` | Loads and exposes ordered campaign route data. |
| `CampaignJournal` | Stores observed progression and completed steps. |
| `CampaignGuide` | Chooses relevant next objectives from area and journal state. |
| `StepAim` | Describes the target or intent of one campaign step. |
| `StepPanel` | Builds the presentable current/next-step view. |
| `RouteGuideFeature` | Draws campaign guidance from the selected step and available world data. |

### Loot features

| Type | Responsibility |
| --- | --- |
| `LootValuesFeature` | Adds value information to captured ground loot labels. |
| `HoverPriceFeature` | Presents the known value of the currently inspected item. |
| `ModTierFeature` | Converts item modifier data into tier annotations. |
| `SkillSupportFeature` | Presents curated support recommendations for a skill. |
| `SlotHighlightFeature` | Highlights relevant visible item slots based on feature rules. |

### Combat and world features

| Type | Responsibility |
| --- | --- |
| `MonsterHpBars` | Draws validated monster health/threat information. |
| `PlayerWorldFeature` | Presents player-centered world information. |
| `DebugWorldFeature` | Displays diagnostic entity/world details for development. |

### AutoPotion and input

| Type | Responsibility |
| --- | --- |
| `AutoPotion` | Evaluates life/mana thresholds, cooldowns, focus, valid-frame state, dry-run mode, and the kill switch before requesting a flask press. |
| `AutoPotionSettings` | Stores explicit enablement, thresholds, modes, cooldowns, keys, and the transient dry-run flag. |
| `SendInputNative` | Isolated Win32 `SendInput` adapter that emits one scancode press/release pair. No other feature should call it. |

### Pricing

| Type | Responsibility |
| --- | --- |
| `PriceBook` | Loads local price data and resolves normalized item names to values. |
| `ModTierTable` | Loads modifier-family tier ceilings and derives display tiers. |
| `SupportAdvice` | Loads curated, ordered support recommendations. |

### Settings and UI

| Type | Responsibility |
| --- | --- |
| `RadarSettings` | Root configuration object aggregating all feature settings. |
| `SettingsStore` | Loads, validates, and persists user configuration. |
| `TransientAttribute` | Marks runtime-only settings that must not be persisted. |
| `GeneralSettings` | Global presentation and behavior preferences. |
| `PlayerSettings` | Player marker/display configuration. |
| `NativeMapSettings` | Terrain, exploration, icon, label, and native-map presentation options. |
| `LootSettings`, `ChipCorner`, `SkillSupportAnchor` | Loot visibility and placement configuration. |
| `HpBarSettings` | Monster health-bar and threat display configuration. |
| `LevellingSettings` | Campaign guidance preferences. |
| `DebugEntitySettings` | Development-only entity visibility choices. |
| `SettingsWindow` | ImGui settings interface that edits the configuration groups. |

## EasyExile.Diagnostics

| Tool | Responsibility |
| --- | --- |
| `ChainProbe` | Validates the root-to-area memory chain. |
| `AreaIdentityWatch` | Observes stable area identity values. |
| `AreaTransitionWatch` | Compares state across area transitions. |
| `TransitionNameProbe` | Inspects transition labels and identity. |
| `CameraWatch` | Samples camera and projection behavior. |
| `MapUiWatch` / `MapAlignWatch` | Inspects game-map state and overlay alignment. |
| `PanelWatch` | Observes UI panel activation and structure. |
| `TextHunt` / `TextLineProbe` | Searches bounded UI memory for text structures. |
| `TooltipTree` / `TooltipShape` | Inspects tooltip hierarchy and geometry. |
| `HoverProbe` | Samples state changes caused by hovering UI objects. |
| `SlotWatch` | Observes inventory/item-slot structures. |
| `LootFunnelWatch` | Traces the path from UI/world state to loot-label snapshots. |
| `UiBench` | Measures or validates UI traversal behavior. |
| `Program` | Parses diagnostic commands and dispatches the selected tool. |

## Supporting tools and data

- `tools/devtree/diff.py` compares structured diagnostic captures.
- `tools/support-advice/fetch.py` builds curated support data from its documented source.
- `campaign.txt` is runtime campaign route data.
- `modtiers.json` stores modifier-family tier ceilings.
- `supports.json` stores ordered support recommendations.
- `CustomLandmarks.json` is embedded into Core as the curated landmark catalog.
