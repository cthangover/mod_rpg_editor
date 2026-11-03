namespace Cthangover.Tools.RpgEditor.Tests
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Creating a zone needs only the map file; movement, resizing, copies, undo and foreign-map patches preserve it.</summary>
    public class RpgMapEventDocumentTests
    {
        [ModTest("map_events/create_edit_save_reload_and_undo")]
        static void Own(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            var id = doc.AddEvent(0, 0, 2, 2);
            ModAssert.NotNull(id, "new event needs no object");
            ModAssert.True(doc.SetEvent(id, new JsonObject { ["Commands"] = new JsonArray("flag self.done 1", "end"), ["When"] = "self.done != 1" }), "write script and condition");
            ModAssert.True(doc.SetPlacementBounds(id, 1, 0, 3, 2), "resize and move in one step");
            ModAssert.True(doc.Undo() && doc.EventOf(id).X == 0 && doc.EventOf(id).Width == 2, "undo restores origin and extent");
            ModAssert.True(doc.Redo(), "redo extent");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only map file changes");
            ModAssert.True(changes[0].Path.StartsWith("maps/", StringComparison.Ordinal), "no placement or object is created");
            var restored = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod,
                new[] { changes[0], ("placements/my_map/events.json", RpgEditorFixture.OwnPlacementsText) });
            var entry = restored.EventOf(id);
            ModAssert.True(entry.X == 1 && entry.Y == 0 && entry.Width == 3 && entry.Height == 2, "saved rectangle roundtrip");
            ModAssert.Equal("flag self.done 1", entry.Commands[0], "script roundtrip");
            ModAssert.Equal("self.done != 1", entry.When, "condition roundtrip");
            ModAssert.False(restored.MovePlacement(id, 3, 2), "event cannot move partly outside map");
            var copies = restored.Paste(restored.ClipOf(new[] { id }), 0, 0);
            ModAssert.True(copies.Count == 1 && copies[0] != id && restored.EventOf(copies[0]) != null, "copy makes a separate event identity");
            ModAssert.True(restored.Undo() && restored.Events.Count == 1, "undo copy is atomic");
            ModAssert.True(restored.DeletePlacement(id) && restored.Events.Count == 0, "delete event");
            ModAssert.True(restored.Undo() && restored.Events.Count == 1, "undo deletion");
        }

        [ModTest("map_events/incoming_event_target_is_written_in_source_map")]
        static void Incoming(ModTestContext ctx)
        {
            var mapA = RpgEditorFixture.OwnMapText.Replace("my_map", "A");
            const string mapB = @"{""Kind"":""rpg_map"",""Items"":[{""Id"":""B"",""Width"":4,""Height"":4,""Events"":[
                {""Id"":""exit"",""X"":1,""Y"":1,""Trigger"":""Step"",""Teleport"":{""Map"":""A"",""X"":1,""Y"":1}}]}]}";
            var doc = RpgEditorFixture.OpenOwn("A", RpgEditorFixture.OwnMod, new[] { ("maps/A.json", mapA), ("maps/B.json", mapB) });
            ModAssert.True(doc.HasOwnTeleport("B/exit") && doc.SetTeleportCell("B/exit", 2, 1), "incoming event target moves");
            var changes = doc.Changes();
            ModAssert.True(changes.Count == 1 && changes[0].Path == "maps/B.json", "only source map file changes");
            var written = JsonNode.Parse(changes[0].Text)["Items"][0]["Events"][0];
            ModAssert.Equal(2, written["Teleport"]["X"].GetValue<int>(), "target cell written");
            ModAssert.Equal("A", written["Teleport"]["Map"].GetValue<string>(), "destination map preserved");
            ModAssert.True(doc.Undo() && doc.Changes().Count == 0, "one undo restores link");
        }

        [ModTest("map_events/foreign_map_patch_and_atomic_undo")]
        static void Foreign(ModTestContext ctx)
        {
            var maps = RpgEditorFixture.EntitiesOf(new[] { ("maps/my_map.json", RpgEditorFixture.OwnMapText) }, "rpg_map", "dependency");
            var before = maps[0].Written.ToJsonString();
            var doc = RpgEditorFixture.Open(Array.Empty<(string Path, string Text)>(), maps);
            var id = doc.AddEvent(0, 0, 2, 1);
            ModAssert.NotNull(id, "events can extend a dependency map through patches");
            var changes = doc.Changes();
            ModAssert.True(changes.Count == 1 && changes[0].Path.StartsWith("patches/", StringComparison.Ordinal), "only own map patch written");
            ModAssert.Equal(before, maps[0].Written.ToJsonString(), "owner data unchanged");
            ModAssert.True(doc.Undo() && doc.Changes().Count == 0, "undo removes own patch");
        }
    }
}
