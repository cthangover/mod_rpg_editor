using System.Collections.Generic;
using System.Text.Json;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.CellRules
{
    /// <summary>Explains effective precedence while retaining suppressed sources and preserving authoring JSON.</summary>
    public sealed class RpgCellReportTests
    {
        [ModTest("cell_rules/precedence_and_suppressed_masks")]
        static void Precedence(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            map.Legend = new Dictionary<string, string>
            {
                ["g"] = "rules_fixture/water",
                ["s"] = "rules_fixture/wall",
            };
            map.Ground = new List<string>();
            map.Structure = new List<string>();
            for (var y = 0; y < map.Height; y++)
            {
                map.Ground.Add(y == 0 ? "g......." : "........");
                map.Structure.Add(y == 0 ? "s......." : "........");
            }

            var objects = new Dictionary<string, RpgObject>
            {
                ["rules_fixture/water"] = new()
                {
                    ID = "rules_fixture/water",
                    Form = RpgObjectForm.Surface,
                    Solid = true,
                },
                ["rules_fixture/wall"] = new()
                {
                    ID = "rules_fixture/wall",
                    Form = RpgObjectForm.Segment,
                    Solid = true,
                },
                ["rules_fixture/prop"] = new()
                {
                    ID = "rules_fixture/prop",
                    Form = RpgObjectForm.Sprite,
                    Solid = true,
                },
            };
            var placement = new RpgPlacement
            {
                ID = map.ID + "/prop",
                Object = "rules_fixture/prop",
            };
            var rules = new[]
            {
                ('#', "block", false),
                ('o', "open", true),
                ('.', "placement", false),
            };
            foreach (var (code, reason, walkable) in rules)
            {
                map.Collision[0] = code + ".......";
                var fixture = new RpgRulesFixture(map, objects, placement);
                var before = fixture.Document.Data.ToJsonString();
                var report = RpgCellReport.At(fixture.Scene(), Vector2I.Zero);
                ModAssert.True(report.Cell == Vector2I.Zero && report.InBounds, "requested map coordinate is retained");
                ModAssert.Equal(reason, report.Rule, "explicit collision precedes all effective masks");
                ModAssert.True(walkable == report.Walkable, "explanation agrees with the effective grid");
                ModAssert.Equal(code, report.Collision, "effective override is retained");
                ModAssert.Equal("rules_fixture/water", report.Ground, "suppressed ground remains inspectable");
                ModAssert.Equal("rules_fixture/wall", report.Structure, "suppressed structure remains inspectable");
                ModAssert.Equal("Collision[0][0]", report.CollisionSource, "row origin is exact");
                ModAssert.Equal("Ground[0][0]", report.GroundSource, "ground row origin is exact");
                ModAssert.Equal("Structure[0][0]", report.StructureSource, "structure row origin is exact");
                ModAssert.Equal(1, report.Blockers.Count, "suppressed placement mask remains inspectable");
                ModAssert.Equal(placement.ID, report.Blockers[0], "mask source has a stable placement identity");
                ModAssert.Equal(before, fixture.Document.Data.ToJsonString(), "explanation never rewrites an intentional override");
                ModAssert.False(fixture.Document.CanUndo, "explanation adds no history");
                ModAssert.Equal(0, fixture.Document.Changes().Count, "explanation adds no saved changes");
            }

            map.Collision[0] = "........";
            var withoutPlacement = new RpgRulesFixture(map, objects);
            ModAssert.Equal("structure", RpgCellReport.At(withoutPlacement.Scene(), Vector2I.Zero).Rule,
                "structure precedes ground");
            map.Structure[0] = "........";
            ModAssert.Equal("ground", RpgCellReport.At(new RpgRulesFixture(map, objects).Scene(), Vector2I.Zero).Rule,
                "ground remains when no higher blocker applies");
            var free = RpgCellReport.At(withoutPlacement.Scene(), new Vector2I(7, 7));
            ModAssert.True(free.Walkable && free.Rule == "free" && free.Blockers.Count == 0, "empty cells have no blocker");
            ModAssert.Null(free.Ground, "empty surface is explicit");
            ModAssert.Null(free.Structure, "empty structure is explicit");
            var outside = RpgCellReport.At(withoutPlacement.Scene(), new Vector2I(-1, 0));
            ModAssert.True(!outside.InBounds && !outside.Walkable && outside.Rule == "outside", "outside is never open");
            ModAssert.Equal('#', outside.Collision, "outside collision agrees with the grid");
            ModAssert.Null(outside.CollisionSource, "outside does not invent a source row");
            ModAssert.False(RpgCellReport.At(null, Vector2I.Zero).InBounds, "an unopened editor has no cell data");
            objects["rules_fixture/prop"].Hidden = true;
            var hidden = RpgCellReport.At(new RpgRulesFixture(map, objects, placement).Scene(), Vector2I.Zero);
            ModAssert.Equal(0, hidden.Blockers.Count, "a hidden effective placement contributes no blocking mask");
            ModAssert.Equal("ground", hidden.Rule, "the next applicable terrain rule remains visible");
        }

        [ModTest("cell_rules/ordered_stroke_sources_and_json_roundtrip")]
        static void Sources(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            map.Strokes = new List<RpgStroke>
            {
                new()
                {
                    Id = "first",
                    Layer = RpgStrokeLayer.Collision,
                    Code = "#",
                    Rects = new() { new[] { 2, 2, 2, 2 } },
                },
                new()
                {
                    Id = "opening",
                    Layer = RpgStrokeLayer.Collision,
                    Code = "o",
                    Cells = new() { new[] { 2, 2 } },
                },
                new()
                {
                    Id = "clear_surface",
                    Layer = RpgStrokeLayer.Ground,
                    Cells = new() { new[] { 2, 2 } },
                },
            };
            var fixture = new RpgRulesFixture(map);
            var before = fixture.Document.Data.ToJsonString();
            var scene = fixture.Scene();
            var report = RpgCellReport.At(scene, new Vector2I(2, 2));
            ModAssert.Equal("Strokes[1]:opening", report.CollisionSource, "last applicable stroke wins");
            ModAssert.Equal("Strokes[2]:clear_surface", report.GroundSource, "explicit clear has a source");
            ModAssert.True(report.Walkable && report.Collision == 'o', "opening survives inspection");
            ModAssert.Equal("Strokes[0]:first", RpgCellReport.At(scene, new Vector2I(3, 3)).CollisionSource,
                "rectangle still explains its other cells");
            var json = JsonSerializer.Serialize(map, JsonOptionsProvider.ReadOptions);
            var restored = JsonSerializer.Deserialize<RpgMap>(json, JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(json, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "all map/stroke/source coordinates survive a complete DTO JSON round trip");
            ModAssert.Equal(before, fixture.Document.Data.ToJsonString(), "source inspection is byte-stable");
        }
    }
}
