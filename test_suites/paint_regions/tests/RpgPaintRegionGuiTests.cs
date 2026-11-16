using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Settings;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>Uses the real utility selector and canvas release transaction in both projections and locales.</summary>
    public sealed class RpgPaintRegionGuiTests
    {
        [ModTest("paint_regions/native_fill_release_undo_and_brush_default", TimeoutSeconds = 90)]
        static async Task Paint(ModTestContext ctx)
        {
            var previous = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "en";
            var locales = new[] { "ru", "en" };
            try
            {
                foreach (var locale in locales)
                {
                    LocaleLoader.LoadLocale(locale);
                    await Run(locale);
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        [ModTest("paint_regions/native_rectangle_preview_release_erase_and_collision", TimeoutSeconds = 90)]
        static async Task Rectangle(ModTestContext ctx)
        {
            var previous = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "en";
            var locales = new[] { "ru", "en" };
            var projections = new[]
            {
                RpgEditorCanvas.TopDown,
                RpgEditorCanvas.Isometric,
            };
            try
            {
                foreach (var locale in locales)
                {
                    LocaleLoader.LoadLocale(locale);
                    foreach (var projection in projections)
                    {
                        await RectangleRun(locale, projection);
                    }
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private static async Task RectangleRun(string locale, string projection)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile("rpg_editor__paint_regions", "maps/rpg_fill_fixture.json");
                window.SetProjection(projection);
                window.ChooseObject("village_pack/ground/water", RpgObjectForm.Surface);
                await Frames(4);
                var modes = window.FindChild("PaintMode", true, false) as OptionButton;
                ModAssert.NotNull(modes, "rectangle uses the utility gesture selector");
                ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/paint_rectangle"), modes.GetItemText(2),
                    "rectangle caption follows the selected locale");
                modes.Select(2);
                modes.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
                var before = window.Document.Data.ToJsonString();
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var first = new Vector2I(4, 3);
                var last = new Vector2I(2, 1);
                Send(window, first, MouseButton.Left, true);
                using var drag = new InputEventMouseMotion
                {
                    Position = window.Canvas.CanvasPointOf(last),
                    ButtonMask = MouseButtonMask.Left,
                    Relative = window.Canvas.CanvasPointOf(last) - window.Canvas.CanvasPointOf(first),
                };
                window.Canvas._GuiInput(drag);
                await Frames(2);
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "rectangle drag is preview-only");
                ModAssert.False(window.Document.CanUndo, "rectangle preview adds no history");
                Send(window, last, MouseButton.Left, false);
                await Frames(3);
                AssertRectangle(window.Document, "village_pack/ground/water");
                using var leave = new InputEventMouseMotion { Position = new Vector2(-100, -100) };
                window.Canvas._GuiInput(leave);
                await Frames(2);
                SaveShot("rectangle_" + locale + "_" + projection);
                var painted = window.Document.Data.ToJsonString();
                ModAssert.True(window.Undo(), "one undo removes the rectangle");
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "rectangle undo restores exact map data");
                ModAssert.False(window.Document.CanUndo, "drag created only one undo step");
                ModAssert.True(window.Redo(), "one redo restores the rectangle");
                ModAssert.Equal(painted, window.Document.Data.ToJsonString(), "redo restores exact painted data");
                Send(window, first, MouseButton.Right, true);
                Send(window, last, MouseButton.Right, false);
                await Frames(3);
                AssertRectangle(window.Document, null);
                ModAssert.True(window.Undo(), "right-button clear is one transaction");
                ModAssert.Equal(painted, window.Document.Data.ToJsonString(), "undo clear restores the full rectangle");
                ModAssert.True(window.Undo(), "undo the original rectangle");
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "ground probe returns to exact source");
                window.SelectTool(RpgEditorTool.Collision);
                Send(window, Vector2I.Zero, MouseButton.Left, true);
                Send(window, new Vector2I(1, 2), MouseButton.Left, false);
                await Frames(3);
                for (var y = 0; y < window.Document.Height; y++)
                {
                    for (var x = 0; x < window.Document.Width; x++)
                    {
                        ModAssert.Equal(x <= 1 && y <= 2 ? '#' : '.', window.Document.CodeAt(x, y),
                            "release coordinates complete the collision rectangle even without a motion event");
                    }
                }

                ModAssert.True(window.Undo(), "collision rectangle has one undo");
                Send(window, new Vector2I(2, 0), MouseButton.Right, true);
                Send(window, new Vector2I(3, 0), MouseButton.Right, false);
                await Frames(3);
                ModAssert.Equal('o', window.Document.CodeAt(2, 0), "right collision rectangle explicitly opens its first cell");
                ModAssert.Equal('o', window.Document.CodeAt(3, 0), "right collision rectangle explicitly opens its last cell");
                ModAssert.Equal('.', window.Document.CodeAt(4, 0), "right collision rectangle preserves outside cells");
                ModAssert.True(window.Undo(), "explicit open rectangle has one undo");
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "all rectangle probes preserve source JSON");
                ModAssert.Equal(0, window.Document.Changes().Count, "rectangle probes leave no saved changes");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags), "rectangle does not execute events");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static void AssertRectangle(RpgMapDocument document, string inside)
        {
            for (var y = 0; y < document.Height; y++)
            {
                for (var x = 0; x < document.Width; x++)
                {
                    var expected = x >= 2 && x <= 4 && y >= 1 && y <= 3 ? inside
                        : x < 4 ? "village_pack/ground/grass" : "village_pack/ground/town_paving";
                    ModAssert.Equal(expected, document.ObjectAt(RpgStrokeLayer.Ground, x, y),
                        "rectangle changes every included cell and preserves every excluded cell");
                }
            }
        }

        private static async Task Run(string locale)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile("rpg_editor__paint_regions", "maps/rpg_fill_fixture.json");
                await Frames(4);
                var modes = window.FindChild("PaintMode", true, false) as OptionButton;
                var options = window.FindChild("PaintOptions", true, false) as Control;
                ModAssert.NotNull(modes, "paint gestures use the real workbench selector");
                ModAssert.NotNull(options, "paint options have a dedicated visible row");
                ModAssert.Equal(0, modes.Selected, "the existing brush remains the default");
                ModAssert.False(options.Visible, "selection mode does not occupy a paint-options row");
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var before = window.Document.Data.ToJsonString();
                var projections = new[]
                {
                    RpgEditorCanvas.TopDown,
                    RpgEditorCanvas.Isometric
                };
                foreach (var projection in projections)
                {
                    window.SetProjection(projection);
                    window.ChooseObject("village_pack/ground/water", RpgObjectForm.Surface);
                    await Frames(4);
                    ModAssert.True(options.Visible, "painting exposes the gesture row");
                    ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/paint_fill"), modes.GetItemText(1),
                        "fill mode has the active locale's caption");
                    ModAssert.True(modes.GetGlobalRect().End.X <= window.Size.X,
                        "the gesture selector stays reachable without horizontal toolbar scrolling");
                    modes.Select(1);
                    modes.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
                    await Frames(3);
                    var origin = new Vector2I(1, 1);
                    Send(window, origin, MouseButton.Left, true);
                    await Frames(2);
                    ModAssert.Equal(before, window.Document.Data.ToJsonString(), "press only previews the region");
                    ModAssert.False(window.Document.CanUndo, "preview is not an undo transaction");
                    using var drag = new InputEventMouseMotion
                    {
                        Position = window.Canvas.CanvasPointOf(new Vector2I(6, 1)),
                        ButtonMask = MouseButtonMask.Left,
                        Relative = window.Canvas.CanvasPointOf(new Vector2I(6, 1)) - window.Canvas.CanvasPointOf(origin),
                    };
                    window.Canvas._GuiInput(drag);
                    Send(window, new Vector2I(6, 1), MouseButton.Left, false);
                    await Frames(4);
                    AssertHalf(window.Document, "village_pack/ground/water");
                    using var leave = new InputEventMouseMotion
                    {
                        Position = new Vector2(-100, -100),
                    };
                    window.Canvas._GuiInput(leave);
                    await Frames(2);
                    SaveShot(locale + "_" + projection);
                    ModAssert.True(window.Undo(), "one real undo retires the full fill");
                    ModAssert.Equal(before, window.Document.Data.ToJsonString(), "undo restores exact authored data");
                    ModAssert.False(window.Document.CanUndo, "no per-cell history remains");
                    ModAssert.True(window.Redo(), "one real redo restores the entire fill");
                    AssertHalf(window.Document, "village_pack/ground/water");
                    ModAssert.True(window.Undo(), "reset before the next gesture");
                    modes.Select(0);
                    modes.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                    await Frames(2);
                    Send(window, origin, MouseButton.Left, true);
                    Send(window, origin, MouseButton.Left, false);
                    await Frames(3);
                    ModAssert.Equal("village_pack/ground/water", window.Document.ObjectAt(RpgStrokeLayer.Ground, 1, 1),
                        "ordinary brush still paints its clicked cell");
                    ModAssert.Equal("village_pack/ground/grass", window.Document.ObjectAt(RpgStrokeLayer.Ground, 2, 1),
                        "default brush does not turn into a fill");
                    ModAssert.True(window.Undo(), "the ordinary brush also retains its transaction");
                    ModAssert.Equal(before, window.Document.Data.ToJsonString(), "all gesture probes return to original data");
                }

                window.SelectTool(RpgEditorTool.Select);
                await Frames(2);
                ModAssert.False(options.Visible, "leaving paint tools hides their options");
                ModAssert.Equal(0, window.Document.Changes().Count, "the probe leaves no saved changes");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags),
                    "authoring never executes event commands");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static void AssertHalf(RpgMapDocument document, string left)
        {
            for (var y = 0; y < document.Height; y++)
            {
                for (var x = 0; x < document.Width; x++)
                {
                    ModAssert.Equal(x < 4 ? left : "village_pack/ground/town_paving",
                        document.ObjectAt(RpgStrokeLayer.Ground, x, y), "release commits only the starting connected region");
                }
            }
        }

        private static void Send(RpgEditorWindow window, Vector2I cell, MouseButton button, bool pressed)
        {
            using var input = new InputEventMouseButton
            {
                Position = window.Canvas.CanvasPointOf(cell),
                ButtonIndex = button,
                Pressed = pressed,
            };
            window.Canvas._GuiInput(input);
        }

        private static void SaveShot(string name)
        {
            var folder = System.Environment.GetEnvironmentVariable("ISO2D_FILL_SHOTS");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Godot.Engine.GetMainLoop()).Root,
                Path.Combine(folder, name + ".png")), "native capture uses the shared colour pipeline");
        }

        private static async Task Frames(int count)
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var frame = 0; frame < count; frame++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
