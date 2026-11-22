# Graph Report - Factorio  (2026-11-22)

## Corpus Check
- 29 files · ~53,033 words
- Verdict: corpus is large enough that graph structure adds value.
- Unclassified: 30 file(s) not represented in the graph (top: .uid 21, (none) 2, .cfg 1)
- WARNING: 30 of 59 files seen (51%) have no supported extractor and are missing from this graph entirely. It may look complete while representing only part of the codebase.

## Summary
- 846 nodes · 2763 edges · 42 communities (22 shown, 20 thin omitted)
- Extraction: 87% EXTRACTED · 13% INFERRED · 0% AMBIGUOUS · INFERRED: 348 edges (avg confidence: 0.85)
- Token cost: 181,439 input · 0 output

## Community Hubs (Navigation)
- HUD and UI panels
- Self-tests
- Juice effects and camera
- Game definitions (Defs)
- Spec and changelog features
- Global hotkeys and input
- Code-drawn sprites
- Namespaces and source files
- World tick and machines
- Belt logic
- Placing, undo and upgrades
- Entity base class
- Mini window
- Chest and item source saving
- Crossing and underground items
- Linux packaging script
- Save files (Persist)
- Power and machine speed
- Sound and music
- Game lifecycle (Main)
- Generator and fluid networks
- Splitter
- Spec overview
- Pipes and fluids
- Blueprints and copy-paste
- Settings and hotkey setup
- Furnace
- Inserter filter
- Screen corners
- Audio design
- Project build settings
- Window modes
- AppImage script
- App icon
- Settings change event
- Inserter (spec)
- Juice families (spec)
- Stats and hover (spec)

## God Nodes (most connected - your core abstractions)
1. `World` - 161 edges
2. `Hud` - 78 edges
3. `WorldView` - 72 edges
4. `Entity` - 61 edges
5. `SelfTest` - 60 edges
6. `AppWindow` - 37 edges
7. `Sprites` - 37 edges
8. `Defs` - 35 edges
9. `Canvas` - 35 edges
10. `EntityData` - 33 edges

## Surprising Connections (you probably didn't know these)
- `Belts carry rated speed (throughput fix)` --implements--> `Carriers update downstream first`  [INFERRED]
  CHANGELOG.md → SPEC.md
- `Lightning Rod building (power bursts)` --semantically_similar_to--> `Solar Array`  [INFERRED] [semantically similar]
  CHANGELOG.md → SPEC.md
- `Main` --references--> `Settings`  [EXTRACTED]
  scripts/Main.cs → scripts/Persist.cs
- `Main` --references--> `AppWindow`  [EXTRACTED]
  scripts/Main.cs → scripts/Platform/AppWindow.cs
- `Main` --references--> `GlobalHotkeys`  [EXTRACTED]
  scripts/Main.cs → scripts/Platform/GlobalHotkeys.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Item carriers updated downstream-first** — spec_belts, spec_splitter, spec_underground_belt, spec_crossing, spec_carrier_update_order [EXTRACTED 1.00]
- **Power network sources** — spec_power_system, spec_generator, spec_solar_array, spec_power_poles, changelog_lightning_rod_building [INFERRED 0.85]
- **Hub economy (coins, upgrades, cosmetics, potions)** — spec_hub, spec_coins_auto_sell, spec_upgrades, spec_cosmetics, spec_potions [EXTRACTED 1.00]

## Communities (42 total, 20 thin omitted)

### Community 0 - "HUD and UI panels"
Cohesion: 0.05
Nodes (9): Settings, Hud, HasPanel, W, SettingsUi, TitleScreen, BouncyTitle, TutorialActive (+1 more)

### Community 1 - "Self-tests"
Cohesion: 0.10
Nodes (6): Miner, Status, SelfTest, PotionSave, SaveData, PotionStack

### Community 2 - "Juice effects and camera"
Cohesion: 0.09
Nodes (9): Juice, Floater, Particle, ShakeOffset, WorldView, BeltTime, CamTarget, HoverTile (+1 more)

### Community 3 - "Game definitions (Defs)"
Cohesion: 0.07
Nodes (16): BuildingDef, Cosmetic, Defs, ItemDef, Milestone, Potion, Recipe, Upgrade (+8 more)

### Community 4 - "Spec and changelog features"
Cohesion: 0.06
Nodes (34): Belt animation at game speed, Belt Rush potion, Belts carry rated speed (throughput fix), Item list groups, Lightning Rod building (power bursts), Screws, nails and rods (metal parts), Mini window transparency and hover-grow fixes, Splitter picks output at centre (never jams) (+26 more)

### Community 5 - "Global hotkeys and input"
Cohesion: 0.08
Nodes (7): GlobalHotkeys, Global, IKeyState, Paused, PressedEventHandler, WinKeys, X11Keys

### Community 7 - "Namespaces and source files"
Cohesion: 0.18
Nodes (6): TinyWorks.Sim, TinyWorks.View, TinyWorks.UI, TinyWorks.Platform, TinyWorks, SessionStats

### Community 8 - "World tick and machines"
Cohesion: 0.10
Nodes (11): GameEvent, World, ArmSpeed, CurrentMilestone, Free, GeneratorKW, HandYield, MachineSpeed (+3 more)

### Community 9 - "Belt logic"
Cohesion: 0.09
Nodes (5): Belt, Item, Speed, Tail, Hub

### Community 11 - "Entity base class"
Cohesion: 0.10
Nodes (13): Entity, Back, Config, Dir, Front, Size, Status, ItemVoid (+5 more)

### Community 12 - "Mini window"
Cohesion: 0.14
Nodes (11): AppWindow, AnchorBottom, AnchorRight, BigSize, ContentScale, Ease, Hidden, MiniOpacity (+3 more)

### Community 13 - "Chest and item source saving"
Cohesion: 0.14
Nodes (6): Chest, Count, ItemSource, Config, Status, EntityData

### Community 14 - "Crossing and underground items"
Cohesion: 0.17
Nodes (5): Crossing, Item, Tunnel, Item, Speed

### Community 15 - "Linux packaging script"
Cohesion: 0.34
Nodes (5): add(), arch(), deb(), dirs_of(), tree_tar()

### Community 16 - "Save files (Persist)"
Cohesion: 0.18
Nodes (4): Persist, BlueprintsPath, SavesDir, SettingsPath

### Community 18 - "Sound and music"
Cohesion: 0.33
Nodes (4): Sfx, MusicFolder, MusicVolume, Muted

### Community 21 - "Generator and fluid networks"
Cohesion: 0.18
Nodes (5): Generator, CanSupply, Status, FluidNet, Cap

### Community 22 - "Splitter"
Cohesion: 0.24
Nodes (4): Splitter, Item, Speed, Status

### Community 23 - "Spec overview"
Cohesion: 0.22
Nodes (8): Changelog, Blueprints (global, shared between saves), Box-select copy/paste, Godot 4 .NET (C#), Hotbar and tier strip, Mk2/Mk3 machines, Tiny Works (game spec), Undo / redo

### Community 29 - "Screen corners"
Cohesion: 0.40
Nodes (5): ScreenCorner, BottomLeft, BottomRight, TopLeft, TopRight

### Community 31 - "Project build settings"
Cohesion: 0.67
Nodes (3): net8.0, Godot.NET.Sdk/4.7.2, TinyWorks

### Community 32 - "Window modes"
Cohesion: 0.67
Nodes (3): WindowMode, Mini, Normal

## Knowledge Gaps
- **75 isolated node(s):** `net8.0`, `Godot.NET.Sdk/4.7.2`, `SavesDir`, `SettingsPath`, `BlueprintsPath` (+70 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 143 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **20 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `World` connect `World tick and machines` to `HUD and UI panels`, `Self-tests`, `Juice effects and camera`, `Game definitions (Defs)`, `Code-drawn sprites`, `Namespaces and source files`, `Belt logic`, `Placing, undo and upgrades`, `Entity base class`, `Chest and item source saving`, `Power and machine speed`, `Game lifecycle (Main)`, `Generator and fluid networks`, `Splitter`, `Pipes and fluids`, `Blueprints and copy-paste`, `Settings and hotkey setup`, `Furnace`, `Inserter filter`?**
  _High betweenness centrality (0.247) - this node is a cross-community bridge._
- **What connects `net8.0`, `Godot.NET.Sdk/4.7.2`, `SavesDir` to the rest of the system?**
  _75 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `HUD and UI panels` be split into smaller, more focused modules?**
  _Cohesion score 0.05105807086614173 - nodes in this community are weakly interconnected._
- **Why does `Hud` connect `HUD and UI panels` to `Juice effects and camera`, `Game definitions (Defs)`, `Namespaces and source files`, `World tick and machines`, `Chest and item source saving`, `Sound and music`, `Game lifecycle (Main)`, `UI test`, `Blueprints and copy-paste`, `Inserter filter`?**
  _High betweenness centrality (0.115) - this node is a cross-community bridge._
- **Should `Self-tests` be split into smaller, more focused modules?**
  _Cohesion score 0.09823509823509824 - nodes in this community are weakly interconnected._
- **Why does `WorldView` connect `Juice effects and camera` to `HUD and UI panels`, `Game definitions (Defs)`, `Code-drawn sprites`, `Namespaces and source files`, `World tick and machines`, `Belt logic`, `Placing, undo and upgrades`, `Entity base class`, `Chest and item source saving`, `Sound and music`, `Game lifecycle (Main)`, `Pipes and fluids`, `Blueprints and copy-paste`?**
  _High betweenness centrality (0.113) - this node is a cross-community bridge._
- **Should `Juice effects and camera` be split into smaller, more focused modules?**
  _Cohesion score 0.09134906231094979 - nodes in this community are weakly interconnected._