namespace Cthangover.Tools.RpgEditor.Tests
{
    using Cthangover.Core.Mods.Testing;
    using Godot;

    /// <summary>
    /// Where a dragged placement lands (plan 14.G9, 14.G18): the origin moves by the pointer's offset from the cell pressed, so
    /// the object keeps its grip; a press on an arrow passes the placement's own cell and the object follows the pointer.
    /// </summary>
    public class RpgDragOffsetTests
    {
        [ModTest("view/drag_keeps_the_grip")]
        static void Grip(ModTestContext ctx)
        {
            ModAssert.True(RpgDragOffset.Landing(new Vector2I(5, 5), new Vector2I(7, 5), new Vector2I(9, 6)) == new Vector2I(7, 6),
                "a press two cells right of the origin and a pointer one cell right and one down: the origin moves by (2, 1)");
            ModAssert.True(RpgDragOffset.Landing(new Vector2I(5, 5), new Vector2I(7, 5), new Vector2I(7, 5)) == new Vector2I(5, 5),
                "a pointer that stays on the pressed cell leaves the origin");
        }

        [ModTest("view/drag_from_the_origin_follows_the_pointer")]
        static void FollowsPointer(ModTestContext ctx)
        {
            ModAssert.True(RpgDragOffset.Landing(new Vector2I(4, 1), new Vector2I(4, 1), new Vector2I(9, 1)) == new Vector2I(9, 1),
                "pressed on the origin, the object lands under the pointer");
        }
    }
}
