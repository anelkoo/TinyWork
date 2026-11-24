# Tiny Works: spec

A relaxing, peaceful factory-builder in the same genre as Factorio. Original art, names and items. Runs on Windows and Linux (X11), saves locally, and is light on CPU and GPU.

## Tech

- Godot 4 .NET, everything written in C#. Not installed yet; the .NET 10 SDK is.
- Global hotkeys use InputLib's approach, reimplemented (InputLib has no license, so none of its code is copied): a background thread polls one key combo about every 30 ms. Windows uses `GetAsyncKeyState` (user32), X11 uses `XQueryKeymap` (libX11).
- Wayland has no global hotkeys. On Wayland the keys only work while the window is focused.
- All art is drawn in code at 16 px tiles and scaled up with nearest-neighbour. SFX come from sfxr-style synthesis, music from a CC0 source.
- English only, with all UI strings kept in one file.

## Windows and modes

- **Normal mode:** an ordinary decorated, resizable window.
- **Mini mode:** borderless and always on top, 400x300 by default, resizable by dragging its edges. It anchors to a screen corner with a margin; the corner is picked in settings. An opacity slider (20 to 100%) changes the rendered alpha on a transparent window. It never lets clicks through and stays fully playable, with the UI collapsed into a small toolbar.
- **F8** (rebindable, modifiers allowed) hides or shows the window from anywhere, in both modes.
- **F7** (rebindable) switches between normal and mini mode. Each window also has a shrink/expand button.
- The game remembers each mode's size and position.
- Nothing special happens over fullscreen apps. F8 is the way to get the window out of the way.

## Running and time

- The simulation runs at a fixed 20 ticks per second whenever the app is open, including while hidden, minimized or unfocused.
- No progress happens while the app is closed. On reopen, a "welcome back" card shows the last session's stats and grants nothing.
- Frame caps are set in settings. Defaults: 60 fps focused, 60 fps in mini mode or unfocused. Nothing is drawn while hidden, and audio mutes while hidden.
- Game speed controls (pause, 1x, 2x, 4x, single-step) exist in both Normal and Sandbox saves.

## Launch and saves

- A title screen with Continue, Load, New, Settings and Quit. A setting skips it and goes straight to the last save in the last mode.
- Unlimited named save slots. Each slot is Normal or Sandbox.
- Autosave every 2 minutes and on quit. The game writes a temp file and then renames it so a crash can't corrupt the save. Saves live in `user://`.
- Blueprints are stored globally and shared between saves.

## World

- Infinite procedural chunks with ore patches. Ore never runs out.
- Fully peaceful: no enemies, no pollution, nothing to fail.
- Free camera only: WASD or middle-mouse drag to pan, wheel to zoom with easing.
- No day/night cycle and no weather.
- Sandbox can generate a flat, empty world instead.

## Economy and progression

- Endless play with 6 milestone tiers. Delivering items to the Hub fills milestone bars, and each tier unlocks buildings and recipes.
- **Central storage:** the Hub feeds one global pool. Building costs come out of the pool, and removing a building refunds 100%.
- **Coins:** each item can be set to auto-sell at the Hub. Coins buy cosmetics (belt and machine colours, Hub hats, particle styles), global upgrades and raw materials.
  - Upgrades: Faster Machines, Quick Arms, Strong Hands, Better Deals, Bigger Generators, Strong Pumps and Long Wires (+2 tiles of wire reach per level, 3 levels). Prices double per level.
  - Better Deals is endless: +10% per level, with a price that doubles up to level 10 and then grows x1.5 per level.
  - No music tracks in the shop: you pick your own music folder in Settings > Sound > Music. There is no Hub capacity upgrade either, since central storage has no limit.
- **Potions** (a Hub tab): Coin Rush, Overdrive, Arm Tonic and Rock Crusher, bought for 2 or 10 minutes or any length from 1 to 120. Each comes in three strengths. Strengths never stack: the strongest runs first and weaker ones wait. Prices grow with milestones and coins earned, and timers only run while the game runs.
- **Start:** a small starter kit, and you can click ore patches to mine by hand at any time.
- **Sandbox:** everything unlocked, free building, an item source and an item void, and speed controls.

## Buildings

- Miner, chest, furnace, assembler, Hub.
- Belts: one lane, six speed tiers: Belt 8, Fast 12, Express 16, Turbo 24, Hyper 32 and Ultra 40 items/s. Dragging lays a belt line that auto-rotates at corners.
- Splitter/merger in three tiers (16, 24, 40 items/s).
- Underground belt (range 5) in three tiers (12, 24, 40 items/s) and underground pipe (range 10). A view toggle switches between the surface layer and the underground layer.
- Crossing: 40 items/s per lane, as fast as the fastest belt.
- Inserter: it takes from the tile behind it and drops in front, with an optional single-item filter.
- Miner, furnace, assembler and inserter each come as Mk2 (twice as fast, twice the power) and Mk3 (three times).
- Tiers: every tier of a building shares its hotbar slot. Building a tier onto another tier of the same building swaps it in place: the old one is refunded, the recipe or filter stays, and an underground pair swaps both ends. Undo swaps back.
- Carriers (belts, splitters, undergrounds, crossings) update downstream first and hand on what runs over an edge, so a line carries its rated speed in whatever order it was built.
- Power: generators (coal or sparkgas, 600 kW), solar arrays (100 kW, always on, used before coal) and poles. Poles come in three tiers sharing one hotbar slot: Power Pole (wires 7 tiles, powers 5x5), Big Pole (12, 7x7) and Super Pole (18, 9x9). Two poles link within the shorter of their two ranges. Each connected group is its own network. Power is a bonus: full power doubles a machine's speed, too little gives part of it.
- Fluids: pump, pipe, refinery. Connected pipes form one network holding a single fluid type and an amount, with no pressure physics. There is no water on the map: pumps work anywhere, like a well.
- Tools: undo/redo, box-select copy/paste with rotation, named blueprints.

## Items (30)

| Group | Items |
|---|---|
| Raw (4) | iron ore, copper ore, stone, coal |
| Fluids (3) | water, black goo, sparkgas |
| Processed (6) | iron plate, copper plate, brick, steel plate, sand, glass |
| Intermediate (10) | gear, copper wire, circuit, smart chip, plastic, sulfur, battery, motor, frame, lens |
| Products (7) | lamp, toy robot, clock, drone, solar panel, computer, sky beacon |

Milestones, each unlocking what the next one needs:

| # | Name | Deliver | Unlocks |
|---|---|---|---|
| 1 | First Plates | 40 iron plate, 20 copper plate | assembler, chest, splitter, underground belt |
| 2 | Gears Turning | 40 gear, 60 copper wire | fast belt, steel, big pole, miner/furnace/inserter Mk2 |
| 3 | Smart Factory | 30 circuit, 15 steel plate | express belt, super pole, pipes, pump, refinery, assembler Mk2, fast splitter, fast underground |
| 4 | Clear Glass | 40 plastic, 30 sulfur | sand, glass, lens, battery, motor, lamp, turbo belt |
| 5 | Bright Ideas | 40 lamp, 30 battery, 30 motor | smart chip, frame, toy robot, clock, solar panel, solar array, hyper belt, miner/furnace/inserter Mk3, hyper splitter, hyper underground |
| 6 | Reach the Sky | 20 toy robot, 20 clock, 20 solar panel | drone, computer, sky beacon, ultra belt, assembler Mk3 |

After milestone 6 play is endless; the Sky Beacon sells for the most. Recipes live in `Defs.cs` and the self-test checks that every milestone can be made from what earlier ones unlock.

Auto-sell keeps a reserve in storage (50 per item by default, adjustable per item), so selling can't use up the material you build with.

## UI

- A bottom hotbar with a slot for every building, locked ones dimmed. Keys 1 to 9 and 0 reach the first ten, and pressing a slot again puts the building away. A tiered slot starts with the tier used last and shows a strip of all its tiers above the slot (locked ones dimmed): click one to pick it, or press Tab for the next unlocked tier. Q opens a grid of all buildings. R rotates, X or right-click removes. In mini mode the hotbar shrinks to fit.
- Stats: a window (P) with coins per minute and production per item, each with a 10 minute bar graph; coins per minute next to the coin count; a hover card at the top centre for whatever the mouse is over (contents, recipe progress, power, problems, or how to mine an ore patch); and bottleneck bubbles over machines. Each can be turned on or off in settings.
- A first-start tutorial: a 7-step checklist card at the top right that ticks itself off as you play, with a cheer per step. It can be hidden per world, and saves already past milestone 1 skip it.
- Keys: Space pauses, the full stop steps one tick, Ctrl+Z / Ctrl+Y undo and redo, Ctrl+C copies, Ctrl+V pastes, B opens blueprints, V switches to the underground view.

## Juice

All four families are in:

- **Placement and removal:** squash-and-stretch, dust puffs, a wobbly ghost preview, a thunk sound.
- **Production:** machines bob and puff while working, items pop out, floating +1 numbers, sparkles on milestone items.
- **Screen and camera:** shake, eased zoom and pan, slow motion plus a flash and confetti on milestone unlocks.
- **UI:** buttons squish, panels slide and overshoot, counters tick up, tooltips spring in.

Each effect family has its own toggles for normal mode and for mini mode.

## Audio

SFX plus a calm music loop, with separate volume sliders. Audio mutes while hidden and plays quieter in mini mode.

## Packaging

- Windows: a portable zip and an Inno Setup installer.
- Linux x86_64: a .deb, an Arch package, an AppImage and a tar.gz.

## Known platform limits

- Wayland: no global hotkeys.
- X11 without a compositor: the mini window can't be transparent and may show black behind it.
- Tiling window managers may ignore the borderless and always-on-top hints.
- Mini mode anchors to the monitor the window is currently on.
- Godot can't hide its main window. Windows hides it natively (`ShowWindow`, so it also leaves the taskbar). Linux minimizes it for now.

## Build order

Each step ends in something playable:

1. **Platform spike** in an empty project. Mini window: borderless, on top, transparent, with opacity. Hide and show while the sim keeps ticking. The hotkey poller: detects key-down edges, takes F8 from only one source while focused, pauses during rebinding.
2. Miner, belt, inserter, furnace, Hub, central storage, saving and slots. Inserters come in here because without them nothing can feed a furnace.
3. Assemblers, chests, milestones, coins, Q menu, settings UI. Also added here: the Crossing (two belt lines pass through each other), side-feed belt joins, and bottleneck bubbles. Cosmetics in the coin shop and a customizable hotbar are deferred to step 6.
4. Power. Decided with the user: power is a bonus, not a requirement. Unpowered machines run at normal speed, full power doubles it, too little power gives part of the bonus. Generators burn coal (600 kW); poles wire to their 3 nearest poles within 7 tiles and power a 5x5 square. Key 1 cycles the belt tiers, so the hotbar fits 9 buildings.
5. Fluids, undergrounds and the splitter. Done:
   - **Splitter** (1x1, milestone 1): takes items from behind and hands them out in turn to the front, right and left, skipping any side that can't take one. Belts already merge through side joins, so there is no separate merger.
   - **Underground belt** (milestone 1, range 5) and **underground pipe** (milestone 3, range 10): place the entrance, then a second piece facing the same way. It becomes the exit and the ghost shows the link. The tiles between stay free on the surface. **V** switches to the underground view, which dims the surface and draws every run below it. A piece without a partner is outlined red.
   - **Pipes, pump, refinery** (milestone 3): pipes that touch form one network with one fluid. When two fluids join, the larger one stays and the smaller one spills, with a toast. Pumps work on any tile, pushing water into the pipe in front. The refinery takes its fluid from the pipe behind and puts fluid out to the pipe in front, or items out to an inserter. Its item ingredients come in by inserter.
   - Refinery recipes: water + coal → black goo; black goo + coal → plastic; black goo → sparkgas; sparkgas + stone → sulfur. Generators next to a sparkgas pipe burn it (200 kJ per unit).
   - New items: plastic and sulfur, plus the fluids water, black goo and sparkgas.
   - Milestones 4 to 6 came later, in v0.0.6. The customizable hotbar was dropped: every building got its own slot instead.
6. Undo, copy/paste, blueprints, Sandbox tools, stats. Done in v0.0.5, together with game speed, the welcome-back card, music and the cosmetics shop. The music is synthesized in code instead of coming from a CC0 source, and you can pick your own music folder instead of buying tracks.
7. Installers and packages. Done: `.\build.ps1 -Version <ver>` builds a Windows installer (Inno Setup, per user, no admin) and a portable zip, a `.deb` (Debian/Ubuntu), a `.pkg.tar.zst` (Arch), an AppImage (other distros) and a Linux tar.gz. All are self-contained, with no .NET or ICU needed. The packaging files are in `packaging/`, and the icon is the Hub (`--export-icon`).
8. Content. Done in v0.0.6: milestones 4 to 6, the 14 remaining items (all 30 now exist), the solar array and the Long Wires upgrade.
   v0.0.7 added screws, nails and rods (now in building costs), six belt tiers, Mk2/Mk3 machines, upgrading in place, own music and the Belt Rush potion.
   v0.1.0 finished the materials plan: seven new ores (tin, lead, gold, zinc, quartz, wood, sparkcrystal), bronze and brass from molten metal, concrete, fuel, fertilizer and explosives, the fluids steam, molten metal, acid, oil and diesel, a working Lightning Rod, splitter filters, chest limits, the tank and the accumulator. Every building from milestone 1 on needs screws, nails or rods. Not built: a cooler and a research lab (no job for the first, the second would duplicate milestones).
   v0.1.1 added the item book (I), fixed Hub tabs, brew sliders for strength and length, a faster camera that ignores keys typed into fields, and an easier mini-window resize.
   v0.1.2 made saves safer (flushed to disk, a backup of the previous save, a check before writing), remembers potion strengths and hides a jumpscare secret behind the title logo.

Juice and sound get added to each system as it lands.

## Out of scope

Enemies, pollution, ore depletion, two-lane belts, fluid pressure physics, a player character, offline earnings, click-through, auto-hide over fullscreen apps, translations, macOS, and global hotkeys on Wayland.
