namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using Cthangover.Core.Mods.Testing;
    using Godot;

    /// <summary>
    /// The hit test of the teleport arrows (plan 14.G18): a press near an arrow's head or line hits it, the head wins over the
    /// line, the nearest arrow wins, and the same rule works in cell space with its own tolerances.
    /// </summary>
    public class RpgArrowHitTests
    {
        [ModTest("view/arrow_head_and_shaft_hit")]
        static void HeadAndShaft(ModTestContext ctx)
        {
            var arrows = new List<(Vector2 From, Vector2 To)> { (new Vector2(0, 0), new Vector2(100, 0)) };
            var index = RpgArrowHit.Pick(arrows, new Vector2(100, 4), out var handle);
            ModAssert.Equal(0, index, "a press a few pixels from the head hits the arrow");
            ModAssert.True(handle == RpgArrowHandle.Head, "and the press is on its head");
            index = RpgArrowHit.Pick(arrows, new Vector2(50, -4), out handle);
            ModAssert.Equal(0, index, "a press a few pixels from the line hits the arrow");
            ModAssert.True(handle == RpgArrowHandle.Shaft, "and the press is on its shaft");
            index = RpgArrowHit.Pick(arrows, new Vector2(50, 20), out handle);
            ModAssert.Equal(-1, index, "a press away from the arrow misses");
            ModAssert.True(handle == RpgArrowHandle.None, "and hits nothing");
        }

        [ModTest("view/arrow_nearest_wins")]
        static void NearestWins(ModTestContext ctx)
        {
            var arrows = new List<(Vector2 From, Vector2 To)>
            {
                (new Vector2(0, 0), new Vector2(100, 0)),
                (new Vector2(0, 5), new Vector2(100, 5)),
            };
            var index = RpgArrowHit.Pick(arrows, new Vector2(50, 3), out var handle);
            ModAssert.Equal(1, index, "of two lines in reach the nearer one is picked");
            ModAssert.True(handle == RpgArrowHandle.Shaft, "on its shaft");
            index = RpgArrowHit.Pick(arrows, new Vector2(100, 0), out handle);
            ModAssert.Equal(0, index, "a head at zero distance wins over a line at two");
            ModAssert.True(handle == RpgArrowHandle.Head, "on the head");
        }

        [ModTest("view/arrow_hit_in_cell_space")]
        static void CellSpace(ModTestContext ctx)
        {
            // Cell space: cell centres are at x + 0.5, so the tolerances are fractions of a cell.
            var arrows = new List<(Vector2 From, Vector2 To)> { (new Vector2(0.5f, 0.5f), new Vector2(3.5f, 0.5f)) };
            var index = RpgArrowHit.Pick(arrows, new Vector2(3.5f, 0.6f), 0.25f, 0.2f, out var handle);
            ModAssert.Equal(0, index, "a point 0.1 cells from the head hits it with a 0.25 tolerance");
            ModAssert.True(handle == RpgArrowHandle.Head, "as the head");
            index = RpgArrowHit.Pick(arrows, new Vector2(2f, 0.9f), 0.25f, 0.2f, out handle);
            ModAssert.Equal(-1, index, "a point 0.4 cells below the line misses at 0.2 tolerance");
            ModAssert.True(handle == RpgArrowHandle.None, "and hits nothing");
            index = RpgArrowHit.Pick(arrows, new Vector2(2f, 0.6f), 0.25f, 0.2f, out handle);
            ModAssert.Equal(0, index, "a point 0.1 cells from the line hits it");
            ModAssert.True(handle == RpgArrowHandle.Shaft, "as the shaft");
        }

        [ModTest("view/distance_to_segment_clamps_to_its_ends")]
        static void Distance(ModTestContext ctx)
        {
            ModAssert.True(Mathf.Abs(RpgArrowHit.DistanceToSegment(new Vector2(-3, 4), new Vector2(0, 0), new Vector2(10, 0)) - 5f) < 0.001f,
                "beyond the start the distance is to the start point");
            ModAssert.True(Mathf.Abs(RpgArrowHit.DistanceToSegment(new Vector2(5, 3), new Vector2(0, 0), new Vector2(10, 0)) - 3f) < 0.001f,
                "beside the middle it is the perpendicular distance");
            ModAssert.True(Mathf.Abs(RpgArrowHit.DistanceToSegment(new Vector2(1, 1), new Vector2(2, 2), new Vector2(2, 2)) - Mathf.Sqrt(2f)) < 0.001f,
                "a zero-length arrow is a point");
        }
    }
}
