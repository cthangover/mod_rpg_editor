using System;
using System.Collections.Generic;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Read-only authoring regions, independent of gameplay collision and placement occupancy.
    /// A fill follows equal resolved authored values in four directions. Budget exhaustion returns
    /// no cells, so a caller cannot accidentally commit a partial fill as a successful gesture.
    /// </summary>
    public sealed class RpgPaintRegion
    {
        private const int CellBudget = 128 * 128;

        private static readonly Vector2I[] Directions =
        {
            Vector2I.Up,
            Vector2I.Right,
            Vector2I.Down,
            Vector2I.Left,
        };

        /// <summary>False when the connected area exceeds 16,384 cells; callers must not apply an incomplete operation.</summary>
        public bool Complete { get; }

        /// <summary>Deterministic connected cells; empty for an absent/outside selection or an incomplete fill.</summary>
        public IReadOnlyList<Vector2I> Cells { get; }

        private RpgPaintRegion(bool complete, List<Vector2I> cells)
        {
            Complete = complete;
            Cells = cells.AsReadOnly();
        }

        /// <summary>
        /// Finds the maximal cardinal component with the clicked ground/structure object id or collision code.
        /// Reads the document's ordered strokes and rows through its public API, including foreign patches;
        /// does not write data or history. Object ids compare case-insensitively like the entity resolver.
        /// </summary>
        public static RpgPaintRegion Fill(RpgMapDocument document, RpgStrokeLayer layer, Vector2I origin)
        {
            if (layer is not (RpgStrokeLayer.Ground or RpgStrokeLayer.Structure or RpgStrokeLayer.Collision))
            {
                throw new ArgumentOutOfRangeException(nameof(layer));
            }

            var result = new List<Vector2I>();
            if (!Inside(document, origin))
            {
                return new RpgPaintRegion(true, result);
            }

            var target = Value(document, layer, origin);
            var seen = new HashSet<Vector2I> { origin };
            var pending = new Queue<Vector2I>();
            pending.Enqueue(origin);
            while (pending.Count > 0)
            {
                var cell = pending.Dequeue();
                if (result.Count == CellBudget)
                {
                    return new RpgPaintRegion(false, new List<Vector2I>());
                }

                result.Add(cell);
                foreach (var direction in Directions)
                {
                    var next = cell + direction;
                    if (Inside(document, next) && seen.Add(next)
                        && string.Equals(Value(document, layer, next), target, StringComparison.OrdinalIgnoreCase))
                    {
                        pending.Enqueue(next);
                    }
                }
            }

            return new RpgPaintRegion(true, result);
        }

        private static bool Inside(RpgMapDocument document, Vector2I cell)
        {
            return document != null && cell.X >= 0 && cell.Y >= 0 && cell.X < document.Width && cell.Y < document.Height;
        }

        private static string Value(RpgMapDocument document, RpgStrokeLayer layer, Vector2I cell)
        {
            return layer == RpgStrokeLayer.Collision ? document.CodeAt(cell.X, cell.Y).ToString()
                : document.ObjectAt(layer, cell.X, cell.Y);
        }
    }
}
