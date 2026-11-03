namespace Cthangover.Tools.RpgEditor
{
    using Godot;

    /// <summary>
    /// Where a dragged placement lands (plan 14.G9, 14.G18). The placement's origin moves by the pointer's offset from the cell
    /// pressed, so an object held by any of its cells does not jump under the pointer. A press on a teleport arrow passes the
    /// placement's own cell as the pressed cell: the placement then follows the pointer's cell.
    /// </summary>
    public static class RpgDragOffset
    {
        /// <summary>The origin after the drag: <paramref name="origin"/> moved by <paramref name="pointer"/> minus <paramref name="pressed"/>.</summary>
        public static Vector2I Landing(Vector2I origin, Vector2I pressed, Vector2I pointer)
            => new(origin.X + pointer.X - pressed.X, origin.Y + pointer.Y - pressed.Y);
    }
}
