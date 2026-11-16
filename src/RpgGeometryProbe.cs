using System;
using System.Collections.Generic;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Repeatable clockwise perimeter over the editor's effective walk grid. Occupied cells are bounded
    /// separately from artwork, so a full-room picture with a small desk mask gets a desk-sized probe.
    /// Blocked or off-map steps remain explicit; the probe never skips them or invents a traversable loop.
    /// </summary>
    public sealed class RpgGeometryProbe
    {
        private readonly bool[] walkable;

        /// <summary>Declared placement footprint, independent of the picture and its transparent padding.</summary>
        public Rect2I Footprint { get; }

        /// <summary>Occupied in-map bounds, or the clipped declared footprint when it has no solid cells.</summary>
        public Rect2I Bounds { get; }

        /// <summary>True when the perimeter follows actual solid cells rather than a nonblocking footprint.</summary>
        public bool UsesSolidCells { get; }

        /// <summary>Clockwise four-neighbour ring, with each corner included once and no repeated closing cell.</summary>
        public IReadOnlyList<Vector2I> Cells { get; }

        /// <summary>Ring cells the effective grid rejects, including those beyond map bounds.</summary>
        public IReadOnlyList<Vector2I> BlockedCells { get; }

        /// <summary>True only when every ring step, including the closing edge, can be walked.</summary>
        public bool Closed { get; }

        private RpgGeometryProbe(Rect2I footprint, Rect2I bounds, bool usesSolidCells, List<Vector2I> cells,
            List<Vector2I> blocked, bool[] walkable)
        {
            Footprint = footprint;
            Bounds = bounds;
            UsesSolidCells = usesSolidCells;
            Cells = cells.AsReadOnly();
            BlockedCells = blocked.AsReadOnly();
            this.walkable = walkable;
            Closed = cells.Count > 0 && blocked.Count == 0;
        }

        /// <summary>Checks a recorded step without retaining or modifying the scene; invalid indices are unavailable.</summary>
        public bool CanEnter(int index)
        {
            return index >= 0 && index < walkable.Length && walkable[index];
        }

        /// <summary>
        /// Samples only cells intersecting the map and delegates collision to BlocksAt/Walk.IsWalkable.
        /// Missing, deleted or hidden selections have no ring. Work is bounded by the map and its perimeter.
        /// </summary>
        public static RpgGeometryProbe Around(RpgEditorScene scene, string placementId)
        {
            RpgObject selected = null;
            var origin = Vector2I.Zero;
            if (scene != null && placementId != null)
            {
                foreach (var entry in scene.Editables)
                {
                    if (string.Equals(entry.Id, placementId, StringComparison.OrdinalIgnoreCase))
                    {
                        selected = entry.Object;
                        origin = new Vector2I(entry.X, entry.Y);
                        break;
                    }
                }
            }

            var cells = new List<Vector2I>();
            var blocked = new List<Vector2I>();
            if (selected == null || selected.Hidden)
            {
                return new RpgGeometryProbe(default, default, false, cells, blocked, Array.Empty<bool>());
            }

            var footprint = new Rect2I(origin, new Vector2I(selected.Width, selected.Height));
            var clipped = footprint.Intersection(new Rect2I(0, 0, scene.Map.Width, scene.Map.Height));
            if (clipped.Size.X < 1 || clipped.Size.Y < 1)
            {
                return new RpgGeometryProbe(footprint, default, false, cells, blocked, Array.Empty<bool>());
            }

            var minimum = clipped.End;
            var maximum = clipped.Position;
            var solid = false;
            for (var y = clipped.Position.Y; y < clipped.End.Y; y++)
            {
                for (var x = clipped.Position.X; x < clipped.End.X; x++)
                {
                    if (!selected.BlocksAt(x - origin.X, y - origin.Y))
                    {
                        continue;
                    }

                    minimum = new Vector2I(Math.Min(minimum.X, x), Math.Min(minimum.Y, y));
                    maximum = new Vector2I(Math.Max(maximum.X, x + 1), Math.Max(maximum.Y, y + 1));
                    solid = true;
                }
            }

            var bounds = solid ? new Rect2I(minimum, maximum - minimum) : clipped;
            var left = bounds.Position.X - 1;
            var top = bounds.Position.Y - 1;
            var right = bounds.End.X;
            var bottom = bounds.End.Y;
            for (var x = left; x <= right; x++)
            {
                cells.Add(new Vector2I(x, top));
            }

            for (var y = top + 1; y <= bottom; y++)
            {
                cells.Add(new Vector2I(right, y));
            }

            for (var x = right - 1; x >= left; x--)
            {
                cells.Add(new Vector2I(x, bottom));
            }

            for (var y = bottom - 1; y > top; y--)
            {
                cells.Add(new Vector2I(left, y));
            }

            var walkable = new bool[cells.Count];
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                walkable[index] = scene.Walk.IsWalkable(cell.X, cell.Y);
                if (!walkable[index])
                {
                    blocked.Add(cell);
                }
            }

            return new RpgGeometryProbe(footprint, bounds, solid, cells, blocked, walkable);
        }
    }
}
