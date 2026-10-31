namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// The real maps and placements of <c>rpg_plus_game</c> round-trip byte for byte: the writer of the document gives
    /// back each file's text, opening a map as its own and saving it writes nothing, and a change undone writes nothing.
    /// </summary>
    public class RpgRoundTripTests
    {
        private const int MinimumMaps = 13;

        [ModTest("round_trip/real_map_and_placement_files_are_written_byte_for_byte")]
        static void FilesAreByteStable(ModTestContext ctx)
        {
            var files = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod).Where(f => f.Path.StartsWith("maps/", System.StringComparison.Ordinal)
                || f.Path.StartsWith("placements/", System.StringComparison.Ordinal)).ToList();
            var maps = files.Count(f => f.Path.StartsWith("maps/", System.StringComparison.Ordinal));
            ModAssert.True(maps >= MinimumMaps, $"the game has its maps ({maps} found)");
            foreach (var (path, text) in files)
            {
                var written = EntityDocument.Serialize(JsonNode.Parse(text).AsObject());
                ModAssert.Equal(text, written, $"{path} is written as it is read");
            }
        }

        [ModTest("round_trip/real_maps_open_as_own_and_save_nothing")]
        static void OpenAndSaveNothing(ModTestContext ctx)
        {
            var files = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var mapFiles = files.Where(f => f.Path.StartsWith("maps/", System.StringComparison.Ordinal)).ToList();
            ModAssert.True(mapFiles.Count >= MinimumMaps, "the maps are there");
            foreach (var (path, text) in mapFiles)
            {
                var map = (JsonNode.Parse(text)["Items"] as JsonArray)[0] as JsonObject;
                var id = map["Id"].GetValue<string>();
                var doc = RpgEditorFixture.OpenOwn(id, RpgEditorFixture.GameMod, files);
                ModAssert.False(doc.IsForeign, $"{id} is the game's own map");
                ModAssert.Equal(0, doc.Changes().Count, $"{id} saves nothing when unchanged");
                ModAssert.Equal(id, JsonSerializer.Deserialize<RpgMap>(map.ToJsonString(), JsonOptionsProvider.ReadOptions).ID, $"{path} reads as a v2 map");
            }
        }

        [ModTest("round_trip/paint_and_undo_on_a_real_map_writes_nothing")]
        static void PaintAndUndo(ModTestContext ctx)
        {
            var files = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var doc = RpgEditorFixture.OpenOwn("rpg_village", RpgEditorFixture.GameMod, files);
            var before = doc.ObjectAt(RpgStrokeLayer.Ground, 0, 0);
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Water), "paint a real map");
            ModAssert.True(doc.Changes().Count == 1, "the map file is written");
            ModAssert.True(doc.Undo(), "undo");
            ModAssert.Equal(0, doc.Changes().Count, "undone, nothing is written");
            ModAssert.Equal(before, doc.ObjectAt(RpgStrokeLayer.Ground, 0, 0), "the cell is as it was");
        }
    }
}
