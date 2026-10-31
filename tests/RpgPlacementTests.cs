namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;

    /// <summary>
    /// Placements of an own map: add (unique ids, refusals), move, and delete, with the file each one is written to.
    /// A placement of another map is not this map's and is not touched.
    /// </summary>
    public class RpgPlacementTests
    {
        [ModTest("placements/add_gives_a_unique_id_and_refuses_bad_ones")]
        static void Add(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.Equal("my_map/torch", doc.AddPlacement("torch", RpgEditorFixture.Trigger, 2, 0), "the new id");
            ModAssert.Equal("my_map/torch_2", doc.AddPlacement("torch", RpgEditorFixture.Trigger, 3, 0), "a taken local id gets a suffix");
            ModAssert.Equal("my_map/npc", doc.AddPlacement("npc", RpgEditorFixture.Merchant, 0, 2), "an actor is a placement");
            ModAssert.True(Ids(doc).Contains("my_map/torch_2") && Ids(doc).Contains("my_map/npc"), "the new placements are listed");

            ModAssert.Null(doc.AddPlacement("bad/id", RpgEditorFixture.Trigger, 0, 0), "a local id with '/' is refused");
            ModAssert.Null(doc.AddPlacement("", RpgEditorFixture.Trigger, 0, 0), "an empty local id is refused");
            ModAssert.Null(doc.AddPlacement("wall", RpgEditorFixture.Fence, 0, 0), "a segment is not a placement");
            ModAssert.Null(doc.AddPlacement("far", RpgEditorFixture.Trigger, 4, 0), "a cell off the map is refused");
            ModAssert.Null(doc.AddPlacement("ghost", "no/such/object", 0, 0), "an unknown object is refused");

            var text = RpgEditorFixture.TextOf(doc.Changes(), "placements/my_map/my_map.json");
            ModAssert.NotNull(text, "new placements go to the map's own placement file");
            ModAssert.Equal(3, (JsonNode.Parse(text)["Items"] as JsonArray).Count, "the three added placements are in it");
        }

        [ModTest("placements/move_changes_the_cell_and_undo_restores_it")]
        static void Move(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.MovePlacement("my_map/sign", 3, 2), "the sign moves");
            ModAssert.Equal(3, X(doc, "my_map/sign"), "X after the move");
            ModAssert.Equal(2, Y(doc, "my_map/sign"), "Y after the move");
            ModAssert.False(doc.MovePlacement("my_map/sign", 3, 2), "the same cell is no change");
            ModAssert.False(doc.MovePlacement("my_map/sign", 9, 9), "a cell off the map is refused");
            ModAssert.False(doc.MovePlacement("other_map/well", 1, 1), "a placement of another map is not this map's");
            ModAssert.False(doc.MovePlacement("my_map/missing", 1, 1), "an unknown placement");

            ModAssert.True(doc.Undo(), "the move is undone");
            ModAssert.Equal(1, X(doc, "my_map/sign"), "X back");
            ModAssert.Equal(1, Y(doc, "my_map/sign"), "Y back");
        }

        [ModTest("placements/delete_removes_only_the_placement")]
        static void Delete(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.DeletePlacement("my_map/well"), "the well is deleted");
            ModAssert.False(Ids(doc).Contains("my_map/well"), "it is no longer listed");
            ModAssert.False(doc.DeletePlacement("my_map/well"), "a second delete finds nothing");
            ModAssert.False(doc.DeletePlacement("other_map/well"), "another map's placement is not deleted from here");

            var text = RpgEditorFixture.TextOf(doc.Changes(), "placements/my_map/events.json");
            ModAssert.NotNull(text, "the file of the deleted placement is written");
            var ids = (JsonNode.Parse(text)["Items"] as JsonArray).Select(item => item["Id"].ToString()).ToList();
            ModAssert.True(ids.SequenceEqual(new[] { "my_map/sign", "other_map/well" }), "the other placements of the file stay in order");
        }

        private static List<string> Ids(RpgMapDocument doc)
            => doc.Placements.Select(p => p.Id).ToList();

        private static int X(RpgMapDocument doc, string id)
            => doc.Placements.First(p => p.Id == id).Data["X"].GetValue<int>();

        private static int Y(RpgMapDocument doc, string id)
            => doc.Placements.First(p => p.Id == id).Data["Y"].GetValue<int>();
    }
}
