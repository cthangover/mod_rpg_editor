using System;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>Fills authored cardinal components atomically, preserving unknown data, foreign owners and one-step undo.</summary>
    public sealed class RpgPaintRegionTests
    {
        private const string Water = "village_pack/ground/water";

        [ModTest("paint_regions/own_fill_single_undo_roundtrip_and_noop")]
        static void Own(ModTestContext ctx)
        {
            var fixture = new RpgPaintFixture(ctx);
            var document = fixture.Document;
            var before = document.Data.ToJsonString();
            var events = document.Data["Events"].ToJsonString();
            var extra = document.Data["AuthorData"].ToJsonString();
            var region = RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, new Vector2I(1, 1));
            ModAssert.True(region.Complete, "working-size region is complete");
            ModAssert.Equal(24, region.Cells.Count, "equal ground is limited to the left half");
            ModAssert.False(document.CanUndo, "region discovery is read-only");
            ModAssert.Equal(before, document.Data.ToJsonString(), "discovery preserves the exact document JSON");
            ModAssert.True(document.Paint(RpgStrokeLayer.Ground, RpgPaintFixture.Coordinates(region), Water),
                "one public batch applies the complete region");
            for (var y = 0; y < document.Height; y++)
            {
                for (var x = 0; x < document.Width; x++)
                {
                    ModAssert.Equal(x < 4 ? Water : "village_pack/ground/town_paving",
                        document.ObjectAt(RpgStrokeLayer.Ground, x, y), "fill touches exactly its connected component");
                }
            }

            ModAssert.Equal(events, document.Data["Events"].ToJsonString(), "event identities and commands are unchanged");
            ModAssert.Equal(extra, document.Data["AuthorData"].ToJsonString(), "unknown author data is unchanged");
            var changed = document.Data.ToJsonString();
            var dto = JsonSerializer.Deserialize<RpgMap>(changed, JsonOptionsProvider.ReadOptions);
            var serialized = JsonSerializer.Serialize(dto, JsonOptionsProvider.ReadOptions);
            var restored = JsonSerializer.Deserialize<RpgMap>(serialized, JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(serialized, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "all map, event, legend, stroke and extension fields survive a full JSON round trip");
            ModAssert.True(JsonNode.DeepEquals(JsonNode.Parse(extra),
                JsonNode.Parse(restored.ExtraFields["AuthorData"].GetRawText())), "extension values survive DTO loading");
            ModAssert.True(document.Undo(), "one undo retires the entire fill");
            ModAssert.Equal(before, document.Data.ToJsonString(), "one undo restores exact source data");
            ModAssert.False(document.CanUndo, "the gesture did not add per-cell undo entries");
            ModAssert.Equal(0, document.Changes().Count, "undo leaves no saved changes");
            ModAssert.True(document.Redo(), "the complete fill can be redone");
            ModAssert.Equal(changed, document.Data.ToJsonString(), "redo restores exactly the committed fill");
            ModAssert.False(document.Paint(RpgStrokeLayer.Ground, RpgPaintFixture.Coordinates(region), Water),
                "same-value fill is an explicit no-op");
            ModAssert.True(document.Undo(), "no-op did not consume an undo slot");
            ModAssert.Equal(before, document.Data.ToJsonString(), "undo after no-op still returns to the original map");
        }

        [ModTest("paint_regions/foreign_fill_is_one_minimal_patch_and_preserves_owner")]
        static void Foreign(ModTestContext ctx)
        {
            var fixture = new RpgPaintFixture(ctx, foreign: true);
            var document = fixture.Document;
            var before = document.Data.ToJsonString();
            var written = document.Map.Written.ToJsonString();
            var region = RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, Vector2I.Zero);
            ModAssert.Equal(24, region.Cells.Count, "foreign resolved region has the same authored topology");
            ModAssert.True(document.Paint(RpgStrokeLayer.Ground, RpgPaintFixture.Coordinates(region), Water),
                "the foreign gesture uses the same public batch");
            var changes = document.Changes();
            ModAssert.Equal(1, changes.Count, "only one owned patch is produced");
            ModAssert.True(changes[0].Path.StartsWith("patches/rpg_map/", StringComparison.Ordinal),
                "the source map file is not a save target");
            var entry = JsonNode.Parse(changes[0].Text)["Items"][0];
            var strokes = (entry["Strokes$add"] ?? entry["Strokes"]) as JsonArray;
            ModAssert.NotNull(strokes, "foreign painting retains the established stroke patch format");
            ModAssert.Equal(1, strokes.Count, "the complete gesture is a single stroke");
            ModAssert.Equal(Water, strokes[0]["Object"].ToString(), "stroke names the requested object");
            ModAssert.Equal(24, ((JsonArray)strokes[0]["Cells"]).Count, "stroke retains every selected coordinate");
            ModAssert.Null(entry["Ground"], "foreign rows are not replaced");
            ModAssert.Null(entry["Events"], "unrelated behavior is not copied into the patch");
            ModAssert.Null(entry["AuthorData"], "unknown owner metadata is not copied into the patch");
            ModAssert.Equal(written, document.Map.Written.ToJsonString(), "the foreign owner's written map is byte-stable");
            ModAssert.True(document.Undo(), "one undo removes the owned fill patch");
            ModAssert.Equal(before, document.Data.ToJsonString(), "foreign effective data returns exactly to its prior state");
            ModAssert.Equal(0, document.Changes().Count, "undo leaves no empty patch file");
            ModAssert.False(document.CanUndo, "foreign fill also has one history transaction");
        }

        [ModTest("paint_regions/cardinal_topology_strokes_and_collision_values")]
        static void Topology(ModTestContext ctx)
        {
            var source = new RpgPaintFixture(ctx).Document.Data.DeepClone() as JsonObject;
            source["Width"] = 3;
            source["Height"] = 3;
            source["Ground"] = new JsonArray("gtt", "tgt", "ttg");
            source["Structure"] = new JsonArray("...", "...", "...");
            source["Collision"] = new JsonArray("...", "...", "...");
            var diagonal = new RpgPaintFixture(ctx, source).Document;
            ModAssert.Equal(1, RpgPaintRegion.Fill(diagonal, RpgStrokeLayer.Ground, Vector2I.Zero).Cells.Count,
                "diagonal equal cells are separate components");
            ModAssert.Equal(1, RpgPaintRegion.Fill(diagonal, RpgStrokeLayer.Ground, Vector2I.One).Cells.Count,
                "the central diagonal cell also remains separate");
            source["Legend"]["u"] = "VILLAGE_PACK/GROUND/GRASS";
            source["Ground"] = new JsonArray("guu", "ttt", "ttt");
            source["Events"][0]["Solid"] = true;
            var mixedCase = new RpgPaintFixture(ctx, source).Document;
            ModAssert.Equal(3, RpgPaintRegion.Fill(mixedCase, RpgStrokeLayer.Ground, Vector2I.Zero).Cells.Count,
                "entity id case and a solid event do not split an authored ground component");
            source = new RpgPaintFixture(ctx).Document.Data.DeepClone() as JsonObject;
            source["Strokes"] = JsonNode.Parse(@"[
                {""Id"":""surface_column"",""Layer"":""Ground"",""Object"":""village_pack/ground/town_paving"",""Rects"":[[0,0,1,6]]},
                {""Id"":""surface_gap"",""Layer"":""Ground"",""Object"":null,""Cells"":[[0,3]]},
                {""Id"":""collision_column"",""Layer"":""Collision"",""Code"":""#"",""Rects"":[[2,0,1,6]]},
                {""Id"":""intentional_open"",""Layer"":""Collision"",""Code"":""o"",""Cells"":[[2,3]]}
            ]");
            var document = new RpgPaintFixture(ctx, source).Document;
            var top = RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, Vector2I.Zero);
            ModAssert.Equal(3, top.Cells.Count, "ordered clear stroke divides the painted column");
            var bottom = RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, new Vector2I(0, 4));
            ModAssert.Equal(2, bottom.Cells.Count, "later stroke is respected on the other side of the gap");
            ModAssert.Equal(1, RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, new Vector2I(0, 3)).Cells.Count,
                "explicit clearing is an inspectable empty component");
            var collision = RpgPaintRegion.Fill(document, RpgStrokeLayer.Collision, new Vector2I(1, 1));
            ModAssert.Equal(12, collision.Cells.Count, "equal-code fill does not cross # or intentional o");
            var before = document.Data.ToJsonString();
            ModAssert.True(document.PaintCollision(RpgPaintFixture.Coordinates(collision), '#'),
                "one batch changes the selected code region");
            ModAssert.Equal('o', document.CodeAt(2, 3), "the intentional open override is untouched");
            ModAssert.Equal('.', document.CodeAt(7, 3), "the disconnected other side is untouched");
            ModAssert.True(document.Undo(), "collision fill has one undo");
            ModAssert.Equal(before, document.Data.ToJsonString(), "collision undo preserves all ordered stroke data");
            var emptyStructure = RpgPaintRegion.Fill(document, RpgStrokeLayer.Structure, Vector2I.Zero);
            ModAssert.Equal(48, emptyStructure.Cells.Count, "structure fill follows its own layer, independently of ground and collision");
        }

        [ModTest("paint_regions/budget_absent_bounds_and_invalid_layer")]
        static void Limits(ModTestContext ctx)
        {
            var document = new RpgPaintFixture(ctx).Document;
            var outside = RpgPaintRegion.Fill(document, RpgStrokeLayer.Ground, new Vector2I(-1, 0));
            ModAssert.True(outside.Complete && outside.Cells.Count == 0, "outside selection has no paint cells");
            ModAssert.Equal(0, RpgPaintRegion.Fill(null, RpgStrokeLayer.Ground, Vector2I.Zero).Cells.Count,
                "unopened document has no region");
            var source = document.Data.DeepClone() as JsonObject;
            source["Width"] = 129;
            source["Height"] = 128;
            source.Remove("Ground");
            source.Remove("Structure");
            source.Remove("Collision");
            source["Legend"] = new JsonObject();
            var large = new RpgPaintFixture(ctx, source).Document;
            var before = large.Data.ToJsonString();
            var incomplete = RpgPaintRegion.Fill(large, RpgStrokeLayer.Ground, Vector2I.Zero);
            ModAssert.False(incomplete.Complete, "oversize connected area is explicitly incomplete");
            ModAssert.Equal(0, incomplete.Cells.Count, "budget failure never exposes a partial paint operation");
            ModAssert.Equal(before, large.Data.ToJsonString(), "budget failure is read-only");
            ModAssert.False(large.CanUndo, "budget failure creates no history");
            ModAssert.Equal(0, large.Changes().Count, "budget failure creates no saved changes");
            var rejected = false;
            try
            {
                RpgPaintRegion.Fill(document, (RpgStrokeLayer)99, Vector2I.Zero);
            }
            catch (ArgumentOutOfRangeException)
            {
                rejected = true;
            }

            ModAssert.True(rejected, "undefined layer never silently paints a different layer");
            source["Width"] = 128;
            var exact = RpgPaintRegion.Fill(new RpgPaintFixture(ctx, source).Document, RpgStrokeLayer.Ground, Vector2I.Zero);
            ModAssert.True(exact.Complete && exact.Cells.Count == 16384, "the full128x128 target workload is allowed");
        }
    }
}
