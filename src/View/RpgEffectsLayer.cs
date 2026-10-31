namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>
    /// The marks of the effect masks of a map (plan 14.G10): every cell an <c>Effects</c> entry covers is tinted, and an
    /// entry without <c>Cells</c> tints the whole map. The effect itself is drawn by the game's renderer, which the canvas
    /// already shares with the game (<c>TileCanvasEffects</c>); this layer only shows where the masks are.
    /// </summary>
    public static class RpgEffectsLayer
    {
        /// <summary>Tint of a masked cell.</summary>
        public static readonly Color MaskFill = new(0.72f, 0.45f, 1f, 0.28f);

        /// <summary>Adds one mark per masked cell of <paramref name="map"/> (one for the whole map when an entry has no cells).</summary>
        public static void AddMarks(RpgMap map, ICollection<CanvasRect> marks)
        {
            if (map?.Effects == null)
                return;
            foreach (var mask in map.Effects)
            {
                if (mask == null)
                    continue;
                if (mask.Cells == null)
                {
                    marks.Add(new CanvasRect(0, 0, map.Width, map.Height, MaskFill, true));
                    continue;
                }
                foreach (var cell in mask.Cells)
                {
                    if (cell is { Length: >= 2 })
                        marks.Add(new CanvasRect(cell[0], cell[1], 1, 1, MaskFill, true));
                }
            }
        }
    }
}
