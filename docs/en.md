# RPG Map Editor — painting maps and placing objects

The RPG Map Editor changes the tile maps of the RPG world while the game runs. You paint the ground and the walls, place sprites and characters on cells, paint the collision, move the spawn point and set the random encounters. **Save** writes the map's files, and the game walks the new map at once — no restart.

It opens from the tool box (the hammer on the toolbar) as **RPG Map Editor**. It opens on the map the game is showing; use the map picker to open another one.

## What you see

![The RPG Map Editor on the elder's house of RPG Plus: the toolbar with the map picker, Save, Undo, Redo and the tools; the object palette with the Surfaces tab on the left; the map in the middle; the inspector on the right with the map size, spawn point, teleport links and encounters.](assets/rpg-editor-open.jpg)

- **Toolbar** — the mod and the map file (**+** makes a new map), **Save**, **Undo**, **Redo**, the five tools, the collision code picker, the projection picker (**Top-down** or **Isometric**) and **Fit**, which shows the whole map.
- **Palette** (left) — the objects of the mod's RPG objects, in four tabs by shape: **Surfaces** (ground), **Segments** (one wall or fence cell), **Sprites** (objects on the grid) and **Actors** (characters on one cell). Each tab has a search by name or id and a thumbnail per row.
- **Map** (middle) — the map drawn by the game's own renderer. Thin frames show the footprint of each placement; the selected one is filled.
- **Inspector** (right) — the selected placement, the map's properties, its teleport links and the encounters.

The projection picker switches the drawing only. The map is the same in both projections, and you can switch while you edit:

![The same elder's house in the isometric projection.](assets/rpg-editor-isometric.jpg)

## The tools

- **Select** — click a placement to select it; drag it to another cell to move it; **Delete** removes it; **Esc** clears the selection. Drag a rectangle over empty ground to select several placements at once; **Ctrl+A** selects them all. **Ctrl+C** copies the selected placements, and **Ctrl+V** pastes them with their first placement on the cell under the pointer (the copies keep their offsets and are selected). Delete, copy and paste each work on the whole selection, and each is one step of undo.
- **Effects** — paint the effect mask of the atmosphere profile chosen beside the toolbar (for example fog or snow) over cells; the right mouse button erases. The masked cells are tinted and the game draws the effect only there. A profile's mask with no cells left is removed from the map. Click **Open map editor** in the Effects Editor to open this tool with its profile chosen.
- **Paint** — choose a surface or a segment in the palette, then click or drag over cells. Surfaces paint the ground, segments paint the structure layer. The right mouse button erases.
- **Place** — choose a sprite or an actor, then click a cell. A ghost of the object follows the pointer; it turns red where the object would block cells. The **Characters** tab (the last tab) places a character as its actor object: the object is written into the map's mod (`objects/characters.json`) when the character is chosen, and a character with a join scenario (see the Characters Editor) joins the party when talked to.
- **Collision** — paint the walkability of cells: **As objects (.)** leaves it to the objects, **Blocked (#)** blocks the cells, **Forced open (o)** makes them walkable.
- **Spawn** — click a cell to put the player's start there. The inspector's **Pick a cell** button switches to this tool.

A painted stroke shows its cells as you drag. Each stroke, placement, move, collision change and spawn change is one step of undo.

![A surface being painted: the earth surface is chosen in the Surfaces tab, the Paint tool is selected and the cell under the pointer is outlined.](assets/rpg-editor-paint.jpg)

To place a sprite, choose it in the **Sprites** tab, select **Place** and click the cell. The new placement is selected, so the inspector shows it at once. Clicking an existing placement with the **Select** tool shows its properties in the inspector:

![Placing a sprite: the Sprites tab with a barrel chosen and the Place tool active; the ghost of the barrel sits on the cell under the pointer.](assets/rpg-editor-place.jpg)

## The inspector

- **Placement** — the object's name and id, and where the placement comes from. Its own placements show *This map's own placement*. A placement from another mod shows that mod's name: your edits are then written as a patch in your mod, and the other mod's file stays as it is. You can change **Cell X** and **Cell Y**, **Remove** it, or **Reset** your mod's patch so the game sees the owner's placement again.
- **Interaction** — what the placement does when the player uses it or steps on it. **Does** chooses **Gives an item** (the item picker and **Count**), **Leads to another cell or map** (the map picker, the target X and Y, the facing and **Pick the cell on the map**), **Opens a dialog** (the scenario picker), **Opens a shop** (the shop picker) or **Nothing**. **When** chooses use (E or F while facing the object) or stepping onto it. The choice replaces the object's own interaction for this placement only, so a chest's own commands are replaced too. To pick the cell on another map, save first: the editor opens that map, you click the arrival cell, and the map you came from opens again with the target written (Ctrl+S saves it; Esc cancels). On a placement of another mod that already has its own interaction, a choice that drops one of its fields is refused, because a patch cannot drop it: change the owner's file.
- **Map** — the size in cells, the spawn point (**Pick a cell**), the **Variant** (the look of objects in this map variant) and the **Atmosphere** (for example `woodland`; none for none). Both are chosen from lists.
- **Teleport links** — where this map's teleports lead and which teleports lead here. On the map, a blue arrow runs from a door to its target cell on this map, or leaves towards the name of the other map; a green arrow comes in from another map; a red arrow means a broken target. The list shows the same links (→ leads out, ← leads in). Click a link to open the target map. A red mark means the target is broken: no such map, a cell off the map, or a blocked cell.
- **Encounters** — shown only when the random battle integration (`rpg__battles`) is in the game. **Steps per battle** is the average number of steps between battles (0 turns them off). **Enemy groups** takes one group per line, enemies separated by commas; **Apply encounters** writes them.
- **Add an enemy group...** (in the same section) adds a line with that enemy as a group of its own; edit the line to put several enemies in one group.
- **State preview** — for a placement whose object has states or an interaction with `self.` flags, the inspector lists those flags as check boxes. Ticking a flag shows the state that applies to it, chosen by the game's own rule. It is a preview only: the values are kept while the window is open, nothing is saved and the game's flags do not change. The canvas still draws the object's own look: the sprite does not switch with the state.

![The village map of rpg_plus_game: the inspector lists its teleport links, outgoing (→, with the target map and cell) and incoming (←).](assets/rpg-editor-village.jpg)

![A placement of the elder's house selected with the Select tool: the barrel is filled in yellow, and the inspector shows its object, its id, that it is this map's own placement, its cell and the Remove button.](assets/rpg-editor-placement.jpg)

## Saving and whose files change

- **Save** (Ctrl+S) validates the map and its placements first. A problem stops the save, and the status line names it. Otherwise the editor writes only the files that changed, in the mod you chose.
- A new map, made with **+** (16 × 12 cells, spawn at 1,1), is written on its first save together with its scene. After that the game knows the map at once.
- You can open the map of a mod you depend on. Painting that map is written as strokes in a patch of your mod; the owner's map file stays byte for byte the same.
- Placements are not added to a map of another mod. Only mods installed as folders can be saved to.
- **Play from here** (in the toolbar, next to Save) starts the running game on the open map at the cell you clicked last (the spawn point before any click). Unsaved changes are saved first, because the game plays the saved map. It works while an RPG game is running, for example when the editor is opened from the in-game toolbar.

## Deferred

These are not in this version, so do not look for them:

- renaming a placement or a map id with the references updated;
- rectangle selection, copy and paste of placements;
- opening a placed object in the RPG Object Editor from the map;
- placement-level states and variants (the state preview reads the object's own states; placement overrides and variants are not edited here);
- painting the effect masks of an atmosphere on the map — open the Effects Editor for the map's look, the masks are not edited here;
- **Interaction**, pickup: **Place on map** in the Items Editor puts the pickup object on the map; the pickup gives its item once the placement has the interaction **Gives an item** (set it in the inspector).

## If something goes wrong

1. **The map is not in the file list.** It is neither in the chosen mod nor in one of its dependencies.
2. **Save does nothing and the status line shows a problem.** A map or a placement is invalid; fix the cell or the value the line names.
3. **The game does not know a new map.** Save first: the map and its scene are written on the first save only.
4. **A placement is refused.** The object is not a sprite or an actor, the cell is outside the map, or the map belongs to another mod.
5. **There is no encounters panel.** The game has no random battles (`rpg__battles`).
