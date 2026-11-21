# Changelog

Everything that changed since the last release goes under "Unreleased". A new release moves it under its version number.

## Unreleased

- Pausing stops the belt animation too. Belts now animate at game speed, so they run faster at 2x and 4x and with Belt Rush.
- Item lists come in groups: ores, plates, metal parts, intermediates, products and fluids. Storage on the left, the Hub's Sell tab and the recipe picker show a heading per group and hide empty groups. A new metal's plate and parts land in the right group by themselves.

## v0.0.7 Beta (2026-11-21)

- Belt Rush potion in the Hub: belts, splitters, undergrounds and crossings move x2, x3, x5 or x10 as fast while it runs. A basic belt on x10 carries 80 items/s.
- Screws, nails and rods from every metal: iron, copper and steel. In an assembler one plate makes 4 screws, 4 nails or 2 rods, and each sells for a bit more than its share of the plate. Steel parts unlock with steel smelting. A new metal only needs one line to get all three parts.
- Buildings now use the new parts. Chests and Fast Belts take iron nails; Splitters and Pumps take iron screws; Underground Belts, Pipes and Underground Pipes take iron rods; Refineries take steel screws; Big and Super Poles and the Solar Array take steel rods. Buildings you have from the start still cost plates, so a new game plays the same until the Assembler. Removing a building refunds what it costs now, so a chest from an old save gives back plates and nails.
- Lightning Rod: 2 copper rods and 1 steel plate in an assembler. For now it's an item to sell, like everything else.
- Six belt tiers instead of three: Turbo (24 items/s, milestone 4), Hyper (32, milestone 5) and Ultra (40, milestone 6) join Belt, Fast and Express.
- Faster versions of other buildings: Fast and Hyper Splitter, Fast and Hyper Underground Belt, and Mk2 and Mk3 of the miner, furnace, assembler and inserter (twice and three times as fast, drawing that much more power). They cost screws, rods and the parts of their milestone.
- Picking tiers: a number key picks the tier you used last, and pressing it again puts the building away. A strip above the slot shows every tier, locked ones dimmed. Click one, or press Tab for the next. The number key no longer steps through the tiers.
- Upgrading in place: build a tier straight onto another tier of the same building. The old one is refunded, the recipe or filter stays, and both ends of an underground pair swap. Dragging a belt line over a belt of another tier swaps it too. Ctrl+Z swaps back.
- Belts now carry what they say. Before, a line carried a lot less than its speed (a basic belt about 5.5 items/s instead of 8), and splitters, undergrounds and crossings let only one item through per tick. Existing saves get the faster belts straight away.
- The crossing now carries 40 items/s per lane, so it never slows a line down.
- The build menu (Q) has three wider columns, so long names keep their icon.
- Your own music: Settings > Sound > Music, Choose folder. Every OGG, MP3 and WAV in that folder plays in name order, round and round, instead of the built-in loop. Built-in switches back. A folder with no music files plays the built-in loop.

## v0.0.6 Beta (2026-11-21)

- Milestones 4 to 6: Clear Glass (plastic and sulfur), Bright Ideas (lamps, batteries, motors) and Reach the Sky (toy robots, clocks, solar panels). After the last one, play goes on, and Sky Beacons sell for the most.
- 14 new items, so all 30 from the plan are in: sand, glass, lens, battery, motor, lamp, smart chip, frame, toy robot, clock, solar panel, drone, computer and sky beacon. Sand comes from stone in an assembler, glass from sand in a furnace, and the rest from assemblers.
- Solar Array (milestone 5): 100 kW for its power network, always on, no fuel. Generators only burn coal for whatever solar doesn't cover.
- Long Wires upgrade in the Hub shop: +2 tiles of wire reach for every pole, 3 levels. Poles that are already built rewire straight away.
- Storage on the left opens a second column when the list would run into the hotbar.
- The recipe window is wider and shows the full recipe as a tooltip, so long recipes aren't cut off.

## v0.0.5 Beta (2026-11-20)

- Game speed: Pause, 1x, 2x, 4x and Step buttons at the top, in normal and sandbox saves. Space pauses and resumes, and the full stop key moves one tick forward while paused. Every save starts at 1x.
- Undo and redo: Ctrl+Z undoes the last thing you built, removed or turned, and Ctrl+Y or Ctrl+Shift+Z redoes it. A whole dragged belt line or a whole paste counts as one step. Undo refunds and redo pays as usual; if something can't come back because the tile is taken or materials ran out, a message says how many.
- Copy and paste: Ctrl+C, then drag a box over buildings. The copy follows the mouse, R turns it, a click places it (as often as you like), Esc or right-click puts it away and Ctrl+V brings it back. Recipes, inserter filters and underground pairs come along.
- Blueprints (B, or the Blueprints button): give the current copy a name to keep it. Blueprints are shared by every save.
- Sandbox tools: the Item Source makes any item you pick and hands it to the tile in front, and the Item Void swallows whatever it's given. They're only in sandbox saves.
- Stats window (P, or the Stats button): coins per minute and how much of each item your factory makes per minute, each with a bar graph of the last 10 minutes.
- Coins per minute shows next to your coins. It can be turned off under Settings, Show on screen.
- Welcome back: opening a save shows a card with what happened last session (play time, items made and delivered, coins, buildings, milestones). Nothing is earned while the game is closed.
- Music: a calm looping tune, made in code like the sound effects. It has its own volume slider, plays quieter in the mini window and stops while the window is hidden.
- Cosmetics in the Hub shop: belt colours (Mint, Rose, Dusk), machine colours (Sky, Moss, Gold), Hub hats (Party Hat, Top Hat, Crown) and particle styles (Rainbow, Gold Dust, Snow). Buy once, then switch freely, or go back to Plain.
- The hover card sits a little lower, under the new buttons at the top.
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
