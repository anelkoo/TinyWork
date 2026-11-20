# Changelog

Everything that changed since the last release goes under "Unreleased". A new release moves it under its version number.

## Unreleased

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
