namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>Map-owned event edits participate in the map's undo and patch transaction; no placement file is produced.</summary>
    public sealed partial class RpgMapDocument
    {
        /// <summary>Current events, including unsaved edits, in authored order.</summary>
        public IReadOnlyList<RpgMapEvent> Events => Data?["Events"] is JsonArray array
            ? array.OfType<JsonObject>().Select(ReadEvent).ToList() : new List<RpgMapEvent>();

        /// <summary>All identities the canvas can select: placements and independent events.</summary>
        public IEnumerable<string> SelectionIds => Placements.Where(r => !IsDeleted(r.Id)).Select(r => r.Id)
            .Concat(Events.Select(e => e.Scope(MapId)));

        /// <summary>An event by its full map/local identity, or null for a placement or a missing event.</summary>
        public RpgMapEvent EventOf(string id) => Events.FirstOrDefault(e => e.Scope(MapId) == id);

        /// <summary>All map records in the transaction, including unsaved edits to another map's incoming event link.</summary>
        internal IEnumerable<EntityRecord> MapRecords => maps.Records.Concat(maps.Foreign);

        /// <summary>Creates a Step event over a rectangle as one undo step. Returns its full stable identity.</summary>
        public string AddEvent(int x, int y, int width, int height)
        {
            if (!EventFits(x, y, width, height)) return null;
            var ids = new HashSet<string>(SelectionIds, StringComparer.OrdinalIgnoreCase);
            var local = "event";
            var index = 1;
            while (ids.Contains(MapId + "/" + local)) local = "event_" + index++;
            var entry = new JsonObject { ["Id"] = local, ["X"] = x, ["Y"] = y, ["Trigger"] = "Step" };
            if (width != 1) entry["Width"] = width;
            if (height != 1) entry["Height"] = height;
            var array = EventArray();
            array.Add(entry);
            return SetProperty("Events", array) ? MapId + "/" + local : null;
        }

        /// <summary>Replaces one event's editable data while preserving its identity and any unknown fields.</summary>
        public bool SetEvent(string id, JsonObject changes)
        {
            var array = EventArray();
            var entry = array.OfType<JsonObject>().FirstOrDefault(e => MapId + "/" + e["Id"]?.ToString() == id);
            if (entry == null || changes == null) return false;
            foreach (var pair in changes)
            {
                if (pair.Key == "Id") continue;
                if (pair.Value == null) entry.Remove(pair.Key);
                else entry[pair.Key] = pair.Value.DeepClone();
            }
            var value = ReadEvent(entry);
            return EventFits(value.X, value.Y, value.Width, value.Height) && SetProperty("Events", array);
        }

        private bool SetEventBounds(string id, int x, int y, int width, int height)
            => SetEvent(id, new JsonObject { ["X"] = x, ["Y"] = y, ["Width"] = width, ["Height"] = height });

        private bool RemoveEvents(IEnumerable<string> ids)
        {
            var wanted = new HashSet<string>(ids, StringComparer.Ordinal);
            var array = EventArray();
            var removed = array.OfType<JsonObject>().Where(e => wanted.Contains(MapId + "/" + e["Id"]?.ToString())).ToList();
            if (removed.Count == 0) return false;
            foreach (var entry in removed) array.Remove(entry);
            return SetProperty("Events", array);
        }

        private JsonArray EventArray() => Data?["Events"]?.DeepClone() as JsonArray ?? new JsonArray();

        private JsonObject EventNode(string id, out EntityRecord map)
        {
            var owner = MapOfPlacement(id);
            map = owner == null ? null : maps.FindAny(owner);
            return (map?.Data?["Events"] as JsonArray)?.OfType<JsonObject>()
                .FirstOrDefault(e => owner + "/" + e["Id"]?.ToString() == id);
        }
        private bool EventFits(int x, int y, int width, int height)
            => InBounds(x, y) && width > 0 && height > 0 && width <= Width - x && height <= Height - y;
        private static RpgMapEvent ReadEvent(JsonObject data)
            => JsonSerializer.Deserialize<RpgMapEvent>(data.ToJsonString(), JsonOptionsProvider.ReadOptions);
    }
}
