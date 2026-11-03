namespace Cthangover.Tools.RpgEditor.Tests
{
    using Godot;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Grid corners keep their opposite edge and cannot invert or escape the map in either projection.</summary>
    public class RpgAreaResizeTests
    {
        [ModTest("areas/corners_clamp_and_keep_opposite_edges")]
        static void Corners(ModTestContext context)
        {
            var area = new Rect2I(3, 3, 2, 2);
            var map = new Vector2I(10, 8);
            ModAssert.True(new Rect2I(1, 2, 4, 3) == RpgAreaResize.Bounds(area, 0, new Vector2(-2, -1), map), "north-west expands");
            ModAssert.True(new Rect2I(3, 2, 4, 3) == RpgAreaResize.Bounds(area, 1, new Vector2(2, -1), map), "north-east expands");
            ModAssert.True(new Rect2I(3, 3, 7, 5) == RpgAreaResize.Bounds(area, 2, new Vector2(20, 20), map), "south-east clamps");
            ModAssert.True(new Rect2I(0, 3, 5, 3) == RpgAreaResize.Bounds(area, 3, new Vector2(-20, 1), map), "south-west clamps");
            ModAssert.True(new Rect2I(4, 4, 1, 1) == RpgAreaResize.Bounds(area, 0, new Vector2(20, 20), map), "shrinking keeps one cell");
        }
    }
}
