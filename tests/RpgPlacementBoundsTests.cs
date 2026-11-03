namespace Cthangover.Tools.RpgEditor.Tests
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Event bounds are persisted as a placement override, with one undo step for origin and extent.</summary>
    public class RpgPlacementBoundsTests
    {
        [ModTest("areas/bounds_save_roundtrip_and_atomic_undo")]
        static void Own(ModTestContext context)
        {
            var doc = RpgEditorFixture.OwnMap();
            ModAssert.True(doc.SetPlacementBounds("my_map/sign", 0, 0, 3, 2), "resize towards top-left");
            var saved = doc.Changes();
            var written = saved.First(c => c.Path.StartsWith("placements/", StringComparison.Ordinal));
            var roundtrip = RpgEditorFixture.OpenOwn("my_map", RpgEditorFixture.OwnMod,
                new[] { ("maps/my_map.json", RpgEditorFixture.OwnMapText), (written.Path, written.Text) });
            var value = roundtrip.Placements.First(p => p.Id == "my_map/sign").Data;
            ModAssert.Equal(0, value["X"].GetValue<int>(), "origin roundtrip");
            ModAssert.Equal(3, value["Footprint"]["Size"][0].GetValue<int>(), "width roundtrip");
            ModAssert.Equal(2, value["Footprint"]["Size"][1].GetValue<int>(), "height roundtrip");
            ModAssert.False(doc.SetPlacementBounds("my_map/sign", 0, 0, 3, 2), "no-op resize has no step");
            ModAssert.True(doc.Undo(), "undo resize");
            value = doc.Placements.First(p => p.Id == "my_map/sign").Data;
            ModAssert.Equal(1, value["X"].GetValue<int>(), "one undo restores origin");
            ModAssert.Null(value["Footprint"], "same undo restores inherited extent");
            ModAssert.False(doc.CanUndo, "one resize is one step");
            ModAssert.False(doc.SetPlacementBounds("my_map/sign", -1, 0, 2, 2), "negative origin rejected");
            ModAssert.False(doc.SetPlacementBounds("my_map/sign", 0, 0, 5, 2), "outside map rejected");
            ModAssert.False(doc.SetPlacementBounds("my_map/sign", 0, 0, 0, 2), "empty area rejected");
        }

        [ModTest("areas/foreign_bounds_are_an_own_patch")]
        static void Foreign(ModTestContext context)
        {
            var maps = RpgEditorFixture.EntitiesOf(new[] { ("maps/my_map.json", RpgEditorFixture.OwnMapText) }, "rpg_map", "dependency");
            var placements = RpgEditorFixture.EntitiesOf(new[] { ("placements/my_map/events.json", RpgEditorFixture.OwnPlacementsText) }, "rpg_placement", "dependency");
            var doc = RpgEditorFixture.Open(Array.Empty<(string Path, string Text)>(), maps, "my_map", placements);
            var before = placements.First(p => p.Id == "my_map/sign").Written.ToJsonString();
            ModAssert.True(doc.SetPlacementBounds("my_map/sign", 0, 1, 4, 2), "foreign event resized");
            var changes = doc.Changes();
            ModAssert.Equal(1, changes.Count, "only own patch file changes");
            ModAssert.True(changes[0].Path.StartsWith("patches/", StringComparison.Ordinal), "dependency file never written");
            ModAssert.Equal(before, placements.First(p => p.Id == "my_map/sign").Written.ToJsonString(), "owner source unchanged");
            ModAssert.True(doc.Undo(), "patch undo");
            ModAssert.Equal(0, doc.Changes().Count, "patch gone after one undo");
        }
    }
}
