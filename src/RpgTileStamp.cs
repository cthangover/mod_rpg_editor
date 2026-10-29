using System;
using Cthangover.Mods.Rpg;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>A rectangular palette selection in logical map ids; empty atlas slots are skipped when stamping.</summary>
    public class RpgTileStamp
    {
        private readonly int[] tiles;

        /// <summary>Selection width in cells.</summary>
        public int Width { get; }

        /// <summary>Selection height in cells.</summary>
        public int Height { get; }

        /// <summary>Copies a rectangular set of logical tile ids; negative ids denote gaps.</summary>
        public RpgTileStamp(int width, int height, int[] values)
        {
            if (width < 1 || height < 1 || values == null || values.Length != width * height)
                throw new ArgumentException("A stamp must contain exactly width times height cells.");
            Width = width;
            Height = height;
            tiles = (int[])values.Clone();
        }

        /// <summary>Logical id at the given offset, or -1 outside the selection.</summary>
        public int TileAt(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height ? tiles[y * Width + x] : -1;

        /// <summary>Single-tile brush, also used for surface materials and the pipette.</summary>
        public static RpgTileStamp Single(int index) => new(1, 1, new[] { index });

        /// <summary>Converts physical object-atlas coordinates into logical ids without exposing padding cells.</summary>
        public static RpgTileStamp FromAtlas(RpgTileset tileset, Rect2I rectangle)
        {
            var values = new int[rectangle.Size.X * rectangle.Size.Y];
            var packed = tileset.Tiles.Exists(t => t.AtlasIndex != null);
            for (var y = 0; y < rectangle.Size.Y; y++)
                for (var x = 0; x < rectangle.Size.X; x++)
                {
                    var physical = (rectangle.Position.Y + y) * tileset.Columns + rectangle.Position.X + x;
                    values[y * rectangle.Size.X + x] = packed
                        ? tileset.Tiles.Find(t => t.AtlasIndex == physical)?.Index ?? -1 : physical;
                }
            return new RpgTileStamp(rectangle.Size.X, rectangle.Size.Y, values);
        }
    }
}
