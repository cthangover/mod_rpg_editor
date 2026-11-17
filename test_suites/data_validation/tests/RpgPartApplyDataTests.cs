namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.RpgObjectEditor;
    using Godot;

    /// <summary>
    /// Nonfinite input stays editable; corrected candidates publish detached data without changing their source.
    /// </summary>
    public sealed class RpgPartApplyDataTests
    {
        private JsonArray applied;

        private int publications;

        [ModTest("data_gui/parent_apply_unknown_fields_and_one_undo", TimeoutSeconds = 60)]
        static async Task Parent(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "the parent workspace needs ordinary native editor controls");
            var window = ToolWindow.Open<RpgObjectEditorWindow>();
            try
            {
                window.OpenMod("rpg_editor__data_validation");
                var record = window.Document.Find("rpg_editor__data_validation/parts_metadata");
                ModAssert.NotNull(record, "the fixture has both legacy and keyed parts with unknown metadata");
                window.Select(record);
                var original = record.Data.ToJsonString();
                ModAssert.False(window.Document.CanUndo, "opening a fixture creates no history");
                window.OpenVisualParts();
                await Frames();
                var candidate = window.PartsWindow.Canvas.EditableParts;
                candidate[0].Size[0] = float.NaN;
                window.PartsWindow.Apply();
                ModAssert.Equal(original, record.Data.ToJsonString(), "invalid Apply leaves the complete parent unchanged");
                ModAssert.False(window.Document.CanUndo, "invalid Apply creates no parent history");
                candidate[0].Size[0] = 2;
                window.PartsWindow.Apply();
                ModAssert.True(record.Data["Parts"][0]["Size"][0].GetValue<float>() == 2,
                    "the parent's candidate contains the corrected size");
                ModAssert.Equal("legacy", record.Data["Parts"][0]["F11Extra"]["role"].GetValue<string>(),
                    "an unkeyed part retains unknown fields by its existing position");
                ModAssert.Equal("named", record.Data["Parts"][1]["F11Extra"]["role"].GetValue<string>(),
                    "a keyed part retains unknown fields by its stable id");
                ModAssert.Equal(42, record.Data["F11Extra"]["keep"].GetValue<int>(),
                    "the object root also retains unknown metadata");
                ModAssert.True(window.Document.Undo(), "one parent undo reverses the entire successful Apply");
                ModAssert.Equal(original, window.Document.Find(record.Id).Data.ToJsonString(),
                    "undo restores every known and unknown source field");
                ModAssert.False(window.Document.CanUndo, "Apply created exactly one parent transaction");
            }
            finally
            {
                window.QueueFree();
                await Frames();
            }
        }

        [ModTest("data_gui/part_apply_before_serialization", TimeoutSeconds = 120)]
        static async Task Apply(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "part Apply uses a real utility window");
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
                        await new RpgPartApplyDataTests().Run(projection);
                    }
                }
            }
            finally
            {
                LocaleLoader.LoadLocale(previous);
            }
        }

        private async Task Run(string projection)
        {
            var source = Entities.Collect<RpgObject>()["village_pack/chest"];
            var before = JsonSerializer.Serialize(source, JsonOptionsProvider.ReadOptions);
            var window = new RpgPartEditorWindow(source);
            window.Applied += OnApplied;
            try
            {
                ToolWindow.ShowWindow(window);
                window.Canvas.SelectProjection(projection);
                await Frames();
                var part = window.Canvas.EditableParts[0];
                part.Size = new float[]
                {
                    float.NaN,
                    1,
                };
                window.Apply();
                ModAssert.True(applied == null, "invalid values never reach the parent's candidate");
                ModAssert.Equal(0, publications, "invalid Apply emits no candidate transaction");
                ModAssert.False(window.IsQueuedForDeletion(), "the invalid workspace stays open");
                ModAssert.True(float.IsNaN(part.Size[0]), "invalid input stays available for correction");
                ModAssert.Equal(before, JsonSerializer.Serialize(source, JsonOptionsProvider.ReadOptions),
                    "failed Apply never changes the shared source object");
                part.Size[0] = 1;
                window.Apply();
                ModAssert.NotNull(applied, "corrected input publishes complete data");
                ModAssert.Equal(1, publications, "one successful Apply produces one candidate");
                ModAssert.Equal("container/chest_closed", applied[0]["Image"].GetValue<string>(),
                    "the untouched source picture remains in the candidate");
                ModAssert.True(applied[0]["Size"][0].GetValue<float>() == 1,
                    "corrected geometry is present in the emitted JSON");
                ModAssert.Equal(before, JsonSerializer.Serialize(source, JsonOptionsProvider.ReadOptions),
                    "successful Apply also leaves the shared catalog unchanged");
                ModAssert.True(window.IsQueuedForDeletion(), "successful Apply closes the workspace");
            }
            finally
            {
                window.Applied -= OnApplied;
                if (GodotObject.IsInstanceValid(window) && !window.IsQueuedForDeletion())
                {
                    window.QueueFree();
                }

                await Frames();
            }
        }

        private void OnApplied(JsonArray parts)
        {
            applied = (JsonArray)parts.DeepClone();
            publications++;
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
