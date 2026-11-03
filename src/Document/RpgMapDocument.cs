namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.EntityEditor;

    /// <summary>
    /// The editor's document of one RPG map (plan 12.4), without any UI: the map's grids, its placements and their
    /// strokes, undo and redo, and what a save writes. Built on the data documents of <c>tools_base</c>
    /// (<see cref="EntityDocument"/>), so own files keep their bytes and foreign entities are edited as patches of the
    /// edited mod.
    /// <para/>
    /// <b>Own and foreign.</b> A map or placement the edited mod's own files define is edited in place. A map or a
    /// placement of a dependency is edited as a patch: a painted cell becomes a stroke in <c>Strokes$add</c>, a moved
    /// placement a patch of its <c>X</c>/<c>Y</c>, a deleted one <c>$delete</c>. The owner's files are never written.
    /// A package does not place new placements on a map of another mod (plan 7), so <see cref="AddPlacement"/> refuses there.
    /// <para/>
    /// <b>Legend.</b> Painting a surface or a segment on an own map writes its cell in <c>Ground</c> or <c>Structure</c>
    /// and gives the object a legend symbol (reused or new, see <see cref="RpgLegendSymbols"/>). Symbols no grid uses
    /// any more are dropped by <see cref="Changes"/>, the save.
    /// <para/>
    /// <b>Undo.</b> Every change is one step on both data documents, so the map and the placements step back together.
    /// A change that would change nothing is refused before it begins and leaves no step.
    /// <para/>
    /// Placement ids are global (<c>&lt;map&gt;/&lt;local id&gt;</c>); a placement belongs to the map named before the
    /// first <c>/</c> (<see cref="MapOfPlacement"/>), as in the runtime.
    /// </summary>
    public sealed partial class RpgMapDocument
    {
        private const string Category = "RPG_EDITOR";
        private const string MapKind = "rpg_map";
        private const string PlacementKind = "rpg_placement";

        private readonly EntityDocument maps;
        private readonly EntityDocument placements;
        private readonly Func<string, RpgObjectForm?> formOf;

        private RpgMapDocument(string mapId, string ownMod, EntityDocument maps, EntityDocument placements, Func<string, RpgObjectForm?> formOf, IReadOnlyList<string> skipped)
        {
            MapId = mapId;
            OwnMod = ownMod;
            this.maps = maps;
            this.placements = placements;
            this.formOf = formOf;
            SkippedFiles = skipped;
        }

        /// <summary>Id of the edited map (its scene name).</summary>
        public string MapId { get; }

        /// <summary>The edited mod: the owner of the patch files and of the stroke ids.</summary>
        public string OwnMod { get; }

        /// <summary>Own files that declare the kind but do not read as JSON; they are not part of the document.</summary>
        public IReadOnlyList<string> SkippedFiles { get; }

        /// <summary>The map's record: own, or foreign (edited as a patch). Null when it disappeared.</summary>
        public EntityRecord Map => maps.FindAny(MapId);

        /// <summary>True when another mod defines the map; its grids then change only through strokes.</summary>
        public bool IsForeign => Map?.IsForeign ?? false;

        /// <summary>Columns of the map.</summary>
        public int Width => IntOf(Data, "Width");

        /// <summary>Rows of the map.</summary>
        public int Height => IntOf(Data, "Height");

        /// <summary>The legend: symbol → object id, in file order.</summary>
        public IReadOnlyDictionary<string, string> Legend
        {
            get
            {
                var result = new Dictionary<string, string>(StringComparer.Ordinal);
                if (Data?["Legend"] is JsonObject legend)
                {
                    foreach (var pair in legend)
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue(out string id))
                            result[pair.Key] = id;
                    }
                }
                return result;
            }
        }

        /// <summary>The placements of this map: own ones first (file order), then foreign ones.</summary>
        public IReadOnlyList<EntityRecord> Placements
            => placements.Records.Where(r => MapOfPlacement(r.Id) == MapId)
                .Concat(placements.Foreign.Where(r => MapOfPlacement(r.Id) == MapId))
                .ToList();

        /// <summary>
        /// Every placement of the mod's maps that the document can change, of every map (plan 14.G18): the own ones and the
        /// dependencies' ones (as their data stands now, an unsaved change included); a deleted foreign placement is left out.
        /// </summary>
        public IEnumerable<EntityRecord> PlacementRecords
            => placements.Records.Concat(placements.Foreign.Where(r => !placements.IsDeleted(r)));

        /// <summary>The placement with this id on any map of the mod (own or a dependency's), or null when there is none or it is deleted.</summary>
        public EntityRecord AnyPlacement(string id)
        {
            var record = placements.Find(id) ?? placements.FindForeign(id);
            return record != null && !(record.IsForeign && placements.IsDeleted(record)) ? record : null;
        }

        /// <summary>True when the placement with this id (of any map of the mod) is a dependency's that the edited mod deletes.</summary>
        public bool IsDeletedAny(string id)
        {
            var record = placements.FindForeign(id);
            return record != null && placements.IsDeleted(record);
        }

        /// <summary>True when the placement (of any map) has its own <c>Interact.Teleport</c>; a teleport from its object has none.</summary>
        public bool HasOwnTeleport(string id)
        {
            if (EventNode(id, out _) is { } entry) return entry["Teleport"] is JsonObject;
            var record = AnyPlacement(id);
            return record?.Data?["Interact"] is JsonObject interact && interact["Teleport"] is JsonObject;
        }

        /// <summary>True when there is a step to undo.</summary>
        public bool CanUndo => maps.CanUndo;

        /// <summary>True when there is a step to redo.</summary>
        public bool CanRedo => maps.CanRedo;

        /// <summary>
        /// Opens the document of <paramref name="mapId"/>. <paramref name="files"/> are every JSON file of the edited mod
        /// (path, text), including its patch files; each data document takes its own kind and skips the rest. The foreign
        /// entities are those of the mod's dependencies (<see cref="ForeignEntities.Scan"/> in the game): their written
        /// values. <paramref name="baseOf"/> gives an entity's value as the game reads it without the edited mod's patch
        /// (kind, id; normalised as <see cref="EntityJson.Normalize"/> does, see <see cref="ForeignEntities.BaseWithout"/>).
        /// <paramref name="formOf"/> gives an object's form, null for an unknown object. Returns null (and logs) when the
        /// map is neither own nor foreign.
        /// </summary>
        public static RpgMapDocument Open(string mapId, string ownMod, IEnumerable<(string Path, string Text)> files,
            IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> foreignMaps,
            IEnumerable<(string Id, string OwnerMod, string File, JsonObject Written)> foreignPlacements,
            Func<string, string, JsonObject> baseOf, Func<string, RpgObjectForm?> formOf)
        {
            var sources = files.ToList();
            var mapDoc = EntityDocument.Load(MapKind, "maps/" + mapId + ".json", sources, out var skippedMaps);
            var placementDoc = EntityDocument.Load(PlacementKind, "placements/" + mapId + "/" + mapId + ".json", sources, out var skippedPlacements);
            mapDoc.AttachForeign(ForeignEntities.PatchFile(MapKind, ownMod), TextOf(sources, ForeignEntities.PatchFile(MapKind, ownMod)),
                typeof(RpgMap), foreignMaps, id => baseOf(MapKind, id));
            placementDoc.AttachForeign(ForeignEntities.PatchFile(PlacementKind, ownMod), TextOf(sources, ForeignEntities.PatchFile(PlacementKind, ownMod)),
                typeof(RpgPlacement), foreignPlacements, id => baseOf(PlacementKind, id));

            var document = new RpgMapDocument(mapId, ownMod, mapDoc, placementDoc, formOf, skippedMaps.Concat(skippedPlacements).ToList());
            if (document.Data == null)
            {
                GameLogger.Log(Category, $"map '{mapId}' is neither in mod '{ownMod}' nor in its dependencies", LogLevel.Error);
                return null;
            }
            return document;
        }

        /// <summary>The map id a placement id belongs to: the text before the first <c>/</c>; null without one.</summary>
        public static string MapOfPlacement(string id)
        {
            var slash = id?.IndexOf('/') ?? -1;
            return slash > 0 ? id.Substring(0, slash) : null;
        }

        /// <summary>
        /// The object on a cell of a surface or segment grid: the stroke that covers it last, else the legend symbol of the
        /// row. Null for an empty cell or outside the map.
        /// </summary>
        public string ObjectAt(RpgStrokeLayer layer, int x, int y)
        {
            var data = Data;
            if (data == null || !InBounds(x, y) || layer == RpgStrokeLayer.Collision)
                return null;
            var key = layer.ToString();
            var symbol = RpgRows.CharAt(data, key, x, y);
            string value = null;
            if (symbol != RpgRows.Empty && data["Legend"] is JsonObject legend && legend[symbol.ToString()] is JsonValue entry && entry.TryGetValue(out string id))
                value = id;
            if (RpgStrokeBook.TryRead(data, key, x, y, out var painted))
                value = painted;
            return value;
        }

        /// <summary>The collision code of a cell (<c>.</c>, <c>#</c>, <c>o</c>) after the strokes; <c>#</c> outside the map, as in the runtime.</summary>
        public char CodeAt(int x, int y)
        {
            var data = Data;
            if (data == null || !InBounds(x, y))
                return RpgCollision.Block;
            if (RpgStrokeBook.TryRead(data, RpgStrokeBook.CollisionLayer, x, y, out var code) && code.Length == 1)
                return code[0];
            return RpgRows.CharAt(data, RpgStrokeBook.CollisionLayer, x, y);
        }

        /// <summary>
        /// Paints <paramref name="objectId"/> (null clears) over the cells of a surface (<c>Ground</c>) or segment
        /// (<c>Structure</c>) grid. Cells outside the map and cells that already show the object are skipped; when
        /// nothing would change, nothing is done and false is returned. The object must have the layer's form.
        /// </summary>
        public bool Paint(RpgStrokeLayer layer, IEnumerable<(int X, int Y)> cells, string objectId)
        {
            if (layer == RpgStrokeLayer.Collision || !FormFits(layer, objectId))
                return false;
            var changed = Changed(cells, c => !string.Equals(ObjectAt(layer, c.X, c.Y), objectId, StringComparison.Ordinal));
            if (changed.Count == 0)
                return false;
            if (IsForeign)
            {
                Begin();
                var map = Map;
                RpgStrokeBook.Add(map.Data, RpgStrokeBook.NewId(map.Data, OwnMod), layer.ToString(), objectId, changed);
                maps.CommitForeign(map);
                return true;
            }

            var legend = Data["Legend"] as JsonObject ?? new JsonObject();
            if (objectId != null && (RpgLegendSymbols.SymbolOf(legend, objectId) ?? RpgLegendSymbols.FreeSymbol(legend)) == null)
            {
                Refuse($"the legend of map '{MapId}' is full, no symbol is left for '{objectId}'");
                return false;
            }
            Begin();
            var symbol = objectId == null ? RpgRows.Empty : RpgLegendSymbols.Assign(RpgLegendSymbols.Ensure(Data), objectId).Value;
            var rows = RpgRows.Ensure(Data, layer.ToString(), Width, Height);
            foreach (var cell in changed)
                RpgRows.Set(rows, Width, cell.X, cell.Y, symbol);
            return true;
        }

        /// <summary>Writes a collision code (<c>.</c>, <c>#</c> or <c>o</c>) over cells; an own map writes its rows, a foreign one a stroke.</summary>
        public bool PaintCollision(IEnumerable<(int X, int Y)> cells, char code)
        {
            if (code != RpgCollision.Auto && code != RpgCollision.Block && code != RpgCollision.Open)
            {
                Refuse($"collision code '{code}' is not '.', '#' or 'o'");
                return false;
            }
            var changed = Changed(cells, c => CodeAt(c.X, c.Y) != code);
            if (changed.Count == 0)
                return false;
            Begin();
            if (IsForeign)
            {
                var map = Map;
                RpgStrokeBook.Add(map.Data, RpgStrokeBook.NewId(map.Data, OwnMod), RpgStrokeBook.CollisionLayer, code.ToString(), changed);
                maps.CommitForeign(map);
                return true;
            }
            var rows = RpgRows.Ensure(Data, RpgStrokeBook.CollisionLayer, Width, Height);
            foreach (var cell in changed)
                RpgRows.Set(rows, Width, cell.X, cell.Y, code);
            return true;
        }

        /// <summary>
        /// Adds a placement of <paramref name="objectId"/> (a sprite or an actor) at (<paramref name="x"/>, <paramref name="y"/>)
        /// with the local id <paramref name="localId"/>; the id is made unique. Returns the new id, or null when refused
        /// (a foreign map, a bad object, a cell off the map, an empty or nested local id).
        /// </summary>
        public string AddPlacement(string localId, string objectId, int x, int y)
        {
            if (IsForeign)
            {
                Refuse($"placements are not added to the map of another mod '{MapId}'");
                return null;
            }
            if (string.IsNullOrWhiteSpace(localId) || localId.Contains('/'))
            {
                Refuse($"local id '{localId}' is empty or has '/'");
                return null;
            }
            var form = objectId == null ? null : formOf?.Invoke(objectId);
            if (form is not (RpgObjectForm.Sprite or RpgObjectForm.Actor))
            {
                Refuse($"object '{objectId}' is not a sprite or an actor");
                return null;
            }
            if (!InBounds(x, y))
            {
                Refuse($"cell {x},{y} is off the map '{MapId}'");
                return null;
            }

            Begin();
            var id = placements.FreeId(MapId + "/" + localId);
            while (Events.Any(e => e.Scope(MapId).Equals(id, StringComparison.OrdinalIgnoreCase)))
                id = placements.FreeId(id + "_object");
            placements.Add(new JsonObject { ["Id"] = id, ["Object"] = objectId, ["X"] = x, ["Y"] = y });
            return id;
        }

        /// <summary>Moves a placement of this map to (<paramref name="x"/>, <paramref name="y"/>); a foreign one becomes its patch.</summary>
        public bool MovePlacement(string id, int x, int y)
        {
            if (EventOf(id) is { } mapEvent)
                return SetEventBounds(id, x, y, mapEvent.Width, mapEvent.Height);
            var record = PlacementOf(id);
            if (record?.Data == null || !InBounds(x, y))
                return false;
            if (IntOf(record.Data, "X") == x && IntOf(record.Data, "Y") == y)
                return false;
            Begin();
            record.Data["X"] = x;
            record.Data["Y"] = y;
            if (record.IsForeign)
                placements.CommitForeign(record);
            return true;
        }

        /// <summary>Changes an event's origin and footprint together as one undo step; foreign placements become patches.</summary>
        public bool SetPlacementBounds(string id, int x, int y, int width, int height)
        {
            if (EventOf(id) != null) return SetEventBounds(id, x, y, width, height);
            var record = PlacementOf(id);
            if (record?.Data == null || width < 1 || height < 1 || !InBounds(x, y)
                || width > Width - x || height > Height - y)
                return false;
            var footprint = record.Data["Footprint"] as JsonObject;
            var size = footprint?["Size"] as JsonArray;
            if (IntOf(record.Data, "X") == x && IntOf(record.Data, "Y") == y
                && size?.Count == 2 && size[0]?.GetValue<int>() == width && size[1]?.GetValue<int>() == height)
                return false;
            Begin();
            record.Data["X"] = x;
            record.Data["Y"] = y;
            footprint ??= new JsonObject();
            if (record.Data["Footprint"] == null) record.Data["Footprint"] = footprint;
            footprint["Size"] = new JsonArray(width, height);
            if (record.IsForeign) placements.CommitForeign(record);
            return true;
        }

        /// <summary>
        /// Sets one field of a placement of this map (<c>Interact</c>, <c>Variant</c>…) to <paramref name="value"/>; null writes a
        /// JSON <c>null</c>, which the game reads as "remove this override". A foreign placement gets the change as its patch.
        /// Returns false when the field already reads so; then no step is made. Used by <see cref="RpgInteractEdit"/>.
        /// </summary>
        public bool SetPlacementField(string id, string key, JsonNode value)
        {
            var record = PlacementOf(id);
            if (record?.Data == null || string.IsNullOrEmpty(key))
                return false;
            if (record.Data.ContainsKey(key) && Text(record.Data[key]) == Text(value))
                return false;
            Begin();
            record.Data[key] = value == null ? null : JsonNode.Parse(value.ToJsonString());
            if (record.IsForeign)
                placements.CommitForeign(record);
            return true;
        }

        /// <summary>
        /// Writes the target cell of the teleport of a placement of any map of the mod (plan 14.G18): <c>Interact.Teleport.X/Y</c>
        /// of the placement. An own placement changes in its file, a foreign one as its patch. Used for the target of a teleport
        /// on this map (its arrowhead) and for the arrival cell of a teleport from another map (the source placement). Returns
        /// false when the placement has no teleport of its own (see <see cref="HasOwnTeleport"/>), is not in the document, or
        /// already has the cell; then no step is made.
        /// </summary>
        public bool SetTeleportCell(string placementId, int x, int y)
        {
            if (EventNode(placementId, out var eventMap)?["Teleport"] is JsonObject eventTarget)
            {
                if (IntOf(eventTarget, "X") == x && IntOf(eventTarget, "Y") == y) return false;
                Begin();
                eventTarget["X"] = x;
                eventTarget["Y"] = y;
                if (eventMap.IsForeign) maps.CommitForeign(eventMap);
                return true;
            }
            var record = AnyPlacement(placementId);
            if (record?.Data == null || !HasOwnTeleport(placementId))
                return false;
            var teleport = (JsonObject)record.Data["Interact"]["Teleport"];
            if (IntOf(teleport, "X") == x && IntOf(teleport, "Y") == y)
                return false;
            Begin();
            teleport["X"] = x;
            teleport["Y"] = y;
            if (record.IsForeign)
                placements.CommitForeign(record);
            return true;
        }

        /// <summary>Deletes a placement of this map: an own one is removed, a foreign one gets <c>"$delete": true</c>.</summary>
        public bool DeletePlacement(string id)
        {
            if (EventOf(id) != null) return RemoveEvents(new[] { id });
            var record = PlacementOf(id);
            if (record == null || (record.IsForeign && placements.IsDeleted(record)))
                return false;
            Begin();
            if (record.IsForeign)
                placements.DeleteForeign(record);
            else
                placements.Remove(record);
            return true;
        }

        /// <summary>True when the placement is another mod's and the edited mod patches it (its patch entry exists).</summary>
        public bool IsPatched(string id)
        {
            var record = PlacementOf(id);
            return record != null && record.IsForeign && placements.PatchOf(record.Id) != null;
        }

        /// <summary>True when the placement is another mod's and the edited mod deletes it (<c>"$delete": true</c>).</summary>
        public bool IsDeleted(string id)
        {
            var record = PlacementOf(id);
            return record != null && placements.IsDeleted(record);
        }

        /// <summary>Drops the edited mod's patch of a foreign placement: the game sees the owner's placement again.</summary>
        public bool ResetPlacement(string id)
        {
            var record = PlacementOf(id);
            if (record == null || !record.IsForeign || placements.PatchOf(record.Id) == null)
                return false;
            Begin();
            placements.ResetForeign(record);
            return true;
        }

        /// <summary>Steps back one change on the map and the placements; false when there is nothing to undo.</summary>
        public bool Undo()
        {
            var undone = maps.Undo();
            placements.Undo();
            return undone;
        }

        /// <summary>Steps forward again; false when there is nothing to redo.</summary>
        public bool Redo()
        {
            var redone = maps.Redo();
            placements.Redo();
            return redone;
        }

        /// <summary>
        /// Sets one top-level property of the map (<c>Variant</c>, <c>Atmosphere</c>, <c>Encounters</c>…); null removes it.
        /// A foreign map gets the change as its patch. Returns false when the value would not change.
        /// </summary>
        public bool SetProperty(string key, JsonNode value)
        {
            var map = Map;
            if (map?.Data == null || string.IsNullOrEmpty(key) || Text(map.Data[key]) == Text(value))
                return false;
            Begin();
            if (value == null)
                map.Data.Remove(key);
            else
                map.Data[key] = JsonNode.Parse(value.ToJsonString());
            if (map.IsForeign)
                maps.CommitForeign(map);
            return true;
        }

        /// <summary>Puts the map's spawn point on a cell of the map, keeping its direction; false when the cell is off the map or unchanged.</summary>
        public bool SetSpawn(int x, int y)
        {
            if (!InBounds(x, y))
                return false;
            var current = Data?["Spawn"] as JsonObject;
            var next = new JsonObject
            {
                ["X"] = x,
                ["Y"] = y,
                ["Dir"] = current?["Dir"]?.ToString() ?? "Down"
            };
            return SetProperty("Spawn", next);
        }

        /// <summary>
        /// The clips of placements of this map (own or foreign, not deleted) for a copy, in the order of <paramref name="ids"/>;
        /// ids of other maps, deleted and unknown ids are left out. Other fields are kept, <c>$</c> markers are not.
        /// </summary>
        public List<RpgPlacementClip> ClipOf(IEnumerable<string> ids)
        {
            var clips = new List<RpgPlacementClip>();
            foreach (var id in ids ?? Enumerable.Empty<string>())
            {
                if (EventNode(id, out _) is { } entry && EventOf(id) is { } mapEvent)
                {
                    clips.Add(new RpgPlacementClip { LocalId = mapEvent.Id, X = mapEvent.X, Y = mapEvent.Y,
                        EventData = (JsonObject)entry.DeepClone() });
                    continue;
                }
                var record = PlacementOf(id);
                if (record?.Data == null || IsDeleted(record.Id))
                    continue;
                var fields = new JsonObject();
                foreach (var pair in record.Data)
                {
                    if (pair.Value == null || pair.Key is "Id" or "Object" or "X" or "Y" || pair.Key.StartsWith("$", StringComparison.Ordinal))
                        continue;
                    fields[pair.Key] = pair.Value.DeepClone();
                }
                var slash = record.Id.IndexOf('/');
                clips.Add(new RpgPlacementClip
                {
                    LocalId = slash >= 0 ? record.Id.Substring(slash + 1) : record.Id,
                    Object = record.Data["Object"] is JsonValue value && value.TryGetValue(out string name) ? name : null,
                    X = IntOf(record.Data, "X"),
                    Y = IntOf(record.Data, "Y"),
                    Fields = fields,
                });
            }
            return clips;
        }

        /// <summary>
        /// Pastes copied placements on this map with the first clip's cell on (<paramref name="anchorX"/>, <paramref name="anchorY"/>),
        /// the others keeping their offsets. Each new placement gets a free id of this map, so a paste on the same map makes
        /// copies. One step: undo removes them all. Refused (null, nothing added) on a foreign map, when a clip would land off the
        /// map, or when a clip's object is not a sprite or an actor. Returns the new ids.
        /// </summary>
        public List<string> Paste(IReadOnlyList<RpgPlacementClip> clips, int anchorX, int anchorY)
        {
            if (clips == null || clips.Count == 0)
                return null;
            if (IsForeign)
            {
                Refuse($"placements are not pasted on the map of another mod '{MapId}'");
                return null;
            }
            var minX = clips.Min(c => c.X);
            var minY = clips.Min(c => c.Y);
            foreach (var clip in clips)
            {
                var x = anchorX + clip.X - minX;
                var y = anchorY + clip.Y - minY;
                if (!InBounds(x, y))
                {
                    Refuse($"paste does not fit: cell {x},{y} is off the map '{MapId}'");
                    return null;
                }
                if (clip.EventData != null)
                {
                    var entry = ReadEvent(clip.EventData);
                    if (!EventFits(x, y, entry.Width, entry.Height)) return null;
                    continue;
                }
                var form = clip.Object == null ? null : formOf?.Invoke(clip.Object);
                if (form is not (RpgObjectForm.Sprite or RpgObjectForm.Actor))
                {
                    Refuse($"paste refused: object '{clip.Object}' is not a sprite or an actor");
                    return null;
                }
            }

            Begin();
            var ids = new List<string>();
            var occupied = new HashSet<string>(SelectionIds, StringComparer.OrdinalIgnoreCase);
            foreach (var clip in clips)
            {
                var local = string.IsNullOrWhiteSpace(clip.LocalId) || clip.LocalId.Contains('/') ? "copy" : clip.LocalId;
                var id = placements.FreeId(MapId + "/" + local);
                var suffix = 1;
                while (occupied.Contains(id)) id = MapId + "/" + local + "_" + suffix++;
                occupied.Add(id);
                if (clip.EventData != null)
                {
                    var entry = (JsonObject)clip.EventData.DeepClone();
                    entry["Id"] = id.Substring(MapId.Length + 1);
                    entry["X"] = anchorX + clip.X - minX;
                    entry["Y"] = anchorY + clip.Y - minY;
                    if (Data["Events"] is not JsonArray events) { events = new JsonArray(); Data["Events"] = events; }
                    events.Add(entry);
                    ids.Add(id);
                    continue;
                }
                var entity = new JsonObject
                {
                    ["Id"] = id,
                    ["Object"] = clip.Object,
                    ["X"] = anchorX + clip.X - minX,
                    ["Y"] = anchorY + clip.Y - minY,
                };
                foreach (var pair in clip.Fields)
                    entity[pair.Key] = pair.Value?.DeepClone();
                placements.Add(entity);
                ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// Deletes several placements of this map in one step (an own one is removed, a foreign one gets <c>"$delete": true</c>).
        /// Ids of other maps and already deleted ones are skipped; false when none is left to delete.
        /// </summary>
        public bool DeletePlacements(IEnumerable<string> ids)
        {
            var targets = (ids ?? Enumerable.Empty<string>()).Distinct().ToList();
            if (!targets.Any(id => EventOf(id) != null || PlacementOf(id) is { } r && !(r.IsForeign && placements.IsDeleted(r))))
                return false;
            Begin();
            // Change the map data inside the same transaction as the placement removals.
            if (Data?["Events"] is JsonArray events)
                foreach (var entry in events.OfType<JsonObject>().Where(e => targets.Contains(MapId + "/" + e["Id"]?.ToString())).ToList())
                    events.Remove(entry);
            if (IsForeign) maps.CommitForeign(Map);
            foreach (var id in targets)
            {
                var record = PlacementOf(id);
                if (record == null || (record.IsForeign && placements.IsDeleted(record)))
                    continue;
                if (record.IsForeign)
                    placements.DeleteForeign(record);
                else
                    placements.Remove(record);
            }
            return true;
        }

        /// <summary>
        /// Paints (or, with <paramref name="erase"/>, erases) cells of the effect mask of <paramref name="profile"/>: the map's
        /// <c>Effects</c> entry of that profile gets the cells, made on the first paint and dropped when its last cell is erased.
        /// A whole-map entry (no <c>Cells</c>) already covers every cell, so painting it changes nothing; erasing from it first
        /// makes its cells explicit. Cells off the map are skipped. One step; false when nothing would change.
        /// </summary>
        public bool PaintEffect(string profile, IEnumerable<(int X, int Y)> cells, bool erase)
        {
            if (string.IsNullOrEmpty(profile) || Data == null)
                return false;
            var list = (Data["Effects"] as JsonArray)?.OfType<JsonObject>().Select(e => (JsonObject)e.DeepClone()).ToList() ?? new List<JsonObject>();
            var index = list.FindIndex(e => e["Profile"] is JsonValue p && p.TryGetValue(out string name) && name == profile);
            var wholeMap = index >= 0 && list[index]["Cells"] == null;
            if (wholeMap && !erase)
                return false;

            var set = wholeMap ? AllCells() : index >= 0 ? CellsOf(list[index]) : new List<(int X, int Y)>();
            var target = new HashSet<(int X, int Y)>(cells.Where(c => InBounds(c.X, c.Y)));
            var changed = false;
            if (erase)
            {
                changed = set.RemoveAll(target.Contains) > 0;
            }
            else
            {
                foreach (var cell in target.Where(c => !set.Contains(c)).ToList())
                {
                    set.Add(cell);
                    changed = true;
                }
            }
            if (!changed)
                return false;

            if (set.Count == 0)
            {
                list.RemoveAt(index);
            }
            else
            {
                var entry = index >= 0 ? list[index] : new JsonObject { ["Profile"] = profile };
                entry["Cells"] = new JsonArray(set.Select(c => (JsonNode)new JsonArray { c.X, c.Y }).ToArray());
                if (index < 0)
                    list.Add(entry);
            }
            return SetProperty("Effects", new JsonArray(list.Select(e => (JsonNode)e).ToArray()));
        }

        /// <summary>The explicit cells of an effect entry (<c>Cells</c> as <c>[x, y]</c> pairs), in file order.</summary>
        private static List<(int X, int Y)> CellsOf(JsonObject entry)
        {
            var result = new List<(int X, int Y)>();
            if (entry["Cells"] is not JsonArray cells)
                return result;
            foreach (var pair in cells)
            {
                if (pair is JsonArray xy && xy.Count >= 2 && xy[0] is JsonValue x && xy[1] is JsonValue y
                    && x.TryGetValue(out int cx) && y.TryGetValue(out int cy) && !result.Contains((cx, cy)))
                    result.Add((cx, cy));
            }
            return result;
        }

        private List<(int X, int Y)> AllCells()
        {
            var result = new List<(int X, int Y)>();
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++)
                    result.Add((x, y));
            }
            return result;
        }

        /// <summary>
        /// What a save writes: the changed files and the changed patch files (a null text removes a patch file that lost its
        /// last entry). Drops the own map's legend symbols that no grid uses first, so the file has only what it draws.
        /// </summary>
        public List<(string Path, string Text)> Changes()
        {
            DropUnusedSymbols();
            var result = new List<(string Path, string Text)>();
            result.AddRange(maps.ChangedFiles());
            result.AddRange(placements.ChangedFiles());
            if (maps.ChangedPatch() is { } mapPatch)
                result.Add(mapPatch);
            if (placements.ChangedPatch() is { } placementPatch)
                result.Add(placementPatch);
            return result;
        }

        /// <summary>The current state is what is on disk: call after the files of <see cref="Changes"/> are written.</summary>
        public void MarkSaved()
        {
            maps.MarkSaved();
            placements.MarkSaved();
        }

        /// <summary>The problems that would break the game's data (duplicate or empty ids), as in <see cref="EntityDocument.Validate"/>.</summary>
        public List<(string Key, string Argument)> Validate()
        {
            var result = maps.Validate().Concat(placements.Validate()).ToList();
            var ids = new HashSet<string>(Placements.Where(r => !IsDeleted(r.Id)).Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Events)
                if (string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Contains('/') || !ids.Add(entry.Scope(MapId))
                    || !EventFits(entry.X, entry.Y, entry.Width, entry.Height))
                    result.Add(("tools/rpg_editor/event_invalid", entry.Id ?? "?"));
            return result;
        }

        /// <summary>The edited map's data (the item as the game reads it, for a foreign map too); null when the map is gone.</summary>
        public JsonObject Data => Map?.Data;

        private EntityRecord PlacementOf(string id)
        {
            if (id == null)
                return null;
            var record = placements.Find(id) ?? placements.FindForeign(id);
            return record != null && MapOfPlacement(record.Id) == MapId ? record : null;
        }

        private void Begin()
        {
            maps.BeginChange();
            placements.BeginChange();
        }

        private void DropUnusedSymbols()
        {
            if (IsForeign || Data == null || Data["Legend"] is not JsonObject)
                return;
            var used = RpgRows.CharsOf(Data, RpgStrokeLayer.Ground.ToString());
            used.UnionWith(RpgRows.CharsOf(Data, RpgStrokeLayer.Structure.ToString()));
            var removed = RpgLegendSymbols.DropUnused(RpgLegendSymbols.Ensure(Data), used);
            foreach (var symbol in removed)
                GameLogger.Log(Category, $"map '{MapId}': legend symbol '{symbol}' is not used by any grid and is dropped", LogLevel.Message);
        }

        private bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        private bool FormFits(RpgStrokeLayer layer, string objectId)
        {
            if (objectId == null)
                return true;
            var form = formOf?.Invoke(objectId);
            var wanted = layer == RpgStrokeLayer.Ground ? RpgObjectForm.Surface : RpgObjectForm.Segment;
            if (form == null)
            {
                Refuse($"object '{objectId}' is unknown");
                return false;
            }
            if (form != wanted)
            {
                Refuse($"object '{objectId}' is a {form.ToString().ToLowerInvariant()}, but {layer} takes {wanted.ToString().ToLowerInvariant()} objects");
                return false;
            }
            return true;
        }

        private List<(int X, int Y)> Changed(IEnumerable<(int X, int Y)> cells, Func<(int X, int Y), bool> differs)
            => cells.Where(c => InBounds(c.X, c.Y)).Distinct().Where(differs).ToList();

        private static void Refuse(string reason)
            => GameLogger.Log(Category, $"refused: {reason}", LogLevel.Warning);

        private static string Text(JsonNode node) => node?.ToJsonString() ?? "null";

        private static int IntOf(JsonObject data, string key)
            => data?[key] is JsonValue value && value.TryGetValue(out int number) ? number : 0;

        private static string TextOf(IEnumerable<(string Path, string Text)> files, string path)
            => files.FirstOrDefault(f => f.Path == path).Text;
    }
}
