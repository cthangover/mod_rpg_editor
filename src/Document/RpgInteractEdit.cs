namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The write API of a placement's <c>Interact</c> for the novice's pickers (plan 14.G2): the kinds the pickers make,
    /// the reading of an existing <c>Interact</c> back into a kind, and the placement override that makes the game read
    /// exactly the chosen interaction.
    /// <para/>
    /// <b>Override.</b> A placement's fields are deep-merged over its object (<see cref="RpgObjectResolve.Effective"/>), so
    /// a plain <c>Interact</c> with only the chosen keys would keep the object's <c>Scenario</c>, <c>Commands</c> or
    /// <c>Teleport</c>. Every such key that the choice does not have is written as <c>null</c>, which removes it (the sample
    /// game does the same with <c>"Commands": null</c>). <c>Commands</c> is an array, so the chosen one replaces the object's.
    /// <para/>
    /// <b>Commands of a kind.</b> The item and the shop kinds are one command line each, in the spelling the palette writes
    /// (<c>action item.add id=… count=…</c>, <c>action trade.open shop=…</c>); reading is the inverse, so a line the pickers
    /// wrote reads back as its kind.
    /// <para/>
    /// The placement's <c>Interact</c> is written through <see cref="RpgMapDocument.SetPlacementField"/>, so an own
    /// placement keeps its file and a foreign one becomes a patch of the edited mod, and the change is one undo step.
    /// </summary>
    public static class RpgInteractEdit
    {
        private const string Category = "RPG_EDITOR";
        private const string InteractKey = "Interact";
        private const string ObjectKey = "Object";
        private static readonly Regex ItemLine = new(@"^action item\.add id=(\S+) count=(\d+)$", RegexOptions.CultureInvariant);
        private static readonly Regex ShopLine = new(@"^action trade\.open shop=(\S+)$", RegexOptions.CultureInvariant);

        /// <summary>The command line of the item kind.</summary>
        public static string ItemCommand(string itemId, int count)
            => string.Format(CultureInfo.InvariantCulture, "action item.add id={0} count={1}", itemId, count);

        /// <summary>The command line of the shop kind.</summary>
        public static string ShopCommand(string shopId) => "action trade.open shop=" + shopId;

        /// <summary>The kind an interaction has as the pickers read it; null gives <see cref="RpgInteractKind.None"/>.</summary>
        public static RpgInteractKind KindOf(RpgInteract interact)
        {
            if (interact == null)
                return RpgInteractKind.None;
            if (interact.Teleport != null)
                return RpgInteractKind.Map;
            var commands = interact.Commands ?? new List<string>();
            if (!string.IsNullOrWhiteSpace(interact.Scenario))
                return commands.Count == 0 ? RpgInteractKind.Dialog : RpgInteractKind.Custom;
            if (commands.Count == 0)
                return RpgInteractKind.None;
            if (commands.Count == 1 && ItemLine.IsMatch(commands[0]))
                return RpgInteractKind.Item;
            if (commands.Count == 1 && ShopLine.IsMatch(commands[0]))
                return RpgInteractKind.Shop;
            return RpgInteractKind.Custom;
        }

        /// <summary>The item id of an item interaction, or null for another kind.</summary>
        public static string ItemOf(RpgInteract interact) => MatchOf(interact, ItemLine, 1);

        /// <summary>The count of an item interaction; 1 when the kind is not an item.</summary>
        public static int CountOf(RpgInteract interact)
        {
            var text = MatchOf(interact, ItemLine, 2);
            return text != null && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) && count > 0 ? count : 1;
        }

        /// <summary>The shop id of a shop interaction, or null for another kind.</summary>
        public static string ShopOf(RpgInteract interact) => MatchOf(interact, ShopLine, 1);

        /// <summary>
        /// The interaction a picker choice describes. <paramref name="value"/> is the item id, the shop id or the scenario
        /// path, by the kind; <paramref name="target"/> is the teleport of the map kind. Returns null for the kinds the
        /// pickers do not make (<see cref="RpgInteractKind.None"/> and <see cref="RpgInteractKind.Custom"/>).
        /// </summary>
        public static RpgInteract Make(RpgInteractKind kind, RpgInteractTrigger trigger, string value, int count, RpgTeleport target)
        {
            switch (kind)
            {
                case RpgInteractKind.Item:
                    return new RpgInteract { Trigger = trigger, Commands = new List<string> { ItemCommand(value, Math.Max(1, count)) } };
                case RpgInteractKind.Shop:
                    return new RpgInteract { Trigger = trigger, Commands = new List<string> { ShopCommand(value) } };
                case RpgInteractKind.Dialog:
                    return new RpgInteract { Trigger = trigger, Scenario = value };
                case RpgInteractKind.Map:
                    return new RpgInteract
                    {
                        Trigger = trigger,
                        Teleport = new RpgTeleport { Map = target?.Map, X = target?.X ?? 0, Y = target?.Y ?? 0, Dir = target?.Dir ?? RpgDirection.Down },
                    };
                default:
                    return null;
            }
        }

        /// <summary>
        /// The value of a placement's <c>Interact</c> that makes the game read <paramref name="wanted"/> on an object whose
        /// interaction is <paramref name="objectInteract"/> (null when the object has none). Null when nothing is wanted:
        /// the caller writes a JSON <c>null</c>, which removes the object's interaction for this placement.
        /// </summary>
        public static JsonNode OverrideOf(RpgInteract wanted, RpgInteract objectInteract)
        {
            if (wanted == null)
                return null;
            var node = new JsonObject { ["Trigger"] = wanted.Trigger.ToString() };
            if (!string.IsNullOrEmpty(wanted.Scenario))
                node["Scenario"] = wanted.Scenario;
            else if (objectInteract?.Scenario != null)
                node["Scenario"] = null;
            if (wanted.Commands is { Count: > 0 })
            {
                var lines = new JsonArray();
                foreach (var line in wanted.Commands)
                    lines.Add(JsonValue.Create(line));
                node["Commands"] = lines;
            }
            else if (objectInteract?.Commands != null)
                node["Commands"] = null;
            if (wanted.Teleport != null)
            {
                node["Teleport"] = new JsonObject
                {
                    ["Map"] = wanted.Teleport.Map,
                    ["X"] = wanted.Teleport.X,
                    ["Y"] = wanted.Teleport.Y,
                    ["Dir"] = wanted.Teleport.Dir.ToString(),
                };
            }
            else if (objectInteract?.Teleport != null)
                node["Teleport"] = null;
            return node;
        }

        /// <summary>
        /// Writes <paramref name="wanted"/> as the interaction of placement <paramref name="placementId"/> of the open document.
        /// <paramref name="objects"/> resolves an object id (null for an unknown one). Returns false when nothing changes or the
        /// placement is not in the document; then no undo step is made.
        /// </summary>
        public static bool Apply(RpgMapDocument document, string placementId, RpgInteract wanted, Func<string, RpgObject> objects)
        {
            var record = document?.Placements.FirstOrDefault(r => r.Id == placementId);
            if (record?.Data == null)
            {
                GameLogger.Log(Category, $"interaction of '{placementId}' not written: no such placement in this map", LogLevel.Warning);
                return false;
            }
            var objectInteract = objects?.Invoke(record.Data[ObjectKey]?.ToString())?.Interact;
            if (wanted == null && objectInteract == null && !record.Data.ContainsKey(InteractKey))
                return false;
            var written = OverrideOf(wanted, objectInteract);
            if (record.IsForeign && OwnerBlocks(record.Data, written))
            {
                GameLogger.Log(Category, $"interaction of '{placementId}' not written: the owner's placement sets fields a patch cannot drop or take back; change its file", LogLevel.Warning);
                return false;
            }
            return document.SetPlacementField(placementId, InteractKey, written);
        }

        /// <summary>
        /// True when a patch of a foreign placement cannot express the write: the owner's placement sets an interaction field that
        /// the write drops (a patch cannot drop a field a layer sets), or uses a list operation a patch cannot take back. The engine
        /// refuses both, so the write is refused before it starts.
        /// </summary>
        private static bool OwnerBlocks(JsonObject placement, JsonNode written)
        {
            if (placement[InteractKey] is not JsonObject owners)
                return false;
            if (owners.Any(pair => pair.Key.Contains('$')))
                return true;
            if (written == null)
                return true;
            if (written is not JsonObject fields)
                return false;
            return owners.Any(pair => pair.Value != null && (!fields.ContainsKey(pair.Key) || fields[pair.Key] == null));
        }

        private static string MatchOf(RpgInteract interact, Regex line, int group)
        {
            if (interact?.Commands is not { Count: 1 } commands)
                return null;
            var match = line.Match(commands[0]);
            return match.Success ? match.Groups[group].Value : null;
        }
    }
}
