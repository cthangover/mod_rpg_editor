namespace Cthangover.Tools.RpgEditor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.EntityEditor;
    using Godot;

    /// <summary>
    /// The data the document tests read: the JSON files of the game's mods (through <c>res://</c>, so the tests see the
    /// real maps, placements and objects), the forms of their objects, and a small own map of the test mod for the cases
    /// that need one. Foreign entities are passed the way the game passes them: the written value of the owner's file
    /// and the value as the game reads it (<see cref="EntityJson.Normalize"/>).
    /// </summary>
    public static class RpgEditorFixture
    {
        /// <summary>The mod the document is edited as (its patch files and stroke ids use this name).</summary>
        public const string OwnMod = "my_mod";

        /// <summary>The game's map mod: its maps, placements and the villages' objects are the real data.</summary>
        public const string GameMod = "rpg_plus_game";

        /// <summary>A surface object of the village pictures.</summary>
        public const string Grass = "village_pack/ground/grass";

        /// <summary>A surface object that is solid (water).</summary>
        public const string Water = "village_pack/ground/water";

        /// <summary>A segment object (a fence).</summary>
        public const string Fence = "village_pack/structure/fence_wood";

        /// <summary>A sprite object with no picture (an invisible trigger).</summary>
        public const string Trigger = "village_pack/marker/trigger";

        /// <summary>An actor object.</summary>
        public const string Merchant = "village_pack/npc/merchant";

        /// <summary>A 4×3 own map of the test mod: legend <c>g</c> grass, <c>f</c> fence; a solid cell and an open one in collision.</summary>
        public const string OwnMapText = @"{
  ""Kind"": ""rpg_map"",
  ""Items"": [
    {
      ""Id"": ""my_map"",
      ""Width"": 4,
      ""Height"": 3,
      ""Legend"": {""g"": ""village_pack/ground/grass"", ""f"": ""village_pack/structure/fence_wood""},
      ""Ground"": [""gggg"", ""gggg"", ""gggg""],
      ""Structure"": [""f..."", ""...."", ""....""],
      ""Collision"": [""...."", ""..#o"", ""....""]
    }
  ]
}";

        /// <summary>Two placements of the own map and one of another map, in one file.</summary>
        public const string OwnPlacementsText = @"{
  ""Kind"": ""rpg_placement"",
  ""Items"": [
    {""Id"": ""my_map/sign"", ""Object"": ""village_pack/marker/trigger"", ""X"": 1, ""Y"": 1},
    {""Id"": ""my_map/well"", ""Object"": ""village_pack/marker/trigger"", ""X"": 2, ""Y"": 2},
    {""Id"": ""other_map/well"", ""Object"": ""village_pack/marker/trigger"", ""X"": 0, ""Y"": 0}
  ]
}";

        private static Dictionary<string, RpgObjectForm> forms;
        private static Dictionary<string, JsonObject> objects;

        /// <summary>Every JSON file except test suites under the mod's actual provider root, as (path relative to the mod, text).</summary>
        public static List<(string Path, string Text)> ReadModJson(string mod)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "mod files are read with Godot FileAccess, which needs the running engine");
            var result = new List<(string Path, string Text)>();
            var provider = ModManager.Instance.GetMod(mod)?.FileProvider;
            ModAssert.NotNull(provider, $"the mod {mod} is loaded");
            CollectJson(provider.RootPath, string.Empty, result);
            return result;
        }

        /// <summary>The entities of <paramref name="kind"/> in the files of <paramref name="owner"/>: id, owner, file and the item as written.</summary>
        public static List<(string Id, string OwnerMod, string File, JsonObject Written)> EntitiesOf(IEnumerable<(string Path, string Text)> files, string kind, string owner)
        {
            var result = new List<(string Id, string OwnerMod, string File, JsonObject Written)>();
            foreach (var (path, text) in files)
            {
                if (JsonNode.Parse(text) is not JsonObject root || root["Kind"]?.ToString() != kind || root["Items"] is not JsonArray items)
                    continue;
                foreach (var item in items.OfType<JsonObject>())
                    result.Add((item["Id"]?.ToString(), owner, path, item));
            }
            return result;
        }

        /// <summary>
        /// The value of a foreign entity as the game reads it without the test's patch: the written item, normalised by
        /// the DTO of its kind (<c>rpg_map</c> or <c>rpg_placement</c>), as <c>ForeignEntities.BaseWithout</c> does in the game.
        /// </summary>
        public static Func<string, string, JsonObject> BaseLookup(IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> maps,
            IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> placements)
        {
            var all = maps.Select(e => (Kind: "rpg_map", e.Id, e.Written)).Concat(placements.Select(e => (Kind: "rpg_placement", e.Id, e.Written))).ToList();
            return (kind, id) =>
            {
                var found = all.FirstOrDefault(e => e.Kind == kind && string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
                if (found.Written == null)
                    return null;
                var type = kind == "rpg_map" ? typeof(RpgMap) : typeof(RpgPlacement);
                return EntityJson.Normalize((JsonObject)found.Written.DeepClone(), type);
            };
        }

        /// <summary>The form of every object of the village and game mods, by id; unknown ids give null.</summary>
        public static Func<string, RpgObjectForm?> FormLookup()
        {
            Load();
            return id => id != null && forms.TryGetValue(id, out var form) ? (RpgObjectForm?)form : null;
        }

        /// <summary>The objects of the village and game mods as the runtime reads them (<see cref="RpgObject"/>), for the runtime checks.</summary>
        public static Func<string, RpgObject> ObjectLookup()
        {
            Load();
            return id => id != null && objects.TryGetValue(id, out var item)
                ? JsonSerializer.Deserialize<RpgObject>(item.ToJsonString(), JsonOptionsProvider.ReadOptions)
                : null;
        }

        /// <summary>Opens <paramref name="mapId"/> as an own map of <paramref name="ownMod"/> with the given files and no foreign entities.</summary>
        public static RpgMapDocument OpenOwn(string mapId, string ownMod, IEnumerable<(string Path, string Text)> files)
        {
            var empty = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            var document = RpgMapDocument.Open(mapId, ownMod, files, empty, empty, BaseLookup(empty, empty), FormLookup());
            ModAssert.NotNull(document, $"own map '{mapId}' opens");
            return document;
        }

        /// <summary>The own map of the test mod (<see cref="OwnMapText"/>) with its own placements, opened as the editor opens them.</summary>
        public static RpgMapDocument OwnMap()
            => OpenOwn("my_map", OwnMod, new[] { ("maps/my_map.json", OwnMapText), ("placements/my_map/events.json", OwnPlacementsText) });

        /// <summary>Opens <paramref name="mapId"/> of the test mod with the given own files and foreign entities; fails the test when it does not open.</summary>
        public static RpgMapDocument Open(IEnumerable<(string Path, string Text)> files,
            IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> foreignMaps,
            string mapId = "my_map",
            IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> foreignPlacements = null)
        {
            var placements = foreignPlacements ?? Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            var document = RpgMapDocument.Open(mapId, OwnMod, files, foreignMaps, placements, BaseLookup(foreignMaps, placements), FormLookup());
            ModAssert.NotNull(document, $"map '{mapId}' opens");
            return document;
        }

        /// <summary>The game's maps and placements as foreign entities of the test mod.</summary>
        public static (List<(string Id, string OwnerMod, string File, JsonObject Written)> Maps, List<(string Id, string OwnerMod, string File, JsonObject Written)> Placements) GameEntities(IEnumerable<(string Path, string Text)> gameFiles)
            => (EntitiesOf(gameFiles, "rpg_map", GameMod), EntitiesOf(gameFiles, "rpg_placement", GameMod));

        /// <summary>
        /// Frozen legacy doors for placement-patch compatibility tests. Production maps now store these doors as Events;
        /// those tests must exercise real old-format data rather than depend on the sample game's current authoring format.
        /// </summary>
        public static (List<(string Id, string OwnerMod, string File, JsonObject Written)> Maps, List<(string Id, string OwnerMod, string File, JsonObject Written)> Placements) LegacyGameEntities(IEnumerable<(string Path, string Text)> gameFiles)
        {
            var (maps, placements) = GameEntities(gameFiles);
            foreach (var map in maps) map.Written.Remove("Events");
            var doors = new[]
            {
                ("legacy/doors.json", @"
                {""Kind"":""rpg_placement"",""Items"":[
                  {""Id"":""rpg_village/door_merchant"",""Object"":""village_pack/marker/trigger"",""X"":21,""Y"":6,""Solid"":true,
                   ""Interact"":{""Trigger"":""Use"",""Scenario"":""rpg_plus/events/locked_door""}},
                  {""Id"":""rpg_desert/rpg_plus_enter_desert_house"",""Object"":""village_pack/marker/trigger"",""X"":4,""Y"":4,
                   ""Interact"":{""Trigger"":""Step"",""Teleport"":{""Map"":""rpg_plus_desert_desert_house"",""X"":2,""Y"":3,""Dir"":""Up""}}}
                ]}
                ")
            };
            placements.AddRange(EntitiesOf(doors, "rpg_placement", GameMod));
            return (maps, placements);
        }

        /// <summary>The text of one file of the given list, or null.</summary>
        public static string TextOf(IEnumerable<(string Path, string Text)> files, string path)
            => files.FirstOrDefault(f => f.Path == path).Text;

        /// <summary>A map's legend as symbol → object id.</summary>
        public static Dictionary<string, string> LegendOf(string mapText)
        {
            var legend = (JsonNode.Parse(mapText)["Items"] as JsonArray)[0]["Legend"] as JsonObject ?? new JsonObject();
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in legend)
                result[pair.Key] = pair.Value?.ToString();
            return result;
        }

        private static void Load()
        {
            if (forms != null)
                return;
            forms = new Dictionary<string, RpgObjectForm>(StringComparer.OrdinalIgnoreCase);
            objects = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in new[] { "village_pack", GameMod })
            {
                foreach (var (path, text) in ReadModJson(mod))
                {
                    if (JsonNode.Parse(text) is not JsonObject root || root["Kind"]?.ToString() != "rpg_object" || root["Items"] is not JsonArray items)
                        continue;
                    foreach (var item in items.OfType<JsonObject>())
                    {
                        var id = item["Id"]?.ToString() ?? string.Empty;
                        objects[id] = item;
                        if (Enum.TryParse(item["Form"]?.ToString(), true, out RpgObjectForm form))
                            forms[id] = form;
                    }
                }
            }
        }

        private static void CollectJson(string path, string relative, List<(string Path, string Text)> result)
        {
            var dir = DirAccess.Open(path);
            ModAssert.NotNull(dir, $"the folder {path} opens");
            foreach (var name in dir.GetFiles())
            {
                if (!name.EndsWith(".json", StringComparison.Ordinal))
                    continue;
                var file = FileAccess.Open(path + "/" + name, FileAccess.ModeFlags.Read);
                ModAssert.NotNull(file, $"{path}/{name} opens");
                result.Add((relative + name, file.GetAsText()));
                file.Close();
            }
            foreach (var folder in dir.GetDirectories())
            {
                if (folder != "test_suites")
                    CollectJson(path + "/" + folder, relative + folder + "/", result);
            }
        }
    }
}
