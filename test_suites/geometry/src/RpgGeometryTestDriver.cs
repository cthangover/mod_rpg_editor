namespace Cthangover.Tools.RpgEditor.Tests.Geometry
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// Uses inspector controls and canvas input, then saves and reopens the resized map. A disposable
    /// map in the writable sample mod avoids modifying real content; its files and manifest are restored
    /// on every exit. Both projections run the same assertions, including a one-native-pixel offset.
    /// </summary>
    public partial class RpgGeometryTestDriver : Node
    {
        private const string Mod = "rpg_plus_game";
        private const string Map = "zz_geometry_editor_test";
        private const string Placement = Map + "/background";
        private readonly Dictionary<string, string> before = new();
        private readonly Timer timer = new() { WaitTime = 0.4, Autostart = true };
        private RpgEditorWindow window;
        private string projection = RpgEditorCanvas.TopDown;
        private string shots;
        private Vector2 initialPosition;
        private int step;
        private int passed;
        private int failed;
        private double elapsed;
        private bool finished;

        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--editor-renderer=isometric") projection = RpgEditorCanvas.Isometric;
                if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            }
            AddChild(timer);
            timer.Timeout += Tick;
        }

        public override void _ExitTree() => Restore();

        private void Tick()
        {
            if (finished) return;
            elapsed += timer.WaitTime;
            try
            {
                if (elapsed > 90) throw new InvalidOperationException("timeout at step " + step);
                Run();
            }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }

        private void Run()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || RpgWorldHost.Current?.Session == null || GodotSceneService.IsTransitioning) return;
                    Prepare();
                    window = ToolWindow.Open<RpgEditorWindow>();
                    window.OpenMapFile(Mod, "maps/" + Map + ".json");
                    window.SetProjection(projection);
                    window.Canvas.FitToView();
                    break;
                case 1:
                    Check(window.Document.MapId == Map, "fixture opens through map selector");
                    initialPosition = Position();
                    Named<SpinBox>("MapWidth").Value = 12;
                    Named<SpinBox>("MapHeight").Value = 10;
                    Press("MapResizeApply");
                    Check(window.Document.Width == 12 && window.Document.Height == 10, "inspector expands both dimensions");
                    Check(window.Canvas.Scene.Size == new Vector2I(12, 10), "renderer and grid receive expanded bounds");
                    break;
                case 2:
                    var cell = new Vector2I(11, 9);
                    var point = window.Canvas.CanvasPointOf(cell);
                    Check(window.Canvas.CellAt(point) == cell, "new far edge is pickable");
                    window.SelectTool(RpgEditorTool.Collision);
                    window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = point });
                    window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = point });
                    Check(window.Document.CodeAt(11, 9) == '#', "canvas edits a cell outside the old grid");
                    Named<CheckBox>("MapGrid").ButtonPressed = false;
                    Check(!window.Canvas.ShowGrid, "grid toggle hides lines");
                    Named<CheckBox>("MapGrid").ButtonPressed = true;
                    var drawnBefore = DrawnPosition();
                    Named<SpinBox>("BackdropX").Value = initialPosition.X + 1;
                    Named<SpinBox>("BackdropY").Value = initialPosition.Y - 7;
                    Check(Position() == initialPosition, "live preview does not write the document before Apply");
                    Check(((DrawnPosition() - drawnBefore) * 32).DistanceTo(new Vector2(1, -7)) < 0.001f,
                        "live preview moves the rendered picture by the requested pixels");
                    Press("BackdropApply");
                    Check(Position() == initialPosition + new Vector2(1, -7), "inspector moves the picture by native pixels, not cells");
                    break;
                case 3:
                    Check(window.Undo() && Position() == initialPosition, "one undo restores picture position");
                    Check(window.Redo() && Position() == initialPosition + new Vector2(1, -7), "redo restores pixel position");
                    Check(window.Scene.ObjectOf(Placement).Width == 6, "background footprint stays independent of grid size");
                    Check(window.Save(), "save resized grid and picture override");
                    window.OpenMapFile(Mod, "maps/" + Map + ".json");
                    Check(window.Document.Width == 12 && window.Document.Height == 10, "reopened dimensions persist");
                    Check(window.Document.CodeAt(11, 9) == '#', "new edge edit persists");
                    Check(Position() == initialPosition + new Vector2(1, -7), "reopened picture offset persists");
                    break;
                case 4:
                    Shot("grid_backdrop_" + projection);
                    var other = projection == RpgEditorCanvas.TopDown ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown;
                    window.SetProjection(other);
                    var expected = other == RpgEditorCanvas.TopDown ? Vector2.Zero : new Vector2(-320, -64);
                    Check(Position() == expected, "alternate projection retains its picture placement");
                    window.SetProjection(projection);
                    Check(Position() == initialPosition + new Vector2(1, -7), "switching back retains pixel edit");
                    var savedVisual = DrawnPosition();
                    Named<SpinBox>("BackdropX").Value += 5;
                    window.SetProjection(other);
                    window.SetProjection(projection);
                    Check(DrawnPosition().DistanceTo(savedVisual) < 0.001f, "projection switch drops unapplied preview");
                    var saved = Entities.Collect<RpgMap>()[Map];
                    Check(saved.Width == 12 && saved.Height == 10, "runtime reads saved expanded map");
                    var originalSize = DrawnSize();
                    Named<RpgScaleControl>("BackdropScale").GetNode<SpinBox>("Number").Value = 0.01;
                    Check(DrawnSize().IsEqualApprox(originalSize * 0.01f), "numeric scale redraws the backdrop immediately");
                    Check(window.Scene.ObjectOf(Placement).Width == 6 && window.Canvas.Scene.Size == new Vector2I(12, 10), "scale leaves footprint and grid unchanged");
                    break;
                case 5:
                    var slider = Named<RpgScaleControl>("BackdropScale").GetNode<HSlider>("Slider");
                    slider.EmitSignal(Slider.SignalName.DragStarted);
                    slider.Value = 2;
                    slider.Value = 3;
                    Check(window.Document.Placements.First(p => p.Id == Placement).Data["Scale"].GetValue<float>() == 0.01f, "slider drag previews before its single document change");
                    Check(DrawnSize().IsEqualApprox(new Vector2(60, 36)), "picture redraws while slider is still held");
                    slider.EmitSignal(Slider.SignalName.DragEnded, true);
                    Check(window.Undo() && Math.Abs(window.Scene.ObjectOf(Placement).Scale - 0.01f) < 0.0001f, "one undo reverts the whole slider drag");
                    Check(window.Redo() && window.Scene.ObjectOf(Placement).Scale == 3, "redo restores scale");
                    window.SelectTool(RpgEditorTool.Select);
                    var propPoint = window.Canvas.CanvasPointOf(new Vector2I(4, 3));
                    window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = propPoint });
                    window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = propPoint });
                    Check(window.SelectedId == Map + "/prop", "ordinary object selected independently of backdrop");
                    Named<RpgScaleControl>("PlacementScale").GetNode<SpinBox>("Number").Value = 2.5;
                    Check(window.Canvas.Scene.Visuals.First(v => v.Id.StartsWith(Map + "/prop#", StringComparison.Ordinal)).Bounds.Size.IsEqualApprox(new Vector2(2.5f, 5)), "selected object numeric scale redraws immediately");
                    break;
                case 6:
                    Check(window.Save(), "save both picture scales");
                    window.OpenMapFile(Mod, "maps/" + Map + ".json");
                    Check(window.Scene.ObjectOf(Placement).Scale == 3 && window.Scene.ObjectOf(Map + "/prop").Scale == 2.5f, "backdrop and object scale survive save and reopen");
                    var scaleNumber = Named<RpgScaleControl>("BackdropScale").GetNode<SpinBox>("Number");
                    scaleNumber.Value = 100;
                    Check(DrawnSize().IsEqualApprox(new Vector2(2000, 1200)), "upper scale limit draws correctly");
                    scaleNumber.Value = 0;
                    Check(Math.Abs(window.Scene.ObjectOf(Placement).Scale - 0.01f) < 0.0001f, "numeric input clamps to lower limit");
                    Shot("picture_scale_" + projection);
                    Finish();
                    return;
            }
            step++;
        }

        private Vector2 DrawnSize() => window.Canvas.Scene.Visuals.First(v => v.Id.StartsWith(Placement + "#", StringComparison.Ordinal)
            && v.Only == window.Canvas.Projection).Bounds.Size;

        private Vector2 Position() => RpgBackdropEdit.Position(window.Scene.ObjectOf(Placement), window.Canvas.Projection, 32);

        private Vector2 DrawnPosition() => window.Canvas.Scene.Visuals.First(v => v.Id.StartsWith(Placement + "#", StringComparison.Ordinal)
            && v.Only == window.Canvas.Projection).Bounds.Position;

        private T Named<T>(string name) where T : Node
            => window.Inspector.FindChild(name, true, false) as T ?? throw new InvalidOperationException("missing control " + name);

        private void Press(string name) => Named<Button>(name).EmitSignal(BaseButton.SignalName.Pressed);

        private void Prepare()
        {
            Remember("manifest.json");
            Remember("maps/" + Map + ".json");
            Remember("scenes/" + Map + ".json");
            Remember("placements/" + Map + "/backdrop.json");
            Write("maps/" + Map + ".json", "{\"Kind\":\"rpg_map\",\"Items\":[{\"Id\":\"" + Map + "\",\"Width\":6,\"Height\":5,\"TileSize\":32,\"Zoom\":1,\"Spawn\":{\"X\":1,\"Y\":1},\"Legend\":{},\"Collision\":[\"......\",\"......\",\"......\",\"......\",\"......\"]}]}");
            Write("scenes/" + Map + ".json", "{\"name\":\"" + Map + "\"}");
            Write("placements/" + Map + "/backdrop.json", "{\"Kind\":\"rpg_placement\",\"Items\":[{\"Id\":\"" + Placement + "\",\"Object\":\"village_pack/building/cottage\",\"X\":0,\"Y\":0,\"Solid\":false,\"Footprint\":{\"Size\":[6,5]},\"Anchor\":[0,0],\"Parts\":[{\"Image\":\"building/cottage\",\"Geometry\":\"upright\",\"At\":[-10,-2],\"Size\":[20,12],\"Only\":\"isometric\",\"DrawOrder\":\"BelowCharacters\"},{\"Image\":\"building/cottage\",\"Geometry\":\"ground\",\"At\":[0,0],\"Size\":[20,12],\"Only\":\"topdown\",\"DrawOrder\":\"BelowCharacters\"}]},{\"Id\":\"" + Map + "/prop\",\"Object\":\"village_pack/building/cottage\",\"X\":4,\"Y\":3,\"Footprint\":{\"Size\":[1,1]},\"Parts\":[{\"Image\":\"building/cottage\",\"Size\":[1,2]}]}]}");
        }

        private void Remember(string path) => before[path] = ModFileService.ReadText(Mod, path);
        private static void Write(string path, string text)
        {
            if (!ModFileService.WriteText(Mod, path, text)) throw new IOException("cannot write fixture " + path);
        }
        private void Restore()
        {
            foreach (var pair in before)
                if (pair.Value == null) ModFileService.DeleteFile(Mod, pair.Key);
                else ModFileService.WriteText(Mod, pair.Key, pair.Value);
            before.Clear();
        }
        private void Shot(string name)
        {
            if (shots == null || DisplayServer.GetName() == "headless") return;
            Directory.CreateDirectory(shots);
            ViewportCapture.SavePng(GetViewport(), Path.Combine(shots, name + ".png"));
        }
        private void Check(bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            GameLogger.Log("TEST", (ok ? "[PASS] " : "[FAIL] ") + detail);
        }
        private void Finish()
        {
            if (finished) return;
            finished = true;
            timer.Stop();
            window?.QueueFree();
            Restore();
            GameLogger.Log("TEST", "[TEST_RESULT] name=rpg_editor_geometry passed=" + passed + " failed=" + failed);
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
