using System;
using System.Collections.Generic;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Bounded, read-only authoring audit over the current preview state. Reachability starts at spawn;
    /// Use targets accept reachable footprint cells or cardinal neighbours, Step targets require entry.
    /// Conditions, interactions and nondeterministic routes are never executed. Warnings describe this
    /// snapshot and do not veto saving or pretend to predict later gameplay states.
    /// </summary>
    public sealed class RpgMapDiagnostics
    {
        private static readonly Vector2I[] Directions =
        {
            Vector2I.Up,
            Vector2I.Right,
            Vector2I.Down,
            Vector2I.Left,
        };

        private readonly RpgEditorScene scene;

        private readonly List<(string Code, string Id, Vector2I Cell)> issues = new();

        private readonly int[] parents;

        /// <summary>False when the map exceeds 128×128 cells; no unreachable claim is made from partial work.</summary>
        public bool Complete { get; }

        /// <summary>Number of cells reachable from a valid spawn in the current effective grid.</summary>
        public int Reached { get; private set; }

        /// <summary>Advisory issues with stable codes, source identities and logical coordinates.</summary>
        public IReadOnlyList<(string Code, string Id, Vector2I Cell)> Issues { get; }

        /// <summary>
        /// Audits the borrowed scene. The optional resolver supplies detached target scenes for outgoing
        /// arrivals, including their placement masks. A missing resolver/target produces an explicit
        /// unavailable-target warning rather than accepting an unchecked destination. Incoming arrival coordinates
        /// are checked against this edited scene, so new masks also reveal broken links owned by other maps.
        /// </summary>
        public RpgMapDiagnostics(RpgEditorScene scene, Func<string, RpgEditorScene> targets = null,
            IEnumerable<(string Id, Vector2I Cell)> arrivals = null)
        {
            this.scene = scene;
            Issues = issues.AsReadOnly();
            var count = (long)(scene?.Map.Width ?? 0) * (scene?.Map.Height ?? 0);
            Complete = count > 0 && count <= 128 * 128 && scene.Map.Width > 0 && scene.Map.Height > 0;
            if (!Complete)
            {
                issues.Add(("budget", scene?.Map.ID ?? "", default));
                return;
            }

            parents = new int[(int)count];
            Array.Fill(parents, -1);
            var spawn = new Vector2I(scene.Map.Spawn?.X ?? 0, scene.Map.Spawn?.Y ?? 0);
            if (!scene.Walk.IsWalkable(spawn.X, spawn.Y))
            {
                issues.Add(("spawn", scene.Map.ID, spawn));
            }
            else
            {
                Flood(spawn);
            }

            foreach (var arrival in arrivals ?? Array.Empty<(string Id, Vector2I Cell)>())
            {
                if (!scene.Walk.IsWalkable(arrival.Cell.X, arrival.Cell.Y))
                {
                    issues.Add(("arrival", arrival.Id, arrival.Cell));
                }
            }

            foreach (var entry in scene.Editables)
            {
                var obj = entry.Object;
                if (obj.Hidden)
                {
                    continue;
                }

                var rect = new Rect2I(entry.X, entry.Y, obj.Width, obj.Height);
                if (obj.Interact != null && Reached > 0 && !Accessible(rect, obj.Interact.Trigger))
                {
                    issues.Add(("event", entry.Id, rect.Position));
                }

                if (obj.Interact?.Teleport is { } target)
                {
                    var map = string.IsNullOrEmpty(target.Map) ? scene.Map.ID : target.Map;
                    var destination = string.Equals(map, scene.Map.ID, StringComparison.OrdinalIgnoreCase) ? scene : targets?.Invoke(map);
                    var cell = new Vector2I(target.X, target.Y);
                    if (destination == null)
                    {
                        issues.Add(("target", entry.Id, cell));
                    }
                    else if (!destination.Walk.IsWalkable(cell.X, cell.Y))
                    {
                        issues.Add(("arrival", entry.Id, cell));
                    }
                }

                if (obj.Route != null)
                {
                    CheckRoute(entry.Id, obj, rect.Position);
                }
            }
        }

        /// <summary>False outside the map, after an invalid spawn, or when the audit exceeds its explicit cell budget.</summary>
        public bool CanReach(Vector2I cell)
        {
            var index = Index(cell);
            return index >= 0 && parents != null && parents[index] >= 0;
        }

        /// <summary>Cardinal shortest path including start and goal; null means unavailable, never a direct-motion fallback.</summary>
        public IReadOnlyList<Vector2I> PathTo(Vector2I cell)
        {
            if (!CanReach(cell))
            {
                return null;
            }

            var path = new List<Vector2I>();
            var at = Index(cell);
            while (true)
            {
                path.Add(new Vector2I(at % scene.Map.Width, at / scene.Map.Width));
                if (parents[at] == at)
                {
                    break;
                }

                at = parents[at];
            }

            path.Reverse();
            return path.AsReadOnly();
        }

        private int Index(Vector2I cell)
        {
            return scene != null && scene.Cells.InBounds(cell.X, cell.Y) ? cell.Y * scene.Map.Width + cell.X : -1;
        }

        private void Flood(Vector2I spawn)
        {
            var pending = new Queue<Vector2I>();
            parents[Index(spawn)] = Index(spawn);
            pending.Enqueue(spawn);
            Reached = 1;
            while (pending.Count > 0)
            {
                var from = pending.Dequeue();
                foreach (var direction in Directions)
                {
                    var next = from + direction;
                    var index = Index(next);
                    if (index >= 0 && parents[index] < 0 && scene.Walk.IsWalkable(next.X, next.Y))
                    {
                        parents[index] = Index(from);
                        Reached++;
                        pending.Enqueue(next);
                    }
                }
            }
        }

        private bool Accessible(Rect2I bounds, RpgInteractTrigger trigger)
        {
            var clipped = bounds.Intersection(new Rect2I(0, 0, scene.Map.Width, scene.Map.Height));
            for (var y = clipped.Position.Y; y < clipped.End.Y; y++)
            {
                for (var x = clipped.Position.X; x < clipped.End.X; x++)
                {
                    var cell = new Vector2I(x, y);
                    if (CanReach(cell))
                    {
                        return true;
                    }

                    if (trigger == RpgInteractTrigger.Use)
                    {
                        foreach (var direction in Directions)
                        {
                            if (CanReach(cell + direction))
                            {
                                return true;
                            }
                        }
                    }
                }
            }

            return false;
        }

        private void CheckRoute(string id, RpgObject obj, Vector2I origin)
        {
            var grid = new RpgWalkGrid(scene.Cells);
            foreach (var entry in scene.Editables)
            {
                if (!string.Equals(entry.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    grid.AddStatic(entry.Object, entry.X, entry.Y);
                }
            }

            var choices = obj.Route.Variants?.Count > 0 ? obj.Route.Variants : new List<List<string>> { obj.Route.Steps };
            for (var index = 0; index < choices.Count; index++)
            {
                var prefix = new List<string>();
                var dynamic = false;
                foreach (var instruction in choices[index] ?? new List<string>())
                {
                    var word = (instruction ?? "").Trim().ToLowerInvariant();
                    if (word is "random" or "toward" or "away")
                    {
                        dynamic = true;
                        break;
                    }

                    prefix.Add(instruction);
                }

                var trace = new RpgRouteTrace(origin, prefix);
                if (dynamic)
                {
                    issues.Add(("route_dynamic", id + ":" + index, trace.End));
                }

                foreach (var segment in trace.Segments)
                {
                    if (!Fits(grid, obj, segment.To))
                    {
                        issues.Add(("route", id + ":" + index, segment.To));
                        break;
                    }
                }
            }
        }

        private static bool Fits(RpgWalkGrid grid, RpgObject obj, Vector2I origin)
        {
            if (!obj.IsSolid)
            {
                return grid.IsWalkable(origin.X, origin.Y);
            }

            for (var y = 0; y < obj.Height; y++)
            {
                for (var x = 0; x < obj.Width; x++)
                {
                    if (obj.BlocksAt(x, y) && !grid.IsWalkable(origin.X + x, origin.Y + y))
                    {
                        return false;
                    }
                }
            }

            return true;
        }
    }
}
