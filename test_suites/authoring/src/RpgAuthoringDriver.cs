namespace Cthangover.Tools.RpgEditor.Tests.Authoring
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.RpgTilesetEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Checks the author's complete UI path using isolated map/tileset fixtures and public controls.</summary>
    public partial class RpgAuthoringDriver : Node
    {
        private const string Mod = "rpg_editor__authoring";
        private const string MapFile = "maps/tools_authoring_map.json";
        private const string EventFile = "map_events/tools_authoring_map/barrels.json";
        private const string TilesetFile = "tilesets/authoring.json";
        private const string TilesetId = "tools_authoring_tileset";
        private readonly Dictionary<string, string> backups = new();
        private RpgMapEditorWindow editor;
        private RpgTilesetEditorWindow atlas;
        private RpgMapEditorCanvas canvas;
        private double elapsed;
        private double next;
        private int step;
        private int passed;
        private int failed;
        private bool done;

        public override void _Process(double delta)
        {
            if (done) return;
            elapsed += delta;
            if (elapsed > 90) { Check(false, "authoring watchdog at step " + step); Finish(); return; }
            if (!Boot.IsReady || elapsed < next) return;
            next = elapsed + 0.35;
            try { Step(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }

        private void Step()
        {
            switch (step++)
            {
                case 0:
                    foreach (var file in new[] { MapFile, EventFile, TilesetFile })
                        backups[file] = ModFileService.ReadText(Mod, file);
                    editor = ToolWindow.Open<RpgMapEditorWindow>();
                    break;
                case 1:
                    SelectFile(editor, "rpg_demo_game", "maps/rpg_village.json");
                    canvas = Find<RpgMapEditorCanvas>(editor);
                    Press(editor, "tools/rpg_editor/layer_events");
                    var barrel = canvas.Document.LockedEvents.First(e => e.Sprite == "desert/barrel_closed");
                    SelectEvent(barrel);
                    Check(canvas.SelectedEvent?.Id == barrel.Id, "a barrel in another mod can be selected");
                    var source = editor.FindChild("EditEventSource", true, false) as Button;
                    Check(source?.Visible == true, "source-file action is visible");
                    source.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(canvas.Document.EventsModId == barrel.ModId && canvas.Document.EventsFile == barrel.File,
                        "opens the barrel's owner and file instead of copying it into the map mod");
                    Check(canvas.Document.Events.Any(e => e.Id == barrel.Id), "source barrel becomes editable");
                    editor.Free(); editor = ToolWindow.Open<RpgMapEditorWindow>();
                    break;
                case 2:
                    SelectFile(editor, Mod, MapFile);
                    canvas = Find<RpgMapEditorCanvas>(editor);
                    Press(editor, "tools/rpg_editor/layer_events");
                    SelectEvent(canvas.Document.LockedEvents.Single());
                    (editor.FindChild("EditEventSource", true, false) as Button).EmitSignal(BaseButton.SignalName.Pressed);
                    var selected = canvas.SelectedEvent;
                    var stateSprite = editor.FindChild("StateSprite", true, false) as LineEdit;
                    stateSprite.Text = "desert/barrel_closed";
                    stateSprite.EmitSignal(LineEdit.SignalName.TextChanged, stateSprite.Text);
                    Check(selected.Pages[0].Sprite == "desert/barrel_closed", "open-state sprite is editable in its own form");
                    Press(editor, "tools/rpg_editor/undo");
                    Check(canvas.SelectedEvent.Pages[0].Sprite == "desert/barrel_open", "one undo restores the state");
                    var stateCommands = editor.FindChild("StateCommands", true, false) as CodeEdit;
                    stateCommands.Text = "# authoring state check";
                    stateCommands.EmitSignal(TextEdit.SignalName.TextChanged);
                    Press(editor, "tools/common/save");
                    Check(ModFileService.ReadText(Mod, EventFile).Contains("authoring state check"), "Save writes the selected barrels file");
                    Check(ModFileService.ReadText(Mod, MapFile) == backups[MapFile], "editing events leaves the map file byte-for-byte unchanged");
                    var saved = RpgMapDocument.Load(backups[MapFile], ModFileService.ReadText(Mod, EventFile));
                    Check(saved.Events[0].Pages[0].Route.Steps[0] == "wait", "unexposed state fields survive saving");
                    Check(saved.Events[0].Commands[0] == "flag tools_authoring_open 1", "base reward commands stay separate from open-state commands");
                    editor.Free(); editor = null;
                    atlas = ToolWindow.Open<RpgTilesetEditorWindow>();
                    break;
                case 3:
                    SelectFile(atlas, Mod, TilesetFile);
                    var document = RpgTilesetDocument.Load(ModFileService.ReadText(Mod, TilesetFile));
                    var origin = RpgTileSetBuilder.AtlasCoords(document.Tileset, 100);
                    var view = Find<RpgTilesetAtlasView>(atlas);
                    var layer = atlas.FindChild("TargetLayer", true, false) as OptionButton;
                    view.Select(new Rect2I(origin, new Vector2I(2, 1)));
                    Choose(layer, 3);
                    view.Select(new Rect2I(origin + Vector2I.Down, new Vector2I(2, 1)));
                    Choose(layer, 2);
                    Press(atlas, "tools/common/save");
                    var updated = RpgTilesetFactory.Instance.Get(TilesetId);
                    Check(updated.TileOf(100).Layer == RpgLayer.Front && updated.TileOf(102).Layer == RpgLayer.Main,
                        "tileset UI saves different layers for roof and foundation");
                    Check(ModFileService.ReadText(Mod, TilesetFile).Contains("UnknownMetadata"), "tileset preserves unknown data");
                    atlas.Free(); atlas = null;
                    editor = ToolWindow.Open<RpgMapEditorWindow>();
                    break;
                case 4:
                    SelectFile(editor, Mod, MapFile);
                    canvas = Find<RpgMapEditorCanvas>(editor);
                    Press(editor, "tools/rpg_editor/layer_main");
                    PickTile(100);
                    ClickCell(new Vector2I(1, 1));
                    ClickCell(new Vector2I(5, 3));
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Front, 1, 1) == 100
                        && canvas.Document.Grid.GetTile(RpgLayer.Main, 1, 2) == 102, "one palette stamp places the parts on their layers");
                    Press(editor, "tools/rpg_editor/layer_front");
                    PickTile(200);
                    ClickCell(new Vector2I(2, 1));
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Front, 1, 1) == -1
                        && canvas.Document.Grid.GetTile(RpgLayer.Main, 1, 2) == -1, "replacing one part removes the entire old house");
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Front, 5, 3) == 100, "neighbouring house is not removed");
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Front, 2, 1) == 200, "replacement remains");
                    Press(editor, "tools/rpg_editor/undo");
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Main, 1, 2) == 102, "undo restores the complete house");
                    Press(editor, "tools/rpg_editor/redo");
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Main, 1, 2) == -1, "redo removes the tail again");
                    Press(editor, "tools/rpg_editor/layer_main");
                    PickTile(100);
                    ClickCell(new Vector2I(1, 1));
                    canvas.Document.Paint(RpgEditLayer.Front, 2, 1, -1, RpgCollision.Auto);
                    canvas.Refresh();
                    EraseVisibleFragment();
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Main, 1, 2) == -1
                        && canvas.Document.Grid.GetTile(RpgLayer.Front, 1, 1) == -1, "eraser removes a legacy fragment at its visible isometric position");
                    Check(canvas.Document.Grid.GetTile(RpgLayer.Front, 5, 3) == 100, "fragment erasure keeps the neighbour");
                    Finish();
                    break;
            }
        }

        private void EraseVisibleFragment()
        {
            using var textures = new RpgTextures();
            using var builder = new RpgRenderSceneBuilder(textures);
            var scene = builder.Build(canvas.Document.Map, canvas.Document.Grid, canvas.Tileset);
            var visual = scene.Visuals.First(v => v.Id == "test_house" && v.Only == "isometric" && v.Layer == 1 && v.Bounds.Position == new Vector2(1, 2));
            var projection = TileRendererRegistry.Instance.Resolve(canvas.RendererId).Projection(canvas.Tileset.TileSize);
            using var image = visual.Texture.GetImage();
            for (var y = 0; y < (int)visual.Source.Size.Y; y++)
                for (var x = 0; x < (int)visual.Source.Size.X; x++)
                {
                    if (image.GetPixel((int)visual.Source.Position.X + x, (int)visual.Source.Position.Y + y).A < 0.5f) continue;
                    var point = projection.Place(visual).Transform * new Vector2((x + 0.5f) / visual.Source.Size.X, (y + 0.5f) / visual.Source.Size.Y);
                    Press(editor, "tools/rpg_editor/tool_eraser");
                    Click(canvas, canvas.Preview.Root.Transform * point);
                    scene.Release();
                    return;
                }
            scene.Release();
            throw new InvalidOperationException("fragment fixture has no visible pixel");
        }

        private void SelectEvent(RpgMapEvent mapEvent)
        {
            var position = canvas.Preview.ProjectToView(new Vector3(mapEvent.X + 0.5f, mapEvent.Y + 0.5f, 0));
            for (var i = 0; i < canvas.Document.EventsAt(mapEvent.X, mapEvent.Y, true).Count; i++)
            {
                Click(canvas, position);
                if (canvas.SelectedEvent?.Id == mapEvent.Id) return;
            }
        }

        private void PickTile(int id)
        {
            var palette = Find<RpgTilePalette>(editor);
            var at = RpgTileSetBuilder.AtlasCoords(canvas.Tileset, id);
            var scale = Math.Min(1.5f, 384f / (canvas.Tileset.Columns * canvas.Tileset.TileSize));
            Click(palette, ((Vector2)at + Vector2.One * 0.5f) * canvas.Tileset.TileSize * scale);
        }

        private void ClickCell(Vector2I cell)
            => Click(canvas, canvas.Preview.ProjectToView(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0)));

        private static void Click(Control control, Vector2 position)
        {
            control.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
            control.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = false });
        }

        private static T Find<T>(Node root) where T : Node => root.FindChildren("*", "", true, false).OfType<T>().Single();

        private static void SelectFile(Window window, string mod, string file)
        {
            var selector = Find<ModFileSelector>(window);
            var choices = selector.GetChildren().OfType<OptionButton>().ToArray();
            Choose(choices[0], Enumerable.Range(0, choices[0].ItemCount).First(i => choices[0].GetItemText(i) == mod));
            var shown = file.Substring(file.IndexOf('/') + 1);
            Choose(choices[1], Enumerable.Range(0, choices[1].ItemCount).First(i => choices[1].GetItemText(i) == shown));
        }

        private static void Choose(OptionButton picker, int index)
        {
            picker.Select(index);
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }

        private static void Press(Window window, string key)
            => window.FindChildren("*", "", true, false).OfType<Button>().First(b => b.Text == TranslationServer.Translate(key)).EmitSignal(BaseButton.SignalName.Pressed);

        private void Check(bool value, string message)
        {
            if (value) passed++; else failed++;
            GameLogger.Log("TEST", (value ? "[PASS] " : "[FAIL] ") + message);
        }

        public override void _ExitTree() => Cleanup();

        private void Cleanup()
        {
            if (editor != null && IsInstanceValid(editor)) editor.Free();
            if (atlas != null && IsInstanceValid(atlas)) atlas.Free();
            editor = null; atlas = null;
            foreach (var entry in backups) ModFileService.WriteText(Mod, entry.Key, entry.Value);
            backups.Clear();
            RpgMapEvents.Invalidate();
        }

        private void Finish()
        {
            done = true;
            Cleanup();
            GameLogger.Log("TEST", $"[TEST_RESULT] name=rpg_authoring passed={passed} failed={failed}");
            Cthangover.Core.Bootstrap.ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
