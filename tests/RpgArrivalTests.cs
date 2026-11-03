namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;

    /// <summary>
    /// The arrival and the target of a teleport written on the source placement (plan 14.G18): a placement of another map of the
    /// same mod changes in its own file, a dependency's placement changes as a patch of the edited mod, a placement whose teleport
    /// comes from its object has nothing to write, and every write is one undo step.
    /// </summary>
    public class RpgArrivalTests
    {
        private const string OwnPlacements = @"{
  ""Kind"": ""rpg_placement"",
  ""Items"": [
    {""Id"": ""A/own"", ""Object"": ""village_pack/marker/trigger"", ""X"": 1, ""Y"": 1,
     ""Interact"": {""Trigger"": ""Step"", ""Teleport"": {""Map"": ""B"", ""X"": 0, ""Y"": 0, ""Dir"": ""Up""}}},
    {""Id"": ""B/door"", ""Object"": ""village_pack/marker/trigger"", ""X"": 2, ""Y"": 2,
     ""Interact"": {""Trigger"": ""Step"", ""Teleport"": {""Map"": ""A"", ""X"": 1, ""Y"": 3, ""Dir"": ""Up""}}},
    {""Id"": ""B/plain"", ""Object"": ""village_pack/marker/trigger"", ""X"": 0, ""Y"": 1}
  ]
}";

        private const string MapA = @"{ ""Kind"": ""rpg_map"", ""Items"": [ { ""Id"": ""A"", ""Width"": 4, ""Height"": 4, ""Legend"": {} } ] }";

        [ModTest("arrival/own_placement_of_another_map_is_written_in_its_file")]
        static void OwnOtherMap(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OpenOwn("A", RpgEditorFixture.OwnMod,
                new[] { ("maps/A.json", MapA), ("placements/B/B.json", OwnPlacements) });
            ModAssert.True(doc.AnyPlacement("B/door") != null, "the placement of map B is in the document");
            ModAssert.True(doc.HasOwnTeleport("B/door"), "its teleport is its own");
            ModAssert.True(doc.SetTeleportCell("B/door", 2, 1), "the arrival moves to a cell of map A");
            ModAssert.Equal(2, TeleportOf(doc, "B/door")["X"].GetValue<int>(), "the placement's X is written");
            ModAssert.Equal(1, TeleportOf(doc, "B/door")["Y"].GetValue<int>(), "and its Y");
            ModAssert.True(doc.PlacementRecords.Any(r => r.Id == "B/door"), "the record is listed among the placements of the mod");
            ModAssert.False(doc.SetTeleportCell("B/door", 2, 1), "the same cell changes nothing and makes no step");
            var text = doc.Changes().Single(c => c.Path == "placements/B/B.json").Text;
            var item = JsonNode.Parse(text)["Items"].AsArray().First(i => i["Id"]?.ToString() == "B/door");
            ModAssert.Equal(2, item["Interact"]["Teleport"]["X"].GetValue<int>(), "the saved file holds the new cell");
            ModAssert.Equal("A", item["Interact"]["Teleport"]["Map"].ToString(), "and keeps the map it leads to");
            ModAssert.True(doc.Undo(), "the move is one undo step");
            ModAssert.Equal(1, TeleportOf(doc, "B/door")["X"].GetValue<int>(), "undo brings the arrival back");
        }

        [ModTest("arrival/object_teleport_and_unknown_ids_are_not_written")]
        static void Refused(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OpenOwn("A", RpgEditorFixture.OwnMod,
                new[] { ("maps/A.json", MapA), ("placements/B/B.json", OwnPlacements) });
            ModAssert.False(doc.HasOwnTeleport("B/plain"), "a placement without its own interaction has no own teleport");
            ModAssert.False(doc.SetTeleportCell("B/plain", 1, 1), "so nothing is written to it");
            ModAssert.False(doc.SetTeleportCell("no_such/door", 1, 1), "an unknown placement is not written");
            ModAssert.False(doc.Undo(), "and no step was made");
        }

        [ModTest("arrival/dependency_placement_is_a_patch_of_the_edited_mod")]
        static void Foreign(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, "rpg_village", placements);
            const string door = "rpg_desert/rpg_plus_enter_desert_house";
            ModAssert.True(doc.AnyPlacement(door) != null && doc.AnyPlacement(door).IsForeign, "a dependency's door is in the document");
            ModAssert.True(doc.SetTeleportCell(door, 6, 8), "its arrival moves");
            var patch = doc.Changes().FirstOrDefault(c => c.Path.EndsWith("rpg_placement/" + RpgEditorFixture.OwnMod + ".json"));
            ModAssert.True(patch.Text != null, "the change is a patch file of the edited mod");
            var item = JsonNode.Parse(patch.Text)["Items"].AsArray().First(i => i["Id"]?.ToString() == door);
            ModAssert.Equal(6, item["Interact"]["Teleport"]["X"].GetValue<int>(), "the patch holds the new X");
            ModAssert.True(doc.Undo(), "one undo step");
            ModAssert.False(doc.Changes().Any(c => c.Path.EndsWith("rpg_placement/" + RpgEditorFixture.OwnMod + ".json")),
                "undo leaves no patch behind");
        }

        private static JsonNode TeleportOf(RpgMapDocument doc, string id)
            => doc.AnyPlacement(id).Data["Interact"]["Teleport"];
    }
}
