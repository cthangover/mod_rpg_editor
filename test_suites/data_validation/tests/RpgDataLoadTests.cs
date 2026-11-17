namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>Records the owner's 128×128 and twenty-NPC workload; the audit caches dimensions rather than retained GPU images.</summary>
    public sealed class RpgDataLoadTests
    {
        private IReadOnlyDictionary<string, RpgObject> objects;

        [ModTest("data_inventory/working_load_128_twenty_npcs", TimeoutSeconds = 90)]
        static void Load(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "workload decodes ordinary images through the runtime file groups");
            new RpgDataLoadTests().Run(ctx);
        }

        private void Run(ModTestContext ctx)
        {
            objects = Entities.Collect<RpgObject>();
            var rows = new JsonArray();
            for (var y = 0; y < 128; y++)
            {
                rows.Add(new string('g', 128));
            }

            var map = new JsonObject
            {
                ["Id"] = "validation_load",
                ["Width"] = 128,
                ["Height"] = 128,
                ["Legend"] = new JsonObject
                {
                    ["g"] = "village_pack/ground/grass",
                },
                ["Ground"] = rows,
            };
            var placements = new JsonArray();
            for (var i = 0; i < 20; i++)
            {
                placements.Add(new JsonObject
                {
                    ["Id"] = "validation_load/npc_" + i,
                    ["Object"] = "village_pack/npc/elder",
                    ["X"] = 2 + i * 3,
                    ["Y"] = 5,
                });
            }

            var files = new List<(string Path, string Text)>
            {
                ("maps/validation_load.json", Envelope("rpg_map", new JsonArray(map))),
                ("placements/validation_load/props.json", Envelope("rpg_placement", placements)),
            };
            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            var document = RpgMapDocument.Open("validation_load", "inventory", files, foreign, foreign, null, FormOf);
            var original = document.Data.ToJsonString();
            var timings = new Dictionary<string, double>(StringComparer.Ordinal);
            var projections = new[]
            {
                "topdown",
                "isometric",
            };
            foreach (var projection in projections)
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var watch = Stopwatch.StartNew();
                    var report = RpgMapDataAudit.Check(document, projection, ObjectOf);
                    watch.Stop();
                    ModAssert.True(report.Complete, "128×128 is within the complete-check budget");
                    ModAssert.Equal(0, report.Issues.Count, "twenty ordinary NPCs retain valid data");
                    ModAssert.Equal(21, report.ObjectsChecked, "painted grass plus twenty actual placed actors were checked");
                    ModAssert.Equal(2, report.ImagesDecoded, "one ground image and one shared walking sheet are decoded once");
                    timings.Add(projection + "_" + attempt + "_ms", watch.Elapsed.TotalMilliseconds);
                    ctx.Log(projection + " pass=" + attempt + ": " + watch.Elapsed.TotalMilliseconds + " ms; decodes=2");
                }
            }

            ModAssert.Equal(original, document.Data.ToJsonString(), "all six samples leave map data unchanged");
            ModAssert.False(document.CanUndo, "sampling creates no authoring history");
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
            var json = JsonSerializer.Serialize(timings);
            var restored = JsonSerializer.Deserialize<Dictionary<string, double>>(json);
            ModAssert.Equal(timings.Count, restored.Count, "every measured sample survives JSON");
            foreach (var entry in timings)
            {
                ModAssert.True(restored[entry.Key] == entry.Value, "reported sample is exact: " + entry.Key);
            }

            File.WriteAllText(Path.Combine(directory, "working_load.json"), json);
        }

        private static string Envelope(string kind, JsonArray items)
        {
            return new JsonObject
            {
                ["Kind"] = kind,
                ["Items"] = items,
            }.ToJsonString();
        }

        private RpgObject ObjectOf(string id)
        {
            return id != null && objects.TryGetValue(id, out var value) ? value : null;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }
    }
}
