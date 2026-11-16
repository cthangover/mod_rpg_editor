using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Settings;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.CellRules
{
    /// <summary>Drives actual utility controls in ru/en and both projections, including a remote arrival's solid mask.</summary>
    public sealed class RpgCellRulesGuiTests
    {
        [ModTest("cell_rules/native_inspector_audit_and_cleanup", TimeoutSeconds = 90)]
        static async Task Inspect(ModTestContext ctx)
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

        private static async Task Run(string locale)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile("rpg_editor__cell_rules", "maps/rules_fixture.json");
                await Frames(4);
                var before = window.Document.Data.ToJsonString();
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var toggle = window.FindChild("CellRulesEnabled", true, false) as CheckButton;
                var check = window.FindChild("CellRulesCheck", true, false) as Button;
                var details = window.FindChild("CellRulesDetails", true, false) as TextEdit;
                var results = window.FindChild("CellRulesResults", true, false) as TextEdit;
                ModAssert.NotNull(toggle, "cell rules use the real utility layout");
                ModAssert.NotNull(check, "map audit has an explicit control");
                ModAssert.NotNull(details, "cell explanation is inspectable through the GUI");
                ModAssert.NotNull(results, "audit notices are inspectable through the GUI");
                ModAssert.False(toggle.ButtonPressed || details.Visible || results.Visible,
                    "diagnostics are opt-in and initially collapsed");
                var projections = new[]
                {
                    RpgEditorCanvas.TopDown,
                    RpgEditorCanvas.Isometric
                };
                foreach (var projection in projections)
                {
                    window.SetProjection(projection);
                    window.Canvas.FitToView();
                    toggle.ButtonPressed = true;
                    await Frames(4);
                    var cell = new Vector2I(4, 3);
                    using var motion = new InputEventMouseMotion
                    {
                        Position = window.Canvas.CanvasPointOf(cell),
                    };
                    window.Canvas._GuiInput(motion);
                    await Frames(2);
                    ModAssert.True(details.Text.Contains(ToolUi.Tr("tools/rpg_editor/rules_reason_open")),
                        "the effective force-open reason reaches the visible form");
                    ModAssert.True(details.Text.Contains("Collision o ← Collision[3][4]"),
                        "the form identifies the exact winning row and code");
                    check.EmitSignal(BaseButton.SignalName.Pressed);
                    await Frames(3);
                    ModAssert.True(results.Visible && results.Text.Contains("rules_fixture/remote"),
                        "remote arrival is checked against destination placement/event masks");
                    ModAssert.True(results.Text.Contains(ToolUi.Tr("tools/rpg_editor/rules_issue_arrival")),
                        "arrival warning is localized");
                    ModAssert.True(details.Text.Contains(ToolUi.Tr("tools/rpg_editor/rules_path").Split('{')[0]),
                        "the inspected cell also shows confirmed route length");
                    var inspectorScroll = window.Inspector.GetParent<ScrollContainer>();
                    ModAssert.True(inspectorScroll.GetGlobalRect().End.X <= window.Size.X,
                        "expanded diagnostics preserve the inspector viewport inside the window");
                    ModAssert.True(window.Canvas.Size.X > window.Size.X * 0.35f,
                        "expanded diagnostics preserve useful canvas width");
                    SaveShot(locale + "_" + projection, window);
                    toggle.ButtonPressed = false;
                    await Frames(2);
                    ModAssert.False(details.Visible || results.Visible, "collapse hides results without affecting map data");
                }

                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "inspection/audit never edits the map");
                ModAssert.False(window.Document.CanUndo, "inspection adds no history");
                ModAssert.Equal(0, window.Document.Changes().Count, "inspection adds no saved changes");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags), "inspection changes no live flags");
                window.OpenMapFile("rpg_editor__cell_rules", "maps/rules_destination.json");
                toggle.ButtonPressed = true;
                await Frames(4);
                ModAssert.False(results.Visible, "changing the document retires the previous reachability audit");
                check.EmitSignal(BaseButton.SignalName.Pressed);
                await Frames(3);
                ModAssert.True(results.Text.Contains("rules_fixture/remote"),
                    "checking the destination also diagnoses the incoming link owned by the source map");
                ModAssert.True(results.Text.Contains(ToolUi.Tr("tools/rpg_editor/rules_issue_arrival")),
                    "incoming arrival sees the current destination event mask");
                SaveShot(locale + "_incoming", window);
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static void SaveShot(string name, RpgEditorWindow window)
        {
            var folder = System.Environment.GetEnvironmentVariable("ISO2D_RULES_SHOTS");
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
            for (var i = 0; i < count; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
