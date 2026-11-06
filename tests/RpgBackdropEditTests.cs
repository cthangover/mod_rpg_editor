namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Subtile pixel offsets change one instance and one projection, preserving its image and all other parts.</summary>
    public class RpgBackdropEditTests
    {
        [ModTest("backdrop/one_native_pixel_round_trip_and_undo")]
        static void PixelShift(ModTestContext ctx)
        {
            var obj = Backdrop();
            ModAssert.True(new Vector2(-1920, -320) == RpgBackdropEdit.Position(obj, "isometric", 128), "native pixels before zoom");
            var parts = RpgBackdropEdit.PartsAt(obj, "isometric", 128, -1919, -327);
            ModAssert.NotNull(parts, "one-pixel X and negative seven-pixel Y change");
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.SetPlacementField("my_map/well", "Parts", parts), "placement override");
            var savedFiles = doc.Changes().Select(f => (f.Path, f.Text))
                .Append(("maps/my_map.json", RpgEditorFixture.OwnMapText));
            var reopened = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod, savedFiles);
            var placement = RpgEditorScene.PlacementOf(reopened.AnyPlacement("my_map/well").Data);
            var effective = RpgObjectResolve.Effective(obj, null, placement);
            ModAssert.True(new Vector2(-1919, -327) == RpgBackdropEdit.Position(effective, "isometric", 128), "pixel offsets survive save/load");
            ModAssert.True(new Vector2(0, 0) == RpgBackdropEdit.Position(effective, "topdown", 128), "alternate view untouched");
            ModAssert.True(new Vector2(-1920, -320) == RpgBackdropEdit.Position(obj, "isometric", 128), "shared object unchanged");
            ModAssert.Equal(16, effective.Width, "collision footprint unchanged");
            ModAssert.Equal("test/background", effective.Parts[0].Image, "same picture");
            ModAssert.Equal(20f, effective.Parts[0].Size[1], what: "same picture scale");
            doc.Undo();
            ModAssert.Equal(0, doc.Changes().Count, "one undo restores placement file bytes");
        }

        [ModTest("backdrop/noop_unsupported_and_multiple_rectangles")]
        static void Parts(ModTestContext ctx)
        {
            var obj = Backdrop();
            ModAssert.Null(RpgBackdropEdit.PartsAt(obj, "isometric", 128, -1920, -320), "no-op");
            ModAssert.Null(RpgBackdropEdit.PartsAt(obj, "isometric", 128, double.NaN, 0), "invalid offset");
            obj.Parts.Add(new RpgObjectPart { Image = "test/overlay", At = new[] { -14f, -2f }, Only = "isometric", DrawOrder = TileDrawOrder.BelowCharacters });
            var parts = RpgBackdropEdit.PartsAt(obj, "isometric", 128, -1919, -319);
            var roundTrip = JsonSerializer.Deserialize<List<RpgObjectPart>>(parts.ToJsonString(), JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(-14f + 1f / 128, roundTrip[2].At[0], what: "same pixel delta on another background rectangle");
            obj.Parts[0].DrawOrder = TileDrawOrder.ByPosition;
            obj.Parts.RemoveAt(2);
            ModAssert.Null(RpgBackdropEdit.Part(obj, "isometric"), "ordinary foreground sprite is not a background");
        }

        private static RpgObject Backdrop() => new()
        {
            ID = "test/backdrop", Form = RpgObjectForm.Sprite, Solid = false,
            Footprint = new RpgFootprint { Size = new[] { 16, 16 } },
            Parts = new List<RpgObjectPart>
            {
                new() { Image = "test/background", Geometry = RpgPartGeometry.Upright, At = new[] { -15f, -2.5f }, Size = new[] { 30f, 20f }, Only = "isometric", DrawOrder = TileDrawOrder.BelowCharacters },
                new() { Image = "test/background", Geometry = RpgPartGeometry.Ground, At = new[] { 0f, 0f }, Size = new[] { 16f, 16f }, Only = "topdown", DrawOrder = TileDrawOrder.BelowCharacters },
            }
        };
    }
}
