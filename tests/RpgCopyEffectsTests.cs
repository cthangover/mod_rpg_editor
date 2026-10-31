namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;

    /// <summary>
    /// Copy and paste of placements (plan 14.G9) and the effect masks (plan 14.G10) on the document: a paste makes copies with
    /// their offsets and free ids, one undo step takes them back, a paste that does not fit is refused, and a mask paints,
    /// erases and drops its entry as the cells run out.
    /// </summary>
    public class RpgCopyEffectsTests
    {
        private const string Fog = "test_fog";

        [ModTest("copy/paste_makes_copies_with_offsets_and_one_undo_takes_them_back")]
        static void PasteCopies(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            var clips = doc.ClipOf(new[] { "my_map/sign", "my_map/well" });
            ModAssert.Equal(2, clips.Count, "two placements are copied");

            var ids = doc.Paste(clips, 0, 0);
            ModAssert.True(ids != null && ids.Count == 2, "the paste makes two placements");
            ModAssert.Equal("my_map/sign_2", ids[0], "the copy of sign gets a free id");
            ModAssert.Equal(0, PlacementX(doc, ids[0]), "the first clip lands on the anchor column");
            ModAssert.Equal(0, PlacementY(doc, ids[0]), "and the anchor row");
            ModAssert.Equal(1, PlacementX(doc, ids[1]), "the second keeps its offset (one column right)");
            ModAssert.Equal(1, PlacementY(doc, ids[1]), "and one row down");

            ModAssert.True(doc.Undo(), "undo the paste");
            ModAssert.False(doc.Placements.Any(p => p.Id == ids[0]) || doc.Placements.Any(p => p.Id == ids[1]), "one step removes both copies");
            ModAssert.True(doc.Placements.Any(p => p.Id == "my_map/sign"), "the originals stay");
        }

        [ModTest("copy/paste_that_does_not_fit_is_refused_without_a_step")]
        static void PasteRefused(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            var clips = doc.ClipOf(new[] { "my_map/sign", "my_map/well" });
            // The map is 4 columns wide: the well's copy would stand on column 4.
            ModAssert.True(doc.Paste(clips, 3, 0) == null, "a paste that leaves the map is refused");
            ModAssert.False(doc.CanUndo, "and makes no step");
        }

        [ModTest("copy/delete_of_several_placements_is_one_step")]
        static void DeleteMany(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.DeletePlacements(new[] { "my_map/sign", "my_map/well", "my_map/sign" }), "two placements are deleted");
            ModAssert.False(doc.Placements.Any(p => p.Id == "my_map/sign" || p.Id == "my_map/well"), "both are gone");
            ModAssert.True(doc.Undo(), "one undo step");
            ModAssert.True(doc.Placements.Any(p => p.Id == "my_map/sign") && doc.Placements.Any(p => p.Id == "my_map/well"), "both are back");
            ModAssert.False(doc.DeletePlacements(new[] { "other_map/well" }), "a placement of another map is not deleted here");
        }

        [ModTest("effects/paint_and_erase_write_the_entry_and_drop_it_when_empty")]
        static void PaintAndErase(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            var cells = new[] { (0, 0), (1, 0), (0, 1), (9, 9) };
            ModAssert.True(doc.PaintEffect(Fog, cells, false), "paint four cells (one off the map is skipped)");
            ModAssert.Equal(3, CellCount(doc, Fog), "three cells are in the mask");
            ModAssert.False(doc.PaintEffect(Fog, cells, false), "painting the same cells again changes nothing");

            ModAssert.True(doc.PaintEffect(Fog, new[] { (1, 0) }, true), "erase one cell");
            ModAssert.Equal(2, CellCount(doc, Fog), "two cells remain");
            ModAssert.True(doc.PaintEffect(Fog, new[] { (0, 0), (0, 1) }, true), "erase the rest");
            ModAssert.Equal(-1, CellCount(doc, Fog), "an empty mask leaves no entry");
            ModAssert.True(doc.Undo(), "undo the last erase");
            ModAssert.Equal(2, CellCount(doc, Fog), "and its cells come back");
        }

        [ModTest("effects/whole_map_entry_is_made_explicit_by_an_erase")]
        static void WholeMap(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.SetProperty("Effects", JsonNode.Parse("[{\"Profile\":\"" + Fog + "\"}]")), "a whole-map entry");
            ModAssert.False(doc.PaintEffect(Fog, new[] { (2, 2) }, false), "painting over a whole-map entry changes nothing");
            ModAssert.True(doc.PaintEffect(Fog, new[] { (2, 2) }, true), "an erase makes the cells explicit");
            ModAssert.Equal(11, CellCount(doc, Fog), "every cell but the erased one (4 x 3 map)");
        }

        private static int PlacementX(RpgMapDocument doc, string id)
            => doc.Placements.First(p => p.Id == id).Data["X"].GetValue<int>();

        private static int PlacementY(RpgMapDocument doc, string id)
            => doc.Placements.First(p => p.Id == id).Data["Y"].GetValue<int>();

        /// <summary>Explicit cells of the entry of <paramref name="profile"/>; -1 when there is no entry, 0 for a whole-map entry.</summary>
        private static int CellCount(RpgMapDocument doc, string profile)
        {
            if (doc.Data?["Effects"] is not JsonArray entries)
                return -1;
            foreach (var node in entries)
            {
                if (node is JsonObject entry && entry["Profile"] is JsonValue name && name.TryGetValue(out string text) && text == profile)
                    return entry["Cells"] is JsonArray cells ? cells.Count : 0;
            }
            return -1;
        }
    }
}
