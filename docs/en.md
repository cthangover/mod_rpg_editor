# RPG Map Editor — painting tile maps and placing events

The RPG Map Editor edits the tile maps of the RPG engine while the game runs: the three tile layers, walls and passages, the player's start, random battles and the events — characters, signs, doors, items to pick up. Saving updates the map in the game at once, and **Play** walks it.

It opens from the tool box (the hammer on the toolbar) as **RPG Map Editor**, on the map the player is on.

## An edit and its result in the game

The elder's house of RPG Plus before the edit:

![The elder's house in the game: a wooden room with the elder standing at the back and a chest on the right.](assets/house-before.jpg)

In the editor: the **Appearance** tab, the merchant's sprite, **Place NPC**, a click on a free cell. The new event is selected on the **Events** layer and its properties are on the right:

![The RPG Map Editor on rpg_demo_game / rpg_elder_house.json: the Appearance list with merchant · village/merchant selected, the map with the elder and the new merchant framed as events and dashed frames around events of other files, and on the right the map properties (tileset village, 12 × 10, spawn 6, 8 facing Up) and the new event npc_village_merchant at 9, 3 with charset village/merchant, Facing Down, Trigger Action key (E/F). The status line says "merchant" placed as an NPC.](assets/editor-npc.jpg)

After **Save** (Ctrl+S) the merchant stands in the house:

![The same room in the game, now with the merchant at the right of the back wall.](assets/house-after.jpg)

His dialogue and behaviour are set in the event's properties: a scenario, a condition, a trigger.

## The layers and tools

- **Back**, **Main**, **Front** — the tile layers. Back and Main can block walking with solid tiles; Front is drawn over the characters and never blocks.
- **Collision** — painted over the tiles: *Wall* (always blocked), *Passage* (always walkable) or *Auto* (from the tiles).
- **Events** — click to select or drag, double-click an empty cell to add, Del to delete.
- **Pencil**, **Eraser**, **Fill**; the right mouse button erases, Alt+click picks a tile, keys 1–5 switch layers, B/E/G the brushes.
- **Undo**/**Redo** (Ctrl+Z / Ctrl+Y), the grid, zoom (Ctrl+wheel).

![The Collision layer of the elder's house: the walls are painted red as Wall, the floor is open.](assets/collision.jpg)

The tabs on the left: **Tiles** (the palette of the map's tileset, whole objects such as a house or a tree can be stamped in one go), **Characters** (characters with combat stats, placed as enemies), **Items** (placed as pick-ups with a count) and **Appearance** (every sprite of the loaded mods, placed as NPCs).

The right column holds the map's properties: its name key, tileset, size, zoom, ambient light and atmosphere, the spawn point and facing, and random battles (every ~N steps, the enemy groups such as `slime; wolf,slime`).

## Top-down and isometric

The editor draws the map the way the game does. The view picker in the toolbar switches the preview only; the map data stays the same. The house in the isometric preview:

![The elder's house in the isometric preview of the editor: the same room drawn in isometric view, the tileset palette on the left.](assets/editor-isometric.jpg)

The game's own view is set by the collection setting `tile.renderer` (`topdown` or `isometric`); `--editor-renderer=isometric` opens the editor with the isometric preview.

## Whose files are written

- The map itself is saved into its `maps/<id>.json`.
- Events go into the map's own events file in the same mod (`map_events/<map>/…`). Events that other files or other mods add to the map are shown faded with dashed frames and are never rewritten; their ids cannot be reused.
- **+** next to the file list makes a new map in the selected mod; after saving, the game knows it as a scene.
- Only mods installed as folders can be written to.

## If something goes wrong

1. **Play says the game does not know the scene.** The map needs `scenes/<id>.json` in its mod.
2. **An event cannot be selected.** It belongs to another file or mod (dashed frame).
3. **An NPC does not talk.** Its event has no scenario, or its trigger is not the action key.
4. **Saving changes lines you did not touch.** The editor writes the files in its own order and adds default values of events, so a diff can show more than the edit.
