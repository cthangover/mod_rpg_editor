using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor.Tests.CellRules
{
    /// <summary>Detached documents built through the normal JSON reader; their objects and map data are test-owned.</summary>
    internal sealed class RpgRulesFixture
    {
        private readonly IReadOnlyDictionary<string, RpgObject> objects;

        internal RpgMapDocument Document { get; }

        internal RpgRulesFixture(RpgMap map, IReadOnlyDictionary<string, RpgObject> objects = null,
            params RpgPlacement[] placements)
        {
            this.objects = objects ?? new Dictionary<string, RpgObject>();
            var maps = new JsonObject
            {
                ["Kind"] = "rpg_map",
                ["Items"] = new JsonArray(JsonSerializer.SerializeToNode(map, JsonOptionsProvider.ReadOptions)),
            };
            var instances = new JsonArray();
            foreach (var placement in placements)
            {
                instances.Add(JsonSerializer.SerializeToNode(placement, JsonOptionsProvider.ReadOptions));
            }

            var placed = new JsonObject
            {
                ["Kind"] = "rpg_placement",
                ["Items"] = instances,
            };
            var files = new List<(string Path, string Text)>
            {
                ("maps/fixture.json", maps.ToJsonString()),
                ("placements/" + map.ID + "/fixture.json", placed.ToJsonString()),
            };
            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            Document = RpgMapDocument.Open(map.ID, "rpg_editor__cell_rules", files, foreign, foreign, NoBase, FormOf);
        }

        internal RpgEditorScene Scene()
        {
            return RpgEditorScene.From(Document, ObjectOf, RpgEditorPreview.ForDocument(Document).Resolve);
        }

        internal RpgObject ObjectOf(string id)
        {
            return id != null && objects.TryGetValue(id, out var obj) ? obj : null;
        }

        internal static RpgMap Map()
        {
            var map = new RpgMap
            {
                ID = "rules_fixture",
                Width = 8,
                Height = 8,
                Spawn = new()
                {
                    X = 1,
                    Y = 1,
                },
                Collision = new List<string>(),
            };
            for (var y = 0; y < map.Height; y++)
            {
                map.Collision.Add("........");
            }

            return map;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }

        private static JsonObject NoBase(string kind, string id)
        {
            return null;
        }
    }
}
