namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Settings;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg.Validation;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>Drives the collapsible report, exact finding selection and stale-state lifecycle in a native utility window.</summary>
    public sealed class RpgDataGuiTests
    {
        private RpgEditorWindow window;

        [ModTest("data_gui/inspection_selection_staleness_and_close", TimeoutSeconds = 120)]
        static async Task Inspect(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "native controls require Godot");
            ModAssert.False(DisplayServer.GetName() == "headless", "pixel contracts require a windowed renderer");
            var previous = TranslationServer.GetLocale();
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
                    await new RpgDataGuiTests().Run(ctx, locale);
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private async Task Run(ModTestContext ctx, string locale)
        {
            window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile("rpg_editor__data_validation", "maps/validation_workshop.json");
                await Frames(5);
                var original = window.Document.Data.ToJsonString();
                var placements = JsonSerializer.Serialize(window.Document.Placements);
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var projections = new[]
                {
                    "topdown",
                    "isometric",
                };
                foreach (var projection in projections)
                {
                    window.SetProjection(projection);
                    window.Canvas.FitToView();
                    var toggle = Named<CheckButton>("MapDataEnabled");
                    toggle.ButtonPressed = true;
                    ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/data_toggle"), toggle.Text, "panel follows the active locale");
                    Named<Button>("MapDataCheck").EmitSignal(Button.SignalName.Pressed);
                    await Frames(4);
                    var clean = window.CheckData();
                    ModAssert.True(clean.Complete, "the small teaching map is checked completely");
                    ModAssert.Equal(0, clean.Issues.Count, "valid real workshop resources have no findings");
                    ModAssert.True(window.Canvas.Scene.Visuals.Count > 0, "the ordinary renderer still owns visible map content");
                    ModAssert.False(window.Document.CanUndo, "read-only checking creates no undo step");
                    ModAssert.True(window.Document.SetPlacementField("validation_workshop/chest", "Anchor", new JsonArray(0)),
                        "setup introduces one invalid authored origin without changing files");
                    window.SetProjection(projection);
                    await Frames(3);
                    var before = JsonSerializer.Serialize(window.Document.Placements);
                    var report = window.CheckData();
                    ModAssert.True(report.Issues.Count > 0, "the exact invalid field is reported");
                    var selected = -1;
                    for (var i = 0; i < report.Issues.Count; i++)
                    {
                        if (report.Issues[i].Code == RpgDataRules.Vector && report.Issues[i].Field == "Anchor")
                        {
                            selected = i;
                            break;
                        }
                    }

                    ModAssert.True(selected >= 0, "origin finding retains its stable rule and field path");
                    var list = Named<ItemList>("MapDataResults");
                    ModAssert.Equal(report.Issues.Count, list.ItemCount, "every finding has one visible list row");
                    list.Select(selected);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, (long)selected);
                    await Frames(3);
                    ModAssert.Equal("validation_workshop/chest", window.SelectedId, "report selection uses ordinary map selection");
                    ModAssert.True(Named<CheckButton>("GeometryEnabled").ButtonPressed, "selection exposes actual object geometry");
                    ModAssert.True(Named<Label>("MapDataDetails").Text.Contains("Anchor"), "details explain the authored field");
                    var context = ToolUi.Tr("tools/rpg_editor/data_placement");
                    ModAssert.False(context == "tools/rpg_editor/data_placement", "the context caption has a real translation");
                    ModAssert.True(Named<Label>("MapDataDetails").Text.Contains(context),
                        "the known input layer is explained in the current ru/en language");
                    ModAssert.Equal(before, JsonSerializer.Serialize(window.Document.Placements),
                        "inspection never edits the invalid input");
                    await ToDraw();
                    using var image = ViewportCapture.Take(((SceneTree)Godot.Engine.GetMainLoop()).Root);
                    ModAssert.NotNull(image, "the actual native viewport produces an owned image");
                    var point = window.Canvas.GetGlobalRect().Position
                        + window.Canvas.CanvasPointOf(new Vector2I(10, 8));
                    ModAssert.True(point.X >= 0 && point.Y >= 0 && point.X < image.GetWidth() && point.Y < image.GetHeight(),
                        "the known paving cell stays visible in the actual viewport");
                    var pixel = image.GetPixel((int)point.X, (int)point.Y);
                    ModAssert.True(pixel.R + pixel.G + pixel.B > 0.35f,
                        "real paving pixels remain drawn while an origin finding is inspected");
                    SaveShot(locale + "_" + projection);
                    ModAssert.True(window.Undo(), "the one setup edit remains one undo step");
                    await Frames(3);
                    ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/data_stale"), Named<Label>("MapDataSummary").Text,
                        "editing leaves an explicit stale report");
                    Named<CheckButton>("GeometryEnabled").ButtonPressed = false;
                    toggle.ButtonPressed = false;
                    ModAssert.False(window.Document.CanUndo, "all setup edits are retired");
                }

                ModAssert.Equal(original, window.Document.Data.ToJsonString(), "both projections preserve map JSON");
                ModAssert.Equal(placements, JsonSerializer.Serialize(window.Document.Placements), "both projections preserve placements");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags), "checking never runs game interactions");
                ctx.Log(locale + " read-only data inspector completed");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private T Named<T>(string name) where T : Node
        {
            var node = window.FindChild(name, true, false) as T;
            ModAssert.NotNull(node, "actual utility control exists: " + name);
            return node;
        }

        private static async Task Frames(int count)
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var i = 0; i < count; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }

        private static async Task ToDraw()
        {
            await RenderingServer.Singleton.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        private static void SaveShot(string name)
        {
            string output = null;
            foreach (var argument in OS.GetCmdlineUserArgs())
            {
                if (argument.StartsWith("--validation-out=", StringComparison.Ordinal))
                {
                    output = ProjectSettings.GlobalizePath(argument.Substring("--validation-out=".Length));
                }
            }

            if (output == null)
            {
                return;
            }

            Directory.CreateDirectory(output);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Godot.Engine.GetMainLoop()).Root,
                Path.Combine(output, name + ".png")), "screenshots preserve the shared colour pipeline");
        }
    }
}
