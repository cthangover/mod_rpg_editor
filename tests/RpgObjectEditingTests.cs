namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Object replacement and fragment erasure preserve neighbours, layer assignments and undo boundaries.</summary>
    public class RpgObjectEditingTests
    {
        private static RpgTileset Tileset() => new()
        {
            Columns = 3,
            Tiles = new List<RpgTileInfo>
            {
                new() { Index = 10, AtlasIndex = 0, Layer = RpgLayer.Front },
                new() { Index = 11, AtlasIndex = 1, Layer = RpgLayer.Front },
                new() { Index = 12, AtlasIndex = 3, Layer = RpgLayer.Main },
                new() { Index = 13, AtlasIndex = 4, Layer = RpgLayer.Main }
            },
            VisualGroups = new List<RpgVisualGroup>
            {
                new() { Id = "house", Width = 2, Height = 2, Pattern = new[] { 10, 11, 12, 13 }, AnchorX = 1, AnchorY = 2 }
            }
        };

        [ModTest("objects/replace_removes_old_parts_on_all_layers_and_undo_restores_them")]
        static void ReplaceWholeObject(ModTestContext ctx)
        {
            var tileset = Tileset();
            var document = RpgMapDocument.Create("test", "test", 8, 6, 0);
            var stamp = RpgTileStamp.FromAtlas(tileset, new Rect2I(0, 0, 2, 2));
            document.Stamp(RpgEditLayer.Main, 1, 1, stamp, tileset);
            document.Stamp(RpgEditLayer.Main, 5, 1, stamp, tileset);
            document.BeginChange();
            document.Stamp(RpgEditLayer.Front, 2, 1, RpgTileStamp.Single(99), tileset);
            ModAssert.Equal(-1, document.Grid.GetTile(RpgLayer.Front, 1, 1), "old roof is removed outside the new footprint");
            ModAssert.Equal(-1, document.Grid.GetTile(RpgLayer.Main, 2, 2), "old foundation is removed from another layer");
            ModAssert.Equal(99, document.Grid.GetTile(RpgLayer.Front, 2, 1), "new object remains");
            ModAssert.Equal(10, document.Grid.GetTile(RpgLayer.Front, 5, 1), "neighbouring copy is unchanged");
            ModAssert.True(document.Undo(), "replacement has one undo step");
            ModAssert.Equal(13, document.Grid.GetTile(RpgLayer.Main, 2, 2), "undo restores foundation");
            ModAssert.True(document.Redo(), "replacement can be redone");
            var restored = RpgMapDocument.Load(document.ToJson());
            ModAssert.Equal(-1, restored.Grid.GetTile(RpgLayer.Main, 2, 2), "no tail returns after save/load");
        }

        [ModTest("objects/erase_a_legacy_fragment_without_erasing_a_new_object")]
        static void EraseFragment(ModTestContext ctx)
        {
            var tileset = Tileset();
            var document = RpgMapDocument.Create("test", "test", 8, 6, 0);
            document.Stamp(RpgEditLayer.Main, 1, 1, RpgTileStamp.FromAtlas(tileset, new Rect2I(0, 0, 2, 2)));
            document.Paint(RpgEditLayer.Front, 2, 1, 99, RpgCollision.Auto);
            ModAssert.Equal(3, document.EraseObject(RpgEditLayer.Main, 1, 2, tileset), "all surviving old pieces are erased");
            ModAssert.Equal(99, document.Grid.GetTile(RpgLayer.Front, 2, 1), "the replacement is not part of the old pattern");
            ModAssert.Equal(0, document.EraseObject(RpgEditLayer.Main, 1, 2, tileset), "already empty fragment does not change data");
        }

        [ModTest("objects/preferred_layers_roundtrip_and_borrowed_tiles_keep_them")]
        static void PreferredLayers(ModTestContext ctx)
        {
            var original = Tileset();
            var restored = JsonSerializer.Deserialize<RpgTileset>(JsonSerializer.Serialize(original));
            ModAssert.True(restored.TileOf(10).Layer == RpgLayer.Front, "front layer survives JSON");
            ModAssert.True(restored.TileOf(12).Layer == RpgLayer.Main, "main layer survives JSON");
            var combined = RpgTilesetComposer.Compose(new RpgTileset { TileSize = original.TileSize }, new[] { restored });
            ModAssert.True(combined.TileOf(RpgTilesetComposer.SlotSize + 10).Layer == RpgLayer.Front, "borrowed object keeps its layer");
            var stamp = RpgTileStamp.FromAtlas(restored, new Rect2I(0, 0, 2, 2));
            ModAssert.True(stamp.LayerAt(0, 0, RpgLayer.Back) == RpgLayer.Front, "roof uses its assigned layer");
            ModAssert.True(stamp.LayerAt(0, 1, RpgLayer.Back) == RpgLayer.Main, "foundation uses another layer");
        }

        [ModTest("events/locked_events_can_be_inspected_without_becoming_writable")]
        static void InspectForeignEvents(ModTestContext ctx)
        {
            var document = RpgMapDocument.Create("test", "test", 8, 6, 0);
            var barrel = new RpgMapEvent { Id = "barrel", X = 1, Y = 1, Sprite = "closed", Pages = new List<RpgEventPage>
            {
                new() { Condition = "opened == 1", Sprite = "open", Commands = new List<string>(), Route = new RpgMoveRoute { Steps = new List<string> { "wait" } } }
            }};
            document.SetLockedEvents(new[] { barrel });
            ModAssert.Equal(0, document.EventsAt(1, 1).Count, "read-only event is not editable");
            ModAssert.True(ReferenceEquals(barrel, document.EventsAt(1, 1, true).Single()), "inspector can select the source event");
            ModAssert.False(document.RemoveEvent(barrel), "inspection does not allow deleting a foreign event");
            var editable = RpgMapDocument.Load(document.ToJson(), "{\"Events\":[" + JsonSerializer.Serialize(barrel) + "]}");
            var roundtrip = RpgMapDocument.Load(editable.ToJson(), editable.EventsToJson());
            ModAssert.Equal("open", roundtrip.Events.Single().Pages.Single().Sprite, "open state survives editing and saving");
            ModAssert.Equal("wait", roundtrip.Events.Single().Pages.Single().Route.Steps.Single(), "unexposed route data survives");
        }
    }
}
