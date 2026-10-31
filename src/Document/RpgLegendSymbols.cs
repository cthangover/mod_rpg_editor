namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;

    /// <summary>
    /// The automatic legend of an own map: a symbol is one character of a grid that the <c>Legend</c> maps to an
    /// object id. Painting an object finds its symbol (reuse) or takes the first free one; the author never sees
    /// symbols. A symbol is free when no legend entry has it, so a symbol still used by a grid cannot be given to
    /// another object. Symbols that no grid uses any more are removed by <see cref="DropUnused"/> on save, not
    /// while painting, so an undo of a paint restores the symbol it had.
    /// </summary>
    public static class RpgLegendSymbols
    {
        /// <summary>Candidates in the order they are taken: lower-case letters, upper-case letters, digits.</summary>
        private const string Candidates = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        /// <summary>The map's <c>Legend</c> object, added empty when missing.</summary>
        public static JsonObject Ensure(JsonObject map)
        {
            if (map["Legend"] is JsonObject legend)
                return legend;
            var created = new JsonObject();
            map["Legend"] = created;
            return created;
        }

        /// <summary>The symbol that already stands for <paramref name="objectId"/>, or null.</summary>
        public static char? SymbolOf(JsonObject legend, string objectId)
        {
            foreach (var pair in legend)
            {
                if (pair.Key.Length == 1 && pair.Value is JsonValue value && value.TryGetValue(out string id)
                    && string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase))
                    return pair.Key[0];
            }
            return null;
        }

        /// <summary>The first candidate no legend entry uses, or null when all are taken.</summary>
        public static char? FreeSymbol(JsonObject legend)
        {
            foreach (var c in Candidates)
            {
                if (!legend.ContainsKey(c.ToString()))
                    return c;
            }
            return null;
        }

        /// <summary>
        /// The symbol of <paramref name="objectId"/>: the existing one, else a new entry for it. Null only when the legend
        /// is full (every candidate taken), which the caller reports as a refused paint.
        /// </summary>
        public static char? Assign(JsonObject legend, string objectId)
        {
            var existing = SymbolOf(legend, objectId);
            if (existing != null)
                return existing;
            var free = FreeSymbol(legend);
            if (free != null)
                legend[free.Value.ToString()] = objectId;
            return free;
        }

        /// <summary>Removes every legend entry whose symbol is not among <paramref name="used"/>; returns the removed symbols.</summary>
        public static List<string> DropUnused(JsonObject legend, ISet<char> used)
        {
            var unused = legend.Select(pair => pair.Key).Where(key => key.Length != 1 || !used.Contains(key[0])).ToList();
            foreach (var key in unused)
                legend.Remove(key);
            return unused;
        }
    }
}
