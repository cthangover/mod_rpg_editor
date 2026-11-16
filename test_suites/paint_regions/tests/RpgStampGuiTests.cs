using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Settings;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>Persists groups with the actual utility controls, reopens them and checks scopes, source files and one undo.</summary>
    public sealed class RpgStampGuiTests
    {
        [ModTest("stamps/native_save_reopen_paste_undo_and_invalid_item_isolation", TimeoutSeconds = 90)]
        static async Task Library(ModTestContext ctx)
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
                        using var files = new RpgStampFiles(ctx);
                        await Run(files, locale, projection);
                    }
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private static async Task Run(RpgStampFiles files, string locale, string projection)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            string saved;
            var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile(RpgStampFiles.Owner, RpgStampFiles.MapPath);
                window.SetProjection(projection);
                await Frames(4);
                var toggle = window.FindChild("StampToggle", true, false) as CheckButton;
                ModAssert.NotNull(toggle, "stamps are a real ModWidget utility panel");
                toggle.ButtonPressed = true;
                using var key = new InputEventKey
                {
                    Keycode = Key.A,
                    PhysicalKeycode = Key.A,
                    CtrlPressed = true,
                    Pressed = true,
                };
                window.PushInput(key);
                ModAssert.Equal(2, window.Marked.Count, "ordinary selection marks both source events");
                var name = window.FindChild("StampName", true, false) as LineEdit;
                var capture = window.FindChild("CaptureStamp", true, false) as Button;
                ModAssert.NotNull(name, "library has an editable safe file name");
                ModAssert.NotNull(capture, "library exposes a real capture button");
                ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/stamp_capture"), capture.Text, "capture caption is localized");
                name.Text = "../unsafe";
                capture.EmitSignal(Button.SignalName.Pressed);
                ModAssert.Null(ModFileService.ReadText(RpgStampFiles.Owner, "unsafe.json"), "unsafe names do not escape the library");
                name.Text = "contract_group";
                capture.EmitSignal(Button.SignalName.Pressed);
                await Frames(3);
                saved = ModFileService.ReadText(RpgStampFiles.Owner, RpgStampFiles.StampPath);
                ModAssert.NotNull(saved, "capture creates the actual own-mod stamp file");
                var stamp = JsonSerializer.Deserialize<RpgMapStamp>(saved, JsonOptionsProvider.ReadOptions);
                ModAssert.Equal("contract_group", stamp.Name, "saved group has the requested name");
                ModAssert.Equal("rpg_stamp_probe", stamp.SourceMap, "saved group retains its source map basis");
                ModAssert.Equal(2, stamp.Clips.Count, "saved file contains every selected event");
                ModAssert.False(window.Document.CanUndo, "saving a group does not edit map history");
                capture.EmitSignal(Button.SignalName.Pressed);
                ModAssert.Equal(saved, ModFileService.ReadText(RpgStampFiles.Owner, RpgStampFiles.StampPath),
                    "duplicate name refuses overwrite and keeps exact library bytes");
                ModAssert.Equal(files.MapText, ModFileService.ReadText(RpgStampFiles.Owner, RpgStampFiles.MapPath),
                    "capturing a group never writes its source map");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }

            window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile(RpgStampFiles.Owner, RpgStampFiles.MapPath);
                window.SetProjection(projection);
                await Frames(4);
                var toggle = window.FindChild("StampToggle", true, false) as CheckButton;
                toggle.ButtonPressed = true;
                var picker = window.FindChild("StampPicker", true, false) as OptionButton;
                var paste = window.FindChild("PasteStamp", true, false) as Button;
                var details = window.FindChild("StampDetails", true, false) as Label;
                ModAssert.NotNull(picker, "reopened tool rebuilds the persisted library");
                ModAssert.Equal(2, picker.ItemCount, "only own/dependency groups are visible; malformed and unrelated items are excluded");
                var dependencyIndex = -1;
                for (var item = 0; item < picker.ItemCount; item++)
                {
                    ModAssert.False(picker.GetItemText(item).Contains("contract_unrelated"),
                        "loading another tool mod does not expose its private stamp library");
                    if (picker.GetItemText(item) == "village_pack/contract_dependency")
                    {
                        dependencyIndex = item;
                    }
                }

                ModAssert.True(dependencyIndex >= 0, "a transitive content dependency contributes its group");
                picker.Select(dependencyIndex);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)dependencyIndex);
                ModAssert.True(paste.Disabled, "missing object provider prevents placement");
                ModAssert.True(details.Text.Contains("unloaded_fixture/decor/missing"), "missing provider is explained before placement");
                picker.Select(0);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                ModAssert.True(picker.GetItemText(0).Contains("contract_group"), "reopened library selects the saved group");
                ModAssert.False(paste.Disabled, "valid own destination permits placement");
                ModAssert.True(details.Text.Contains(RpgStampFiles.StampPath) && details.Text.Contains(RpgStampFiles.Owner),
                    "library explains source ownership and destination before placement");
                ModAssert.False(details.Text.Contains("\\n"), "localized details do not display escape sequences");
                var before = window.Document.Data.ToJsonString();
                using var hover = new InputEventMouseMotion
                {
                    Position = window.Canvas.CanvasPointOf(new Vector2I(3, 3)),
                };
                window.Canvas._GuiInput(hover);
                paste.EmitSignal(Button.SignalName.Pressed);
                await Frames(3);
                ModAssert.Equal(2, window.Marked.Count, "one placement marks the whole new instance");
                var first = window.Marked[0];
                var second = window.Marked[1];
                ModAssert.Equal(3, window.Document.EventOf(first).X, "instance uses the actual hovered anchor column");
                ModAssert.Equal(3, window.Document.EventOf(first).Y, "instance uses the actual hovered anchor row");
                ModAssert.Equal(first + ":seen == 1", window.Document.EventOf(second).When,
                    "reopened group rewrites its explicit local condition to the new instance");
                await Frames(2);
                Shot(locale + "_" + projection);
                ModAssert.True(window.Undo(), "one UI undo removes the whole persisted group instance");
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "undo restores exact authored source data");
                ModAssert.False(window.Document.CanUndo, "stamp placement adds one undo transaction");
                ModAssert.Equal(files.MapText, ModFileService.ReadText(RpgStampFiles.Owner, RpgStampFiles.MapPath),
                    "unsaved instance never changes the source file");
                ModAssert.Equal(saved, ModFileService.ReadText(RpgStampFiles.Owner, RpgStampFiles.StampPath),
                    "instance remapping never edits the prototype file");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags), "editing executes no event commands");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static void Shot(string name)
        {
            var folder = System.Environment.GetEnvironmentVariable("ISO2D_FILL_SHOTS");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Engine.GetMainLoop()).Root,
                Path.Combine(folder, "stamp_" + name + ".png")), "stamp capture uses the shared colour pipeline");
        }

        private static async Task Frames(int count)
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            for (var frame = 0; frame < count; frame++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
