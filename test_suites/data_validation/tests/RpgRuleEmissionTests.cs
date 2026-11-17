namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Validation;
    using Godot;

    /// <summary>
    /// Real invalid inputs exercise every emitter; unregistered findings fail before message or policy fallback.
    /// </summary>
    public sealed class RpgRuleEmissionTests
    {
        private readonly HashSet<string> seen = new(StringComparer.Ordinal);

        [ModTest("data_inventory/emitted_rules_are_registered", TimeoutSeconds = 60)]
        static void Coverage(ModTestContext ctx)
        {
            var probe = new RpgRuleEmissionTests();
            probe.Run();
            foreach (var rule in RpgDataRules.All)
            {
                if (rule.Code != RpgDataRules.Unknown)
                {
                    ModAssert.True(probe.seen.Contains(rule.Code), "an actual input exercises the registered emitter: " + rule.Code);
                }
            }

            ModAssert.Equal(RpgDataRules.All.Count - 1, probe.seen.Count, "all emitted identities have canonical metadata");
        }

        private void Run()
        {
            Add(RpgQuadCheck.Problem(null));
            var points = new List<double[]>
            {
                new double[] { 0, 0, 0 },
                new double[] { 1, 0, 0 },
                new double[] { 1, 1, 0 },
                new double[] { 0, 1, 0 },
            };
            points[0][2] = double.NaN;
            Add(RpgQuadCheck.Problem(points));
            points[0][2] = 0;
            points[2][0] = 2;
            Add(RpgQuadCheck.Problem(points));
            points[1][0] = 0;
            points[2][0] = 0;
            Add(RpgQuadCheck.Problem(points));
            var malformed = JsonNode.Parse("{\"Form\":\"sprite\",\"Anchor\":[0],\"Scale\":101,\"Parts\":["
                + "{\"Id\":\"dup\",\"Image\":\"missing\",\"Size\":[0,1],\"Geometry\":\"bad\",\"Only\":\"future\"},"
                + "{\"Id\":\"dup\",\"Image\":\"good\",\"Crop\":[-1,0,8,8]},"
                + "{\"Id\":\"missing_image\",\"Image\":\"missing\"},42]}").AsObject();
            Add(RpgDataCheck.Object(malformed, "topdown", 128, SizeOf));
            Add(RpgDataCheck.Object(JsonNode.Parse("{\"Form\":\"surface\",\"Surface\":{\"Image\":\"rectangle\"}}")
                .AsObject(), "topdown", 128, SizeOf));
            Add(RpgDataCheck.Object(JsonNode.Parse("{\"Form\":\"actor\",\"Charset\":\"bad_sheet\"}")
                .AsObject(), "topdown", 128, SizeOf));
            Add(RpgMapSyntax.Check(new JsonObject
            {
                ["Width"] = 0,
                ["Height"] = 1,
            }));
            Add(RpgMapSyntax.Check(new JsonObject
            {
                ["Width"] = 2,
                ["Height"] = 1,
                ["Ground"] = new JsonArray("x"),
            }));
            var map = MapData();
            map["Width"] = 129;
            map["Height"] = 129;
            CheckMap(map, null);
            map = MapData();
            map["Events"] = new JsonObject();
            CheckMap(map, null);
            map = MapData();
            map["Legend"] = new JsonObject
            {
                ["s"] = "probe/sprite",
            };
            map["Ground"] = new JsonArray("s");
            CheckMap(map, null);
            var placements = new JsonArray
            {
                new JsonObject
                {
                    ["Id"] = "rule_probe/missing",
                    ["Object"] = "not_present",
                    ["X"] = 0,
                    ["Y"] = 0,
                },
            };
            CheckMap(MapData(), placements);
        }

        private void CheckMap(JsonObject map, JsonArray placements)
        {
            var sources = new List<(string Path, string Text)>
            {
                ("maps/rule_probe.json", Envelope("rpg_map", new JsonArray(map))),
            };
            if (placements != null)
            {
                sources.Add(("placements/rule_probe/probe.json", Envelope("rpg_placement", placements)));
            }

            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            var document = RpgMapDocument.Open("rule_probe", "probe", sources, foreign, foreign, null, FormOf);
            var before = document.Data.ToJsonString();
            Add(RpgMapDataAudit.Check(document, "topdown", ObjectOf).Issues);
            ModAssert.Equal(before, document.Data.ToJsonString(), "registry coverage never changes its invalid map input");
            ModAssert.False(document.CanUndo, "invalid-input auditing creates no history");
        }

        private void Add(IEnumerable<RpgDataIssue> issues)
        {
            foreach (var issue in issues)
            {
                Add(issue.Code);
            }
        }

        private void Add(string code)
        {
            ModAssert.NotNull(code, "invalid scenario produces a concrete finding");
            ModAssert.NotNull(RpgDataRules.Find(code), "every emitted finding has a catalogue row: " + code);
            seen.Add(code);
        }

        private static JsonObject MapData()
        {
            return new JsonObject
            {
                ["Id"] = "rule_probe",
                ["Width"] = 1,
                ["Height"] = 1,
            };
        }

        private static string Envelope(string kind, JsonArray items)
        {
            return new JsonObject
            {
                ["Kind"] = kind,
                ["Items"] = items,
            }.ToJsonString();
        }

        private static RpgObject ObjectOf(string id)
        {
            return id == "probe/sprite" ? new RpgObject
            {
                ID = id,
                Form = RpgObjectForm.Sprite,
                Parts = new List<RpgObjectPart>(),
            } : null;
        }

        private static RpgObjectForm? FormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }

        private static Vector2I? SizeOf(string group, string id)
        {
            return id switch
            {
                "missing" => null,
                "rectangle" => new Vector2I(128, 64),
                "bad_sheet" => new Vector2I(15, 10),
                _ => new Vector2I(128, 128),
            };
        }
    }
}
