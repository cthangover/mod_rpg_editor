namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.EntityEditor;

    /// <summary>Resizing preserves existing cells, content and lights through undo, save/reopen and foreign patches.</summary>
    public class RpgMapResizeTests
    {
        [ModTest("resize/grow_paint_new_cells_save_reopen_and_undo")]
        static void Grow(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.Resize(7, 6), "grow both dimensions");
            ModAssert.Equal(7, doc.Width, "width");
            ModAssert.Equal(6, doc.Height, "height");
            ModAssert.Equal(RpgEditorFixture.Grass, doc.ObjectAt(RpgStrokeLayer.Ground, 2, 2), "old floor retains coordinates");
            ModAssert.Equal('#', doc.CodeAt(2, 1), "old collision stays");
            ModAssert.Equal('o', doc.CodeAt(3, 1), "open override stays");
            ModAssert.Equal('.', doc.CodeAt(6, 5), "new edge is editable empty ground");
            ModAssert.True(doc.PaintCollision(new[] { (6, 5) }, '#'), "paint outside the old bounds");
            var reopened = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod,
                doc.Changes().Select(f => (f.Path, f.Text)));
            ModAssert.Equal(7, reopened.Width, "saved width survives reopen");
            ModAssert.Equal(6, reopened.Height, "saved height survives reopen");
            ModAssert.Equal('#', reopened.CodeAt(6, 5), "saved new edge survives reopen");
            ModAssert.True(doc.Undo(), "undo paint");
            ModAssert.True(doc.Undo(), "one step undoes the entire resize");
            ModAssert.Equal(0, doc.Changes().Count, "original files remain byte stable after undo");
            ModAssert.True(doc.Redo(), "redo resize");
            ModAssert.True(doc.Resize(4, 3), "shrink the empty added edges");
            ModAssert.Equal(4, doc.Data["Ground"][0].GetValue<string>().Length, "rows match the smaller width");
        }

        [ModTest("resize/refuses_stranding_content_without_an_undo_step")]
        static void Refused(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.False(doc.Resize(2, 2), "cannot strand the well");
            ModAssert.False(doc.Resize(0, 5), "no empty dimensions");
            ModAssert.False(doc.Resize(int.MaxValue, int.MaxValue), "no overflowing allocations");
            ModAssert.False(doc.Resize(4, 3), "unchanged dimensions");
            ModAssert.False(doc.CanUndo, "refusals record nothing");
            doc.SetSpawn(3, 0);
            ModAssert.False(doc.Resize(3, 3), "spawn must stay inside");
            doc.Undo();
            doc.AddEvent(3, 0, 1, 2);
            ModAssert.False(doc.Resize(3, 3), "whole event must stay inside");
        }

        [ModTest("resize/light_cell_positions_are_stable_after_round_trip")]
        static void Lights(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            doc.SetProperty("Lights", JsonNode.Parse("[{\"id\":\"window\",\"x\":0.5,\"y\":-0.5,\"radius\":3}]"));
            ModAssert.True(doc.Resize(8, 6), "resize lit map");
            var reopened = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod, doc.Changes().Select(f => (f.Path, f.Text)));
            var map = RpgEditorScene.MapOf(reopened.Data);
            ModAssert.Equal(0.25f, map.Lights[0].X, what: "same two-cell light X");
            ModAssert.Equal(-0.25f, map.Lights[0].Y, what: "wall light retains its off-floor Y");
            ModAssert.True(doc.Undo(), "undo resize");
            ModAssert.Equal(0.5f, (float)doc.Data["Lights"][0]["x"].GetValue<double>(), what: "undo light basis");
        }

        [ModTest("resize/foreign_map_changes_only_the_dependent_patch")]
        static void Foreign(ModTestContext ctx)
        {
            var source = RpgEditorFixture.EntitiesOf(new[] { ("maps/my_map.json", RpgEditorFixture.OwnMapText) }, "rpg_map", "owner");
            var before = source[0].Written.ToJsonString();
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], source);
            ModAssert.True(doc.Resize(6, 5), "foreign resize");
            var change = doc.Changes().Single();
            ModAssert.Equal(ForeignEntities.PatchFile("rpg_map", RpgEditorFixture.OwnMod), change.Path, "patch destination");
            ModAssert.Equal(6, JsonNode.Parse(change.Text)["Items"][0]["Width"].GetValue<int>(), "patch carries dimensions");
            ModAssert.Equal(before, source[0].Written.ToJsonString(), "owner untouched");
            doc.Undo();
            ModAssert.Equal(0, doc.Changes().Count, "undo removes patch");
        }
    }
}
