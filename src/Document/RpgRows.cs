namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using System.Text;
    using System.Text.Json.Nodes;

    /// <summary>
    /// The text rows of a map grid (<c>Ground</c>, <c>Structure</c>, <c>Collision</c>) as the map file keeps
    /// them: one string per row, one character per cell, <c>.</c> for an empty cell. A short or missing grid
    /// reads as empty cells and is padded when written, so a hand-written map is edited as it is. The rows are
    /// JSON nodes of the map's tree, so the file keeps its key order and its spelling everywhere else.
    /// </summary>
    public static class RpgRows
    {
        /// <summary>The empty-cell character.</summary>
        public const char Empty = '.';

        /// <summary>
        /// The rows under <paramref name="key"/> of <paramref name="map"/>; a missing grid is added at the end of the
        /// map's keys as <paramref name="height"/> rows of empty cells.
        /// </summary>
        public static JsonArray Ensure(JsonObject map, string key, int width, int height)
        {
            if (map[key] is JsonArray rows)
                return rows;
            var created = new JsonArray();
            for (var y = 0; y < height; y++)
                created.Add(JsonValue.Create(new string(Empty, width)));
            map[key] = created;
            return created;
        }

        /// <summary>The character of cell (<paramref name="x"/>, <paramref name="y"/>) of grid <paramref name="key"/>; <c>.</c> when the row or the cell is missing.</summary>
        public static char CharAt(JsonObject map, string key, int x, int y)
        {
            if (map[key] is not JsonArray rows || y < 0 || y >= rows.Count)
                return Empty;
            var text = RowText(rows[y]);
            return x >= 0 && x < text.Length ? text[x] : Empty;
        }

        /// <summary>Writes one cell; missing rows and cells are added as empty, so the grid gets the map's shape.</summary>
        public static void Set(JsonArray rows, int width, int x, int y, char value)
        {
            while (rows.Count <= y)
                rows.Add(JsonValue.Create(new string(Empty, width)));
            var chars = new StringBuilder(RowText(rows[y]).PadRight(width > x ? width : x + 1, Empty));
            chars[x] = value;
            rows[y] = JsonValue.Create(chars.ToString());
        }

        /// <summary>Every character a grid uses, once each (empty cells included as <c>.</c> when present).</summary>
        public static HashSet<char> CharsOf(JsonObject map, string key)
        {
            var chars = new HashSet<char>();
            if (map[key] is not JsonArray rows)
                return chars;
            foreach (var row in rows)
            {
                foreach (var c in RowText(row))
                    chars.Add(c);
            }
            return chars;
        }

        private static string RowText(JsonNode node)
            => node is JsonValue value && value.TryGetValue(out string text) ? text : string.Empty;
    }
}
