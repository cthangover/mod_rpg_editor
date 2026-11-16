using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Localization;
using Cthangover.Core.Settings;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Preview
{
    /// <summary>Native authoring flow: state inputs change canvas pixels and actor poses in both projections without writes.</summary>
    public sealed class RpgEditorPreviewGuiTests
    {
        private const string Owner = "rpg_editor__preview";

        private const string Left = "preview_fixture/left";

        private const string Actor = "preview_fixture/actor";

        private RpgEditorWindow window;

        private string locale;

        [ModTest("effective_preview/native_canvas_pixels_and_actor_states")]
        static async Task Canvas(ModTestContext ctx)
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
                    await new RpgEditorPreviewGuiTests().Run(ctx, locale);
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
            ModAssert.True(Cthangover.Core.Utils.GameEnvironment.IsRuntime, "native GUI requires Godot");
            window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1280, 900);
                window.OpenMapFile(Owner, "maps/preview_fixture.json");
                await Frames();
                ModAssert.Equal("preview_fixture", window.Document.MapId, "the neutral fixture opens through the public tool API");
                var mapBefore = window.Document.Data.ToJsonString();
                var flagsBefore = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var projections = new[]
                {
                    RpgEditorCanvas.TopDown,
                    RpgEditorCanvas.Isometric,
                };
                foreach (var projection in projections)
                {
                    window.SetProjection(projection);
                    window.Canvas.FitToView();
                    await Frames();
                    Click(new Vector2I(1, 1));
                    await Frames();
                    ModAssert.Equal(Left, window.SelectedId, "a canvas click selects the fixture placement");
                    var preview = Preview();
                    var open = preview.FindChild("Preview_open", true, false) as CheckBox;
                    var gone = preview.FindChild("Preview_gone", true, false) as CheckBox;
                    ModAssert.NotNull(open, "base-state condition appears in the inspector");
                    ModAssert.NotNull(gone, "placement-added state condition appears in the inspector");
                    open.ButtonPressed = false;
                    gone.ButtonPressed = false;
                    await Frames();
                    await PreparePixelCapture();
                    var closedPixels = PixelCounts();
                    SaveShot(projection + "_closed");
                    ModAssert.False(window.Scene.Walk.IsWalkable(1, 1), "closed preview blocks the actual cell");
                    open.ButtonPressed = true;
                    await Frames();
                    ModAssert.Equal("preview_pixels/blue", window.Scene.ObjectOf(Left).Parts[0].Image,
                        "the checkbox updates the canvas effective object");
                    ModAssert.True(window.Scene.Walk.IsWalkable(1, 1), "the same checkbox updates the walk mask");
                    ModAssert.True(ReferenceEquals(preview, Preview()), "presentation refresh preserves the active inspector");
                    await PreparePixelCapture();
                    var openedPixels = PixelCounts();
                    SaveShot(projection + "_opened");
                    ctx.Log(projection + " canvas pixel counts: closed=" + closedPixels + ", opened=" + openedPixels);
                    ModAssert.True(openedPixels.Blue > closedPixels.Blue + 100,
                        "captured canvas gains the blue state image in this projection");
                    ModAssert.True(openedPixels.Red + 100 < closedPixels.Red,
                        "captured canvas loses the red base image in this projection");
                    gone.ButtonPressed = true;
                    await Frames();
                    ModAssert.True(window.Scene.ObjectOf(Left).Hidden, "placement-added last state hides its presentation");
                    ModAssert.True(window.Scene.Walk.IsWalkable(1, 1), "hidden preview also releases collision");
                    ModAssert.Equal("preview_pixels/blue", window.Scene.ObjectOf("preview_fixture/right").Parts[0].Image,
                        "the other placement preserves its own variant");
                    gone.ButtonPressed = false;
                    open.ButtonPressed = false;
                    Click(new Vector2I(3, 1));
                    await Frames();
                    ModAssert.Equal(Actor, window.SelectedId, "the actor can be selected independently");
                    var actorPreview = Preview();
                    var turn = actorPreview.FindChild("Preview_turn", true, false) as CheckBox;
                    var absent = actorPreview.FindChild("Preview_absent", true, false) as CheckBox;
                    ModAssert.NotNull(turn, "actor state input is available");
                    ModAssert.NotNull(absent, "hidden actor input is available");
                    absent.ButtonPressed = false;
                    turn.ButtonPressed = true;
                    await Frames();
                    var pose = window.Canvas.Actors.Find(HasActorId);
                    ModAssert.NotNull(pose, "visible actor has a native presentation pose");
                    ModAssert.Equal(3, pose.Facing, "state facing reaches the pose");
                    ModAssert.Equal(5, pose.Column, "state frame column reaches the pose");
                    ModAssert.Equal("preview_pixels/sheet_alt", window.Scene.ObjectOf(Actor).Charset,
                        "state sheet replacement reaches the effective actor");
                    SaveShot(projection + "_actor");
                    absent.ButtonPressed = true;
                    await Frames();
                    ModAssert.Null(window.Canvas.Actors.Find(HasActorId),
                        "a hidden actor with a null charset emits no pose or missing-sheet error");
                    absent.ButtonPressed = false;
                    turn.ButtonPressed = false;
                }

                ModAssert.Equal(mapBefore, window.Document.Data.ToJsonString(), "GUI preview leaves map data unchanged");
                ModAssert.False(window.Document.CanUndo, "GUI preview adds no undo entries");
                ModAssert.Equal(0, window.Document.Changes().Count, "GUI preview produces no file changes");
                ModAssert.Equal(flagsBefore, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags),
                    "GUI preview leaves live game flags unchanged");
            }
            finally
            {
                window.QueueFree();
                await Frames();
            }
        }

        private RpgStatePreview Preview()
        {
            var preview = window.Inspector.FindChild("StatePreview", true, false) as RpgStatePreview;
            ModAssert.NotNull(preview, "selected placement has its state widget");
            return preview;
        }

        private void Click(Vector2I cell)
        {
            var position = window.Canvas.CanvasPointOf(cell);
            using var press = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = position,
            };
            using var release = new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = false,
                Position = position,
            };
            window.Canvas._GuiInput(press);
            window.Canvas._GuiInput(release);
        }

        private async Task Frames()
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var index = 0; index < 5; index++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }

        private async Task PreparePixelCapture()
        {
            // Selection marks tint the part itself. Compare source-image presentation with that separate authoring layer detached.
            window.Canvas.Marks.Clear();
            window.Canvas.Refresh();
            await Frames();
        }

        private (int Red, int Blue) PixelCounts()
        {
            var root = ((SceneTree)Godot.Engine.GetMainLoop()).Root;
            using var image = ViewportCapture.Take(root);
            ModAssert.NotNull(image, "native viewport capture succeeds");
            ModAssert.True(image.GetFormat() == Image.Format.Rgba8, "capture exposes displayed RGBA8 pixels");
            var bytes = image.GetData();
            var width = image.GetWidth();
            var at = window.Canvas.GlobalPosition + window.Position;
            var rect = new Rect2I((Vector2I)at, (Vector2I)window.Canvas.Size);
            ModAssert.True(new Rect2I(0, 0, image.GetWidth(), image.GetHeight()).Encloses(rect),
                "the canvas capture region is inside the root viewport");
            var red = 0;
            var blue = 0;
            for (var y = rect.Position.Y; y < rect.End.Y; y++)
            {
                for (var x = rect.Position.X; x < rect.End.X; x++)
                {
                    var offset = (y * width + x) * 4;
                    if (bytes[offset] > 180 && bytes[offset] > bytes[offset + 2] + 100)
                    {
                        red++;
                    }

                    if (bytes[offset + 2] > 180 && bytes[offset + 2] > bytes[offset] + 10)
                    {
                        blue++;
                    }
                }
            }

            return (red, blue);
        }

        private void SaveShot(string name)
        {
            var output = System.Environment.GetEnvironmentVariable("ISO2D_PREVIEW_SHOTS");
            if (string.IsNullOrEmpty(output))
            {
                return;
            }

            Directory.CreateDirectory(output);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Godot.Engine.GetMainLoop()).Root,
                Path.Combine(output, locale + "_" + name + ".png")), "preview capture writes through the shared colour pipeline");
        }

        private static bool HasActorId(Cthangover.Mods.TileRender.TileActorPose pose)
        {
            return pose.Id == Actor;
        }
    }
}
