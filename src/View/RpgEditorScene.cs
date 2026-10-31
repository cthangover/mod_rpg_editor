namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The map of the document as the game reads it, without any UI: the map's grids, the placements with their
    /// effective objects (variant, overrides and the state of an object that is not dynamic), the walking grid the
    /// ghost preview checks against, and the placements that draw as actors. The canvas hands the statics and the
    /// actors to the game's own <c>RpgRenderSceneBuilder</c>, so the editor shows what the game shows.
    /// </summary>
    public sealed class RpgEditorScene
    {
        private readonly Dictionary<string, RpgObject> effective = new(StringComparer.OrdinalIgnoreCase);

        private RpgEditorScene(RpgMap map, RpgMapCells cells, RpgWalkGrid walk)
        {
            Map = map;
            Cells = cells;
            Walk = walk;
        }

        /// <summary>The map as the game reads it (its grids as text rows).</summary>
        public RpgMap Map { get; }

        /// <summary>The grids of the map, with the objects of the legend resolved.</summary>
        public RpgMapCells Cells { get; }

        /// <summary>Walkability of the cells with every static placement counted (the ghost preview's mask).</summary>
        public RpgWalkGrid Walk { get; }

        /// <summary>The placements drawn as static sprites: id, effective object, cell.</summary>
        public List<(string Id, RpgObject Object, int X, int Y)> Statics { get; } = new();

        /// <summary>The placements drawn as actors (a pose on their cell): id, effective object, cell.</summary>
        public List<(string Id, RpgObject Object, int X, int Y)> Actors { get; } = new();

        /// <summary>
        /// Builds the scene of <paramref name="doc"/>. <paramref name="objects"/> resolves an object id (null for an
        /// unknown one); a placement of an unknown object or one that is neither a sprite nor an actor is left out.
        /// </summary>
        public static RpgEditorScene From(RpgMapDocument doc, Func<string, RpgObject> objects)
        {
            var map = MapOf(doc.Data);
            var cells = RpgMapCells.Build(map, objects);
            var scene = new RpgEditorScene(map, cells, new RpgWalkGrid(cells));
            foreach (var record in doc.Placements)
            {
                if (record.Data == null || doc.IsDeleted(record.Id))
                    continue;
                var placement = PlacementOf(record.Data);
                var resolved = objects(placement.Object);
                if (resolved == null || (resolved.Form != RpgObjectForm.Sprite && resolved.Form != RpgObjectForm.Actor))
                    continue;
                var eff = Effective(resolved, map, placement);
                scene.effective[placement.ID] = eff;
                if (eff.Form == RpgObjectForm.Actor)
                {
                    scene.Actors.Add((placement.ID, eff, placement.X, placement.Y));
                    continue;
                }
                scene.Statics.Add((placement.ID, eff, placement.X, placement.Y));
                scene.Walk.AddStatic(eff, placement.X, placement.Y);
            }
            return scene;
        }

        /// <summary>The effective object of a placement of this scene, or null.</summary>
        public RpgObject ObjectOf(string placementId)
            => placementId != null && effective.TryGetValue(placementId, out var value) ? value : null;

        /// <summary>
        /// True when the object would block walking on the cell (<paramref name="x"/>, <paramref name="y"/>) if it stood at
        /// the origin (<paramref name="ox"/>, <paramref name="oy"/>): the footprint leaves the map, or one of its solid cells
        /// lands on a cell that is not walkable.
        /// </summary>
        public bool Blocks(RpgObject obj, int ox, int oy)
        {
            if (obj == null)
                return false;
            for (var dy = 0; dy < Math.Max(1, obj.Height); dy++)
            {
                for (var dx = 0; dx < Math.Max(1, obj.Width); dx++)
                {
                    var x = ox + dx;
                    var y = oy + dy;
                    if (!Cells.InBounds(x, y))
                        return true;
                    if (obj.BlocksAt(dx, dy) && !Walk.IsWalkable(x, y))
                        return true;
                }
            }
            return false;
        }

        /// <summary>The map record of a document's data as the game's <see cref="RpgMap"/>.</summary>
        public static RpgMap MapOf(JsonObject data)
            => data == null
                ? new RpgMap()
                : JsonSerializer.Deserialize<RpgMap>(data.ToJsonString(), JsonOptionsProvider.ReadOptions) ?? new RpgMap();

        /// <summary>A placement record's data as the game's <see cref="RpgPlacement"/>.</summary>
        public static RpgPlacement PlacementOf(JsonObject data)
            => JsonSerializer.Deserialize<RpgPlacement>(data.ToJsonString(), JsonOptionsProvider.ReadOptions) ?? new RpgPlacement();

        /// <summary>The object of a placement on a map: variant, overrides and <c>Extends</c> applied as the world applies them.</summary>
        public static RpgObject Effective(RpgObject resolved, RpgMap map, RpgPlacement placement)
            => RpgObjectResolve.Effective(resolved, map?.Variant, placement);
    }
}
