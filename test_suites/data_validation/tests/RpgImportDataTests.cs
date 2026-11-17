namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System;
    using System.IO;
    using System.Text.Json.Nodes;
    using System.Threading.Tasks;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Tools.RpgObjectEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Import validates its file and role before copying, while another invalid field can still be repaired separately.</summary>
    public sealed class RpgImportDataTests
    {
        [ModTest("data_gui/import_file_preflight_and_unrelated_invalid_field", TimeoutSeconds = 60)]
        static async Task Import(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "native PNG decoding and import controls need the runtime");
            using var files = new RpgImportDataFiles();
            var window = ToolWindow.Open<RpgObjectEditorWindow>();
            try
            {
                window.OpenMod("rpg_editor__data_validation");
                var record = window.Document.Find("rpg_editor__data_validation/parts_metadata");
                window.Select(record);
                var sourceFile = ModFileService.ReadText("rpg_editor__data_validation", "objects/parts_metadata.json");
                record.Data["Form"] = "surface";
                record.Data["Surface"] = new JsonObject
                {
                    ["Image"] = "ground/grass",
                };
                record.Data["Anchor"] = new JsonArray(0);
                var original = record.Data.ToJsonString();
                byte[] rectangle;
                using (var image = Image.CreateEmpty(128, 64, false, Image.Format.Rgba8))
                {
                    image.Fill(Colors.Lime);
                    rectangle = image.SavePngToBuffer();
                }

                var rectangularPath = files.Source("rectangle", rectangle);
                var rectangularTarget = files.Target("validation_rectangle");
                ModAssert.Null(window.ImportPicture(rectangularPath, "validation_rectangle"),
                    "a rectangular surface fails before its file is copied");
                ModAssert.False(File.Exists(rectangularTarget), "refused surface import creates no target file");
                ModAssert.Equal(original, record.Data.ToJsonString(), "refused import preserves all entered fields");
                ModAssert.False(window.Document.CanUndo, "a refused import adds no history");

                var header = new byte[24];
                Array.Copy(rectangle, header, header.Length);
                var headerPath = files.Source("truncated", header);
                var truncatedTarget = files.Target("validation_truncated");
                ModAssert.Null(window.ImportPicture(headerPath, "validation_truncated"),
                    "a valid IHDR prefix is insufficient for importing a complete image");
                ModAssert.False(File.Exists(truncatedTarget), "a truncated PNG creates no target file");
                ModAssert.Equal(original, record.Data.ToJsonString(), "a truncated input also retains every field");

                byte[] square;
                using (var image = Image.CreateEmpty(128, 128, false, Image.Format.Rgba8))
                {
                    image.Fill(Colors.Lime);
                    square = image.SavePngToBuffer();
                }

                var squarePath = files.Source("square", square);
                var squareTarget = files.Target("validation_square");
                ModAssert.Equal("container/validation_square", window.ImportPicture(squarePath, "validation_square"),
                    "a correct image imports even while the unrelated Anchor field needs correction");
                ModAssert.True(File.ReadAllBytes(squareTarget).AsSpan().SequenceEqual(square),
                    "import copies the original PNG bytes without rescaling or conversion");
                ModAssert.Equal("container/validation_square", record.Data["Surface"]["Image"].GetValue<string>(),
                    "the candidate points to the actual imported resource");
                ModAssert.Equal(1, record.Data["Anchor"].AsArray().Count, "import never normalizes an unrelated invalid field");
                ModAssert.True(window.Document.Undo(), "the successful picture assignment is one undo step");
                ModAssert.Equal(original, window.Document.Find(record.Id).Data.ToJsonString(), "one undo restores complete input");
                ModAssert.False(window.Document.CanUndo, "failed imports and the successful copy add no extra data transactions");
                ModAssert.Equal(sourceFile, ModFileService.ReadText("rpg_editor__data_validation", "objects/parts_metadata.json"),
                    "import never silently saves the source object definition");
            }
            finally
            {
                window.QueueFree();
                var tree = (SceneTree)Godot.Engine.GetMainLoop();
                for (var i = 0; i < 4; i++)
                {
                    await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                }
            }
        }
    }
}
