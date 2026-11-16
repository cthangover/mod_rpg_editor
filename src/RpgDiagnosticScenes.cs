using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Builds only referenced destination snapshots from resolved data; no textures, live state or file writes.</summary>
    internal sealed class RpgDiagnosticScenes
    {
        private readonly RpgEditorScene current;

        private readonly IReadOnlyDictionary<string, RpgMap> maps;

        private readonly IReadOnlyList<(RpgPlacement Placement, RpgMap Map)> placements;

        private readonly Func<string, RpgObject> objects;

        private readonly Dictionary<string, RpgEditorScene> cache = new(StringComparer.OrdinalIgnoreCase);

        internal RpgDiagnosticScenes(RpgEditorScene current, IReadOnlyDictionary<string, RpgMap> maps,
            IReadOnlyList<(RpgPlacement Placement, RpgMap Map)> placements, Func<string, RpgObject> objects)
        {
            this.current = current;
            var indexed = new Dictionary<string, RpgMap>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in maps)
            {
                indexed[entry.Key] = entry.Value;
            }

            this.maps = indexed;
            this.placements = placements;
            this.objects = objects;
        }

        internal RpgEditorScene Resolve(string id)
        {
            if (string.Equals(id, current.Map.ID, StringComparison.OrdinalIgnoreCase))
            {
                return current;
            }

            if (cache.TryGetValue(id, out var found))
            {
                return found;
            }

            if (!maps.TryGetValue(id, out var map))
            {
                return null;
            }

            var mapItems = new JsonArray(JsonSerializer.SerializeToNode(map, JsonOptionsProvider.ReadOptions));
            var placementItems = new JsonArray();
            foreach (var entry in placements)
            {
                if (entry.Placement.Object != null && string.Equals(entry.Map?.ID, id, StringComparison.OrdinalIgnoreCase))
                {
                    placementItems.Add(JsonSerializer.SerializeToNode(entry.Placement, JsonOptionsProvider.ReadOptions));
                }
            }

            var files = new List<(string Path, string Text)>
            {
                ("maps/snapshot.json", Envelope("rpg_map", mapItems)),
                ("placements/" + id + "/snapshot.json", Envelope("rpg_placement", placementItems)),
            };
            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            var document = RpgMapDocument.Open(id, "diagnostic_snapshot", files, foreign, foreign, NoBase, FormOf);
            found = RpgEditorScene.From(document, objects, RpgEditorPreview.ForDocument(document).Resolve);
            cache[id] = found;
            return found;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return objects(id)?.Form;
        }

        private static JsonObject NoBase(string kind, string id)
        {
            return null;
        }

        private static string Envelope(string kind, JsonArray items)
        {
            return new JsonObject
            {
                ["Kind"] = kind,
                ["Items"] = items,
            }.ToJsonString();
        }
    }
}
