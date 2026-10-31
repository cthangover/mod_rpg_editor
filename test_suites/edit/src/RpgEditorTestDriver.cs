using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Settings;
using Cthangover.Core.Utils;
using Cthangover.Mods.Effects.Atmosphere;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.TileRender;
using Cthangover.Tools.Common;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests
{
    /// <summary>
    /// Drives the RPG map editor like an author, through its canvas input, its keys and its inspector controls: the palette by shape,
    /// the projection toggle during editing, a surface and a segment painted, a sprite placed, moved by a drag and deleted, Ctrl+Z and
    /// Ctrl+Y, a collision cell, the spawn point. Then a new map is made with "+" and saved, and the game knows its map and scene at
    /// once. Then the interactions (plan 14.G2, 14.G3, 14.G5): a chest that gives an item by the item picker, a door that leads to the
    /// demo map on a cell picked on that map (the window opens it, the click writes the target and returns), a broken target drawn red and
    /// drawn fine again, an NPC with a dialog and then a shop by the pickers. The map is saved, and the game plays it: the chest gives the
    /// item, the door teleports. The demo map of rpg_plus_game is never saved, and every file of the mod is compared with its text from
    /// the start afterwards; the new map's files are deleted.
    /// Result line: <c>[TEST_RESULT] name=rpg_editor_edit passed=N failed=M</c>.
    /// </summary>
    public partial class RpgEditorTestDriver : Node
    {
        private const float TickSeconds = 0.15f;
        private const float TimeoutSeconds = 240f;
        private const string Mod = "rpg_plus_game";
        private const string DemoMap = "rpg_elder_house";
        private const string NewMap = "zz_editor_test_map";
        private const string NewMapPath = "maps/zz_editor_test_map.json";
        private const string NewScenePath = "scenes/zz_editor_test_map.json";
        private const string NewPlacementPath = "placements/zz_editor_test_map/zz_editor_test_map.json";
        private const string Grass = "village_pack/ground/grass";
        private const string Water = "village_pack/ground/water";
        private const string Fence = "village_pack/structure/fence_wood";
        private const string Chest = "village_pack/chest";
        private const string Trigger = "village_pack/marker/trigger";
        private const string Npc = "village_pack/npc/merchant";
        private const string Elixir = "rpg_plus/elixir";
        private const string Elder = "rpg_plus/woodland/elder";
        private const string Shop = "rpg_plus/village_merchant";
        private const int ElixirCount = 2;

        private static readonly Vector2I PaintCell = new(2, 2);
        private static readonly Vector2I FenceCell = new(4, 4);
        private static readonly Vector2I PlaceCell = new(7, 3);
        private static readonly Vector2I MoveCell = new(8, 3);
        private static readonly Vector2I WallCell = new(5, 5);
        private static readonly Vector2I SpawnCell = new(6, 7);
        private static readonly Vector2I ChestCell = new(4, 1);
        private static readonly Vector2I DoorCell = new(8, 1);
        private static readonly Vector2I NpcCell = new(10, 5);
        private static readonly Vector2I StandBeforeChest = new(3, 1);
        private static readonly Vector2I StandBeforeDoor = new(7, 1);
        private static readonly Vector2I PlayCell = new(1, 1);
        private static readonly Vector2I[] MaskCells = { new(12, 8), new(13, 8), new(12, 9), new(13, 9) };

        private const int KindNone = 0;
        private const int KindItem = 1;
        private const int KindMap = 2;
        private const int KindDialog = 3;
        private const int KindShop = 4;

        private readonly List<string> failures = new();
        private readonly Dictionary<string, string> before = new();
        private int passed;
        private int step;
        private double elapsed;
        private RpgEditorWindow window;
        private string placedId;
        private string sprite;
        private string paintBefore;
        private string codeBefore;
        private string spawnBefore;
        private string chestId;
        private string doorId;
        private string npcId;
        private int elixirBefore;
        private List<string> pastedIds = new();
        private string maskProfile;
        private int demoSpawnX;
        private int demoSpawnY;

        /// <summary>Creates the driver; <paramref name="shotsDir"/> is unused (no screenshots).</summary>
        public RpgEditorTestDriver(string shotsDir)
        {
            Name = "RpgEditorTestDriver";
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private void Tick()
        {
            if (step < 0)
                return;
            elapsed += TickSeconds;
            if (elapsed > TimeoutSeconds)
            {
                Fail($"timeout at step {step}");
                Finish();
                return;
            }
            try
            {
                RunStep();
            }
            catch (System.Exception ex)
            {
                Fail($"step {step}: {ex.GetType().Name}: {ex.Message}");
                Finish();
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || string.IsNullOrEmpty(GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager")?.CurrentSceneName))
                        return;
                    // The prologue dialogs run first: the map editor opens once the rpg world is on screen.
                    if (AdvanceDialog())
                        return;
                    if (RpgWorldHost.Current?.View == null)
                        return;
                    foreach (var path in ModFileService.ListFiles(Mod, string.Empty, ".json"))
                        before[path] = ModFileService.ReadText(Mod, path);
                    window = ToolWindow.Open<RpgEditorWindow>();
                    step++;
                    return;
                case 1:
                    if (window.Document == null)
                        return;
                    window.OpenMapFile(Mod, "maps/" + DemoMap + ".json");
                    var demo = window.Document;
                    Check(demo != null && demo.MapId == DemoMap && !demo.IsForeign, $"the editor opens the demo map {DemoMap} of {Mod}");
                    Check(window.Palette.Count(RpgObjectForm.Surface) > 0 && window.Palette.Count(RpgObjectForm.Segment) > 0
                        && window.Palette.Count(RpgObjectForm.Sprite) > 0 && window.Palette.Count(RpgObjectForm.Actor) > 0,
                        "the palette lists surfaces, segments, sprites and actors");
                    Check(window.EncountersShown, "the encounters panel is shown: rpg__battles is mounted in this game");
                    var renderer = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--editor-renderer=", System.StringComparison.Ordinal));
                    if (renderer == "--editor-renderer=isometric")
                        window.SetProjection(RpgEditorCanvas.Isometric);
                    sprite = Entities.Collect<RpgObject>().Values
                        .FirstOrDefault(o => o.Form == RpgObjectForm.Sprite && o.Parts is { Count: > 0 } && o.ID.StartsWith("village_pack/", System.StringComparison.Ordinal))?.ID;
                    Check(sprite != null, "a village sprite is there to place");
                    step++;
                    return;
                case 2:
                    window.SetProjection(RpgEditorCanvas.Isometric);
                    Check(window.Canvas.Projection == RpgEditorCanvas.Isometric && window.Canvas.Scene != null, "the isometric projection shows the map");
                    window.SetProjection(RpgEditorCanvas.TopDown);
                    Check(window.Canvas.Projection == RpgEditorCanvas.TopDown && window.Canvas.Scene != null, "switching back to the top-down projection keeps the map");
                    step++;
                    return;
                case 3:
                    paintBefore = window.Document.ObjectAt(RpgStrokeLayer.Ground, PaintCell.X, PaintCell.Y);
                    window.ChooseObject(Water, RpgObjectForm.Surface);
                    Check(window.Tool == RpgEditorTool.Paint, "a surface in the palette selects the paint tool");
                    Click(PaintCell);
                    Check(window.Document.ObjectAt(RpgStrokeLayer.Ground, PaintCell.X, PaintCell.Y) == Water, "a click paints the surface on the ground");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(window.Document.ObjectAt(RpgStrokeLayer.Ground, PaintCell.X, PaintCell.Y) == paintBefore, "Ctrl+Z takes the paint back");
                    window.PushInput(KeyEvent(Key.Y, true));
                    Check(window.Document.ObjectAt(RpgStrokeLayer.Ground, PaintCell.X, PaintCell.Y) == Water, "Ctrl+Y puts it back");
                    window.Undo();
                    step++;
                    return;
                case 4:
                    window.ChooseObject(Fence, RpgObjectForm.Segment);
                    Click(FenceCell);
                    Check(window.Document.ObjectAt(RpgStrokeLayer.Structure, FenceCell.X, FenceCell.Y) == Fence, "a segment paints the structure layer");
                    window.Undo();
                    step++;
                    return;
                case 5:
                    window.ChooseObject(sprite, RpgObjectForm.Sprite);
                    Check(window.Tool == RpgEditorTool.Place, "a sprite selects the place tool");
                    var count = window.Document.Placements.Count;
                    Click(PlaceCell);
                    placedId = window.SelectedId;
                    Check(placedId != null && window.Document.Placements.Count == count + 1, "a click places the sprite and selects it");
                    Check(window.Document.Placements.Any(r => r.Id == placedId && r.Data["X"].GetValue<int>() == PlaceCell.X && r.Data["Y"].GetValue<int>() == PlaceCell.Y),
                        "the placement is on the cell clicked");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(PlacementOf(placedId) == null, "Ctrl+Z removes the new placement");
                    window.PushInput(KeyEvent(Key.Y, true));
                    Check(PlacementOf(placedId) != null, "Ctrl+Y places it again");
                    step++;
                    return;
                case 6:
                    window.SelectTool(RpgEditorTool.Select);
                    Drag(PlaceCell, MoveCell);
                    Check(window.SelectedId == placedId, "a press selects the placement under the cell");
                    var moved = PlacementOf(placedId);
                    Check(moved != null && moved.Data["X"].GetValue<int>() == MoveCell.X && moved.Data["Y"].GetValue<int>() == MoveCell.Y, "a drag moves it to the cell released on");
                    step++;
                    return;
                case 7:
                    window.PushInput(KeyEvent(Key.Delete, false));
                    Check(PlacementOf(placedId) == null, "Delete removes the selected placement");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(PlacementOf(placedId) != null, "Ctrl+Z brings it back");
                    step++;
                    return;
                case 8:
                    codeBefore = window.Document.CodeAt(WallCell.X, WallCell.Y).ToString();
                    window.SetCollisionCode(RpgCollision.Block);
                    window.SelectTool(RpgEditorTool.Collision);
                    Click(WallCell);
                    Check(window.Document.CodeAt(WallCell.X, WallCell.Y) == RpgCollision.Block, "the collision tool paints a blocked cell");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(window.Document.CodeAt(WallCell.X, WallCell.Y).ToString() == codeBefore, "Ctrl+Z restores the collision code");
                    step++;
                    return;
                case 9:
                    spawnBefore = window.Document.Data["Spawn"]?["Y"]?.ToString();
                    window.SelectTool(RpgEditorTool.Spawn);
                    Click(SpawnCell);
                    Check(window.Document.Data["Spawn"]?["Y"]?.ToString() == SpawnCell.Y.ToString() && window.Document.Data["Spawn"]?["X"]?.ToString() == SpawnCell.X.ToString(),
                        "the spawn tool moves the spawn point to the cell");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(window.Document.Data["Spawn"]?["Y"]?.ToString() == spawnBefore, "Ctrl+Z gives the spawn point back");
                    // Demo files are never saved: the window is closed without saving and opened again on the new map.
                    window.QueueFree();
                    window = ToolWindow.Open<RpgEditorWindow>();
                    step++;
                    return;
                case 10:
                    if (window.Document == null)
                        return;
                    window.OpenMapFile(Mod, NewMapPath);
                    Check(window.Document != null && window.Document.MapId == NewMap, "+ makes a new map (the file does not exist yet)");
                    window.ChooseObject(Grass, RpgObjectForm.Surface);
                    Click(new Vector2I(1, 1));
                    Check(window.Document.ObjectAt(RpgStrokeLayer.Ground, 1, 1) == Grass, "the new map takes a surface");
                    Check(!File.Exists(ModFileService.GetWritablePath(Mod, NewMapPath) ?? "-"), "nothing is written before the save");
                    step++;
                    return;
                case 11:
                    Check(window.Save(), "the first save of the new map succeeds");
                    var mapText = ModFileService.ReadText(Mod, NewMapPath) ?? string.Empty;
                    Check(mapText.Contains("\"Kind\": \"rpg_map\"") && !mapText.Contains("\r") && mapText.Contains(Grass), "the map file is written with its kind and surface, LF lines");
                    Check(ModFileService.ReadText(Mod, NewScenePath) != null, "the first save writes the map's scene");
                    Check(Entities.Collect<RpgMap>().ContainsKey(NewMap), "the game knows the new map at once");
                    Check(GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager")?.HasScene(NewMap) == true, "and its scene");
                    Check(ModFileService.ReadText(Mod, "maps/" + DemoMap + ".json") == before["maps/" + DemoMap + ".json"], "the demo map is byte-identical");
                    step = 12;
                    return;
                case 12:
                    // A chest on the new map, given an item by the pickers: the kind, the item, the count.
                    Check(window.Document.Data != null, "the new map is open for the interactions");
                    window.ChooseObject(Chest, RpgObjectForm.Sprite);
                    Click(ChestCell);
                    chestId = window.SelectedId;
                    Check(chestId != null && window.Scene.ObjectOf(chestId) != null, "a chest is placed on the new map and selected");
                    Check(window.Document.Placements.Any(r => r.Id == chestId), "the chest is a placement of the map");
                    Choose(PickerNamed("InteractKind"), KindItem);
                    Check(window.Scene.ObjectOf(chestId)?.Interact?.Commands is { Count: 1 }, "the item kind writes the interaction");
                    Choose(PickerNamed("InteractItem"), IndexOfValue(PickerNamed("InteractItem"), Elixir));
                    SetSpin("InteractCount", ElixirCount);
                    var chestCommands = window.Scene.ObjectOf(chestId)?.Interact?.Commands;
                    Check(chestCommands is { Count: 1 } && chestCommands[0] == "action item.add id=" + Elixir + " count=" + ElixirCount,
                        "the item picker and the count write the command, the canvas reads it at once");
                    step++;
                    return;
                case 13:
                    // A door: a trigger placement that leads to another map, the teleport kind first (same-map pick comes after the save).
                    window.ChooseObject(Trigger, RpgObjectForm.Sprite);
                    Click(DoorCell);
                    doorId = window.SelectedId;
                    Choose(PickerNamed("InteractKind"), KindMap);
                    Check(window.Scene.ObjectOf(doorId)?.Interact?.Teleport != null, "the map kind makes the placement a teleport");
                    Check(window.Canvas.Arrows.Count >= 1, "the teleport is drawn as an arrow on the canvas");
                    step++;
                    return;
                case 14:
                    Check(window.Save(), "the save with the chest and the door succeeds");
                    step++;
                    return;
                case 15:
                    // The target cell is picked on the demo map: the map is chosen first (no write), then the pick opens it.
                    var mapChoice = PickerNamed("InteractMap");
                    mapChoice.Select(IndexOfValue(mapChoice, DemoMap));
                    NamedAs<Button>("InteractPick").EmitSignal(BaseButton.SignalName.Pressed);
                    Check(window.Document.MapId == DemoMap, "a pick on another map opens that map");
                    demoSpawnX = window.Document.Data["Spawn"]?["X"]?.GetValue<int>() ?? 0;
                    demoSpawnY = window.Document.Data["Spawn"]?["Y"]?.GetValue<int>() ?? 0;
                    step++;
                    return;
                case 16:
                    Click(new Vector2I(demoSpawnX, demoSpawnY));
                    Check(window.Document.MapId == NewMap, "a click on the demo map returns to the map the teleport is on");
                    var door = window.Scene.ObjectOf(doorId)?.Interact?.Teleport;
                    Check(door != null && door.Map == DemoMap && door.X == demoSpawnX && door.Y == demoSpawnY,
                        $"the door leads to the cell picked on {DemoMap}, ({demoSpawnX}, {demoSpawnY})");
                    Check(window.Document.Data != null && ModFileService.ReadText(Mod, "maps/" + DemoMap + ".json") == before["maps/" + DemoMap + ".json"],
                        "the demo map was not saved by the pick");
                    step++;
                    return;
                case 17:
                    Check(window.Save(), "the save of the door succeeds");
                    var placementText = ModFileService.ReadText(Mod, NewPlacementPath) ?? string.Empty;
                    Check(placementText.Contains("\"Map\": \"" + DemoMap + "\"") && placementText.Contains("\"Commands\""), "the placement file holds the teleport and the item command");
                    step++;
                    return;
                case 18:
                    // A broken target: a cell off the target map is drawn red; the same cell back on the map is fine again.
                    SetSpin("InteractX", 500);
                    Check(window.Canvas.Arrows.Any(a => a.Broken), "a target off its map is drawn red");
                    SetSpin("InteractX", demoSpawnX);
                    Check(!window.Canvas.Arrows.Any(a => a.Broken), "the same target on the map is no longer red");
                    step++;
                    return;
                case 19:
                    // An NPC: a dialog by the scenario picker, then a shop by the shop picker, then nothing again.
                    window.ChooseObject(Npc, RpgObjectForm.Actor);
                    Click(NpcCell);
                    npcId = window.SelectedId;
                    Choose(PickerNamed("InteractKind"), KindDialog);
                    Choose(PickerNamed("InteractScenario"), IndexOfValue(PickerNamed("InteractScenario"), Elder));
                    Check(window.Scene.ObjectOf(npcId)?.Interact?.Scenario == Elder, "the dialog picker sets the NPC's scenario");
                    step++;
                    return;
                case 20:
                    Choose(PickerNamed("InteractKind"), KindShop);
                    Choose(PickerNamed("InteractShop"), IndexOfValue(PickerNamed("InteractShop"), Shop));
                    var shopCommands = window.Scene.ObjectOf(npcId)?.Interact?.Commands;
                    Check(shopCommands is { Count: 1 } && shopCommands[0] == "action trade.open shop=" + Shop && window.Scene.ObjectOf(npcId)?.Interact?.Scenario == null,
                        "the shop picker opens the shop, and the dialog is replaced");
                    Choose(PickerNamed("InteractKind"), KindNone);
                    Check(window.Scene.ObjectOf(npcId)?.Interact == null, "the kind none leaves the NPC without an interaction");
                    step++;
                    return;
                case 21:
                    Check(window.Save(), "the save of the map succeeds");
                    step = 30;
                    return;
                case 30:
                    // Plan 14.G9: a box over the chest and the door marks exactly those two.
                    window.SelectTool(RpgEditorTool.Select);
                    Drag(new Vector2I(3, 0), new Vector2I(9, 2));
                    var boxed = window.Marked;
                    Check(boxed.Count == 2 && boxed.Contains(chestId) && boxed.Contains(doorId), $"a box over the chest and the door marks both (got {boxed.Count})");
                    step++;
                    return;
                case 31:
                    // Ctrl+C, then Ctrl+V with the pointer over (2, 4): the copies land there with their offsets, and are marked.
                    window.PushInput(KeyEvent(Key.C, true));
                    Hover(new Vector2I(2, 4));
                    window.PushInput(KeyEvent(Key.V, true));
                    pastedIds = window.Marked.ToList();
                    Check(pastedIds.Count == 2, "Ctrl+V pastes both placements and marks the copies");
                    var pastedCells = pastedIds.Select(CellOfPlacement).ToHashSet();
                    Check(pastedCells.SetEquals(new[] { new Vector2I(2, 4), new Vector2I(6, 4) }), "the copies keep their offsets from the pointer cell");
                    step++;
                    return;
                case 32:
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(pastedIds.All(id => PlacementOf(id) == null) && window.Marked.Count == 0, "Ctrl+Z takes the paste back in one step");
                    window.PushInput(KeyEvent(Key.Y, true));
                    Check(pastedIds.All(id => PlacementOf(id) != null), "Ctrl+Y pastes them again");
                    step++;
                    return;
                case 33:
                    // Plan 14.G10: the Effects tool paints the mask of a profile over four cells; the last one is undone and redone.
                    maskProfile = AtmosphereCatalog.Collect().Keys.OrderBy(k => k, System.StringComparer.Ordinal).First();
                    window.ShowMaskTool(maskProfile);
                    Check(window.Tool == RpgEditorTool.Effects && window.EffectProfile == maskProfile, "the Effects tool is chosen with a profile");
                    foreach (var cell in MaskCells)
                        Click(cell);
                    Check(MaskCount(window.Document, maskProfile) == MaskCells.Length, "four clicks paint four cells of the mask");
                    window.PushInput(KeyEvent(Key.Z, true));
                    Check(MaskCount(window.Document, maskProfile) == MaskCells.Length - 1, "Ctrl+Z takes the last cell back");
                    window.PushInput(KeyEvent(Key.Y, true));
                    Check(MaskCount(window.Document, maskProfile) == MaskCells.Length, "Ctrl+Y paints it again");
                    step++;
                    return;
                case 34:
                    Check(window.Save(), "the save with the mask succeeds");
                    var masked = Entities.Collect<RpgMap>()[NewMap].Effects.FirstOrDefault(e => e.Profile == maskProfile);
                    Check(masked?.Cells != null && masked.Cells.Count == MaskCells.Length && MaskCells.All(c => masked.Cells.Any(p => p[0] == c.X && p[1] == c.Y)),
                        "the game reads the mask of the saved map at once (the renderer draws it from the same map data)");
                    Click(MaskCells[0], MouseButton.Right);
                    Check(MaskCount(window.Document, maskProfile) == MaskCells.Length - 1, "the right button erases one cell");
                    Check(window.Save(), "the save after the erase succeeds");
                    step++;
                    return;
                case 35:
                    var erased = Entities.Collect<RpgMap>()[NewMap].Effects.FirstOrDefault(e => e.Profile == maskProfile);
                    Check(erased?.Cells?.Count == MaskCells.Length - 1 && !erased.Cells.Any(p => p[0] == MaskCells[0].X && p[1] == MaskCells[0].Y),
                        "the game sees the erase: the erased cell is no longer in the mask");
                    // Plan 14.G1 (button): the Play button enters the map at the last clicked cell.
                    window.SelectTool(RpgEditorTool.Select);
                    Click(PlayCell);
                    step = 22;
                    return;
                case 22:
                    // "Play from here": the game enters the saved map at the last clicked cell; the editor's window is left to the game.
                    Check(PressButton("tools/rpg_editor/play"), "the Play button is on the toolbar");
                    window = null;
                    step++;
                    return;
                case 23:
                    if (RpgWorldHost.Current?.View?.Map?.ID != NewMap || GodotSceneService.IsTransitioning || AdvanceDialog())
                        return;
                    Check(RpgWorldHost.Current.View.Player.Cell == PlayCell, "Play from here entered the map on the clicked cell");
                    elixirBefore = GameData.Instance.Runtime.Inventory.CheckCount(Elixir);
                    RpgWorldHost.Current.View.PlacePlayer(StandBeforeChest.X, StandBeforeChest.Y, RpgDirection.Right);
                    Check(RpgWorldHost.Current.View.Interact(), "in the game, using the chest starts its interaction");
                    step++;
                    return;
                case 24:
                    if (AdvanceDialog())
                        return;
                    Check(GameData.Instance.Runtime.Inventory.CheckCount(Elixir) == elixirBefore + ElixirCount,
                        $"in the game, the chest gives the item picked, {ElixirCount} piece(s)");
                    RpgWorldHost.Current.View.PlacePlayer(StandBeforeDoor.X, StandBeforeDoor.Y, RpgDirection.Right);
                    Check(RpgWorldHost.Current.View.Interact(), "in the game, using the door starts its teleport");
                    step++;
                    return;
                case 25:
                    if (RpgWorldHost.Current?.View?.Map?.ID != DemoMap || GodotSceneService.IsTransitioning || AdvanceDialog())
                        return;
                    Check(RpgWorldHost.Current.View.Player.Cell == new Vector2I(demoSpawnX, demoSpawnY),
                        $"in the game, the door takes the player to the cell picked on {DemoMap}");
                    step = 26;
                    return;
                case 26:
                    try
                    {
                        // The editor window that Play from here left in the game (and any other still open) is closed.
                        foreach (var open in GetTree().Root.FindChildren("*", "", true, false).OfType<RpgEditorWindow>().ToList())
                            open.QueueFree();
                        foreach (var path in new[] { NewMapPath, NewScenePath, NewPlacementPath })
                        {
                            var full = ModFileService.GetWritablePath(Mod, path);
                            if (full != null && File.Exists(full))
                                File.Delete(full);
                        }
                        var changed = before.Where(f => ModFileService.ReadText(Mod, f.Key) != f.Value).Select(f => f.Key).ToList();
                        var added = ModFileService.ListFiles(Mod, string.Empty, ".json").Where(p => !before.ContainsKey(p)).ToList();
                        Check(changed.Count == 0 && added.Count == 0,
                            "every file of the mod is as it was (the new map's files are gone)"
                            + (changed.Count + added.Count == 0 ? string.Empty : $": changed [{string.Join(", ", changed)}], added [{string.Join(", ", added)}]"));
                        var newFolder = ModFileService.GetWritablePath(Mod, "placements/" + NewMap);
                        if (newFolder != null && Directory.Exists(newFolder) && Directory.GetFileSystemEntries(newFolder).Length == 0)
                            Directory.Delete(newFolder);
                        ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
                        ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
                        SceneManager.ReloadContent();
                    }
                    finally
                    {
                        Finish();
                    }
                    return;
            }
        }

        private EntityRecord PlacementOf(string id) => window.Document.Placements.FirstOrDefault(r => r.Id == id);

        /// <summary>A left click on a cell of the canvas: press and release through the canvas' own input.</summary>
        private void Click(Vector2I cell, MouseButton button = MouseButton.Left)
        {
            var at = window.Canvas.CanvasPointOf(cell);
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true, Position = at });
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = button, Pressed = false, Position = at });
        }

        /// <summary>Moves the pointer over a cell without pressing (the window's hover cell, which a paste uses).</summary>
        private void Hover(Vector2I cell)
            => window.Canvas._GuiInput(new InputEventMouseMotion { Position = window.Canvas.CanvasPointOf(cell) });

        /// <summary>The cell of a placement of the edited map.</summary>
        private Vector2I CellOfPlacement(string id)
        {
            var record = PlacementOf(id);
            return new Vector2I(record.Data["X"].GetValue<int>(), record.Data["Y"].GetValue<int>());
        }

        /// <summary>The cells of the mask of a profile in the open document (0 without the entry).</summary>
        private static int MaskCount(RpgMapDocument doc, string profile)
        {
            if (doc.Data?["Effects"] is not JsonArray entries)
                return 0;
            foreach (var node in entries)
            {
                if (node is JsonObject entry && entry["Profile"] is JsonValue name && name.TryGetValue(out string text) && text == profile)
                    return (entry["Cells"] as JsonArray)?.Count ?? 0;
            }
            return 0;
        }

        /// <summary>Presses the toolbar button of that text; false when there is none.</summary>
        private bool PressButton(string key)
        {
            var button = window?.FindChildren("*", "", true, false).OfType<Button>().FirstOrDefault(b => b.Text == ToolUi.Tr(key));
            if (button == null)
                return false;
            button.EmitSignal(BaseButton.SignalName.Pressed);
            return true;
        }

        /// <summary>A left drag from one cell to another: press, move, release.</summary>
        private void Drag(Vector2I from, Vector2I to)
        {
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window.Canvas.CanvasPointOf(from) });
            window.Canvas._GuiInput(new InputEventMouseMotion { Position = window.Canvas.CanvasPointOf(to), ButtonMask = MouseButtonMask.Left });
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = window.Canvas.CanvasPointOf(to) });
        }

        /// <summary>The inspector's picker of that name (the window rebuilds it on every change, so it is looked up each time).</summary>
        private OptionButton PickerNamed(string name) => NamedAs<OptionButton>(name);

        /// <summary>The window's control of that name and type, or null.</summary>
        private T NamedAs<T>(string name) where T : class => window.FindChild(name, true, false) as T;

        /// <summary>Chooses an item of a picker the way a click does: the selection and its signal.</summary>
        private static void Choose(OptionButton picker, int index)
        {
            picker.Select(index);
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }

        /// <summary>Index of the picker entry whose id (metadata) is <paramref name="value"/>, or -1.</summary>
        private static int IndexOfValue(OptionButton picker, string value)
        {
            for (var i = 0; i < picker.ItemCount; i++)
            {
                if (picker.GetItemMetadata(i).AsString() == value)
                    return i;
            }
            return -1;
        }

        /// <summary>Types a number into a spin box of the inspector: its value changes and the change is written.</summary>
        private void SetSpin(string name, double value)
        {
            var spin = NamedAs<SpinBox>(name);
            if (spin == null)
            {
                Fail($"the spin box {name} is shown");
                return;
            }
            spin.Value = value;
        }

        /// <summary>Plays the dialog box one press per tick (the first answer); true while it still runs.</summary>
        private static bool AdvanceDialog()
        {
            var box = RpgInput.FindDialogBox();
            if (box == null || !box.IsRunning)
                return false;
            if (box.IsWaitingAnswer)
                box.TrySelectVariant(0);
            else
                box.NextAction();
            return true;
        }

        private static InputEventKey KeyEvent(Key code, bool ctrl)
            => new() { Keycode = code, PhysicalKeycode = code, CtrlPressed = ctrl, Pressed = true };

        private void Check(bool condition, string what)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", $"[PASS] {what}");
            }
            else
            {
                Fail(what);
            }
        }

        private void Fail(string what)
        {
            failures.Add(what);
            GameLogger.Log("TEST", $"[FAIL] {what}", LogLevel.Error);
        }

        private void Finish()
        {
            step = -1;
            GameLogger.Log("TEST", $"[TEST_RESULT] name=rpg_editor_edit passed={passed} failed={failures.Count}");
            ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
