namespace Cthangover.Tools.RpgEditor
{
    using System;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Removes a placed object by its source pattern, including surviving fragments and parts on other layers.</summary>
    public static class RpgObjectEditing
    {
        /// <summary>Clears only matching cells of the object under this cell; ordinary tiles clear individually.</summary>
        public static int Erase(RpgMapGrid grid, RpgTileset tileset, RpgLayer layer, int x, int y)
        {
            var tile = grid.GetTile(layer, x, y);
            if (tile < 0)
                return 0;
            RpgVisualGroup best = null;
            var origin = Vector2I.Zero;
            var score = 0;
            foreach (var group in tileset?.VisualGroups ?? new())
            {
                if (RpgVisualGroupValidator.Error(group) != null)
                    continue;
                for (var i = 0; i < group.Pattern.Length; i++)
                {
                    if (group.Pattern[i] != tile)
                        continue;
                    var candidate = new Vector2I(x - i % group.Width, y - i / group.Width);
                    var matches = Count(grid, group, candidate);
                    if (matches > score)
                    {
                        best = group;
                        origin = candidate;
                        score = matches;
                    }
                }
            }
            if (best == null)
            {
                grid.SetTile(layer, x, y, -1);
                return 1;
            }
            var changed = 0;
            for (var i = 0; i < best.Pattern.Length; i++)
                for (var l = 0; l < 3; l++)
                {
                    var cx = origin.X + i % best.Width;
                    var cy = origin.Y + i / best.Width;
                    if (grid.InBounds(cx, cy) && grid.GetTile((RpgLayer)l, cx, cy) == best.Pattern[i])
                    {
                        grid.SetTile((RpgLayer)l, cx, cy, -1);
                        changed++;
                    }
                }
            return changed;
        }

        private static int Count(RpgMapGrid grid, RpgVisualGroup group, Vector2I origin)
        {
            var result = 0;
            for (var i = 0; i < group.Pattern.Length; i++)
                for (var l = 0; l < 3; l++)
                {
                    var x = origin.X + i % group.Width;
                    var y = origin.Y + i / group.Width;
                    if (grid.InBounds(x, y) && grid.GetTile((RpgLayer)l, x, y) == group.Pattern[i])
                        result++;
                }
            return result;
        }
    }
}
