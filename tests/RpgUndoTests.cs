namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Linq;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// Undo and redo over the map and its placements together: one step per change, a refused change leaves no step,
    /// and a new change after an undo drops the redo branch.
    /// </summary>
    public class RpgUndoTests
    {
        [ModTest("undo/paint_and_its_legend_step_back_together")]
        static void PaintAndLegend(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.False(doc.Undo(), "nothing to undo on a fresh document");
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (2, 2) }, RpgEditorFixture.Water), "paint");
            ModAssert.True(doc.Legend.ContainsKey("a"), "the new symbol");

            ModAssert.True(doc.Undo(), "undo the paint");
            ModAssert.Equal(RpgEditorFixture.Grass, doc.ObjectAt(RpgStrokeLayer.Ground, 2, 2), "the cell is grass again");
            ModAssert.False(doc.Legend.ContainsKey("a"), "the symbol went with the paint");
            ModAssert.False(doc.CanUndo, "the first change is undone");

            ModAssert.True(doc.Redo(), "redo the paint");
            ModAssert.Equal(RpgEditorFixture.Water, doc.ObjectAt(RpgStrokeLayer.Ground, 2, 2), "water is back");
            ModAssert.True(doc.Legend.ContainsKey("a"), "and its symbol");
        }

        [ModTest("undo/a_new_change_clears_the_redo_branch")]
        static void NewChangeClearsRedo(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (2, 2) }, RpgEditorFixture.Water), "paint");
            ModAssert.True(doc.Undo(), "undo");
            ModAssert.True(doc.CanRedo, "redo is possible");
            ModAssert.True(doc.MovePlacement("my_map/sign", 3, 0), "a new change");
            ModAssert.False(doc.CanRedo, "the redo branch is gone");
        }

        [ModTest("undo/map_and_placements_undo_in_order")]
        static void InOrder(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.MovePlacement("my_map/sign", 3, 2), "move the sign");
            ModAssert.True(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 2) }, RpgEditorFixture.Water), "paint the ground");
            ModAssert.True(doc.Undo(), "undo the paint");
            ModAssert.Equal(RpgEditorFixture.Grass, doc.ObjectAt(RpgStrokeLayer.Ground, 0, 2), "the paint is undone");
            ModAssert.Equal(3, doc.Placements.First(p => p.Id == "my_map/sign").Data["X"].GetValue<int>(), "the move stays");
            ModAssert.True(doc.Undo(), "undo the move");
            ModAssert.Equal(1, doc.Placements.First(p => p.Id == "my_map/sign").Data["X"].GetValue<int>(), "the move is undone");
        }

        [ModTest("undo/refused_changes_leave_no_step")]
        static void RefusedNoStep(ModTestContext ctx)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.False(doc.Paint(RpgStrokeLayer.Ground, new[] { (0, 0) }, RpgEditorFixture.Fence), "refused");
            ModAssert.False(doc.MovePlacement("my_map/sign", 1, 1), "no change");
            ModAssert.False(doc.CanUndo, "no step was recorded");
        }
    }
}
