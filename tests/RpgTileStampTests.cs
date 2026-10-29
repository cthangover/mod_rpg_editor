using System.Collections.Generic;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Rpg;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests
{
    /// <summary>Palette rectangles resolve physical cells back to stable logical map ids.</summary>
    public class RpgTileStampTests
    {
        [ModTest("stamp/atlas_rectangle_resolves_packed_ids_and_padding")]
        static void PackedRectangle(ModTestContext ctx)
        {
            var tileset = new RpgTileset { Columns = 3, Tiles = new List<RpgTileInfo>
            {
                new() { Index = 64, AtlasIndex = 1 }, new() { Index = 65, AtlasIndex = 2 },
                new() { Index = 67, AtlasIndex = 4 }
            }};
            var stamp = RpgTileStamp.FromAtlas(tileset, new Rect2I(1, 0, 2, 2));
            ModAssert.Equal(2, stamp.Width, "rectangle width");
            ModAssert.Equal(2, stamp.Height, "rectangle height");
            ModAssert.Equal(64, stamp.TileAt(0, 0), "top left logical id");
            ModAssert.Equal(65, stamp.TileAt(1, 0), "top right logical id");
            ModAssert.Equal(67, stamp.TileAt(0, 1), "bottom left logical id");
            ModAssert.Equal(-1, stamp.TileAt(1, 1), "an empty physical slot stays a gap");
            ModAssert.Equal(-1, stamp.TileAt(-1, 0), "outside selection is empty");
            ModAssert.Equal(40, RpgTileStamp.Single(40).TileAt(0, 0), "surface brushes also retain logical ids");
            var values = new[] { 11 };
            var copied = new RpgTileStamp(1, 1, values);
            values[0] = 99;
            ModAssert.Equal(11, copied.TileAt(0, 0), "the brush owns its tile snapshot");
        }
    }
}
