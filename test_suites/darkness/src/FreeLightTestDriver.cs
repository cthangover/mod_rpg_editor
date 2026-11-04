namespace Cthangover.Tools.RpgEditor.Tests.FreeLights
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
    using Cthangover.Mods.TileRender;
    using Cthangover.Mods.Time;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Measures actual wall pixels, compares the novel's night shader, and exercises the real shared lamp in the running world.</summary>
    public partial class FreeLightTestDriver : Node
    {
        private const string Mod = "rpg_editor__darkness";
        private const string MapPath = "maps/rpg_light_wall.json";
        private readonly Timer timer = new() { WaitTime = 0.45, Autostart = true };
        private LightEditorWindow window;
        private RpgEditorCanvas canvas;
        private TextureRect novel;
        private ShaderMaterial novelMaterial;
        private ImageTexture probeTexture;
        private string original;
        private string shots;
        private int step;
        private int projection;
        private int passed;
        private int failed;
        private double elapsed;
        private bool finished;
        private float day;
        private float dark;
        private Vector2 wall;
        private Vector2 lampBefore;
        private float lampRadiusBefore;
        private float lampStrengthBefore;
        private bool lampCaptured;
        private Vector2 runtimeWall;
        private float runtimeUnit;

        public override void _Ready()
        {
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--shots=", StringComparison.Ordinal)) shots = arg.Substring(8);
            AddChild(timer); timer.Timeout += Tick;
        }
        private void Tick()
        {
            if (finished) return;
            elapsed += timer.WaitTime;
            if (elapsed > 90) { Check(false, "timeout at " + step); Finish(); return; }
            try { Run(); } catch (Exception ex) { Check(false, ex.ToString()); Finish(); }
        }
        private void Run()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady || RpgWorldHost.Current?.Session?.Map.ID != "rpg_light_wall") return;
                    original = ModFileService.ReadText(Mod, MapPath);
                    window = ToolWindow.Open<LightEditorWindow>();
                    Select("LightTargetKind", 1);
                    var picker = Find<OptionButton>("LightMapMod");
                    Select("LightMapMod", Enumerable.Range(0, picker.ItemCount).First(i => picker.GetItemText(i) == Mod));
                    picker = Find<OptionButton>("LightMap");
                    Select("LightMap", Enumerable.Range(0, picker.ItemCount).First(i => picker.GetItemText(i) == "rpg_light_wall"));
                    canvas = window.FindChildren("*", "", true, false).OfType<RpgEditorCanvas>().Single();
                    CreateNovelProbe();
                    PrepareDay();
                    step = 1;
                    return;
                case 1:
                    wall = WallCenter();
                    day = Pixel(canvas.GetViewport(), canvas.GlobalPosition + wall + Vector2.Right * Unit() * 0.3f);
                    Check(day > 0.3f, "day reveals the neutral wall, projection " + projection + ": " + day);
                    Check(canvas.CellAt(wall) == null, "wall target lies above the walkable floor");
                    Select("LightMapMode", 2);
                    step = 2;
                    return;
                case 2:
                    dark = Pixel(canvas.GetViewport(), canvas.GlobalPosition + wall + Vector2.Right * Unit() * 0.3f);
                    var novelDark = Pixel(novel.GetViewport(), novel.GlobalPosition + novel.Size / 2);
                    Check(dark < day * 0.2f, $"night conceals unlit wall details ({dark:0.000} < 20% of {day:0.000})");
                    Check(Math.Abs(dark - novelDark) < 0.03f, $"RPG night matches the existing novel shader ({dark:0.000}, {novelDark:0.000})");
                    Shot("night_without_light_" + projection);
                    Mouse(true, wall, true); Mouse(false, wall);
                    Check(window.Controller.Lights.Count == 1, "double-click creates a free source over a wall outside the floor");
                    Check(window.Controller.Selected.Y < 0, "wall coordinate is preserved without floor clamping");
                    Find<SpinBox>("LightRadius").Value = 2;
                    var color = Find<ColorPickerButton>("LightColor");
                    color.Color = Colors.White; color.EmitSignal(ColorPickerButton.SignalName.ColorChanged, Colors.White);
                    step = 3;
                    return;
                case 3:
                    var lit = Pixel(canvas.GetViewport(), canvas.GlobalPosition + wall + Vector2.Right * Unit() * 0.3f);
                    var horizontal = Pixel(canvas.GetViewport(), canvas.GlobalPosition + wall + Vector2.Right * Unit() * 0.7f);
                    var vertical = Pixel(canvas.GetViewport(), canvas.GlobalPosition + wall + Vector2.Down * Unit() * 0.7f);
                    Check(lit > day * 0.7f, $"static source reveals the wall ({lit:0.000} > 70% of {day:0.000})");
                    Check(Math.Abs(horizontal - vertical) < 0.03f, $"equal image-space distances have equal light ({horizontal:0.000}, {vertical:0.000})");
                    Shot("wall_light_" + projection);
                    Mouse(true, wall);
                    var target = wall + Vector2.Up * Unit() * 0.25f;
                    canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseMotion { Position = target, ButtonMask = MouseButtonMask.Left });
                    Mouse(false, target);
                    Check(window.Controller.Selected.Y < 0, "dragging farther up the wall stays free");
                    var before = window.Controller.Selected.X;
                    Find<SpinBox>("LightRadius").Value = 2.5;
                    Check(Math.Abs(window.Controller.Selected.X - before) < 0.000001f, "changing radius preserves the free position");
                    Press("tools/common/save");
                    step = 4;
                    return;
                case 4:
                    if (RpgWorldHost.Current.Session.Map.Lights.Count != 1) return;
                    var mapLight = RpgWorldHost.Current.Session.Map.Lights[0];
                    var renderLights = RpgMapLights.Of(RpgWorldHost.Current.Session.Map);
                    Check(mapLight.Y < 0 && renderLights.Single().ViewSpace, "saved free wall source reaches the live renderer");
                    Find<Button>("DeleteLight").EmitSignal(BaseButton.SignalName.Pressed);
                    Press("tools/common/save");
                    if (projection++ == 0) { PrepareDay(); step = 1; return; }
                    window.Hide();
                    RpgWorldHost.Current.View.SetRenderer(RpgEditorCanvas.Isometric);
                    var controller = UiLightController.Instance;
                    controller.TryGetPortableLight(out lampBefore, out _, out lampRadiusBefore, out lampStrengthBefore);
                    lampCaptured = true;
                    controller.UpdateLightPos(new Vector2(-1000, -1000));
                    step = 5;
                    return;
                case 5:
                    var world = RpgWorldHost.Current.View;
                    var origin = world.GlobalTransform * world.Presentation.ProjectToView(new Vector3(0, 0, 0));
                    var axis = world.GlobalTransform * world.Presentation.ProjectToView(new Vector3(1, 0, 0));
                    runtimeUnit = axis.X - origin.X;
                    runtimeWall = world.GlobalTransform * world.Presentation.ProjectToView(new Vector3(5.5f, 2, 0)) - Vector2.Down * runtimeUnit * 3;
                    var noLamp = Pixel(GetViewport(), runtimeWall + Vector2.Right * runtimeUnit * 0.3f);
                    Check(noLamp < day * 0.2f, "running world stays dark when the shared detective lamp is absent");
                    UiLightController.Instance.SetPlayerLight(runtimeWall, runtimeUnit * 1.2f, 1);
                    UiLightController.Instance.UpdateLightPos(runtimeWall);
                    step = 6;
                    return;
                case 6:
                    UiLightController.Instance.TryGetPortableLight(out var actualPos, out var actualColor, out var actualRadius, out var actualStrength);
                    var mat = RpgWorldHost.Current.View.Presentation.SharedMaterial;
                    GameLogger.Log("TEST", $"lamp diagnostic: wall={runtimeWall}, unit={runtimeUnit}, position={actualPos}, color={actualColor}, radius={actualRadius}, strength={actualStrength}, uniformPosition={mat.GetShaderParameter("portable_position")}, uniformColor={mat.GetShaderParameter("portable_color")}, uniformRadius={mat.GetShaderParameter("portable_radius")}");
                    var lamp = Pixel(GetViewport(), runtimeWall + Vector2.Right * runtimeUnit * 0.25f);
                    var outside = Pixel(GetViewport(), runtimeWall + Vector2.Right * runtimeUnit * 1.4f);
                    Check(lamp > day * 0.5f, $"shared toolbar lamp reveals wall evidence ({lamp:0.000})");
                    Check(outside < day * 0.2f, "outside the portable lamp's radius the same wall remains hidden");
                    Shot("detective_lamp_runtime");
                    UiLightController.Instance.UpdateLightPos(new Vector2(-1000, -1000));
                    step = 7;
                    return;
                case 7:
                    Check(Pixel(GetViewport(), runtimeWall + Vector2.Right * runtimeUnit * 0.25f) < day * 0.2f,
                        "removing the shared lamp conceals the evidence again");
                    Finish();
                    return;
            }
        }
        private T Find<T>(string name) where T : Node => window.FindChildren(name, "", true, false).OfType<T>().Single();
        private void Select(string name, int index)
        {
            var picker = Find<OptionButton>(name); picker.Select(index); picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
        private void PrepareDay()
        {
            Select("LightMapProjection", projection);
            var night = Find<CheckButton>("LightNightPreview"); night.ButtonPressed = false;
            Select("LightMapMode", 1);
            canvas.FitToView();
        }
        private float Unit() => (canvas.CanvasPointAt(Vector2.Right) - canvas.CanvasPointAt(Vector2.Zero)).X;
        private Vector2 WallCenter() => canvas.CanvasPointAt(new Vector2(5.5f, 2)) - Vector2.Down * Unit() * 3;
        private void Mouse(bool down, Vector2 position, bool twice = false)
            => canvas.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = down, Position = position, DoubleClick = twice });
        private void Press(string key)
        {
            var label = ToolUi.Tr(key);
            window.FindChildren("*", "", true, false).OfType<Button>().First(button => button.Text == label).EmitSignal(BaseButton.SignalName.Pressed);
        }
        private void CreateNovelProbe()
        {
            using var image = Image.CreateEmpty(8, 8, false, Image.Format.Rgba8);
            image.Fill(Color.FromHtml("#b4b4b4"));
            probeTexture = ImageTexture.CreateFromImage(image);
            novelMaterial = new ShaderMaterial { Shader = Shaders.Resolve("timed_sprite") };
            novelMaterial.SetShaderParameter("time_weights", new Vector4(0, 0, 0, 1));
            novelMaterial.SetShaderParameter("use_time", true);
            novelMaterial.SetShaderParameter("light_count", 0);
            novel = new TextureRect { Position = new Vector2(10, 10), Size = new Vector2(48, 48), Texture = probeTexture,
                Material = novelMaterial, MouseFilter = Control.MouseFilterEnum.Ignore, ZIndex = 4090 };
            canvas.AddChild(novel);
        }
        private static float Pixel(Viewport viewport, Vector2 point)
        {
            using var image = ViewportCapture.Take(viewport);
            if (point.X < 0 || point.Y < 0 || point.X >= image.GetWidth() || point.Y >= image.GetHeight()) throw new InvalidOperationException("Pixel probe outside viewport: " + point);
            var color = image.GetPixel((int)point.X, (int)point.Y);
            return (color.R + color.G + color.B) / 3;
        }
        private void Shot(string label)
        {
            if (shots == null) return;
            Directory.CreateDirectory(shots);
            ViewportCapture.SavePng(GetViewport(), Path.Combine(shots, label + ".png"));
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
            if (lampCaptured && UiLightController.Instance != null)
            {
                UiLightController.Instance.SetPlayerLight(lampBefore, lampRadiusBefore, lampStrengthBefore);
                UiLightController.Instance.UpdateLightPos(lampBefore);
            }
            if (novel != null && IsInstanceValid(novel)) { novel.Texture = null; novel.Material = null; }
            novelMaterial?.Dispose(); probeTexture?.Dispose();
            if (window != null && IsInstanceValid(window)) window.QueueFree();
            if (original != null) ModFileService.WriteText(Mod, MapPath, original);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=free_lights passed={passed} failed={failed}");
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }
    }
}
