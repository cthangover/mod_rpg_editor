namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Turns a map click into cardinal instructions around obstacles. Null means no walkable connection.</summary>
    public static class RpgRouteSketch
    {
        public static IReadOnlyList<string> Connect(Vector2I from, Vector2I to, Func<Vector2I, bool> walkable)
        {
            if (from == to) return Array.Empty<string>();
            if (!walkable(to)) return null;
            var pending = new Queue<Vector2I>();
            var parents = new Dictionary<Vector2I, (Vector2I Cell, RpgDirection Direction)>();
            pending.Enqueue(from);
            parents[from] = (from, RpgDirection.Down);
            while (pending.Count > 0 && parents.Count <= 65536)
            {
                var cell = pending.Dequeue();
                foreach (var direction in new[] { RpgDirection.Up, RpgDirection.Right, RpgDirection.Down, RpgDirection.Left })
                {
                    var next = cell + RpgDirections.Delta(direction);
                    if (parents.ContainsKey(next) || !walkable(next)) continue;
                    parents[next] = (cell, direction);
                    if (next == to)
                    {
                        var result = new List<string>();
                        for (var at = to; at != from; at = parents[at].Cell)
                            result.Add(parents[at].Direction.ToString().ToLowerInvariant());
                        result.Reverse();
                        return result;
                    }
                    pending.Enqueue(next);
                }
            }
            return null;
        }
    }
}
