namespace Cthangover.Tools.RpgEditor.Tests.Areas
{
    using System;
    using System.IO;
    using System.Collections.Generic;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Drives projected corner handles and both collision buttons, checking previews before release and document data afterwards.</summary>
    public partial class RpgAreasTestDriver : Node
    {
        private RpgEditorWindow window;
        private int step;
        private int passed;
        private int failed;
        private double elapsed;
        private string shots;
        private string before;
        private string priorityMap;
        private Dictionary<string, RpgObject> priorityObjects;
        private const string EventId = "rpg_editor_areas/event";
        private const string Mod = "rpg_editor__areas";
        private readonly Timer timer = new() { WaitTime = 0.35, Autostart = true };

        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--priority-map=", StringComparison.Ordinal)) priorityMap = arg.Substring(15);
            AddChild(timer);
            timer.Timeout += Tick;
        }
        private void Tick()
        {
            elapsed += timer.WaitTime;
            if (elapsed > 60) { Check(false, "timeout step " + step); Finish(); return; }
            try { Run(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        private void Run()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || RpgWorldHost.Current?.Session?.Map.ID != "rpg_editor_areas") return;
                    window = ToolWindow.Open<RpgEditorWindow>();
                    window.OpenMapFile(Mod, "maps/rpg_editor_areas.json");
                    window.SetProjection(RpgEditorCanvas.Isometric);
                    window.Canvas.FitToView();
                    break;
                case 1:
                    Check(window.Document?.MapId == "rpg_editor_areas", "isolated map opened");
                    PressTool("tools/rpg_editor/tool_collision");
                    Check(window.Tool == RpgEditorTool.Collision, "actual Collision button selects mode");
                    Check(Red(2, 2) && Red(3, 3) && Red(7, 5), "red covers terrain, explicit collision and object footprint immediately");
                    Check(!Red(6, 2), "walkable tile stays clear");
                    Check(window.Canvas.CellSpaceAt(window.Canvas.CanvasPointAt(new Vector2(6, 5))).DistanceTo(new Vector2(6, 5)) < 0.001f,
                        "isometric inverse preserves corner coordinates");
                    var zoomAt = window.Canvas.CanvasPointAt(new Vector2(5, 4));
                    for (var zoom = 0; zoom < 4; zoom++)
                        window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = zoomAt });
                    break;
                case 2:
                    Shot("collision_isometric");
                    Mouse(MouseButton.Left, true, Cell(6, 2));
                    Motion(Cell(7, 2), MouseButtonMask.Left);
                    Check(Red(6, 2) && Red(7, 2), "LMB stroke turns red before release");
                    Check(window.Document.CodeAt(6, 2) == '.', "preview has not yet mutated the document");
                    Mouse(MouseButton.Left, false, Cell(7, 2));
                    Check(window.Document.CodeAt(6, 2) == '#' && window.Document.CodeAt(7, 2) == '#', "default LMB blocks both cells");
                    Mouse(MouseButton.Right, true, Cell(2, 2));
                    Check(!Red(2, 2), "RMB clears solid terrain's red preview immediately");
                    Mouse(MouseButton.Right, false, Cell(2, 2));
                    Check(window.Document.CodeAt(2, 2) == 'o' && window.Scene.Walk.IsWalkable(2, 2), "RMB force-opens solid terrain");
                    Check(window.Undo() && Red(2, 2), "undo refreshes collision overlay");
                    Check(window.Redo() && !Red(2, 2), "redo refreshes collision overlay");
                    PressTool("tools/rpg_editor/tool_select");
                    Click(Cell(4, 3));
                    Check(window.SelectedId == EventId && window.Canvas.ResizeArea == new Rect2I(4, 3, 2, 2), "selected event exposes its area");
                    break;
                case 3:
                    var corner = window.Canvas.CanvasPointAt(new Vector2(6, 5));
                    Check(window.Canvas.ResizeHandleAt(corner) == 2, "south-east isometric corner can be grabbed");
                    Mouse(MouseButton.Left, true, corner);
                    Motion(window.Canvas.CanvasPointAt(new Vector2(9, 7)), MouseButtonMask.Left);
                    Check(window.Canvas.ResizeArea == new Rect2I(4, 3, 5, 4), "isometric drag previews expanded bounds");
                    Check(window.Scene.ObjectOf(EventId).Width == 2, "resize preview leaves original effective object unchanged");
                    break;
                case 4:
                    Shot("event_resize_isometric");
                    Mouse(MouseButton.Left, false, window.Canvas.CanvasPointAt(new Vector2(9, 7)));
                    Check(window.Scene.ObjectOf(EventId).Width == 5 && window.Scene.ObjectOf(EventId).Height == 4, "release commits footprint size");
                    Check(window.Undo() && window.Scene.ObjectOf(EventId).Width == 2, "one undo restores entire resize");
                    Check(window.Redo() && window.Scene.ObjectOf(EventId).Width == 5, "redo restores area");
                    Mouse(MouseButton.Left, true, window.Canvas.CanvasPointAt(new Vector2(4, 3)));
                    Motion(window.Canvas.CanvasPointAt(new Vector2(2, 1)), MouseButtonMask.Left);
                    Mouse(MouseButton.Left, false, window.Canvas.CanvasPointAt(new Vector2(2, 1)));
                    Check(window.Canvas.ResizeArea == new Rect2I(2, 1, 7, 6), "north-west moves origin and keeps opposite corner fixed");
                    Check(window.Undo() && window.Canvas.ResizeArea == new Rect2I(4, 3, 5, 4), "origin and size undo together");
                    window.Redo();
                    before = window.Document.AnyPlacement(EventId).Data.ToJsonString();
                    Click(window.Canvas.CanvasPointAt(new Vector2(2, 1)));
                    Check(window.Document.AnyPlacement(EventId).Data.ToJsonString() == before, "click without drag changes nothing");
                    window.SetProjection(RpgEditorCanvas.TopDown);
                    break;
                case 5:
                    Check(window.Canvas.CellSpaceAt(window.Canvas.CanvasPointAt(new Vector2(5, 4))).DistanceTo(new Vector2(5, 4)) < 0.001f,
                        "top-down inverse preserves corner coordinates");
                    Mouse(MouseButton.Left, true, window.Canvas.CanvasPointAt(new Vector2(9, 7)));
                    Motion(window.Canvas.CanvasPointAt(new Vector2(5, 4)), MouseButtonMask.Left);
                    Mouse(MouseButton.Left, false, window.Canvas.CanvasPointAt(new Vector2(5, 4)));
                    Check(window.Canvas.ResizeArea == new Rect2I(2, 1, 3, 3), "same handles shrink in top-down projection");
                    window.SelectTool(RpgEditorTool.Collision);
                    Check(Red(6, 2) && !Red(2, 2), "collision state survives projection and tool switches");
                    break;
                case 6:
                    Shot("collision_topdown");
                    if (priorityMap != null) CheckSavedMapPriority();
                    Finish();
                    return;
            }
            step++;
        }
        private void CheckSavedMapPriority()
        {
            var map = Entities.Collect<RpgMap>()[priorityMap];
            priorityObjects = Entities.Collect<RpgObject>();
            var placements = new List<RpgPlacement>();
            foreach (var placement in Entities.Collect<RpgPlacement>().Values)
                if (RpgMapDocument.MapOfPlacement(placement.ID) == priorityMap) placements.Add(placement);
            var runtime = new RpgWorldSession(map, FindPriorityObject, placements, EvaluatePriorityCondition);
            var overridesMatch = true;
            var opened = 0;
            for (var y = 0; y < map.Height; y++)
                for (var x = 0; x < map.Width; x++)
                {
                    var code = runtime.Cells.Collision(x, y);
                    if (code == RpgCollision.Open) { opened++; overridesMatch &= runtime.CanEnter(new Vector2I(x, y)); }
                    if (code == RpgCollision.Block) overridesMatch &= !runtime.CanEnter(new Vector2I(x, y));
                }
            Check(opened > 0 && overridesMatch, "saved hall: every explicit map override wins over live object collision");
            var walked = false;
            foreach (var instance in runtime.Instances)
            {
                if (walked || !instance.Visible) continue;
                for (var dy = 0; dy < instance.Current.Height && !walked; dy++)
                    for (var dx = 0; dx < instance.Current.Width && !walked; dx++)
                    {
                        var cell = instance.Actor.Cell + new Vector2I(dx, dy);
                        if (!instance.Current.BlocksAt(dx, dy) || runtime.Cells.Collision(cell.X, cell.Y) != RpgCollision.Open) continue;
                        var from = cell + Vector2I.Down;
                        if (!runtime.Cells.InBounds(from.X, from.Y)) continue;
                        runtime.Relocate(from, RpgDirection.Up);
                        runtime.Tick(1, false, RpgDirection.Up);
                        Check(runtime.Player.Cell == cell, "saved hall: player walks onto forced-open solid event " + instance.Id);
                        runtime.RefreshStates();
                        Check(runtime.CanEnter(cell), "saved hall: event state refresh cannot re-block o");
                        walked = true;
                    }
            }
            Check(walked, "saved hall includes a solid dynamic event on an opened cell");
        }
        private RpgObject FindPriorityObject(string id) => priorityObjects.TryGetValue(id, out var obj) ? obj : null;
        private static bool EvaluatePriorityCondition(string condition, string scope)
            => Cthangover.Core.Scenes.ScenarioCondition.Evaluate(condition, scope);

        private Vector2 Cell(int x, int y) => window.Canvas.CanvasPointOf(new Vector2I(x, y));
        private void Mouse(MouseButton button, bool pressed, Vector2 position)
            => window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = button, Pressed = pressed, Position = position });
        private void Motion(Vector2 position, MouseButtonMask mask)
            => window.Canvas._GuiInput(new InputEventMouseMotion { Position = position, ButtonMask = mask });
        private void Click(Vector2 position) { Mouse(MouseButton.Left, true, position); Mouse(MouseButton.Left, false, position); }
        private bool Red(int x, int y)
        {
            foreach (var mark in window.Canvas.Marks)
                if (mark.X == x && mark.Y == y && mark.Fill && mark.Color == RpgCollisionOverlay.BlockedFill) return true;
            return false;
        }
        private void PressTool(string key)
        {
            var button = FindButton(window, TranslationServer.Translate(key));
            if (button == null) throw new InvalidOperationException("Button not found " + key);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        private static Button FindButton(Node node, string text)
        {
            if (node is Button button && button.Text == text) return button;
            foreach (var child in node.GetChildren()) { var found = FindButton(child, text); if (found != null) return found; }
            return null;
        }
        private void Shot(string name)
        {
            if (shots == null) return;
            Directory.CreateDirectory(shots);
            using var image = GetViewport().GetTexture().GetImage();
            image.SavePng(Path.Combine(shots, name + ".png"));
        }
        private void Check(bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            GameLogger.Log("TEST", (ok ? "[PASS] " : "[FAIL] ") + detail);
        }
        private void Finish()
        {
            timer.Stop();
            window?.QueueFree();
            GameLogger.Log("TEST", "[TEST_RESULT] name=rpg_editor_areas passed=" + passed + " failed=" + failed);
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
