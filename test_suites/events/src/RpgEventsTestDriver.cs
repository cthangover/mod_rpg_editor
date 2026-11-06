namespace Cthangover.Tools.RpgEditor.Tests.Events
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.RpgObjectEditor;
    using Godot;

    /// <summary>Draws, edits, resizes, saves and runs a new map event; creates an object from the map and checks live palette refresh.</summary>
    public partial class RpgEventsTestDriver : Node
    {
        private const string Mod = "rpg_editor__events";
        private const string Map = "rpg_map_events_test";
        private readonly Timer timer = new() { WaitTime = 0.4, Autostart = true };
        private readonly Dictionary<string, byte[]> backups = new();
        private RpgEditorWindow window;
        private RpgObjectEditorWindow objectWindow;
        private string fixtureRoot;
        private string eventId;
        private string objectId;
        private string shots;
        private bool isometric;
        private int step;
        private int passed;
        private int failed;
        private int ticks;

        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--shots=")) shots = arg.Substring(8);
                if (arg == "--tile-renderer=isometric") isometric = true;
            }
            fixtureRoot = ProjectSettings.GlobalizePath("res://mods/rpg_editor/test_suites/events");
            foreach (var path in FixtureFiles()) backups[path] = File.ReadAllBytes(path);
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private IEnumerable<string> FixtureFiles() => Directory.EnumerateFiles(fixtureRoot, "*", SearchOption.AllDirectories)
            .Where(p => p.EndsWith(".json") || p.EndsWith(".properties"));

        private void Tick()
        {
            if (++ticks > 150) { Check(false, "timeout at " + step); Finish(); return; }
            try { Run(); }
            catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }

        private void Run()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || RpgWorldHost.Current?.Session?.Map.ID != Map) return;
                    window = ToolWindow.Open<RpgEditorWindow>();
                    window.OpenMapFile(Mod, "maps/" + Map + ".json");
                    window.SetProjection(isometric ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown);
                    window.Canvas.FitToView();
                    break;
                case 1:
                    PressText(window, TranslationServer.Translate("tools/rpg_editor/tool_event"));
                    Mouse(true, Cell(2, 2));
                    window.Canvas._GuiInput(new InputEventMouseMotion { Position = Cell(3, 3), ButtonMask = MouseButtonMask.Left });
                    Check(window.Document.Events.Count == 0, "drag previews without creating data");
                    Mouse(false, Cell(3, 3));
                    eventId = window.SelectedId;
                    var entry = window.Document.EventOf(eventId);
                    Check(entry != null && entry.X == 2 && entry.Y == 2 && entry.Width == 2 && entry.Height == 2, "drawing creates and selects a 2x2 event");
                    Check(window.Document.Placements.Count == 0 && window.Scene.Statics.Count == 0, "event creates no object placement or scenery");
                    break;
                case 2:
                    window.Inspector.GetNode<CodeEdit>("EventScript").Text = "flag self.done 1\nend";
                    var when = window.Inspector.FindChild("EventWhen", true, false) as LineEdit;
                    when.Text = "self.done != 1";
                    PressName(window.Inspector, "ApplyEvent");
                    Check(window.Document.EventOf(eventId).Commands[0] == "flag self.done 1", "inline script edited inside map inspector");
                    var corner = window.Canvas.CanvasPointAt(new Vector2(4, 4));
                    Mouse(true, corner);
                    window.Canvas._GuiInput(new InputEventMouseMotion { Position = window.Canvas.CanvasPointAt(new Vector2(5, 4)), ButtonMask = MouseButtonMask.Left });
                    Mouse(false, window.Canvas.CanvasPointAt(new Vector2(5, 4)));
                    Check(window.Document.EventOf(eventId).Width == 3, "corner handle resizes a new event");
                    Check(window.Undo() && window.Document.EventOf(eventId).Width == 2, "one undo restores event extent");
                    Check(window.Redo() && window.Document.EventOf(eventId).Width == 3, "redo restores event extent");
                    break;
                case 3:
                    Shot("event_authoring");
                    Check(window.Save(), "new event saves with its map");
                    window.OpenMapFile(Mod, "maps/" + Map + ".json");
                    Check(window.Document.EventOf(eventId)?.Commands[0] == "flag self.done 1", "event and inline script reload");
                    Check(Entities.Collect<RpgMap>()[Map].Events.Count == 1, "running resolver sees saved event");
                    PressText(window, TranslationServer.Translate("tools/rpg_editor/tool_event"));
                    Click(Cell(7, 2));
                    var teleportId = window.SelectedId;
                    Pick("EventTargetMap", Map);
                    PressName(window.Inspector, "ApplyEvent");
                    PressName(window.Inspector, "EventTargetPick");
                    Click(Cell(8, 5));
                    var destination = window.Document.EventOf(teleportId)?.Teleport;
                    Check(destination?.Map == Map && destination.X == 8 && destination.Y == 5, "native event picks its teleport destination on the map");
                    Check(window.Canvas.Arrows.Any(a => a.Placement == teleportId), "native event teleport has an editable arrow");
                    Check(window.Save(), "native teleport event saves in the map");
                    PressName(window, "NewMapObject");
                    break;
                case 4:
                    objectWindow = FindObjectWindow(GetTree().Root);
                    Check(objectWindow?.ModId == Mod && objectWindow.SelectedId != null, "new object form opens in the map's own mod");
                    objectId = objectWindow.SelectedId;
                    Check(objectWindow.Save(), "new object can be saved from its form");
                    Check(window.Palette.Count(RpgObjectForm.Sprite) > 0 && Entities.Collect<RpgObject>().ContainsKey(objectId), "saved object becomes available without reopening the map");
                    objectWindow.QueueFree();
                    objectWindow = null;
                    window.ChooseObject(objectId, RpgObjectForm.Sprite);
                    Click(Cell(6, 5));
                    Check(window.Document.Placements.Any(p => p.Data["Object"]?.ToString() == objectId), "new object can be placed immediately");
                    CheckActorRoute();
                    Click(Cell(8,6));
                    step = 7;
                    return;
                case 7:
                    Shot("route_graphical");
                    window.QueueFree();
                    window = null;
                    RpgWorldHost.Teleport(new RpgTeleport { Map = Map, X = 1, Y = 2, Dir = RpgDirection.Right });
                    step = 5;
                    return;
                case 5:
                    var session = RpgWorldHost.Current?.Session;
                    if (session?.Map.ID != Map || session.Find(eventId) == null) return;
                    session.Tick(1, false, RpgDirection.Right);
                    break;
                case 6:
                    if (!ScenarioCondition.Evaluate("self.done == 1", eventId)) return;
                    Check(true, "walking into the saved zone runs its inline script with its own self scope");
                    RpgWorldHost.Current.Session.RefreshStates();
                    Check(RpgWorldHost.Current.Session.At(new Vector2I(2, 2), RpgInteractTrigger.Step) == null, "condition disables the completed event");
                    Shot("event_played");
                    Finish();
                    return;
            }
            step++;
        }

        private void CheckActorRoute()
        {
            window.ChooseObject("village_pack/npc/villager", RpgObjectForm.Actor);
            Click(Cell(8, 6));
            var actorId = window.SelectedId;
            var editor = window.Inspector.FindChild("ActorRoute", true, false) as RpgRouteEditor;
            Check(editor != null, "selecting an actor exposes its route editor");
            if (editor == null) throw new InvalidOperationException("Actor route editor is missing");
            editor.GetChildren().OfType<TextEdit>().Single().Text = "left\nwait:2\nright";
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_apply"));
            var route = window.Scene.ObjectOf(actorId)?.Route;
            Check(route?.Steps.SequenceEqual(new[] { "left", "wait:2", "right" }) == true,
                "route changes travel through the inspector into the actor placement");
            Check(window.Undo() && window.Scene.ObjectOf(actorId)?.Route == null, "one undo removes the route override");
            Check(window.Redo() && window.Scene.ObjectOf(actorId)?.Route?.Steps.Count == 3, "redo restores the route");
            Check(window.Save(), "actor route saves through the map editor");
            window.OpenMapFile(Mod, "maps/" + Map + ".json");
            route = window.Scene.ObjectOf(actorId)?.Route;
            Check(route?.Steps.SequenceEqual(new[] { "left", "wait:2", "right" }) == true, "route instructions survive save and reload");
            Check(route != null && Math.Abs(route.Speed - 1.3) < 0.00001 && Math.Abs(route.Every - 1.2) < 0.00001
                && route.Repeat && route.RetryBlocked, "route speed, interval and flags survive save and reload");
            window.SelectTool(RpgEditorTool.Select);
            Click(Cell(8, 6));
            editor = window.Inspector.FindChild("ActorRoute", true, false) as RpgRouteEditor;
            Check(editor?.GetChildren().OfType<TextEdit>().Single().Text == "left\nwait:2\nright", "reselecting the actor restores the route form");
            CheckDrawnRoute(actorId, editor);
            Click(Cell(6, 5));
            Check(window.Inspector.FindChild("ActorRoute", true, false) == null, "sprite selection has no actor route controls");
            Click(Cell(2, 2));
            Check(window.Inspector.FindChild("ActorRoute", true, false) == null, "map event selection has no actor route controls");
        }

        private void CheckDrawnRoute(string actorId, RpgRouteEditor editor)
        {
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_clear"));
            PressName(editor, "DrawRoute");
            Click(Cell(8,7));
            Click(Cell(9,7));
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_add_pause"));
            Check(editor.SelectedSteps.SequenceEqual(new[] { "down", "right", "wait:5" }), "map clicks draw cardinal steps and add a pause at the endpoint");
            Check(window.Scene.ObjectOf(actorId).Route.Steps.SequenceEqual(new[] { "left", "wait:2", "right" }), "drawing previews without changing saved document data");
            Check(window.Canvas.Routes.Any(r => r.Selected && r.Path.Segments.Count == 2 && r.Path.Stops.Any(s => s.Label == "5s")), "canvas contains a selected path and timed stop marker");
            Mouse(true, Cell(9,7));
            window.Canvas._GuiInput(new InputEventMouseMotion { Position=Cell(9,6), ButtonMask=MouseButtonMask.Left });
            Mouse(false, Cell(9,6));
            Check(editor.SelectedSteps.Last() == "up", "dragging adds the next connected waypoint");
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_apply"));
            Check(window.Scene.ObjectOf(actorId).Route.Steps.Count == 4, "drawn path applies as one document change");
            Check(window.Undo() && window.Scene.ObjectOf(actorId).Route.Steps[0] == "left", "one undo removes the whole drawn draft");
            Check(window.Redo() && window.Scene.ObjectOf(actorId).Route.Steps[0] == "down", "redo restores the drawn path");
            editor = window.Inspector.FindChild("ActorRoute", true, false) as RpgRouteEditor;
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_new_variant"));
            PressName(editor, "DrawRoute");
            Click(Cell(8,5));
            PressText(editor, TranslationServer.Translate("tools/rpg_editor/route_apply"));
            var route = window.Scene.ObjectOf(actorId).Route;
            Check(route.Variants.Count == 2 && route.Variants[1].SequenceEqual(new[] { "up" }), "new graphical alternative preserves the previous path");
            Check(window.Save(), "graphical alternatives save normally");
            window.OpenMapFile(Mod, "maps/"+Map+".json");
            Check(window.Scene.ObjectOf(actorId).Route.Variants.Count == 2, "all alternatives survive map reload");
            Click(Cell(8,6));
            var picker = (window.Inspector.FindChild("ActorRoute", true, false) as RpgRouteEditor).GetNode<OptionButton>("RouteVariant");
            picker.Select(1); picker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            Check(window.Canvas.Routes.Any(r => r.Selected && r.Path.End == new Vector2I(8,5)), "choosing an alternative highlights its actual geometry");
        }

        private Vector2 Cell(int x, int y) => window.Canvas.CanvasPointOf(new Vector2I(x, y));
        private void Mouse(bool pressed, Vector2 position)
            => window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed, Position = position });
        private void Click(Vector2 position) { Mouse(true, position); Mouse(false, position); }
        private void Pick(string name, string value)
        {
            var picker = window.Inspector.FindChild(name, true, false) as OptionButton;
            for (var i = 0; picker != null && i < picker.ItemCount; i++)
                if (picker.GetItemMetadata(i).AsString() == value)
                {
                    picker.Select(i);
                    picker.EmitSignal(OptionButton.SignalName.ItemSelected, i);
                    return;
                }
            throw new InvalidOperationException("Missing option " + value + " in " + name);
        }
        private static void PressName(Node root, string name)
        {
            var button = root.FindChild(name, true, false) as Button;
            if (button == null || button.Disabled) throw new InvalidOperationException("Unavailable button: " + name);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        private static void PressText(Node root, string text)
        {
            if (root is Button button && button.Text == text) { button.EmitSignal(BaseButton.SignalName.Pressed); return; }
            foreach (var child in root.GetChildren()) if (ContainsButton(child, text)) { PressText(child, text); return; }
            throw new InvalidOperationException("Missing button " + text);
        }
        private static bool ContainsButton(Node root, string text)
            => root is Button button && button.Text == text || root.GetChildren().Any(c => ContainsButton(c, text));
        private static RpgObjectEditorWindow FindObjectWindow(Node root)
        {
            if (root is RpgObjectEditorWindow found) return found;
            foreach (var child in root.GetChildren()) { var value = FindObjectWindow(child); if (value != null) return value; }
            return null;
        }
        private void Shot(string name)
        {
            if (shots == null) return;
            Directory.CreateDirectory(shots);
            Check(ViewportCapture.SavePng(GetViewport(), Path.Combine(shots, name + ".png")), "saved screenshot " + name);
        }
        private void Check(bool ok, string detail)
        {
            if (ok) passed++; else failed++;
            GameLogger.Log("TEST", (ok ? "[PASS] " : "[FAIL] ") + detail);
        }
        private void Finish()
        {
            timer.Stop();
            objectWindow?.QueueFree();
            window?.QueueFree();
            foreach (var path in FixtureFiles().ToList()) if (!backups.ContainsKey(path)) File.Delete(path);
            foreach (var pair in backups) File.WriteAllBytes(pair.Key, pair.Value);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=rpg_map_events passed={passed} failed={failed}");
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
