namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json.Nodes;

    /// <summary>
    /// The <c>Strokes</c> of a map as the editor writes and reads them: each entry paints an object (or a collision
    /// code) over single cells, applied after the rows in list order (<c>RpgMapCells</c> in the runtime). The editor
    /// paints a foreign map only this way, so a stroke it adds lands in its own mod's patch as <c>Strokes$add</c>.
    /// Layers: <c>Ground</c> and <c>Structure</c> keep the object in <c>Object</c>, <c>Collision</c> keeps the code in
    /// <c>Code</c>. A stroke with a null <c>Object</c> clears its cells.
    /// </summary>
    public static class RpgStrokeBook
    {
        /// <summary>The collision layer, whose strokes carry a <c>Code</c> instead of an <c>Object</c>.</summary>
        public const string CollisionLayer = "Collision";

        /// <summary>The map's <c>Strokes</c> array, added empty when missing.</summary>
        public static JsonArray Ensure(JsonObject map)
        {
            if (map["Strokes"] is JsonArray strokes)
                return strokes;
            var created = new JsonArray();
            map["Strokes"] = created;
            return created;
        }

        /// <summary>A stroke id no stroke of the map has: <c>&lt;prefix&gt;_1</c>, <c>_2</c>, …</summary>
        public static string NewId(JsonObject map, string prefix)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (map["Strokes"] is JsonArray strokes)
            {
                foreach (var stroke in strokes)
                {
                    if (stroke?["Id"] is JsonValue id && id.TryGetValue(out string text))
                        used.Add(text);
                }
            }
            for (var number = 1; ; number++)
            {
                var candidate = prefix + "_" + number;
                if (!used.Contains(candidate))
                    return candidate;
            }
        }

        /// <summary>Appends a stroke painting <paramref name="value"/> (an object id, or a code for the collision layer; null clears) over <paramref name="cells"/>.</summary>
        public static void Add(JsonObject map, string id, string layer, string value, IEnumerable<(int X, int Y)> cells)
        {
            var valueKey = layer == CollisionLayer ? "Code" : "Object";
            var list = new JsonArray();
            foreach (var cell in cells)
                list.Add(new JsonArray(JsonValue.Create(cell.X), JsonValue.Create(cell.Y)));
            var stroke = new JsonObject
            {
                ["Id"] = id,
                ["Layer"] = layer,
                [valueKey] = value == null ? null : JsonValue.Create(value),
                ["Cells"] = list,
            };
            Ensure(map).Add(stroke);
        }

        /// <summary>
        /// The value of the last stroke of <paramref name="layer"/> that covers cell (<paramref name="x"/>, <paramref name="y"/>):
        /// the object id (null when it clears), or the code of the collision layer. False when no stroke covers the cell.
        /// </summary>
        public static bool TryRead(JsonObject map, string layer, int x, int y, out string value)
        {
            value = null;
            var found = false;
            if (map["Strokes"] is not JsonArray strokes)
                return false;
            foreach (var node in strokes)
            {
                if (node is not JsonObject stroke || !string.Equals(Text(stroke["Layer"]), layer, StringComparison.Ordinal) || !Covers(stroke, x, y))
                    continue;
                value = Text(stroke[layer == CollisionLayer ? "Code" : "Object"]);
                found = true;
            }
            return found;
        }

        private static bool Covers(JsonObject stroke, int x, int y)
        {
            if (stroke["Cells"] is JsonArray cells)
            {
                foreach (var cell in cells)
                {
                    if (cell is JsonArray pair && pair.Count == 2 && Int(pair[0]) == x && Int(pair[1]) == y)
                        return true;
                }
            }
            if (stroke["Rects"] is JsonArray rects)
            {
                foreach (var rect in rects)
                {
                    if (rect is JsonArray box && box.Count == 4 && x >= Int(box[0]) && x < Int(box[0]) + Int(box[2])
                        && y >= Int(box[1]) && y < Int(box[1]) + Int(box[3]))
                        return true;
                }
            }
            return false;
        }

        private static string Text(JsonNode node)
            => node is JsonValue value && value.TryGetValue(out string text) ? text : null;

        private static int Int(JsonNode node)
            => node is JsonValue value && value.TryGetValue(out int number) ? number : int.MinValue;
    }
}
