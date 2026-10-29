using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests
{
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    /// <summary>
    /// Drives the RPG map editor through its controls like an author: layer
    /// buttons are pressed, the palette and the canvas get mouse events through
    /// their <c>gui_input</c> signal, Ctrl+Z goes into the window, a slime found in
    /// the characters tab is placed as an enemy and branches from the items tab as
    /// a pickup. The appearance tab exposes all NPC artwork, places an elder without
    /// a combat entity and supports undo/redo. That window is
    /// freed without saving, so the demo maps never change. A second window then
    /// creates a new map through the file selector's "+" and saves it: the game must
    /// know the map's scene and its event (saved apart, into map_events) at once,
    /// with no mod reload, including a seer placed from the appearance tab; its files are deleted again.
    /// Result line: <c>[TEST_RESULT] name=rpg_editor_edit passed=N failed=M</c>.
    /// </summary>
    public partial class RpgEditorTestDriver : Node
    {
        private const float TickSeconds = 0.15f;
        private const float TimeoutSeconds = 60f;
        private const int RockTile = 11;
        private const string NewMap = "zz_editor_test_map";

        private static readonly Vector2I PaintCell = new(3, 5);
        private static readonly Vector2I EventCell = new(2, 6);
        private static readonly Vector2I CharacterCell = new(4, 6);
        private static readonly Vector2I ItemCell = new(5, 6);

        private readonly string shotsDir;
        private readonly List<string> failures = new();
        private int passed;
        private int step;
        private double elapsed;
        private RpgMapEditorWindow window;
        private RpgMapEditorCanvas canvas;
        private int eventsBefore;
        private string newMapMod;
        private Texture2D closingPreview;
        private Texture2D closingSource;

        /// <summary>Creates the driver; <paramref name="shotsDir"/> null disables screenshots.</summary>
        public RpgEditorTestDriver(string shotsDir)
        {
            this.shotsDir = shotsDir;
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
                    // Dialogs first: the rpg_plus prologue runs before any map is shown.
                    var box = RpgInput.FindDialogBox();
                    if (box != null && box.IsRunning)
                    {
                        if (box.IsWaitingAnswer)
                            box.TrySelectVariant(0);
                        else
                            box.NextAction();
                        return;
                    }
                    if (RpgWorldHost.Current?.View == null)
                        return;
                    window = ToolWindow.Open<RpgMapEditorWindow>();
                    step++;
                    return;
                case 1:
                    canvas = window.FindChildren("*", "", true, false).OfType<RpgMapEditorCanvas>().FirstOrDefault();
                    if (canvas?.Document == null || canvas.Tileset == null)
                        return;
                    Check(canvas.Document.Map.ID.StartsWith("rpg_"), $"the editor opened a demo map ({canvas.Document.Map.ID})");
                    var allEvents = canvas.Document.Events.Concat(canvas.Document.LockedEvents).ToList();
                    Check(canvas.Document.Events.Count > 0
                        && canvas.Document.Events.All(e => e.ModId == canvas.Document.EventsModId && e.File == canvas.Document.EventsFile)
                        && canvas.Document.LockedEvents.All(e => e.ModId != canvas.Document.EventsModId || e.File != canvas.Document.EventsFile)
                        && allEvents.Select(e => e.Id).ToHashSet().SetEquals(RpgMapEvents.Of(canvas.Document.Map.ID).Select(e => e.Id)),
                        $"events retain their source ownership ({canvas.Document.Events.Count} own, {canvas.Document.LockedEvents.Count} locked)");
                    Shot("editor");
                    Press("tools/rpg_editor/layer_main");
                    Check(canvas.ActiveLayer == RpgEditLayer.Main, "the layer button switches the brush to the main layer");
                    ClickPalette(RockTile);
                    Check(canvas.SelectedTile == RockTile, $"a palette click picks tile {RockTile}, got {canvas.SelectedTile}");
                    step++;
                    return;
                case 2:
                    var before = canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X, PaintCell.Y);
                    ClickCanvas(PaintCell, false);
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X, PaintCell.Y) == RockTile, "a canvas click paints the tile");
                    Check(window.Title.StartsWith("* "), "the window is marked dirty");
                    window.PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, CtrlPressed = true, Pressed = true });
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X, PaintCell.Y) == before, "Ctrl+Z undoes the stroke");
                    PrepareStampPreview();
                    step = 20;
                    return;
                case 20:
                    Shot("stamp_preview");
                    PlaceStampAndUndo();
                    Press("tools/rpg_editor/layer_events");
                    eventsBefore = canvas.Document.Events.Count;
                    ClickCanvas(EventCell, true);
                    Check(canvas.Document.Events.Count == eventsBefore + 1 && canvas.SelectedEvent != null, "a double click adds and selects an event");
                    step = 3;
                    return;
                case 3:
                    // Screenshots one tick after the action, when the frame shows it.
                    Shot("events");
                    PlaceSlime();
                    PlaceAppearance();
                    Press("tools/rpg_editor/layer_collision");
                    step++;
                    return;
                case 4:
                    Shot("collision");
                    Shot("appearances");
                    closingPreview = window.FindChildren("*", "", true, false).OfType<RpgEntityPicker>().First(p => p.Name == "Appearances").Selected?.Icon;
                    closingSource = (closingPreview as AtlasTexture)?.Atlas;
                    window.QueueFree();
                    window = ToolWindow.Open<RpgMapEditorWindow>();
                    step++;
                    return;
                case 5:
                    canvas = window.FindChildren("*", "", true, false).OfType<RpgMapEditorCanvas>().FirstOrDefault();
                    if (canvas?.Document == null)
                        return;
                    Check(closingPreview != null && !GodotObject.IsInstanceValid(closingPreview), "closing the editor releases the picker preview");
                    Check(closingSource != null && !GodotObject.IsInstanceValid(closingSource), "closing the editor releases the source texture cache");
                    newMapMod = Selector().CurrentMod;
                    DeleteNewMapFiles();
                    Selector().FindChildren("*", "", true, false).OfType<Button>().First(b => b.Text == "+").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    return;
                case 6:
                    var dialog = window.FindChildren("*", "", true, false).OfType<ConfirmationDialog>().FirstOrDefault();
                    if (dialog == null)
                        return;
                    dialog.FindChildren("*", "", true, false).OfType<LineEdit>().First().Text = NewMap;
                    dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    step++;
                    return;
                case 7:
                    if (canvas.Document?.Map?.ID != NewMap)
                        return;
                    try
                    {
                        canvas.Document.AddEvent(1, 1).Sprite = "village/elder";
                        var appearance = Pick("Appearances", 3, "seer", "rpg_plus/seer");
                        if (appearance != null)
                        {
                            Press("tools/rpg_editor/place_appearance");
                            ClickCanvas(new Vector2I(2, 2), false);
                        }
                        Press("tools/common/save");
                        Check(File.Exists(ModFileService.GetWritablePath(newMapMod, "scenes/" + NewMap + ".json")), "the first save of a new map writes its scene");
                        Check(!ModFileService.ReadText(newMapMod, "maps/" + NewMap + ".json").Contains("\"Events\""), "the map file holds no events");
                        Check(RpgMapEvents.Of(NewMap).Any(e => e.Id == "event_1" && e.ModId == newMapMod && e.File == RpgMapDocument.EventsPath(NewMap)),
                            "the event is saved into map_events and the game reads it at once");
                        Check(RpgMapEvents.Of(NewMap).Any(e => e.Sprite == "rpg_plus/seer" && e.X == 2 && e.Y == 2 && (e.Commands == null || e.Commands.Count == 0)),
                            "an appearance without a character entity saves and reloads as an NPC");
                        Check(GetTree().Root.GetNodeOrNull<SceneManager>("SceneManager")?.HasScene(NewMap) == true, "the game knows the new map's scene without a mod reload");
                    }
                    finally
                    {
                        window.QueueFree();
                        DeleteNewMapFiles();
                        SceneManager.ReloadContent();
                    }
                    Finish();
                    return;
            }
        }

        /// <summary>Characters tab: search "slime", pick it, "place as enemy", click a cell; items tab: two bundles of branches.</summary>
        private void PlaceSlime()
        {
            var picker = Pick("Characters", 1, "slime", "rpg_slime");
            if (picker == null)
                return;
            Check(picker.Selected.Look == "village/slime" && picker.Selected.Icon != null, $"the slime is picked with its charset ({picker.Selected.Look})");

            var count = canvas.Document.Events.Count;
            Press("tools/rpg_editor/place_enemy");
            Check(canvas.Placing && canvas.ActiveLayer == RpgEditLayer.Events, "placing waits for a cell on the events layer");
            ClickCanvas(CharacterCell, false);
            var placed = canvas.SelectedEvent;
            Check(canvas.Document.Events.Count == count + 1 && placed?.Sprite == "village/slime" && placed.X == CharacterCell.X && placed.Y == CharacterCell.Y,
                "a click on the map puts the slime there");
            Check(placed?.Commands?.FirstOrDefault()?.StartsWith("action rpg.battle enemies=rpg_slime ") == true && !canvas.Placing, "the placed enemy starts a battle with it");

            picker = Pick("Items", 2, "branches", "rpg_demo/branches");
            if (picker == null)
                return;
            Check(picker.Selected.Look == "rpg_demo/branch_bundle" && picker.Selected.Icon != null, $"the branches are picked with their picture ({picker.Selected.Look})");
            var countSpin = picker.FindChildren("*", "", true, false).OfType<SpinBox>().First();
            countSpin.Value = 2;
            Press("tools/rpg_editor/place_item");
            ClickCanvas(ItemCell, false);
            placed = canvas.SelectedEvent;
            Check(placed?.Icon == "rpg_demo/branch_bundle" && placed.X == ItemCell.X && placed.Trigger == RpgEventTrigger.Touch && placed.Solid == false,
                "a click on the map lays the branches there, picked up by stepping on them");
            Check(placed?.Commands?.Contains("action item.add id=rpg_demo/branches count=2") == true, "the pickup gives the chosen count");
        }

        /// <summary>Opens the side tab, types the query, checks every row matches it and selects <paramref name="id"/>; null when it is not listed.</summary>
        private RpgEntityPicker Pick(string name, int tab, string query, string id)
        {
            window.FindChildren("*", "", true, false).OfType<TabContainer>().First(t => t.GetNodeOrNull<RpgEntityPicker>("Characters") != null).CurrentTab = tab;
            var picker = window.FindChildren("*", "", true, false).OfType<RpgEntityPicker>().First(p => p.Name == name);
            var search = picker.FindChildren("*", "", true, false).OfType<LineEdit>().First(e => e.GetParent() == picker);
            search.Text = query;
            search.EmitSignal(LineEdit.SignalName.TextChanged, query);
            var list = picker.FindChildren("*", "", true, false).OfType<ItemList>().First();
            Check(list.ItemCount >= 1 && Enumerable.Range(0, list.ItemCount).All(i => list.GetItemText(i).Contains(query, System.StringComparison.OrdinalIgnoreCase)),
                $"the {name} search keeps only '{query}' ({list.ItemCount} rows)");
            for (var i = 0; i < list.ItemCount; i++)
            {
                if (!list.GetItemText(i).EndsWith("·  " + id))
                    continue;
                list.Select(i);
                list.EmitSignal(ItemList.SignalName.ItemSelected, i);
                return picker;
            }
            Fail($"'{id}' is not in the {name} list");
            return null;
        }

        private void PlaceAppearance()
        {
            var required = new[] { "village/hero", "village/slime", "village/wolf", "village/elder", "village/merchant", "village/villager", "rpg_plus/trainer", "rpg_plus/seer" };
            var expected = Files.Collect(RpgTextures.CharsetGroup).Keys
                .Where(path => ModConfig.Instance.GetTextureExtensionSet().Contains(Path.GetExtension(path)))
                .Select(path => Path.ChangeExtension(path, null)).ToHashSet(System.StringComparer.OrdinalIgnoreCase);
            using var appearanceTextures = new RpgTextures();
            var entries = RpgAppearanceCatalog.Load(appearanceTextures);
            Check(expected.IsSupersetOf(required) && entries.Count == expected.Count
                && expected.SetEquals(entries.Select(e => e.Look)) && entries.All(e => e.Icon != null),
                "all loaded appearances have previews, including the eight required NPC and character looks");
            foreach (var entry in entries.Where(e => e.OwnsIcon))
                entry.Icon?.Dispose();
            var picker = Pick("Appearances", 3, "elder", "village/elder");
            if (picker == null) return;
            var count = canvas.Document.Events.Count;
            Press("tools/rpg_editor/place_appearance");
            ClickCanvas(new Vector2I(6, 6), false);
            var placed = canvas.SelectedEvent;
            Check(canvas.Document.Events.Count == count+1 && placed?.Sprite == "village/elder" && placed.X == 6 && placed.Y == 6,
                "the elder appearance is placed through the picker and canvas");
            Check(placed?.Id.StartsWith("npc_") == true && placed.Scenario == null && placed.Condition == null && (placed.Commands == null || placed.Commands.Count == 0),
                "a visual NPC has a safe event id and no fabricated battle enemy");
            window.PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, CtrlPressed = true, Pressed = true });
            Check(canvas.Document.Events.Count == count, "undo removes the appearance placement");
            window.PushInput(new InputEventKey { Keycode = Key.Y, PhysicalKeycode = Key.Y, CtrlPressed = true, Pressed = true });
            Check(canvas.Document.Events.Count == count+1 && canvas.Document.Events.Any(e => e.Sprite == "village/elder" && e.X == 6 && e.Y == 6),
                "redo restores the appearance placement");
            Pick("Appearances", 3, "", "village/elder");
            var previousPreview = picker.Selected?.Icon;
            picker.Reload();
            Check(previousPreview != null && !GodotObject.IsInstanceValid(previousPreview) && picker.Selected?.Id == "village/elder" && GodotObject.IsInstanceValid(picker.Selected?.Icon),
                "reloading releases the old preview and preserves a valid selected appearance");
            var rows = picker.FindChildren("*", "", true, false).OfType<ItemList>().First();
            Check(rows.ItemCount == expected.Count
                && Enumerable.Range(0, rows.ItemCount).Select(i => rows.GetItemText(i)).Distinct().Count() == expected.Count,
                "reloading the appearance tab keeps every loaded row without duplicates");
        }

        private ModFileSelector Selector() => window.FindChildren("*", "", true, false).OfType<ModFileSelector>().First();

        private void DeleteNewMapFiles()
        {
            foreach (var file in new[] { "maps/" + NewMap + ".json", "scenes/" + NewMap + ".json", RpgMapDocument.EventsPath(NewMap) })
            {
                var path = ModFileService.GetWritablePath(newMapMod, file);
                if (path != null && File.Exists(path))
                    File.Delete(path);
            }
            var eventsDir = Path.GetDirectoryName(ModFileService.GetWritablePath(newMapMod, RpgMapDocument.EventsPath(NewMap)) ?? "");
            if (!string.IsNullOrEmpty(eventsDir) && Directory.Exists(eventsDir) && !Directory.EnumerateFileSystemEntries(eventsDir).Any())
                Directory.Delete(eventsDir);
            RpgMapEvents.Invalidate();
        }

        private void Press(string key)
        {
            var text = TranslationServer.Translate(key);
            var button = window.FindChildren("*", "", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text);
            if (button == null)
            {
                Fail($"button '{text}' not found");
                return;
            }
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private void ClickPalette(int index)
        {
            var palette = window.FindChildren("*", "", true, false).OfType<RpgTilePalette>().First();
            var tileset = canvas.Tileset;
            var cell = palette.Size.X / tileset.Columns;
            var position = ((Vector2)RpgTileSetBuilder.AtlasCoords(tileset, index) + new Vector2(0.5f, 0.5f)) * cell;
            palette.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position });
            palette.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
        }

        private void PrepareStampPreview()
        {
            var palette = window.FindChildren("*", "", true, false).OfType<RpgTilePalette>().First();
            var cell = palette.Size.X / canvas.Tileset.Columns;
            var start = ((Vector2)RpgTileSetBuilder.AtlasCoords(canvas.Tileset, 64) + new Vector2(0.5f, 0.5f)) * cell;
            var end = start + new Vector2(cell, cell);
            palette.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = start });
            palette.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = end, ButtonMask = MouseButtonMask.Left });
            palette.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = end });
            Check(canvas.SelectedStamp?.Width == 2 && canvas.SelectedStamp.Height == 2, "dragging the palette selects a two-by-two brush");
            Check(canvas.SelectedStamp?.TileAt(1, 1) == 68, "the selected rectangle preserves object tile arrangement");
            var size = canvas.Tileset.TileSize * canvas.Zoom;
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = canvas.ProjectCell((Vector2)PaintCell + new Vector2(0.5f, 0.5f)) });
        }

        private void PlaceStampAndUndo()
        {
            var before = canvas.Document.ToJson();
            ClickCanvas(PaintCell, false);
            Check(canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X, PaintCell.Y) == 64
                && canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X + 1, PaintCell.Y) == 65
                && canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X, PaintCell.Y + 1) == 67
                && canvas.Document.Grid.GetTile(RpgLayer.Main, PaintCell.X + 1, PaintCell.Y + 1) == 68,
                "one canvas click places every cell of the rectangular brush");
            window.PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z, CtrlPressed = true, Pressed = true });
            Check(canvas.Document.ToJson() == before, "one undo restores the whole map before the group stamp");
        }

        private void ClickCanvas(Vector2I cell, bool doubleClick)
        {
            var size = canvas.Tileset.TileSize * canvas.Zoom;
            var position = canvas.ProjectCell(new Vector2(cell.X + 0.5f, cell.Y + 0.5f));
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, DoubleClick = doubleClick, Position = position });
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
        }

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

        private void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotsDir) || DisplayServer.GetName() == "headless")
                return;
            DirAccess.MakeDirRecursiveAbsolute(shotsDir);
            ViewportCapture.SavePng(GetViewport(), shotsDir.TrimEnd('/') + "/rpg_editor_" + name + ".png");
        }

        private async void Finish()
        {
            if (step < 0)
                return;
            step = -1;
            if (GodotObject.IsInstanceValid(window) && !window.IsQueuedForDeletion())
                window.QueueFree();
            // Allow queued windows and their render resources to leave the tree before quitting.
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=rpg_editor_edit passed={passed} failed={failures.Count}");
            Cthangover.Core.Bootstrap.ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
