using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>Own and foreign transactions share valid detached map JSON; no source provider or borrowed object is written.</summary>
    internal sealed class RpgPaintFixture
    {
        private readonly IReadOnlyDictionary<string, RpgObject> objects;

        private readonly JsonObject written;

        internal RpgMapDocument Document { get; }

        internal string Original { get; }

        internal RpgPaintFixture(ModTestContext ctx, JsonObject map = null, bool foreign = false)
        {
            objects = Entities.Collect<RpgObject>();
            var file = JsonNode.Parse(ctx.ReadFileText("maps/rpg_fill_fixture.json"));
            written = (JsonObject)(map ?? (JsonObject)file["Items"][0]).DeepClone();
            Original = written.ToJsonString();
            var id = written["Id"].ToString();
            var path = "maps/" + id + ".json";
            var files = new List<(string Path, string Text)>();
            var maps = new List<(string Id, string OwnerMod, string File, JsonObject Written)>();
            if (foreign)
            {
                maps.Add((id, "fixture_origin", path, (JsonObject)written.DeepClone()));
            }
            else
            {
                var root = new JsonObject
                {
                    ["Kind"] = "rpg_map",
                    ["Items"] = new JsonArray(written.DeepClone()),
                };
                files.Add((path, root.ToJsonString()));
            }

            var placements = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            Document = RpgMapDocument.Open(id, ctx.ModId, files, maps, placements, BaseOf, FormOf);
            ModAssert.NotNull(Document, "valid paint-region fixture document opens");
        }

        internal static List<(int X, int Y)> Coordinates(RpgPaintRegion region)
        {
            var result = new List<(int X, int Y)>();
            foreach (var cell in region.Cells)
            {
                result.Add((cell.X, cell.Y));
            }

            return result;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return id != null && objects.TryGetValue(id, out var obj) ? obj.Form : null;
        }

        private JsonObject BaseOf(string kind, string id)
        {
            if (kind != "rpg_map" || id != written["Id"].ToString())
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize<RpgMap>(Original, JsonOptionsProvider.ReadOptions);
            return (JsonObject)JsonSerializer.SerializeToNode(dto, JsonOptionsProvider.ReadOptions);
        }
    }
}
