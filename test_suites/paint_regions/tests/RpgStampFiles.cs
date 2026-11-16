using System;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Collections.Generic;
using Cthangover.Core.Utils;
using Cthangover.Core.Mods.Testing;
using Cthangover.Tools.Services;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>Owns uniquely named native-library probe files; refuses preexisting user content and removes only its files.</summary>
    internal sealed class RpgStampFiles : IDisposable
    {
        internal const string Owner = "rpg_plus_game";

        internal const string MapPath = "maps/rpg_stamp_probe.json";

        internal const string StampPath = "editor_stamps/contract_group.json";

        private const string BrokenPath = "editor_stamps/contract_broken.json";

        private const string DependencyPath = "editor_stamps/contract_dependency.json";

        private const string UnrelatedPath = "editor_stamps/contract_unrelated.json";

        internal string MapText { get; }

        internal RpgStampFiles(ModTestContext ctx)
        {
            ModAssert.Null(ModFileService.ReadText(Owner, MapPath), "probe map does not replace a user's map");
            ModAssert.Null(ModFileService.ReadText(Owner, StampPath), "probe stamp does not replace a user's group");
            ModAssert.Null(ModFileService.ReadText(Owner, BrokenPath), "broken probe does not replace a user's file");
            ModAssert.Null(ModFileService.ReadText("village_pack", DependencyPath), "dependency probe replaces no user group");
            ModAssert.Null(ModFileService.ReadText("rpg_editor", UnrelatedPath), "unrelated probe replaces no user group");
            var source = JsonNode.Parse(ctx.ReadFileText("maps/rpg_fill_fixture.json"));
            var map = source["Items"][0];
            map["Id"] = "rpg_stamp_probe";
            ((JsonArray)map["Events"]).Add(new JsonObject
            {
                ["Id"] = "linked",
                ["X"] = 2,
                ["Y"] = 1,
                ["When"] = "rpg_stamp_probe/marker:seen == 1",
                ["Commands"] = new JsonArray("flag self.done 1"),
            });
            MapText = source.ToJsonString();
            try
            {
                ModAssert.True(ModFileService.WriteText(Owner, MapPath, MapText), "the probe owns a real writable map file");
                ModAssert.True(ModFileService.WriteText(Owner, BrokenPath, "{broken"), "one malformed library item is isolated");
                var dependency = new RpgMapStamp
                {
                    Name = "contract_dependency",
                    SourceMap = "dependency_source",
                    Clips = new List<RpgPlacementClip>
                    {
                        new RpgPlacementClip
                        {
                            LocalId = "missing",
                            Object = "unloaded_fixture/decor/missing",
                            Fields = new JsonObject(),
                        },
                    },
                };
                var json = JsonSerializer.Serialize(dependency, JsonOptionsProvider.ReadOptions);
                ModAssert.True(ModFileService.WriteText("village_pack", DependencyPath, json), "loaded dependency contributes a group");
                dependency.Name = "contract_unrelated";
                json = JsonSerializer.Serialize(dependency, JsonOptionsProvider.ReadOptions);
                ModAssert.True(ModFileService.WriteText("rpg_editor", UnrelatedPath, json), "unrelated loaded mod has a separate group");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Removes only the five paths whose absence was verified before the probe began.</summary>
        public void Dispose()
        {
            ModFileService.DeleteFile("village_pack", DependencyPath);
            ModFileService.DeleteFile("rpg_editor", UnrelatedPath);
            ModFileService.DeleteFile(Owner, StampPath);
            ModFileService.DeleteFile(Owner, BrokenPath);
            ModFileService.DeleteFile(Owner, MapPath);
        }
    }
}
