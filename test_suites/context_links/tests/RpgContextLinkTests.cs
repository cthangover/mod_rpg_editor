using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Settings;
using Cthangover.Core.UI.Tool;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Interface.View;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.ContextLinks
{
    /// <summary>Uses public tool controls/documents to verify linked selections, source ownership and preserved unsaved maps.</summary>
    public sealed class RpgContextLinkTests
    {
        [ModTest("context_links/full_editors_ownership_and_toolbar", TimeoutSeconds = 150)]
        static async Task Full(ModTestContext ctx)
        {
            var previous = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "en";
            var locales = new[] { "ru", "en" };
            var projections = new[]
            {
                RpgEditorCanvas.TopDown,
                RpgEditorCanvas.Isometric,
            };
            using var files = new RpgContextFiles();
            try
            {
                foreach (var locale in locales)
                {
                    LocaleLoader.LoadLocale(locale);
                    foreach (var projection in projections)
                    {
                        await Run(files.Actor, locale, projection);
                    }
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private static async Task Run(string actor, string locale, string projection)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            var opened = new List<Window>();
            try
            {
                window.Size = new Vector2I(1500, 900);
                window.OpenMapFile("rpg_plus_game", "maps/rpg_village.json");
                window.SetProjection(projection);
                await Frames(4);
                ModAssert.True(window.Document.SetProperty("ContextProbe", new JsonArray(1, 2)),
                    "the map starts with a real unsaved authoring change");
                var before = window.Document.Data.ToJsonString();
                var flags = JsonSerializer.Serialize(GameData.Instance.Runtime.Flags);
                var mapFile = ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json");
                window.ChooseObject("village_pack/decor/sign", RpgObjectForm.Sprite);
                await Frames(2);
                var button = Find(window, "ContextObject");
                ModAssert.Equal(ToolUi.Tr("tools/rpg_editor/context_object"), button.Text, "context caption follows the locale");
                ModAssert.False(button.Disabled, "available editor and dependency permit a foreign entity link");
                ModAssert.True(button.TooltipText.Contains("village_pack/")
                    && button.TooltipText.Contains("rpg_plus_game/patches/rpg_object/rpg_plus_game.json"),
                    "foreign source and own patch destination are explained before opening");
                button.EmitSignal(Button.SignalName.Pressed);
                await Frames(3);
                var editor = FindEntity("village_pack/decor/sign");
                ModAssert.NotNull(editor, "object link selects the actual foreign object in its editor");
                opened.Add(editor);
                ModAssert.Equal("rpg_plus_game", editor.ModId, "foreign object opens in the map's content mod");
                var record = editor.Document.FindAny(editor.SelectedId);
                ModAssert.True(record.IsForeign, "the linked record retains patch semantics");
                var source = ModFileService.ReadText(record.OwnerMod, record.File);
                editor.QueueFree();
                await Frames(3);
                ModAssert.Equal(source, ModFileService.ReadText(record.OwnerMod, record.File), "viewing leaves owner bytes unchanged");
                window.ChooseObject("rpg_plus/container/supply_barrel", RpgObjectForm.Sprite);
                await Frames(2);
                button = Find(window, "ContextObject");
                ModAssert.True(button.TooltipText.Contains("rpg_plus_game/objects/container.json"),
                    "own entity explains its direct save file");
                button.EmitSignal(Button.SignalName.Pressed);
                await Frames(3);
                editor = FindEntity("rpg_plus/container/supply_barrel");
                ModAssert.NotNull(editor, "own object link selects the actual own entity");
                opened.Add(editor);
                ModAssert.False(editor.Document.FindAny(editor.SelectedId).IsForeign, "own definition stays own");
                editor.QueueFree();
                await Frames(3);
                window.ChooseObject(actor, RpgObjectForm.Actor);
                await Frames(2);
                button = Find(window, "ContextCharacter");
                ModAssert.False(button.Disabled, "generated actor exposes its existing character definition");
                button.EmitSignal(Button.SignalName.Pressed);
                await Frames(3);
                editor = FindEntity("rpg_hero");
                ModAssert.NotNull(editor, "character link selects the actual character rather than the actor object");
                opened.Add(editor);
                ModAssert.Equal("rpg_plus_game", editor.ModId, "character edits remain in the map's own content mod");
                editor.QueueFree();
                await Frames(3);
                window.ChooseObject("rpg_plus/pickup/branches", RpgObjectForm.Sprite);
                await Frames(2);
                button = Find(window, "ContextScenario");
                ModAssert.False(button.Disabled, "declared scenario reference opens its optional editor");
                const string path = "scenarios/rpg_plus/events/pick_branches.scenario";
                var scenario = ModFileService.ReadText("rpg_plus_game", path);
                ModAssert.NotNull(scenario, "probe uses a real scenario source");
                ModAssert.True(button.TooltipText.Contains("rpg_plus_game/" + path),
                    "scenario link explicitly identifies its source editing destination");
                var existing = RootWindows();
                button.EmitSignal(Button.SignalName.Pressed);
                await Frames(4);
                var scenarioWindow = NewWindow(existing);
                ModAssert.NotNull(scenarioWindow, "scenario opens as a separate tool window");
                opened.Add(scenarioWindow);
                ModAssert.True(HasText(scenarioWindow, scenario), "the actual scenario source is selected in its code editor");
                scenarioWindow.QueueFree();
                await Frames(3);
                ModAssert.Equal(scenario, ModFileService.ReadText("rpg_plus_game", path), "opening a scenario does not save it");
                var scroll = window.FindChild("RpgToolbarScroll", true, false) as ScrollContainer;
                var back = Find(window, "ToolbarBack");
                var forward = Find(window, "ToolbarForward");
                ModAssert.NotNull(scroll, "toolbar preserves its existing scroll node name");
                var bar = scroll.GetHScrollBar();
                var old = bar.Value;
                ModAssert.False(forward.Disabled, "long localized toolbar exposes an explicit more-tools button");
                forward.EmitSignal(Button.SignalName.Pressed);
                await Frames(2);
                ModAssert.True(bar.Value > old, "more-tools button reveals hidden controls");
                ModAssert.False(back.Disabled, "earlier tools become explicitly reachable");
                back.EmitSignal(Button.SignalName.Pressed);
                await Frames(2);
                ModAssert.True(bar.Value == old, "back returns exactly to the earlier toolbar position");
                ModAssert.Equal(before, window.Document.Data.ToJsonString(), "all context links preserve exact unsaved map JSON");
                ModAssert.True(window.Document.CanUndo, "context links preserve the map's pending history");
                ModAssert.Equal(mapFile, ModFileService.ReadText("rpg_plus_game", "maps/rpg_village.json"),
                    "navigation never saves the map implicitly");
                ModAssert.Equal(flags, JsonSerializer.Serialize(GameData.Instance.Runtime.Flags),
                    "links do not play scenarios or change flags");
                Shot(locale + "_" + projection);
            }
            finally
            {
                foreach (var child in opened)
                {
                    if (GodotObject.IsInstanceValid(child))
                    {
                        child.QueueFree();
                    }
                }

                window.QueueFree();
                await Frames(3);
            }
        }

        [ModTest("context_links/absent_optional_editors_are_explained", TimeoutSeconds = 60)]
        static async Task Absent(ModTestContext ctx)
        {
            var ids = new[]
            {
                "rpg_object_editor",
                "characters_editor",
                "scenario_editor",
            };
            foreach (var id in ids)
            {
                ModAssert.Null(ToolFactory.Instance.Get(id), "probe keeps optional editors outside the loaded set");
            }

            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.OpenMapFile("rpg_editor__context_links", "maps/context_map.json");
                ModAssert.NotNull(window.Document, "the optional-editor probe opens its declared fixture map");
                window.ChooseObject("village_pack/decor/sign", RpgObjectForm.Sprite);
                await Frames(3);
                var names = new[] { "ContextObject", "ContextCharacter", "ContextScenario" };
                var before = RootWindows().Count;
                foreach (var name in names)
                {
                    var button = Find(window, name);
                    ModAssert.True(button.Disabled, "absent editor cannot be launched through its context link");
                    ModAssert.True(button.TooltipText.Contains(ToolUi.Tr("tools/rpg_editor/context_tool_absent")),
                        "disabled link explains the optional editor requirement");
                    button.EmitSignal(Button.SignalName.Pressed);
                }

                await Frames(2);
                ModAssert.Equal(before, RootWindows().Count, "disabled links create no orphan tool windows");
            }
            finally
            {
                window.QueueFree();
                await Frames(3);
            }
        }

        private static Button Find(Window window, string name)
        {
            var button = window.FindChild(name, true, false) as Button;
            ModAssert.NotNull(button, "actual named utility control exists: " + name);
            return button;
        }

        private static EntityEditorWindow FindEntity(string id)
        {
            foreach (var child in ((SceneTree)Engine.GetMainLoop()).Root.GetChildren())
            {
                if (child is EntityEditorWindow editor && editor.SelectedId == id)
                {
                    return editor;
                }
            }

            return null;
        }

        private static List<Window> RootWindows()
        {
            var result = new List<Window>();
            foreach (var child in ((SceneTree)Engine.GetMainLoop()).Root.GetChildren())
            {
                if (child is Window window)
                {
                    result.Add(window);
                }
            }

            return result;
        }

        private static Window NewWindow(List<Window> previous)
        {
            foreach (var child in RootWindows())
            {
                if (!previous.Contains(child))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasText(Node node, string text)
        {
            if (node is CodeEdit code && code.Text == text.Replace("\r\n", "\n"))
            {
                return true;
            }

            foreach (var child in node.GetChildren())
            {
                if (HasText(child, text))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Shot(string name)
        {
            var folder = System.Environment.GetEnvironmentVariable("ISO2D_CONTEXT_SHOTS");
            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            ModAssert.True(ViewportCapture.SavePng(((SceneTree)Engine.GetMainLoop()).Root,
                Path.Combine(folder, name + ".png")), "context capture uses the shared colour pipeline");
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
