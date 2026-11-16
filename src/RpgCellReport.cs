using System;
using System.Collections.Generic;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Read-only explanation of the same effective grid used by the editor. Suppressed blockers remain
    /// visible when an explicit collision override wins. Sources identify resolved rows/strokes, not
    /// an invented history of upstream patches that the document no longer retains.
    /// </summary>
    public sealed class RpgCellReport
    {
        /// <summary>Requested logical cell, including coordinates outside the map.</summary>
        public Vector2I Cell { get; private init; }

        /// <summary>Whether this coordinate belongs to the effective map.</summary>
        public bool InBounds { get; private init; }

        /// <summary>Authoritative effective walking answer, independently of the descriptive sources.</summary>
        public bool Walkable { get; private init; }

        /// <summary>Winning rule: outside, block, open, placement, structure, ground or free.</summary>
        public string Rule { get; private init; }

        /// <summary>Effective collision override after row and ordered strokes.</summary>
        public char Collision { get; private init; }

        /// <summary>Resolved row or last applicable collision stroke; empty outside the map.</summary>
        public string CollisionSource { get; private init; }

        /// <summary>Effective ground object id; null for an empty cell.</summary>
        public string Ground { get; private init; }

        /// <summary>Ground row or last applicable stroke, including explicit clearing strokes.</summary>
        public string GroundSource { get; private init; }

        /// <summary>Effective structure object id; null for an empty cell.</summary>
        public string Structure { get; private init; }

        /// <summary>Structure row or last applicable stroke, including explicit clearing strokes.</summary>
        public string StructureSource { get; private init; }

        /// <summary>Effective placement/event masks covering this cell; retained even when an override suppresses them.</summary>
        public IReadOnlyList<string> Blockers { get; private init; }

        private RpgCellReport()
        {
        }

        /// <summary>Samples borrowed scene data without changing collision, preview state, document history or resources.</summary>
        public static RpgCellReport At(RpgEditorScene scene, Vector2I cell)
        {
            var blockers = new List<string>();
            var inside = scene?.Cells.InBounds(cell.X, cell.Y) == true;
            if (!inside)
            {
                return new RpgCellReport
                {
                    Cell = cell,
                    Rule = "outside",
                    Collision = RpgCollision.Block,
                    Blockers = blockers.AsReadOnly(),
                };
            }

            foreach (var entry in scene.Editables)
            {
                if (entry.Object.BlocksAt(cell.X - entry.X, cell.Y - entry.Y))
                {
                    blockers.Add(entry.Id);
                }
            }

            var ground = scene.Cells.Ground(cell.X, cell.Y);
            var structure = scene.Cells.Structure(cell.X, cell.Y);
            var code = scene.Cells.Collision(cell.X, cell.Y);
            var rule = code == RpgCollision.Block ? "block" : code == RpgCollision.Open ? "open"
                : blockers.Count > 0 ? "placement" : Solid(structure) ? "structure" : Solid(ground) ? "ground" : "free";
            return new RpgCellReport
            {
                Cell = cell,
                InBounds = true,
                Walkable = scene.Walk.IsWalkable(cell.X, cell.Y),
                Rule = rule,
                Collision = code,
                Ground = ground?.ID,
                Structure = structure?.ID,
                CollisionSource = Source(scene, RpgStrokeLayer.Collision, cell, code.ToString()),
                GroundSource = Source(scene, RpgStrokeLayer.Ground, cell, ground?.ID),
                StructureSource = Source(scene, RpgStrokeLayer.Structure, cell, structure?.ID),
                Blockers = blockers.AsReadOnly(),
            };
        }

        private static bool Solid(RpgObject obj)
        {
            return obj != null && !obj.Hidden && obj.IsSolid;
        }

        private static string Source(RpgEditorScene scene, RpgStrokeLayer layer, Vector2I cell, string effective)
        {
            var source = layer + "[" + cell.Y + "][" + cell.X + "]";
            var index = 0;
            foreach (var stroke in scene.Map.Strokes ?? new List<RpgStroke>())
            {
                if (stroke != null && stroke.Layer == layer && Covers(stroke, cell))
                {
                    var target = layer == RpgStrokeLayer.Collision ? stroke.Code : stroke.Object;
                    var valid = layer != RpgStrokeLayer.Collision || target is "." or "#" or "o";
                    if (valid && string.Equals(target ?? "", effective ?? "", StringComparison.OrdinalIgnoreCase))
                    {
                        source = "Strokes[" + index + "]:" + (stroke.Id ?? "?");
                    }
                }

                index++;
            }

            return source;
        }

        private static bool Covers(RpgStroke stroke, Vector2I cell)
        {
            foreach (var rect in stroke.Rects ?? new List<int[]>())
            {
                if (rect?.Length == 4 && cell.X >= rect[0] && cell.Y >= rect[1]
                    && (long)cell.X < (long)rect[0] + rect[2] && (long)cell.Y < (long)rect[1] + rect[3])
                {
                    return true;
                }
            }

            foreach (var point in stroke.Cells ?? new List<int[]>())
            {
                if (point?.Length == 2 && point[0] == cell.X && point[1] == cell.Y)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
