using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Copies JSON while rewriting explicit internal scopes only in condition/command fields, once per source token.</summary>
    internal sealed class RpgStampScopes
    {
        private readonly IReadOnlyDictionary<string, string> names;

        private readonly Regex tokens;

        internal RpgStampScopes(IReadOnlyDictionary<string, string> mapping)
        {
            names = mapping;
            var alternatives = new List<string>();
            foreach (var name in mapping.Keys)
            {
                alternatives.Add(Regex.Escape(name));
            }

            tokens = new Regex("(?<![A-Za-z0-9_/:.-])(?:" + string.Join("|", alternatives) + "):",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal JsonObject Copy(JsonObject source)
        {
            return source == null ? null : (JsonObject)CopyNode(source, false);
        }

        private JsonNode CopyNode(JsonNode source, bool script)
        {
            if (source is JsonObject fields)
            {
                var result = new JsonObject();
                foreach (var pair in fields)
                {
                    var next = script || pair.Key.Equals("When", StringComparison.OrdinalIgnoreCase)
                        || pair.Key.Equals("Commands", StringComparison.OrdinalIgnoreCase);
                    result[pair.Key] = CopyNode(pair.Value, next);
                }

                return result;
            }

            if (source is JsonArray entries)
            {
                var result = new JsonArray();
                foreach (var entry in entries)
                {
                    result.Add(CopyNode(entry, script));
                }

                return result;
            }

            if (script && source is JsonValue value && value.TryGetValue(out string text))
            {
                return JsonValue.Create(tokens.Replace(text, Replace));
            }

            return source?.DeepClone();
        }

        private string Replace(Match match)
        {
            return names[match.Value.Substring(0, match.Length - 1)] + ":";
        }
    }
}
