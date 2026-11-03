namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Mods.Testing;

    /// <summary>
    /// The pick order of a cell covered by several placements (plan 14.G18): a marker on its origin first, then the other markers,
    /// then the origins, then the rest; a repeat press takes the next one and wraps around.
    /// </summary>
    public class RpgPickCycleTests
    {
        [ModTest("view/pick_order_markers_then_origins")]
        static void Order(ModTestContext ctx)
        {
            var ordered = RpgPickCycle.Order(new[]
            {
                ("building", false, true),
                ("chest", false, false),
                ("marker_far", true, false),
                ("trigger", true, true),
            });
            ModAssert.Equal("trigger", ordered[0], "a marker on the cell's origin comes first, over a building on the same cell");
            ModAssert.Equal("marker_far", ordered[1], "then the other markers");
            ModAssert.Equal("building", ordered[2], "then the placements whose origin is the cell");
            ModAssert.Equal("chest", ordered[3], "and the footprints that only cover it last");
        }

        [ModTest("view/pick_cycle_wraps")]
        static void Cycle(ModTestContext ctx)
        {
            var ordered = new List<string> { "a", "b", "c" };
            ModAssert.Equal("a", RpgPickCycle.Next(ordered, null), "no selection: the first one");
            ModAssert.Equal("a", RpgPickCycle.Next(ordered, "zzz"), "a selection outside the cell: the first one");
            ModAssert.Equal("b", RpgPickCycle.Next(ordered, "a"), "a repeat press takes the next one");
            ModAssert.Equal("a", RpgPickCycle.Next(ordered, "c"), "and wraps to the first after the last");
            ModAssert.Null(RpgPickCycle.Next(new List<string>(), "a"), "an empty cell picks nothing");
        }

        [ModTest("view/pick_order_is_stable")]
        static void Stable(ModTestContext ctx)
        {
            var ordered = RpgPickCycle.Order(new[] { ("x", false, false), ("y", false, false), ("z", false, false) });
            ModAssert.True(string.Join(",", ordered) == "x,y,z", "equal ranks keep the order they came in");
            ModAssert.True(ordered.Count == 3 && ordered.Distinct().Count() == 3, "nothing is lost or repeated");
        }
    }
}
