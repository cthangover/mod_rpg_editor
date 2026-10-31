namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The view model of the window without the canvas: the teleport rule (a broken target is named by its problem),
    /// the outgoing links read from the effective object, the map setters with undo, and the patched and deleted
    /// queries of foreign placements.
    /// </summary>
    public class RpgEditorViewTests
    {
        [ModTest("view/broken_teleport_targets_are_named")]
        static void BrokenTargets(ModTestContext ctx)
        {
            var maps = new Dictionary<string, RpgMap>
            {
                ["here"] = new RpgMap { ID = "here", Width = 4, Height = 3, Collision = new List<string> { "....", ".#..", "...." } },
            };
            ModAssert.Equal(RpgTeleportLink.BrokenMap, RpgTeleportLinks.BrokenOf(new RpgTeleport { Map = "nowhere", X = 0, Y = 0 }, maps), "a missing map");
            ModAssert.Equal(RpgTeleportLink.BrokenCell, RpgTeleportLinks.BrokenOf(new RpgTeleport { Map = "here", X = 4, Y = 0 }, maps), "a cell off the map");
            ModAssert.Equal(RpgTeleportLink.BrokenSolid, RpgTeleportLinks.BrokenOf(new RpgTeleport { Map = "here", X = 1, Y = 1 }, maps), "a blocked cell");
            ModAssert.Null(RpgTeleportLinks.BrokenOf(new RpgTeleport { Map = "HERE", X = 0, Y = 2 }, maps), "a walkable cell of an existing map (ids without case)");
        }

        [ModTest("view/outgoing_reads_the_effective_object")]
        static void OutgoingReadsObject(ModTestContext ctx)
        {
            var door = new RpgObject
            {
                ID = "door",
                Form = RpgObjectForm.Sprite,
                Interact = new RpgInteract { Teleport = new RpgTeleport { Map = "here", X = 2, Y = 2 } },
            };
            var map = new RpgMap { ID = "elsewhere", Width = 5, Height = 5 };
            var placement = new RpgPlacement { ID = "elsewhere/door", Object = "door", X = 0, Y = 0 };
            var maps = new Dictionary<string, RpgMap> { ["here"] = new RpgMap { ID = "here", Width = 4, Height = 4 }, ["elsewhere"] = map };
            var links = RpgTeleportLinks.Outgoing("elsewhere", new[] { placement }, map, id => id == "door" ? door : null, maps);
            ModAssert.Equal(1, links.Count, "one teleport leaves the map");
            ModAssert.Equal("here", links[0].Map, "to the map named by the object");
            ModAssert.Equal(2, links[0].X, "the target cell");
            ModAssert.Null(links[0].Broken, "and the target is walkable");
            ModAssert.Equal(0, RpgTeleportLinks.Outgoing("elsewhere", new[] { new RpgPlacement { ID = "elsewhere/x", Object = "unknown" } }, map, id => null, maps).Count,
                "an unknown object carries no teleport");
        }

        [ModTest("view/map_setters_are_undoable")]
        static void SettersUndo(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.SetSpawn(2, 1), "the spawn moves to a cell of the map");
            ModAssert.Equal(2, doc.Map.Data["Spawn"]["X"].GetValue<int>(), "the spawn X is written");
            ModAssert.False(doc.SetSpawn(2, 1), "the same cell changes nothing");
            ModAssert.False(doc.SetSpawn(9, 9), "a cell off the map is refused");
            ModAssert.True(doc.SetProperty("Variant", JsonValue.Create("winter")), "a property is set");
            ModAssert.Equal("winter", doc.Map.Data["Variant"].ToString(), "the variant is written");
            ModAssert.True(doc.SetProperty("Variant", null), "null removes the property");
            ModAssert.Null(doc.Map.Data["Variant"], "and it is gone");
            ModAssert.True(doc.Undo(), "undo the removal");
            ModAssert.Equal("winter", doc.Map.Data["Variant"].ToString(), "the variant is back");
            ModAssert.True(doc.Undo(), "undo the variant");
            ModAssert.Null(doc.Map.Data["Variant"], "no variant again");
            ModAssert.True(doc.Undo(), "undo the spawn");
            ModAssert.Null(doc.Map.Data["Spawn"], "the map had no spawn before: undo takes it away again");
        }

        [ModTest("view/patched_and_deleted_placements_are_reported")]
        static void PatchedAndDeleted(ModTestContext ctx)
        {
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.GameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, "rpg_village", placements);
            const string merchant = "rpg_village/merchant";
            const string door = "rpg_village/door_merchant";
            ModAssert.False(doc.IsPatched(merchant), "an untouched foreign placement is not patched");
            ModAssert.False(doc.IsDeleted(merchant), "and not deleted");
            var written = placements.First(p => p.Id == merchant);
            var x = written.Written["X"].GetValue<int>();
            var y = written.Written["Y"].GetValue<int>();
            ModAssert.True(doc.MovePlacement(merchant, x + 1, y), "a move of the merchant");
            ModAssert.True(doc.IsPatched(merchant), "the moved foreign placement is patched");
            ModAssert.False(doc.IsDeleted(merchant), "but not deleted");
            ModAssert.True(doc.ResetPlacement(merchant), "reset it");
            ModAssert.False(doc.IsPatched(merchant), "and it is no longer patched");
            ModAssert.True(doc.DeletePlacement(door), "delete the door");
            ModAssert.True(doc.IsDeleted(door), "the deleted door is reported");
            ModAssert.True(doc.Undo(), "undo the deletion");
            ModAssert.False(doc.IsDeleted(door), "and it is no longer deleted");
            ModAssert.False(doc.IsPatched("no_such/placement"), "an unknown id is not patched");
        }
    }
}
