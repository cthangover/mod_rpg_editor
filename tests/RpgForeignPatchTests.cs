namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// Placements and maps of another mod (the game's <c>rpg_plus_game</c>, read as it is on disk) are edited as patches
    /// of the edited mod: the change is the minimal patch entry, the owner's entity is not touched, and undo takes the
    /// patch back. A new placement on a foreign map is refused.
    /// </summary>
    public class RpgForeignPatchTests
    {
        private const string Merchant = "rpg_village/merchant";
        private const string Door = "rpg_village/door_merchant";
        private const string MapOfVillage = "rpg_village";
        private const string PatchPlacements = "patches/rpg_placement/my_mod.json";

        [ModTest("foreign/moved_placement_is_a_minimal_patch")]
        static void MovedIsMinimal(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var merchant = placements.First(p => p.Id == Merchant);
            var writtenBefore = merchant.Written.ToJsonString();
            var x = merchant.Written["X"].GetValue<int>();

            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);
            ModAssert.True(doc.IsForeign, "the village is another mod's map");
            ModAssert.True(doc.MovePlacement(Merchant, x + 1, merchant.Written["Y"].GetValue<int>()), "the merchant moves one cell");

            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only the edited mod's patch file is written");
            var entry = Entry(RpgEditorFixture.TextOf(changes, PatchPlacements), Merchant);
            ModAssert.Equal(2, entry.Count, "the entry holds only Id and the changed X");
            ModAssert.Equal(x + 1, entry["X"].GetValue<int>(), "the new X");
            ModAssert.Equal(writtenBefore, merchant.Written.ToJsonString(), "the owner's entity is byte-identical as written");
            ModAssert.False(changes.Any(c => c.Path.StartsWith("placements/", System.StringComparison.Ordinal)), "the owner's file is never written");
        }

        [ModTest("foreign/second_move_stays_minimal_and_reset_removes_the_patch")]
        static void SecondMoveAndReset(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var merchant = placements.First(p => p.Id == Merchant);
            var x = merchant.Written["X"].GetValue<int>();
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);

            ModAssert.True(doc.MovePlacement(Merchant, x + 1, merchant.Written["Y"].GetValue<int>()), "first move");
            ModAssert.True(doc.MovePlacement(Merchant, x + 2, merchant.Written["Y"].GetValue<int>()), "second move");
            var entry = Entry(RpgEditorFixture.TextOf(doc.Changes(), PatchPlacements), Merchant);
            ModAssert.Equal(x + 2, entry["X"].GetValue<int>(), "the patch holds the last X, not a chain");
            ModAssert.Equal(2, entry.Count, "still only Id and X");

            ModAssert.True(doc.ResetPlacement(Merchant), "the patch of the merchant is dropped");
            ModAssert.Equal(0, doc.Changes().Count, "back at the saved state: nothing is written");
        }

        [ModTest("foreign/reset_of_the_only_entry_removes_the_patch_file")]
        static void ResetRemovesFile(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var existing = new[] { (PatchPlacements, "{\"Items\": [{\"Id\": \"" + Merchant + "\", \"X\": 30}]}") };
            var doc = RpgEditorFixture.Open(existing, maps, MapOfVillage, placements);
            ModAssert.Equal(30, doc.Placements.First(p => p.Id == Merchant).Data["X"].GetValue<int>(), "the saved patch moves the merchant");

            ModAssert.True(doc.ResetPlacement(Merchant), "the patch entry is dropped");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "the patch file is listed");
            ModAssert.Null(changes[0].Text, "without entries the patch file is removed (null text)");
            ModAssert.True(doc.Undo(), "undo the reset");
            ModAssert.Equal(0, doc.Changes().Count, "the saved patch is back, nothing is written");
        }

        [ModTest("foreign/delete_writes_dollar_delete")]
        static void DeleteWritesDelete(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);

            ModAssert.True(doc.DeletePlacement(Door), "the door is deleted for this mod");
            ModAssert.False(doc.DeletePlacement(Door), "a deleted placement is not deleted twice");
            var entry = Entry(RpgEditorFixture.TextOf(doc.Changes(), PatchPlacements), Door);
            ModAssert.True(entry["$delete"].GetValue<bool>(), "the entry is $delete: true");
            ModAssert.Equal(2, entry.Count, "and nothing else");
        }

        [ModTest("foreign/undo_takes_the_patch_back_and_redo_writes_it_again")]
        static void UndoAndRedo(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var merchant = placements.First(p => p.Id == Merchant);
            var x = merchant.Written["X"].GetValue<int>();
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);

            ModAssert.True(doc.MovePlacement(Merchant, x + 1, merchant.Written["Y"].GetValue<int>()), "move");
            ModAssert.True(doc.Undo(), "undo the move");
            ModAssert.Equal(0, doc.Changes().Count, "no patch after the undo");
            ModAssert.True(doc.Redo(), "redo the move");
            ModAssert.Equal(x + 1, Entry(RpgEditorFixture.TextOf(doc.Changes(), PatchPlacements), Merchant)["X"].GetValue<int>(), "the patch is back");
        }

        [ModTest("foreign/new_placement_on_a_foreign_map_is_refused")]
        static void NewPlacementRefused(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);
            ModAssert.Null(doc.AddPlacement("torch", RpgEditorFixture.Trigger, 1, 1), "a package does not place on another mod's map");
            ModAssert.Equal(0, doc.Changes().Count, "nothing is written");
        }

        [ModTest("foreign/ground_stroke_on_a_foreign_map_is_a_stroke_in_the_patch")]
        static void GroundStrokeIsPatched(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.LegacyGameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);
            var mapBefore = maps.First(m => m.Id == MapOfVillage).Written.ToJsonString();

            ModAssert.True(doc.Paint(Cthangover.Mods.Rpg.Model.RpgStrokeLayer.Ground, new[] { (0, 0), (1, 0) }, RpgEditorFixture.Water),
                "two cells of a foreign map are painted");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only the edited mod's map patch is written");
            ModAssert.Equal(ForeignEntities.PatchFile("rpg_map", RpgEditorFixture.OwnMod), changes[0].Path, "in patches/rpg_map/<mod>.json");
            var patch = JsonNode.Parse(changes[0].Text)["Items"] as JsonArray;
            var entry = patch.OfType<JsonObject>().FirstOrDefault(e => e["Id"]?.ToString() == MapOfVillage);
            ModAssert.NotNull(entry, "the patch entry of the village map");
            var strokes = (entry["Strokes$add"] ?? entry["Strokes"]) as JsonArray;   // a list is added with $add (the stroke book's patch form)
            ModAssert.True(strokes != null && strokes.Count == 1, "its stroke list holds one stroke");
            ModAssert.Equal(RpgEditorFixture.Water, strokes[0]["Object"]?.ToString(), "the stroke paints the surface");
            ModAssert.Equal(mapBefore, maps.First(m => m.Id == MapOfVillage).Written.ToJsonString(), "the owner's map is byte-identical as written");

            ModAssert.True(doc.Undo(), "undo the stroke");
            ModAssert.Equal(0, doc.Changes().Count, "no patch after the undo");
        }

        /// <summary>The patch entry of <paramref name="id"/> in a patch file text; fails the test when it is missing.</summary>
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
