# Feature guide

## World radar

World features present the player and classified entities captured by Core. Classification distinguishes players, monsters, NPCs, chests, transitions, items, and other world objects. Display rules determine visibility, labels, icons, colors, and priority.

## Native map

The native-map feature combines terrain, player position, entity positions, landmarks, minimap icons, and exploration state. Projection utilities translate world/grid coordinates into overlay coordinates. Map UI alignment allows output to follow the game's own open map when the required frame data is valid.

## Navigation

Routes are calculated over the captured walkable grid and rendered as guidance. Background replanning responds to meaningful position or destination changes. It does not move the character or send input.

## Campaign guidance

Campaign data describes zone order and progression. The area graph, journal, step selection, and route guide use the current area identity to present the next useful objective. `campaign.txt` is copied beside the application so progression data can be corrected without recompiling code.

## Loot and item information

Loot features use captured ground labels and item-slot snapshots. Price and tier helpers annotate information already available to the application. Local generated data such as `prices.json` is runtime state and must not be committed.

## Health and threat

Health-bar and threat features consume entity vitals, rarity, relation, and position snapshots. They should tolerate missing vitals and never infer a live value from an old frame.

## AutoPotion

AutoPotion is the only feature that sends input. It is disabled on a fresh configuration and can press configured flask keys through Win32 `SendInput` after the user enables it. It requires a foreground game window, a valid in-area frame, plausible vitals, an enabled threshold, and an expired cooldown. F8 is the default persistent kill switch, and dry-run mode evaluates decisions without sending a key.

This feature is optional and carries policy/account risk. Users must review the current game rules before enabling it.

## Support advice

`supports.json` contains curated support recommendations built by `tools/support-advice/fetch.py`. The large HTML directory under `tools/support-advice/cache/` is reproducible tool input/cache and is ignored in a standalone EasyExile repository.

## Diagnostics

The diagnostics project validates raw assumptions against a matching live build. Commands cover chains, area transitions, camera data, panels, map alignment, text/tooltip trees, item slots, loot labels, and related structures. Diagnostic output is research evidence and should not be treated as a stable user interface.
