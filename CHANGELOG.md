# Changelog

Everything that changed since the last release goes under "Unreleased". A new release moves it under its version number.

## Unreleased

- Linux (Arch and others): the mini window shrinks again when the mouse leaves it. On Wayland desktops the game stopped seeing the mouse once it left the window, so the mini window stayed big. Hover now uses the window's own enter and leave events.

## v0.0.4 Beta (2026-11-20)

- Settings sliders keep their place: the percentage next to them has a fixed width.
- Poles are a tier family like the belts: Power Pole, Big Pole (milestone 2) and Super Pole (milestone 3) share one hotbar slot (key 0, press again for the next tier, the last tier is remembered). Better poles wire further (7, 12, 18 tiles) and power a bigger area (5x5, 7x7, 9x9).
- Potions have a strength to pick, for example Coin Rush x1.5, x2 or x3. Stronger costs more. Strengths never add up: the strongest one runs first, and weaker ones wait and take over after it. Potions from v0.0.3 saves load at their old strength.
- Numbers like 10.7k use a dot in every language setting.
- Hover info: a card at the top centre shows what the mouse is over. For a building that's its contents, recipe progress, power and problems; for an ore patch it says how to mine it. It can be turned off under Settings, Stats.
- The milestone box moved to the bottom right.
- VSync can be switched on or off in the settings.
- Type in any FPS limit (0 = no limit), for focused and for mini or background.
- Items fed in from the side of a belt glide in from the edge. They used to jump half a tile to the middle.
- Splitters pick the output when an item reaches the centre, so items no longer slide one way and appear on another belt. If that output stays full for a second, the item switches to a free one, so a splitter never jams.
- Drawing a belt line straight across another running line places a crossing there, instead of turning that belt and bending the other line into yours.
- Belt joins look like T-pieces: the joining belt runs into an opening in the main belt's rail.
- Checkboxes no longer turn into big orange blocks when ticked.

## v0.0.3 Beta (2026-11-20)

- Milestone bars show the item name. Hovering a row shows how to make the item (machine and recipe, or the ore patch).
- Storage rows on the left have the same tooltips.
- Every building now has a hotbar slot, including pipes, pump, refinery, splitter and underground belt. Keys 1 to 9 and 0 reach the first ten. Locked buildings are dimmed, and their tooltip says which milestone unlocks them.
- The hotbar shrinks to fit the mini window.
- New upgrades in the Hub shop: Better Deals (+10% sale price), Bigger Generators (+20% power), Strong Pumps (+25% water). The old upgrades have more levels: Faster Machines 10, Quick Arms 6, Strong Hands 5.
- Potions (a new tab in the Hub): Coin Rush (auto-sell pays double), Overdrive (machines 50% faster), Arm Tonic (inserters 50% faster) and Rock Crusher (5x ore when mining by hand). Each comes in 2 or 10 minutes, or you brew your own with any length from 1 to 120 minutes. Prices grow with milestones and coins earned. Active potions count down under the coins and only while the game runs.

- Mini window: real transparency. Before, the window got darker instead of see-through.
- Mini window: hover-grow is fast (0.12 s) and smooth, because the picture scales instead of the OS window resizing. While small, the empty part of the window lets clicks through to whatever is behind it.
- Scrolling past the top or bottom of a window no longer closes it and zooms the map.
- Zoom is slower and finer: half steps, softer easing, one step per wheel flick.
- Key 1 starts with the belt tier you used last, also after a restart.
- Save Now gives clear feedback: a green "Saved ..." with the time once the file is checked on disk, or a red message with the reason if it failed.
- Big Pole (milestone 2): wires up to 18 tiles to other big poles. Normal poles still link only within 7 tiles.
- Changing a setting no longer closes the settings window.

## v0.0.2 Beta (2026-11-20)

- Tutorial on first start: a checklist that ticks itself off as you play.
- Packages: Windows installer, `.deb` (Debian/Ubuntu), `.pkg.tar.zst` (Arch), AppImage, and portable zip and tar.gz.
- App icon (the Hub).
- Items glide smoothly over belts. They used to stutter or jump at belt edges.
- Sharper graphics: pixels were blurred before. Redrawn belts.
- Hover effects on buttons and item rows.
- Mini mode grows and fades in on hover. Size and opacity can be set in the settings.
- Milestone bars fill their box.

## v0.0.1 Beta (2026-11-20)

- First beta: miners, belts in 3 speeds, crossing, splitter, underground belts, inserters with filters, furnaces, assemblers, chests.
- Power as a bonus. Pipes, pumps, refineries, underground pipes, and the underground view (V).
- 3 milestones, coins, auto-sell, upgrades.
- Normal and mini window, global F7/F8.
