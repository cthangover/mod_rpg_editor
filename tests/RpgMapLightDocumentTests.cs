namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Tools.EntityEditor;

    /// <summary>Light-only edits use the standard map document, including foreign patches and undo, without any object or placement files.</summary>
    public class RpgMapLightDocumentTests
    {
        [ModTest("lights/document_undo_and_save_do_not_create_objects")]
        static void Own(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.Equal(0, doc.Changes().Count, "opening an old map writes nothing");
            var lights = JsonNode.Parse("[{\"id\":\"my_mod/window\",\"x\":0.5,\"y\":0.5,\"radius\":3,\"influence\":0.6,\"color\":\"#ffaa66\"}]");
            ModAssert.True(doc.SetProperty("Lights", lights), "add a resource-backed source");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "one map file only");
            ModAssert.True(changes[0].Path.StartsWith("maps/"), "no object or placement file is created");
            var read = JsonNode.Parse(changes[0].Text)["Items"][0]["Lights"][0];
            ModAssert.Equal("my_mod/window", read["id"].GetValue<string>(), "source is written into the map");
            ModAssert.True(doc.Undo(), "undo one light edit");
            ModAssert.Equal(0, doc.Changes().Count, "undo restores a byte-stable no-op");
            ModAssert.True(doc.Redo(), "redo light edit");
            ModAssert.Equal("my_mod/window", doc.Data["Lights"][0]["id"].GetValue<string>(), "redo restores stable identity");
        }
        [ModTest("lights/foreign_map_writes_only_the_dependent_patch")]
        static void Foreign(ModTestContext ctx)
        {
            var source = RpgEditorFixture.EntitiesOf(new[] { ("maps/my_map.json", RpgEditorFixture.OwnMapText) }, "rpg_map", "owner");
            var owner = source[0].Written.ToJsonString();
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], source);
            ModAssert.True(doc.SetProperty("Lights", JsonNode.Parse("[{\"id\":\"my_mod/light_1\",\"x\":0.5,\"y\":0.5,\"radius\":4}]")), "add source to foreign map");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only the patch changes");
            ModAssert.Equal(ForeignEntities.PatchFile("rpg_map", RpgEditorFixture.OwnMod), changes[0].Path, "normal owner-aware patch destination");
            ModAssert.Equal(owner, source[0].Written.ToJsonString(), "owner remains unchanged");
            ModAssert.NotNull(JsonNode.Parse(changes[0].Text)["Items"][0]["Lights$add"], "keyed source addition");
            ModAssert.True(doc.Undo(), "undo foreign light");
            ModAssert.Equal(0, doc.Changes().Count, "undo removes the pending patch");
        }
    }
}
