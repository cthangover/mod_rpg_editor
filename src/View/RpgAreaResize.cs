namespace Cthangover.Tools.RpgEditor
{
    using Godot;

    /// <summary>Resizes a grid rectangle from one corner, keeping its opposite edges fixed and clamping to the map.</summary>
    public static class RpgAreaResize
    {
        /// <summary>Corners run clockwise from the grid's top-left; delta is in cells, independent of the view projection.</summary>
        public static Rect2I Bounds(Rect2I initial, int corner, Vector2 delta, Vector2I mapSize)
        {
            var left = initial.Position.X;
            var top = initial.Position.Y;
            var right = initial.End.X;
            var bottom = initial.End.Y;
            var dx = Mathf.RoundToInt(delta.X);
            var dy = Mathf.RoundToInt(delta.Y);
            if (corner is 0 or 3) left = Mathf.Clamp(left + dx, 0, right - 1);
            else right = Mathf.Clamp(right + dx, left + 1, mapSize.X);
            if (corner is 0 or 1) top = Mathf.Clamp(top + dy, 0, bottom - 1);
            else bottom = Mathf.Clamp(bottom + dy, top + 1, mapSize.Y);
            return new Rect2I(left, top, right - left, bottom - top);
        }
    }
}
