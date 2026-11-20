namespace Cthangover.Tools.RpgEditor
{
    using System.Text.Json.Nodes;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// The JSON a brand-new RPG map starts with. The editor's file selector and the location
    /// creator both write this template, so a new map is the same empty document whichever way
    /// it was made: size in cells, the spawn in the top-left corner facing down, an empty legend
    /// and, when chosen, the whole-map atmosphere profile.
    /// </summary>
    public static class RpgMapTemplate
    {
        /// <summary>Default width of a new map, in cells.</summary>
        public const int DefaultWidth = 16;

        /// <summary>Default height of a new map, in cells.</summary>
        public const int DefaultHeight = 12;

        /// <summary>
        /// The map item of a new file. <paramref name="atmosphere"/> writes the map's
        /// <c>Atmosphere</c> profile (the location-wide effect); null leaves the map without one.
        /// </summary>
        public static JsonObject Map(string id, int width, int height, string atmosphere)
        {
            var map = new JsonObject
            {
                ["Id"] = id,
                ["Width"] = width,
                ["Height"] = height,
                ["Zoom"] = 2,
                ["Spawn"] = new JsonObject
                {
                    ["X"] = 1,
                    ["Y"] = 1,
                    ["Dir"] = "Down"
                },
                ["Legend"] = new JsonObject(),
            };
            if (!string.IsNullOrEmpty(atmosphere))
                map["Atmosphere"] = atmosphere;
            return map;
        }

        /// <summary>The full new map file text (<c>{"Kind": "rpg_map", "Items": [...]}</c>) as <see cref="EntityDocument"/> writes it.</summary>
        public static string FileText(string id, int width, int height, string atmosphere)
        {
            return EntityDocument.Serialize(new JsonObject
            {
                ["Kind"] = "rpg_map",
                ["Items"] = new JsonArray(Map(id, width, height, atmosphere))
            });
        }
    }
}
