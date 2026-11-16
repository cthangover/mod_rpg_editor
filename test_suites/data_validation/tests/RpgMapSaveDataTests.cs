using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    /// <summary>Save refuses before legend cleanup or writes, and valid unknown metadata survives save, reopen and one undo.</summary>
    public sealed class RpgMapSaveDataTests
    {
        private const string Owner = "rpg_editor__data_validation";

        private const string MapPath = "maps/validation_workshop.json";

        private const string PropsPath = "placements/validation_workshop/props.json";

        [ModTest("data_gui/map_save_preflight_and_round_trip", TimeoutSeconds = 120)]
        static async Task Save(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "map save requires the ordinary native editor");
            var projections = new[]
            {
                "topdown",
                "isometric",
            };
            foreach (var projection in projections)
            {
                await Run(projection);
            }
        }

        private static async Task Run(string projection)
        {
            var paths = new[]
            {
                MapPath,
                PropsPath,
            };
            using var files = new RpgValidationFiles(Owner, paths);
            var originalMap = ModFileService.ReadText(Owner, MapPath);
            var originalProps = ModFileService.ReadText(Owner, PropsPath);
            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.OpenMapFile(Owner, MapPath);
                window.SetProjection(projection);
                await Frames();
                var legend = (JsonObject)window.Document.Data["Legend"].DeepClone();
                legend["z"] = "village_pack/ground/grass";
                ModAssert.True(window.Document.SetProperty("Legend", legend), "setup retains an intentionally unused legend entry");
                ModAssert.True(window.Document.SetPlacementField("validation_workshop/chest", "Anchor", new JsonArray(0)),
                    "setup introduces one invalid origin");
                window.SetProjection(projection);
                var map = window.Document.Data.ToJsonString();
                var props = JsonSerializer.Serialize(window.Document.Placements);
                ModAssert.False(window.Save(), "the audited origin rule prevents writing invalid data");
                var enabled = window.FindChild("MapDataEnabled", true, false) as CheckButton;
                ModAssert.NotNull(enabled, "the refused save exposes the existing diagnostic panel");
                ModAssert.True(enabled.ButtonPressed, "save refusal opens the report so its field can be selected");
                ModAssert.Equal(map, window.Document.Data.ToJsonString(), "refusal occurs before Changes cleans the legend");
                ModAssert.Equal(props, JsonSerializer.Serialize(window.Document.Placements), "refusal preserves every placement");
                ModAssert.Equal(originalMap, ModFileService.ReadText(Owner, MapPath), "refusal writes no map bytes");
                ModAssert.Equal(originalProps, ModFileService.ReadText(Owner, PropsPath), "refusal writes no placement bytes");
                ModAssert.True(window.Undo(), "the original invalid-origin edit is still the top undo step");
                ModAssert.True(window.Undo(), "the original legend edit remains the preceding undo step");
                ModAssert.False(window.Document.CanUndo, "validation itself added no history");
                var metadata = new JsonObject
                {
                    ["value"] = 42,
                    ["tag"] = "kept",
                };
                ModAssert.True(window.Document.SetProperty("F11Metadata", metadata), "setup edits one unknown map field");
                ModAssert.True(window.Save(), "a complete valid map can be saved in " + projection);
                var saved = JsonNode.Parse(ModFileService.ReadText(Owner, MapPath));
                ModAssert.Equal(42, saved["Items"][0]["F11Metadata"]["value"].GetValue<int>(), "saved unknown number survives JSON");
                ModAssert.Equal("kept", saved["Items"][0]["F11Metadata"]["tag"].GetValue<string>(), "unknown text survives JSON");
                ModAssert.True(window.Undo(), "the valid authored edit remains one undo step after save");
                ModAssert.True(window.Document.Data["F11Metadata"] == null, "one undo restores the original data");
            }
            finally
            {
                window.QueueFree();
                await Frames();
            }

            window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.OpenMapFile(Owner, MapPath);
                ModAssert.Equal(42, window.Document.Data["F11Metadata"]["value"].GetValue<int>(),
                    "reopening reads the actual persisted JSON");
                ModAssert.Equal("kept", window.Document.Data["F11Metadata"]["tag"].GetValue<string>(),
                    "reopened unknown metadata remains complete");
            }
            finally
            {
                window.QueueFree();
                await Frames();
            }
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
