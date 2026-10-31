namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The grids of an own map: painting a surface or a segment writes its cell and gives the object a legend symbol
    /// (reused or new), symbols no grid uses go on save, a wrong form or an unknown object is refused, and collision
    /// codes are written as rows.
    /// </summary>
    public class RpgGridTests
    {
        [ModTest("grid/paint_reuses_and_adds_legend_symbols")]
        static void ReusesAndAddsSymbols(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Grass), "a cell that shows the object is not painted");
            ModAssert.False(doc.CanUndo, "a change that changes nothing leaves no undo step");

            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (2, 2) }, RpgEditorFixture.Water), "water is painted");
            ModAssert.Equal(RpgEditorFixture.Water, doc.ObjectAt(RpgStrokeLayer.Ground, 2, 2), "the cell shows water");
            ModAssert.Equal(RpgEditorFixture.Water, doc.Legend["a"], "water takes the first free symbol");
            ModAssert.Equal(3, doc.Legend.Count, "legend: g, f and the new a");

            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (3, 2), (2, 1) }, RpgEditorFixture.Water), "two more cells");
            ModAssert.Equal(3, doc.Legend.Count, "the water symbol is reused, not added again");

            ModAssert.True(doc.Paint(RpgStrokeLayer.Structure, new[] { (1, 0) }, RpgEditorFixture.Fence), "a fence in the structure grid");
            ModAssert.Equal(RpgEditorFixture.Fence, doc.ObjectAt(RpgStrokeLayer.Structure, 1, 0), "the fence cell shows the fence");
            ModAssert.Equal(3, doc.Legend.Count, "the fence reuses the symbol f");
        }

        [ModTest("grid/wrong_form_unknown_object_and_outside_are_refused")]
        static void Refusals(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (1, 1) }, RpgEditorFixture.Fence), "a segment is not painted on the ground");
            ModAssert.False(doc.Paint(RpgStrokeLayer.Structure, new[] { (1, 1) }, RpgEditorFixture.Grass), "a surface is not painted in the structure");
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (1, 1) }, "no/such/object"), "an unknown object is refused");
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (9, 9) }, RpgEditorFixture.Water), "cells outside the map are skipped");
            ModAssert.Equal(RpgEditorFixture.Grass, doc.ObjectAt(RpgStrokeLayer.Ground, 1, 1), "a refused paint changes nothing");
            ModAssert.False(doc.CanUndo, "a refused paint leaves no undo step");
        }

        [ModTest("grid/erase_then_save_drops_the_unused_symbol")]
        static void EraseThenSave(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (2, 2), (3, 2) }, RpgEditorFixture.Water), "water is painted");
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (2, 2), (3, 2) }, null), "and erased");
            ModAssert.True(doc.Legend.ContainsKey("a"), "the symbol stays until the save");

            var changes = doc.Changes();
            var mapText = RpgEditorFixture.TextOf(changes, "maps/my_map.json");
            ModAssert.NotNull(mapText, "the map file is written");
            var legend = RpgEditorFixture.LegendOf(mapText);
            ModAssert.False(legend.ContainsKey("a"), "the unused symbol is dropped on save");
            ModAssert.Equal(RpgEditorFixture.Grass, legend["g"], "a used symbol stays");
            ModAssert.Equal(RpgEditorFixture.Fence, legend["f"], "the fence symbol stays");

            doc.MarkSaved();
            ModAssert.Equal(0, doc.Changes().Count, "after the save nothing is written again");
        }

        [ModTest("grid/full_legend_refuses_a_new_object")]
        static void FullLegend(ModTestContext ctx)
        {
            const string candidates = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var legend = string.Join(", ", candidates.Select(c => $"\"{c}\": \"{RpgEditorFixture.Grass}\""));
            var text = "{\"Kind\": \"rpg_map\", \"Items\": [{\"Id\": \"my_map\", \"Width\": 2, \"Height\": 1, \"Legend\": {" + legend
                + "}, \"Ground\": [\"aa\"]}]}";
            var doc = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod, new[] { ("maps/my_map.json", text) });
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Water), "no symbol is left for water");
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Grass), "grass is already there, and no symbol is needed for it");
        }

        [ModTest("grid/collision_codes_are_rows")]
        static void Collision(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.CodeAt(2, 1) == '#' && doc.CodeAt(3, 1) == 'o' && doc.CodeAt(0, 0) == '.', "the rows as written");
            ModAssert.True(doc.CodeAt(-1, 0) == '#', "outside the map is blocked");
            ModAssert.True(doc.PaintCollision(new[] { (0, 0) }, '#'), "a block is painted");
            ModAssert.True(doc.CodeAt(0, 0) == '#', "the cell is blocked");
            ModAssert.False(doc.PaintCollision(new[] { (0, 0) }, '#'), "the same code is no change");
            ModAssert.False(doc.PaintCollision(new[] { (1, 0) }, 'x'), "an unknown code is refused");
            var text = RpgEditorFixture.TextOf(doc.Changes(), "maps/my_map.json");
            ModAssert.True(text != null && text.Contains("\"#...\""), "the row is written with the new code");
        }
    }
}
