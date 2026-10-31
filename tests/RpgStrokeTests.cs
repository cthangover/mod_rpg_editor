namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Patches;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// <c>Strokes</c>: an own map reads them over its rows; a foreign map is painted only through strokes, which land in
    /// the edited mod's patch as <c>Strokes$add</c>. The runtime reads the patched map and shows the stroke.
    /// </summary>
    public class RpgStrokeTests
    {
        private const string MapOfVillage = "rpg_village";
        private const string PatchMaps = "patches/rpg_map/my_mod.json";

        private const string StrokesText = @"{
  ""Kind"": ""rpg_map"",
  ""Items"": [
    {
      ""Id"": ""my_map"", ""Width"": 4, ""Height"": 3,
      ""Legend"": {""g"": ""village_pack/ground/grass""},
      ""Ground"": [""gggg"", ""gggg"", ""gggg""],
      ""Collision"": [""...."", ""...."", ""....""],
      ""Strokes"": [
        {""Id"": ""bridge"", ""Layer"": ""Ground"", ""Object"": ""village_pack/ground/water"", ""Cells"": [[1, 1]]},
        {""Id"": ""hole"", ""Layer"": ""Ground"", ""Object"": null, ""Cells"": [[2, 2]]},
        {""Id"": ""block"", ""Layer"": ""Collision"", ""Code"": ""#"", ""Rects"": [[0, 2, 2, 1]]}
      ]
    }
  ]
}";

        [ModTest("strokes/own_strokes_are_read_over_the_rows")]
        static void OwnStrokesRead(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod, new[] { ("maps/my_map.json", StrokesText) });
            ModAssert.Equal(RpgEditorFixture.Water, doc.ObjectAt(RpgStrokeLayer.Ground, 1, 1), "a stroke paints over the row");
            ModAssert.Null(doc.ObjectAt(RpgStrokeLayer.Ground, 2, 2), "a stroke with a null object clears the cell");
            ModAssert.Equal(RpgEditorFixture.Grass, doc.ObjectAt(RpgStrokeLayer.Ground, 0, 0), "a cell without a stroke shows the row");
            ModAssert.True(doc.CodeAt(0, 2) == '#' && doc.CodeAt(1, 2) == '#' && doc.CodeAt(2, 2) == '.', "a collision rect stroke");
        }

        [ModTest("strokes/foreign_paint_is_a_strokes_add_patch")]
        static void ForeignPaintIsPatch(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.GameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);
            ModAssert.True(doc.IsForeign, "the village is another mod's map");
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Water), "water is painted over the grass");
            ModAssert.Equal(RpgEditorFixture.Water, doc.ObjectAt(RpgStrokeLayer.Ground, 0, 0), "the cell shows water");

            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only the patch file is written, the village file stays");
            var entry = Entry(RpgEditorFixture.TextOf(changes, PatchMaps), MapOfVillage);
            var strokes = entry["Strokes$add"] as JsonArray;
            ModAssert.NotNull(strokes, "the entry adds strokes");
            ModAssert.Equal(1, strokes.Count, "one stroke per paint");
            var stroke = strokes[0] as JsonObject;
            ModAssert.Equal("my_mod_1", stroke["Id"].GetValue<string>(), "the stroke id is the mod's own");
            ModAssert.Equal("Ground", stroke["Layer"].GetValue<string>(), "the layer");
            ModAssert.Equal(RpgEditorFixture.Water, stroke["Object"].GetValue<string>(), "the object");
            ModAssert.Equal("[[0,0]]", stroke["Cells"].ToJsonString(), "the cell");
            ModAssert.Equal(2, entry.Count, "the entry is Id and the stroke only");
        }

        [ModTest("strokes/runtime_shows_the_foreign_stroke")]
        static void RuntimeShowsStroke(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.GameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Water), "water is painted");
            ModAssert.True(doc.PaintCollision(new[] { (1, 0) }, 'o'), "the wall cell is opened (rpg_village row 0 is all walls)");
            var entry = Entry(RpgEditorFixture.TextOf(doc.Changes(), PatchMaps), MapOfVillage);

            var baseMap = RpgEditorFixture.BaseLookup(maps, placements)("rpg_map", MapOfVillage);
            var body = (JsonObject)entry.DeepClone();
            body.Remove("Id");
            JsonPatchMerger.Apply(baseMap, body, typeof(RpgMap));
            var map = JsonSerializer.Deserialize<RpgMap>(baseMap.ToJsonString(), JsonOptionsProvider.ReadOptions);
            var cells = RpgMapCells.Build(map, RpgEditorFixture.ObjectLookup());
            ModAssert.Equal(RpgEditorFixture.Water, cells.Ground(0, 0)?.ID, "the game paints the stroke over the village's grass");
            ModAssert.Equal(RpgEditorFixture.Grass, cells.Ground(1, 0)?.ID, "the other cells keep the village's ground");
            ModAssert.True(cells.Collision(1, 0) == 'o', "the collision stroke is read by the game too");
        }

        private static JsonObject Entry(string patchText, string id)
        {
            ModAssert.NotNull(patchText, "the patch file is written");
            var items = JsonNode.Parse(patchText)["Items"] as JsonArray;
            var entry = items?.OfType<JsonObject>().FirstOrDefault(e => e["Id"]?.ToString() == id);
            ModAssert.NotNull(entry, $"the patch has an entry for '{id}'");
            return entry;
        }
    }
}
