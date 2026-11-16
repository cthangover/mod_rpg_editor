using System;
using System.Collections.Generic;
using System.Text.Json;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.DepthProbe
{
    /// <summary>Perimeters follow effective solid cells and authoritative collision without authoring side effects.</summary>
    public sealed class RpgGeometryProbeTests
    {
        [ModTest("geometry_probe/sparse_desk_tree_and_wall_rings")]
        static void Rings(ModTestContext ctx)
        {
            var fixture = new RpgGeometryFixture(ctx);
            var scene = fixture.Scene();
            var before = fixture.Document.Data.ToJsonString();
            var cases = new[]
            {
                ("geometry_fixture/desk", 10, new Rect2I(4, 5, 2, 1)),
                ("geometry_fixture/tree", 8, new Rect2I(9, 3, 1, 1)),
                ("geometry_fixture/wall", 14, new Rect2I(3, 1, 4, 1)),
            };
            foreach (var (id, count, bounds) in cases)
            {
                var route = RpgGeometryProbe.Around(scene, id);
                var repeated = RpgGeometryProbe.Around(scene, id);
                ModAssert.True(route.Bounds == bounds, "probe follows occupied bounds: " + id);
                ModAssert.True(route.UsesSolidCells, "solid mask is used: " + id);
                ModAssert.Equal(count, route.Cells.Count, "one-cell perimeter length: " + id);
                ModAssert.Equal(0, route.BlockedCells.Count, "clear fixture perimeter: " + id);
                ModAssert.True(route.Closed, "all sides form a traversable loop: " + id);
                var distinct = new HashSet<Vector2I>();
                for (var index = 0; index < route.Cells.Count; index++)
                {
                    var cell = route.Cells[index];
                    var next = route.Cells[(index + 1) % route.Cells.Count];
                    ModAssert.True(distinct.Add(cell), "corners are included once");
                    ModAssert.Equal(1, Math.Abs(next.X - cell.X) + Math.Abs(next.Y - cell.Y), "steps stay four-neighbour");
                    ModAssert.True(route.CanEnter(index) && scene.Walk.IsWalkable(cell.X, cell.Y), "every recorded step is valid");
                    ModAssert.True(cell == repeated.Cells[index], "repeated runs have the same start and direction");
                }

                ModAssert.False(route.CanEnter(-1) || route.CanEnter(route.Cells.Count), "invalid step indices are unavailable");
            }

            var desk = RpgGeometryProbe.Around(scene, "geometry_fixture/desk");
            ModAssert.True(desk.Footprint.Size == new Vector2I(8, 8), "full-room artwork retains its declared footprint");
            ModAssert.True(desk.Bounds.Size == new Vector2I(2, 1), "the desk probe does not circle the entire picture");
            ModAssert.Equal(before, fixture.Document.Data.ToJsonString(), "sampling never changes map JSON");
            ModAssert.False(fixture.Document.CanUndo, "sampling adds no undo entries");
            ModAssert.Equal(0, fixture.Document.Changes().Count, "sampling produces no saved changes");
            var original = fixture.ObjectOf("geometry_fixture/desk");
            var json = JsonSerializer.Serialize(original, JsonOptionsProvider.ReadOptions);
            var restored = JsonSerializer.Deserialize<RpgObject>(json, JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(json, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "all source footprint/parts/sort data survive a complete JSON round trip");
            ModAssert.Equal(8, restored.Footprint.Width, "declared width survives JSON");
            ModAssert.Equal("...##...", restored.Footprint.Solid[4], "sparse solid rows survive JSON");
            ModAssert.Equal(-3, restored.Parts[0].SortOffset[1], 0, "depth offset survives JSON");
        }

        [ModTest("geometry_probe/blocked_hidden_and_missing_results")]
        static void Blocked(ModTestContext ctx)
        {
            var fixture = new RpgGeometryFixture(ctx);
            var clear = RpgGeometryProbe.Around(fixture.Scene(), "geometry_fixture/tree");
            var blockedCell = clear.Cells[1];
            var cells = new[] { (blockedCell.X, blockedCell.Y) };
            ModAssert.True(fixture.Document.PaintCollision(cells, '#'), "test setup blocks a perimeter cell");
            var blocked = RpgGeometryProbe.Around(fixture.Scene(), "geometry_fixture/tree");
            ModAssert.False(blocked.Closed, "one blocked cell prevents claiming a complete loop");
            ModAssert.Equal(1, blocked.BlockedCells.Count, "blocked cell remains explicit");
            ModAssert.True(blocked.BlockedCells[0] == blockedCell, "the blocked coordinate is retained");
            ModAssert.False(blocked.CanEnter(1), "the blocked step cannot be entered or skipped");
            ModAssert.True(fixture.Document.PaintCollision(cells, 'o'), "test setup forces the same cell open");
            ModAssert.True(RpgGeometryProbe.Around(fixture.Scene(), "geometry_fixture/tree").Closed,
                "the probe follows effective grid overrides");
            RpgEditorPreview.ForDocument(fixture.Document).Set("geometry_fixture/tree", "hidden", true);
            ModAssert.Equal(0, RpgGeometryProbe.Around(fixture.Scene(), "geometry_fixture/tree").Cells.Count,
                "hidden state removes the diagnostic ring");
            ModAssert.Equal(0, RpgGeometryProbe.Around(fixture.Scene(), "absent").Cells.Count, "unknown selection is empty");
            ModAssert.Equal(0, RpgGeometryProbe.Around(null, null).Cells.Count, "an empty window has no probe");
        }
    }
}
