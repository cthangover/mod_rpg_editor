namespace Cthangover.Tools.RpgEditor.Tests.Lighting
{
    using System;
    using System.IO;
    using System.Linq;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.UI.Tool.LightEditor;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Drives the real existing light editor, its map target and rendered pixels; restores the fixture even on failure.</summary>
    public partial class RpgLightingTestDriver : Node
    {
        private int readinessToken = int.MinValue;
        private ulong readinessStarted;
        private ulong readinessFrame;
        private const string Mod = "rpg_editor__lighting";
        private const string MapPath = "maps/rpg_lighting_test.json";
        private readonly Timer timer = new() { WaitTime = 0.05, Autostart = true };
        private LightEditorWindow window;
        private RpgEditorCanvas canvas;
        private int step;
        private int projection;
        private int passed;
        private int failed;
        private double elapsed;
        private float unlit;
        private string original;
        private string shots;
        private string previewMap;
        private string previewBefore;
        private string foreignBefore;
        private string patchBefore;
        private bool finished;
        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--light-preview-map=", StringComparison.Ordinal)) previewMap = arg.Substring(20);
            AddChild(timer); timer.Timeout += Tick;
        }
        private void Tick()
        {
            if (finished) return;
            elapsed += timer.WaitTime;
            if (elapsed > 90) { Check(false, "timeout at " + step); Finish(); return; }
            try { Run(); } catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        // Deferred UI/scene work must finish before the original assertions run.
        private bool WaitForReadyFrames(int token, bool ready, double timeoutSeconds = 30)
        {
            var now = global::Godot.Time.GetTicksMsec();
            if (readinessToken != token)
            {
                readinessToken = token;
                readinessStarted = now;
                readinessFrame = 0;
            }
            if (now - readinessStarted > timeoutSeconds * 1000)
                throw new global::System.TimeoutException($"readiness at step {token}: scene/UI did not become ready");
            if (!ready) { readinessFrame = 0; return false; }
            if (readinessFrame == 0) readinessFrame = global::Godot.Engine.GetProcessFrames();
            return global::Godot.Engine.GetProcessFrames() - readinessFrame >= 2;
        }

        private void Run()
        {
            if (!WaitForReadyFrames(step + projection * 100, Boot.IsReady && RpgWorldHost.Current?.View != null && !Cthangover.Core.Scenes.GodotSceneService.IsTransitioning && (step == 0 || window != null && window.Visible && canvas?.Scene != null && canvas.Size.X > 0), 30)) return;
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || RpgWorldHost.Current?.Session?.Map.ID != "rpg_lighting_test") return;
                    original = ModFileService.ReadText(Mod, MapPath);
                    foreignBefore = ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json");
                    patchBefore = ModFileService.ReadText(Mod, "patches/rpg_map/" + Mod + ".json");
                    window = ToolWindow.Open<LightEditorWindow>();
                    var kind = Find<OptionButton>("LightTargetKind");
                    Check(kind.ItemCount > 1, "existing editor discovers optional map target");
                    kind.Select(1); kind.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                    var mod = Find<OptionButton>("LightMapMod");
                    var index = Enumerable.Range(0, mod.ItemCount).First(i => mod.GetItemText(i) == Mod);
                    mod.Select(index); mod.EmitSignal(OptionButton.SignalName.ItemSelected, index);
                    var ownMapPicker = Find<OptionButton>("LightMap");
                    var ownMapIndex = Enumerable.Range(0, ownMapPicker.ItemCount).First(i => ownMapPicker.GetItemText(i) == "rpg_lighting_test");
                    ownMapPicker.Select(ownMapIndex); ownMapPicker.EmitSignal(OptionButton.SignalName.ItemSelected, ownMapIndex);
                    canvas = window.FindChildren("*", "", true, false).OfType<RpgEditorCanvas>().Single();
                    Check(window.ActiveTarget != null && window.Controller.Lights.Count == 0, "isolated map opens without creating a source");
                    Check(canvas.Scene.Size == new Vector2I(10, 8), "fixture map selected explicitly before testing projected coordinates");
                    SetProjection();
                    step++;
                    return;
                case 1:
                    unlit = Brightness();
                    Mouse(true, new Vector2(3.5f, 3.5f), true);
                    Mouse(false, new Vector2(3.5f, 3.5f));
                    step++;
                    return;
                case 2:
                    Check(window.Controller.Lights.Count == 1, "double-click creates one independent light, projection " + projection);
                    Check(window.Controller.Selected.Id.StartsWith(Mod + "/", StringComparison.Ordinal), "new source has its owning mod's stable identity");
                    Check(Math.Abs(window.Controller.Selected.Radius - 3) < 0.001f, "map radius uses cells in the shared controls");
                    var lit = Brightness();
                    Check(lit > unlit + 0.03f, $"rendered map becomes brighter ({unlit:0.000} -> {lit:0.000}) in projection {projection}");
                    Mouse(true, new Vector2(3.5f, 3.5f));
                    var point = canvas.CanvasPointAt(new Vector2(5.25f, 4.25f));
                    canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = point, ButtonMask = MouseButtonMask.Left });
                    Mouse(false, new Vector2(5.25f, 4.25f));
                    Check(Math.Abs(window.Controller.Selected.X - 0.525f) < 0.001f && Math.Abs(window.Controller.Selected.Y - 0.53125f) < 0.001f,
                        "projected dragging preserves subcell position");
                    SendKey(Key.Z);
                    Check(Math.Abs(window.Controller.Selected.X - 0.35f) < 0.001f, "one undo restores the complete drag");
                    SendKey(Key.Y);
                    Check(Math.Abs(window.Controller.Selected.X - 0.525f) < 0.001f, "redo restores dragged position");
                    Find<SpinBox>("LightRadius").Value = 4.5;
                    Find<SpinBox>("LightInfluence").Value = 0.65;
                    Check(Math.Abs(window.Controller.Selected.Radius - 4.5f) < 0.001f && Math.Abs(window.Controller.Selected.Influence - 0.65f) < 0.001f,
                        "existing radius and strength fields edit the same map source");
                    Press("tools/common/save");
                    Check(!window.ActiveTarget.IsDirty && ModFileService.ReadText(Mod, MapPath).Contains("light_1"), "Save writes the map resource");
                    step++;
                    return;
                case 3:
                    if (RpgWorldHost.Current.Session.Map.Lights.Count != 1) return;
                    var runtime = new RpgWorldLights(RpgWorldHost.Current.Session);
                    var lights = new System.Collections.Generic.List<Cthangover.Mods.TileRender.TileLight>();
                    runtime.Nearest(lights, new Vector2(5, 4));
                    Check(lights.Any(light => light.Position.DistanceTo(new Vector2(5.25f, 4.25f)) < 0.001f), "saved light reaches the running game through normal resource reload: " + string.Join(";", lights.Select(light => light.Position.ToString())));
                    Shot(projection == 0 ? "light_editor_topdown" : "light_editor_isometric");
                    Find<Button>("DeleteLight").EmitSignal(BaseButton.SignalName.Pressed);
                    Press("tools/common/save");
                    Check(window.Controller.Lights.Count == 0, "Delete removes source without a scenery object");
                    if (projection++ == 0) { SetProjection(); step = 1; return; }
                    var mapPicker = Find<OptionButton>("LightMap");
                    var foreignIndex = Enumerable.Range(0, mapPicker.ItemCount).First(i => mapPicker.GetItemText(i) == "rpg_village");
                    mapPicker.Select(foreignIndex); mapPicker.EmitSignal(OptionButton.SignalName.ItemSelected, foreignIndex);
                    Find<Button>("AddLight").EmitSignal(BaseButton.SignalName.Pressed);
                    Press("tools/common/save");
                    var patch = ModFileService.ReadText(Mod, "patches/rpg_map/" + Mod + ".json");
                    Check(patch != null && patch.Contains("Lights$add") && patch.Contains(Mod + "/light_"), "shared editor saves foreign map lights as keyed dependent patches");
                    Check(ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json") == foreignBefore, "foreign owner's map stays byte-identical");
                    Find<Button>("DeleteLight").EmitSignal(BaseButton.SignalName.Pressed);
                    Press("tools/common/save");
                    Check(ModFileService.ReadText(Mod, "patches/rpg_map/" + Mod + ".json") == patchBefore, "removing the added source removes its patch again");
                    var legacy = Find<OptionButton>("LightTargetKind");
                    legacy.Select(0); legacy.EmitSignal(OptionButton.SignalName.ItemSelected, 0);
                    Check(window.ActiveTarget == null && window.Controller.NewRadius == 400 && !window.Controller.UseIds, "switching back restores the original background editor");
                    if (previewMap != null)
                    {
                        legacy.Select(1); legacy.EmitSignal(OptionButton.SignalName.ItemSelected, 1);
                        var modPick = Find<OptionButton>("LightMapMod");
                        var ownerIndex = Enumerable.Range(0, modPick.ItemCount).First(i => modPick.GetItemText(i) == "junior_reaper_paper_grief");
                        modPick.Select(ownerIndex); modPick.EmitSignal(OptionButton.SignalName.ItemSelected, ownerIndex);
                        var previewPicker = Find<OptionButton>("LightMap");
                        var mapIndex = Enumerable.Range(0, previewPicker.ItemCount).First(i => previewPicker.GetItemText(i) == previewMap);
                        previewPicker.Select(mapIndex); previewPicker.EmitSignal(OptionButton.SignalName.ItemSelected, mapIndex);
                        canvas = window.FindChildren("*", "", true, false).OfType<RpgEditorCanvas>().Single();
                        previewBefore = ModFileService.ReadText("junior_reaper_paper_grief", "maps/" + previewMap + ".json");
                        Find<Button>("AddLight").EmitSignal(BaseButton.SignalName.Pressed);
                        step = 4;
                        return;
                    }
                    Finish();
                    return;
                case 4:
                    Shot("light_editor_" + previewMap);
                    Check(ModFileService.ReadText("junior_reaper_paper_grief", "maps/" + previewMap + ".json") == previewBefore,
                        "actual junior reaper map preview does not change its source file");
                    Finish();
                    return;
            }
        }
        private T Find<T>(string name) where T : Node => window.FindChildren(name, "", true, false).OfType<T>().Single();
        private void SetProjection()
        {
            var picker = Find<OptionButton>("LightMapProjection");
            picker.Select(projection); picker.EmitSignal(OptionButton.SignalName.ItemSelected, projection);
            canvas.FitToView();
        }
        private void Mouse(bool pressed, Vector2 at, bool twice = false)
        {
            canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = pressed, DoubleClick = twice, Position = canvas.CanvasPointAt(at) });
        }
        private void SendKey(Key key) => window._UnhandledInput(new InputEventKey { Keycode = key, CtrlPressed = true, Pressed = true });
        private void Press(string key)
        {
            var text = ToolUi.Tr(key);
            window.FindChildren("*", "", true, false).OfType<Button>().First(button => button.Text == text).EmitSignal(BaseButton.SignalName.Pressed);
        }
        private float Brightness()
        {
            using var image = canvas.GetViewport().GetTexture().GetImage();
            var at = canvas.GlobalPosition + canvas.CanvasPointAt(new Vector2(4.0f, 3.5f));
            var pixel = image.GetPixel(Mathf.Clamp((int)at.X, 0, image.GetWidth() - 1), Mathf.Clamp((int)at.Y, 0, image.GetHeight() - 1));
            return (pixel.R + pixel.G + pixel.B) / 3f;
        }
        private void Shot(string name)
        {
            if (shots == null) return;
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
            if (window != null && IsInstanceValid(window)) window.QueueFree();
            if (original != null) ModFileService.WriteText(Mod, MapPath, original);
            var patchPath = "patches/rpg_map/" + Mod + ".json";
            if (patchBefore != null) ModFileService.WriteText(Mod, patchPath, patchBefore);
            else if (ModFileService.ReadText(Mod, patchPath) != null) ModFileService.DeleteFile(Mod, patchPath);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=map_lights passed={passed} failed={failed}");
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
