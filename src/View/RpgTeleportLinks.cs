namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// A teleport between maps as the editor lists it: the placement that carries it (its effective object's
    /// <c>Interact.Teleport</c>), the target map and cell, and why the target is broken (empty when it is fine).
    /// </summary>
    public sealed class RpgTeleportLink
    {
        /// <summary>Problem of a target: <c>map</c> (no such map), <c>cell</c> (off the map) or <c>solid</c> (a blocked cell); null when fine.</summary>
        public const string BrokenMap = "map";

        /// <summary>See <see cref="BrokenMap"/>.</summary>
        public const string BrokenCell = "cell";

        /// <summary>See <see cref="BrokenMap"/>.</summary>
        public const string BrokenSolid = "solid";

        /// <summary>Id of the placement that carries the teleport.</summary>
        public string Placement { get; set; }

        /// <summary>Cell of the carrying placement on its map (set for outgoing links; the arrow starts there).</summary>
        public int FromX { get; set; }

        /// <summary>Cell of the carrying placement on its map (set for outgoing links).</summary>
        public int FromY { get; set; }

        /// <summary>Map the teleport leads to (outgoing) or comes from (incoming).</summary>
        public string Map { get; set; }

        /// <summary>Target cell.</summary>
        public int X { get; set; }

        /// <summary>Target cell.</summary>
        public int Y { get; set; }

        /// <summary>See <see cref="BrokenMap"/>; null when the target is fine.</summary>
        public string Broken { get; set; }
    }

    /// <summary>
    /// The teleport links of one map: where its placements lead (outgoing) and which placements of other maps lead
    /// here (incoming). Pure C#: the maps and the placements come in as values, so the rule is testable without Godot.
    /// A broken target (no such map, a cell off its map, a solid cell) is flagged; the same check as <c>rpg_refs.py</c>
    /// in the audit.
    /// </summary>
    public static class RpgTeleportLinks
    {
        /// <summary>
        /// Teleports that leave <paramref name="mapId"/>: <paramref name="placements"/> are the map's own placements (as the
        /// game reads them), <paramref name="objects"/> resolves object ids, <paramref name="maps"/> holds every map by id.
        /// </summary>
        public static List<RpgTeleportLink> Outgoing(string mapId, IEnumerable<RpgPlacement> placements, RpgMap map,
            Func<string, RpgObject> objects, IReadOnlyDictionary<string, RpgMap> maps)
        {
            var result = new List<RpgTeleportLink>();
            foreach (var placement in placements.Concat((map.Events ?? new List<RpgMapEvent>()).Select(e => e.AsPlacement(mapId))))
            {
                var teleport = TeleportOf(placement, map, objects);
                if (teleport == null)
                    continue;
                result.Add(new RpgTeleportLink
                {
                    Placement = placement.ID,
                    FromX = placement.X,
                    FromY = placement.Y,
                    Map = teleport.Map,
                    X = teleport.X,
                    Y = teleport.Y,
                    Broken = BrokenOf(teleport, maps)
                });
            }
            return result;
        }

        /// <summary>
        /// Teleports that lead into <paramref name="mapId"/> from the placements of other maps: <paramref name="others"/> are
        /// every placement of the other maps with the map it belongs to.
        /// </summary>
        public static List<RpgTeleportLink> Incoming(string mapId, IEnumerable<(RpgPlacement Placement, RpgMap Map)> others, Func<string, RpgObject> objects)
        {
            var result = new List<RpgTeleportLink>();
            foreach (var (placement, map) in others)
            {
                var teleport = TeleportOf(placement, map, objects);
                if (teleport == null || !string.Equals(teleport.Map, mapId, StringComparison.OrdinalIgnoreCase))
                    continue;
                result.Add(new RpgTeleportLink { Placement = placement.ID, Map = map?.ID, X = teleport.X, Y = teleport.Y });
            }
            return result;
        }

        /// <summary>The problem of a target: null when the map exists, the cell is on it and walkable by its collision rows.</summary>
        public static string BrokenOf(RpgTeleport target, IReadOnlyDictionary<string, RpgMap> maps)
        {
            if (target == null || string.IsNullOrEmpty(target.Map) || maps == null)
                return RpgTeleportLink.BrokenMap;
            var found = maps.TryGetValue(target.Map, out var map)
                ? map
                : maps.FirstOrDefault(p => string.Equals(p.Key, target.Map, StringComparison.OrdinalIgnoreCase)).Value;
            if (found == null)
                return RpgTeleportLink.BrokenMap;
            if (target.X < 0 || target.Y < 0 || target.X >= found.Width || target.Y >= found.Height)
                return RpgTeleportLink.BrokenCell;
            return CollisionAt(found, target.X, target.Y) == '#' ? RpgTeleportLink.BrokenSolid : null;
        }

        private static RpgTeleport TeleportOf(RpgPlacement placement, RpgMap map, Func<string, RpgObject> objects)
        {
            var mapEvent = map?.Events?.FirstOrDefault(e => e.Scope(map.ID) == placement?.ID);
            if (mapEvent != null) return mapEvent.Teleport;
            if (placement == null || string.IsNullOrEmpty(placement.Object))
                return null;
            var resolved = objects?.Invoke(placement.Object);
            if (resolved == null)
                return null;
            return RpgEditorScene.Effective(resolved, map, placement)?.Interact?.Teleport;
        }

        private static char CollisionAt(RpgMap map, int x, int y)
        {
            var rows = map.Collision;
            if (rows == null || y < 0 || y >= rows.Count || rows[y] == null || x < 0 || x >= rows[y].Length)
                return RpgCollision.Auto;
            return rows[y][x];
        }
    }
}
