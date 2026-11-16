using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    /// <summary>
    /// Inventories existing loaded maps in both projections; findings remain evidence rather than passing-content assertions.
    /// </summary>
    public sealed class RpgContentDataAuditTests
    {
        private IReadOnlyDictionary<string, RpgObject> objects;

        [ModTest("data_inventory/current_maps_both_projections", TimeoutSeconds = 180)]
        static void Content(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "the inventory uses a native headless host for image decoding");
            new RpgContentDataAuditTests().Run(ctx);
        }

        private void Run(ModTestContext ctx)
        {
            objects = Entities.Collect<RpgObject>();
            var maps = Entities.Collect<RpgMap>();
            var placements = Entities.Collect<RpgPlacement>();
            var reports = new SortedDictionary<string, RpgMapDataReport>(StringComparer.Ordinal);
            var before = JsonSerializer.Serialize(objects, JsonOptionsProvider.ReadOptions);
            var projections = new[]
            {
                "topdown",
                "isometric",
            };
            foreach (var projection in projections)
            {
                foreach (var map in maps.Values)
                {
                    var files = new List<(string Path, string Text)>();
                    var sourceMod = "inventory";
                    var sourcePath = "maps/" + map.ID + ".json";
                    var source = ForeignEntities.Explain("rpg_map", map.ID);
                    if (source?.Fields != null && source.Fields.TryGetValue("Id", out var origin))
                    {
                        sourceMod = origin.ModId;
                        sourcePath = origin.File;
                    }
                    var mapNode = JsonSerializer.SerializeToNode(map, JsonOptionsProvider.ReadOptions);
                    var root = new JsonObject
                    {
                        ["Kind"] = "rpg_map",
                        ["Items"] = new JsonArray(mapNode),
                    };
                    files.Add((sourcePath, root.ToJsonString()));
                    var rows = new JsonArray();
                    foreach (var placement in placements.Values)
                    {
                        if (RpgMapDocument.MapOfPlacement(placement.ID) == map.ID)
                        {
                            rows.Add(JsonSerializer.SerializeToNode(placement, JsonOptionsProvider.ReadOptions));
                        }
                    }

                    var props = new JsonObject
                    {
                        ["Kind"] = "rpg_placement",
                        ["Items"] = rows,
                    };
                    files.Add(("placements/" + map.ID + "/inventory.json", props.ToJsonString()));
                    var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
                    var document = RpgMapDocument.Open(map.ID, sourceMod, files, foreign, foreign, null, FormOf);
                    ModAssert.NotNull(document, "existing map opens for read-only inventory: " + map.ID);
                    var original = document.Data.ToJsonString();
                    var report = RpgMapDataAudit.Check(document, projection, ObjectOf);
                    reports.Add(projection + "/" + map.ID, report);
                    ModAssert.Equal(original, document.Data.ToJsonString(), "inventory preserves map data: " + map.ID);
                    ModAssert.False(document.CanUndo, "inventory records no history: " + map.ID);
                    ctx.Log(projection + "/" + map.ID + ": complete=" + report.Complete + ", issues=" + report.Issues.Count);
                }
            }

            ModAssert.Equal(before, JsonSerializer.Serialize(objects, JsonOptionsProvider.ReadOptions),
                "checking variants and states never edits the shared catalog");
            ModAssert.True(reports.ContainsKey("topdown/validation_workshop"), "teaching content was actually audited");
            ModAssert.True(reports.ContainsKey("isometric/rpg_village"), "the ordinary game was actually audited");
            ModAssert.True(reports.ContainsKey("isometric/jrp_office"), "the authored story rooms were actually audited");
            var output = "res://tests/artifacts/f11_data";
            foreach (var argument in OS.GetCmdlineUserArgs())
            {
                if (argument.StartsWith("--validation-out=", StringComparison.Ordinal))
                {
                    output = argument.Substring("--validation-out=".Length);
                }
            }

            var directory = ProjectSettings.GlobalizePath(output);
            Directory.CreateDirectory(directory);
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(Path.Combine(directory, "content_inventory.json"), JsonSerializer.Serialize(reports, options));
            ctx.Log("Inventory rows=" + reports.Count + "; report=" + directory);
        }

        private RpgObject ObjectOf(string id)
        {
            return id != null && objects.TryGetValue(id, out var value) ? value : null;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }

        [ModTest("data_inventory/report_json_round_trip")]
        static void Serialization(ModTestContext ctx)
        {
            var source = new RpgMapDataReport
            {
                Complete = false,
                ObjectsChecked = 20,
                ImagesDecoded = 3,
                UnusedLegendEntries = 2,
            };
            source.Issues.Add(new Cthangover.Mods.Rpg.Validation.RpgDataIssue
            {
                Code = "budget",
                Field = "Width/Height",
                Uses = 1,
            });
            var json = JsonSerializer.Serialize(source);
            var restored = JsonSerializer.Deserialize<RpgMapDataReport>(json);
            ModAssert.False(restored.Complete, "incomplete status survives JSON");
            ModAssert.Equal(source.ObjectsChecked, restored.ObjectsChecked, "object count survives JSON");
            ModAssert.Equal(source.ImagesDecoded, restored.ImagesDecoded, "decode count survives JSON");
            ModAssert.Equal(source.UnusedLegendEntries, restored.UnusedLegendEntries, "unused references remain distinct");
            ModAssert.Equal(1, restored.Issues.Count, "nested findings survive JSON");
            ModAssert.Equal("budget", restored.Issues[0].Code, "nested rule survives JSON");
            ModAssert.Equal(json, JsonSerializer.Serialize(restored), "complete report round trip is lossless");
        }
    }
}
