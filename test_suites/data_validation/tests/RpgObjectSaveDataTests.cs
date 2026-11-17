namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Save routes refuse before writes, preserve one-step history, and keep own metadata and foreign patches exact.</summary>
    public sealed class RpgObjectSaveDataTests
    {
        private const string Owner = "rpg_editor__data_validation";

        private const string OwnPath = "objects/parts_metadata.json";

        private const string OwnId = Owner + "/parts_metadata";

        private const string PatchPath = "patches/rpg_object/" + Owner + ".json";

        [ModTest("data_gui/object_save_hook_routes_and_round_trip", TimeoutSeconds = 180)]
        static async Task Save(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "the shared Save routes require native tool controls");
            var previous = TranslationServer.GetLocale();
            var locales = new[]
            {
                "ru",
                "en",
            };
            var projections = new[]
            {
                "topdown",
                "isometric",
            };
            try
            {
                foreach (var locale in locales)
                {
                    LocaleLoader.LoadLocale(locale);
                    foreach (var projection in projections)
                    {
                        await Run(projection);
                    }
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private static async Task Run(string projection)
        {
            var paths = new[]
            {
                OwnPath,
                PatchPath,
                "manifest.json",
            };
            using var files = new RpgValidationFiles(Owner, paths);
            var initialFile = ModFileService.ReadText(Owner, OwnPath);
            var window = ToolWindow.Open<RpgObjectSaveProbe>();
            try
            {
                window.OpenMod(Owner);
                var own = window.Document.Find(OwnId);
                window.Select(own);
                window.Preview.SelectProjection(projection);
                await Frames();
                var initial = own.Data.ToJsonString();
                window.Document.BeginChange();
                own.Data["Anchor"] = new JsonArray(0);
                var invalid = own.Data.ToJsonString();
                var save = SaveButton(window);
                ModAssert.NotNull(save, "the ordinary toolbar Save button is present");
                save.EmitSignal(Button.SignalName.Pressed);
                ModAssert.Equal(1, window.Checks, "button Save enters exactly one subject check");
                ModAssert.Equal(initialFile, ModFileService.ReadText(Owner, OwnPath), "button refusal writes no own file");
                ModAssert.Equal(invalid, own.Data.ToJsonString(), "button refusal retains every entered value");
                using var key = new InputEventKey
                {
                    Keycode = Key.S,
                    CtrlPressed = true,
                    Pressed = true,
                };
                window._Input(key);
                ModAssert.Equal(2, window.Checks, "Ctrl+S also enters exactly one subject check");
                ModAssert.Equal(initialFile, ModFileService.ReadText(Owner, OwnPath), "shortcut refusal writes no file");
                ModAssert.True(window.Document.Undo(), "the invalid edit remains the sole undo transaction");
                own = window.Document.Find(OwnId);
                ModAssert.Equal(initial, own.Data.ToJsonString(), "undo restores every known and unknown input field");
                ModAssert.False(window.Document.CanUndo, "neither Save route adds history");
                window.Select(own);
                window.Document.BeginChange();
                own.Data["F11Extra"]["saved"] = 17;
                ModAssert.True(window.Save(), "a corrected own object saves through the same hook");
                ModAssert.Equal(3, window.Checks, "valid own Save has one preflight");
                var savedFile = ModFileService.ReadText(Owner, OwnPath);
                var saved = JsonNode.Parse(savedFile)["Items"][0];
                ModAssert.Equal(17, saved["F11Extra"]["saved"].GetValue<int>(), "the new unknown field is persisted");
                ModAssert.Equal(42, saved["F11Extra"]["keep"].GetValue<int>(), "existing root metadata survives");
                ModAssert.Equal("legacy", saved["Parts"][0]["F11Extra"]["role"].GetValue<string>(),
                    "legacy part metadata survives actual file serialization");
                ModAssert.True(window.Document.Undo(), "one undo reverses the saved edit");
                ModAssert.Equal(initial, window.Document.Find(OwnId).Data.ToJsonString(), "saved own edit round-trips exactly");
                ModAssert.False(window.Document.CanUndo, "valid Save adds no hidden undo transaction");

                var foreign = window.Document.FindForeign("village_pack/container/barrel");
                ModAssert.NotNull(foreign, "the foreign patch uses a real loaded barrel");
                var originalSource = ModFileService.ReadText(foreign.OwnerMod, foreign.File);
                ModAssert.NotNull(originalSource, "the foreign source bytes can be compared");
                window.Select(foreign);
                var originalPatch = ModFileService.ReadText(Owner, PatchPath);
                window.Document.BeginChange();
                foreign.Data["Anchor"] = new JsonArray(0);
                window.Document.CommitForeign(foreign);
                ModAssert.False(window.Save(), "an invalid foreign override cannot write a patch");
                ModAssert.Equal(4, window.Checks, "foreign refusal has one preflight");
                ModAssert.Equal(originalPatch, ModFileService.ReadText(Owner, PatchPath), "foreign refusal writes no patch bytes");
                ModAssert.Equal(savedFile, ModFileService.ReadText(Owner, OwnPath), "foreign refusal writes no other own file");
                ModAssert.Equal(originalSource, ModFileService.ReadText(foreign.OwnerMod, foreign.File),
                    "foreign refusal never writes the source");
                ModAssert.True(window.Document.Undo(), "the invalid foreign edit is still one undo step");
                foreign = window.Document.FindForeign(foreign.Id);
                window.Select(foreign);
                window.Document.BeginChange();
                foreign.Data["Anchor"] = new JsonArray(-0.25, 0.5);
                window.Document.CommitForeign(foreign);
                ModAssert.True(window.Save(), "a corrected negative fractional origin is valid");
                ModAssert.Equal(5, window.Checks, "corrected foreign Save has one preflight");
                var patch = JsonNode.Parse(ModFileService.ReadText(Owner, PatchPath))["Items"][0].AsObject();
                ModAssert.Equal(2, patch.Count, "the foreign patch contains only Id and the edited Anchor");
                ModAssert.Equal(foreign.Id, patch["Id"].GetValue<string>(), "the patch addresses its original entity");
                ModAssert.True(patch["Anchor"][0].GetValue<double>() == -0.25
                    && patch["Anchor"][1].GetValue<double>() == 0.5, "both coordinates round-trip through actual patch JSON");
                ModAssert.Equal(originalSource, ModFileService.ReadText(foreign.OwnerMod, foreign.File),
                    "successful patch Save leaves the owner source byte-identical");
                ModAssert.True(window.Document.Undo(), "one undo removes the successful foreign override");
                ModAssert.True(window.Document.PatchOf(foreign.Id) == null, "undo restores the original absence of a patch entry");
                ModAssert.False(window.Document.CanUndo, "foreign Save adds no extra transaction");
            }
            finally
            {
                window.QueueFree();
                await Frames();
            }
        }

        private static Button SaveButton(Node parent)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is Button button && button.Text == ToolUi.Tr("tools/common/save"))
                {
                    return button;
                }

                var found = SaveButton(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static async Task Frames()
        {
            var tree = (SceneTree)Godot.Engine.GetMainLoop();
            for (var i = 0; i < 4; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
