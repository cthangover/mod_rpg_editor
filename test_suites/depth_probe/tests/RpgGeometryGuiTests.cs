using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Settings;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Cthangover.Mods.TileRender;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.DepthProbe
{
    /// <summary>Runs real diagnostic controls around three shapes in both projections and checks presentation-only ownership.</summary>
    public sealed class RpgGeometryGuiTests
    {
        private const string Owner = "rpg_editor__depth_probe";

        private RpgEditorWindow window;

        private string locale;

        [ModTest("geometry_probe/native_steps_playback_and_cleanup", TimeoutSeconds = 90)]
        static async Task Geometry(ModTestContext ctx)
        {
            var previous = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "en";
            var locales = new[]
            {
                "ru",
                "en",
            };
            try
            {
                foreach (var locale in locales)
                {
                    LocaleLoader.LoadLocale(locale);
                    await new RpgGeometryGuiTests().Run(ctx, locale);
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private async Task Run(ModTestContext ctx, string locale)
        {
            this.locale = locale;
            window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile(Owner, "maps/geometry_fixture.json");
                await Frames(4);
                var before = window.Document.Data.ToJsonString();
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var toggle = window.FindChild("GeometryEnabled", true, false) as CheckButton;
                ModAssert.NotNull(toggle, "geometry control is visible through the real workbench");
                var projections = new[]
                {
                    RpgEditorCanvas.TopDown,
                    RpgEditorCanvas.Isometric,
                };
                var cases = new[]
                {
                    ("geometry_fixture/desk", new Vector2I(4, 5)),
                    ("geometry_fixture/tree", new Vector2I(9, 3)),
                    ("geometry_fixture/wall", new Vector2I(3, 1)),
                    ("geometry_fixture/desk_event", new Vector2I(4, 5)),
                };
                foreach (var projection in projections)
                {
                    window.SetProjection(projection);
                    window.Canvas.FitToView();
                    await Frames(4);
                    foreach (var (id, cell) in cases)
                    {
                        Select(id, cell);
                        toggle.ButtonPressed = true;
                        await Frames(4);
                        var inspectorScroll = window.Inspector.GetParent<ScrollContainer>();
                        ModAssert.True(inspectorScroll.GetGlobalRect().End.X <= window.Size.X,
                            "the selected form stays inside the tool window");
                        ModAssert.True(window.Canvas.Size.X > window.Size.X * 0.35f,
                            "long event references preserve a useful canvas width");
                        var model = RpgGeometryProbe.Around(window.Scene, id);
                        ModAssert.True(model.Closed, "fixture has a complete perimeter: " + id);
                        var pose = Probe();
                        ModAssert.NotNull(pose, "enabled diagnostic attaches a real renderer pose");
                        var start = pose.Position;
                        var step = Named<Button>("GeometryStep");
                        for (var index = 0; index < model.Cells.Count; index++)
                        {
                            ModAssert.False(step.Disabled, "manual stepping remains available");
                            step.EmitSignal(BaseButton.SignalName.Pressed);
                            await Frames(2);
                            pose = Probe();
                            var expected = model.Cells[(index + 1) % model.Cells.Count];
                            ModAssert.True(pose.Position == (Vector2)expected, "the next native pose follows the recorded ring");
                            ModAssert.True(window.Scene.Walk.IsWalkable(expected.X, expected.Y), "the figure never enters a blocked cell");
                            if (index == model.Cells.Count / 2)
                            {
                                SaveShot(projection + "_" + id.Split('/')[1] + "_opposite");
                            }
                        }

                        ModAssert.True(Probe().Position == start, "manual traversal returns to the same starting point");
                        SaveShot(projection + "_" + id.Split('/')[1] + "_start");
                        var play = Named<Button>("GeometryPlay");
                        play.EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames(12);
                        ModAssert.True(Probe().Position != start, "the ordinary frame process advances only the preview figure");
                        play.EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames(2);
                        Named<Button>("GeometryReset").EmitSignal(BaseButton.SignalName.Pressed);
                        await Frames(2);
                        ModAssert.True(Probe().Position == start, "restart is deterministic");
                        toggle.ButtonPressed = false;
                        await Frames(2);
                        ModAssert.Null(Probe(), "disabling detaches the renderer borrower");
                    }
                }

                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "diagnostic controls do not edit map data");
                ModAssert.False(window.Document.CanUndo, "walk-around adds no undo entries");
                ModAssert.Equal(0, window.Document.Changes().Count, "walk-around produces no saved file changes");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags), "walk-around changes no live flags");
                ctx.Log(locale + " geometry loops completed without authoring/gameplay writes");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private void Select(string id, Vector2I cell)
        {
            window.SelectTool(RpgEditorTool.Select);
            var count = window.Scene.Statics.Count + window.Scene.Actors.Count + window.Scene.Events.Count + 1;
            for (var index = 0; index < count && window.SelectedId != id; index++)
            {
                var point = window.Canvas.CanvasPointOf(cell);
                using var press = new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Pressed = true,
                    Position = point,
                };
                using var release = new InputEventMouseButton
                {
                    ButtonIndex = MouseButton.Left,
                    Position = point,
                };
                window.Canvas._GuiInput(press);
                window.Canvas._GuiInput(release);
            }

            ModAssert.Equal(id, window.SelectedId, "the author selects the intended overlapping placement by repeated clicks");
        }

        private TileActorPose Probe()
        {
            return window.Canvas.Actors.Find(IsProbe);
        }

        private static bool IsProbe(TileActorPose pose)
        {
            return pose.Id == "rpg_editor:geometry_probe";
        }

        private T Named<T>(string name) where T : Node
        {
            var node = window.FindChild(name, true, false) as T;
            ModAssert.NotNull(node, "utility control exists: " + name);
            return node;
        }

        private static async Task Frames(int count)
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var index = 0; index < count; index++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }

        private void SaveShot(string name)
        {
            var output = System.Environment.GetEnvironmentVariable("ISO2D_GEOMETRY_SHOTS");
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            Directory.CreateDirectory(output);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Godot.Engine.GetMainLoop()).Root,
                Path.Combine(output, locale + "_" + name + ".png")), "geometry capture uses the shared colour pipeline");
        }
    }
}
