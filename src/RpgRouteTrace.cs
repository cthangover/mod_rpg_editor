namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Predictable segments and stop markers of a route. Hidden reset teleports never become walking lines.</summary>
    public sealed class RpgRouteTrace
    {
        public List<(Vector2I From, Vector2I To)> Segments { get; } = new();
        public List<(Vector2I Cell, string Label)> Stops { get; } = new();
        public Vector2I End { get; }

        public RpgRouteTrace(Vector2I origin, IEnumerable<string> steps)
        {
            var cell = origin;
            var hidden = false;
            foreach (var text in steps ?? Array.Empty<string>())
            {
                var word = (text ?? "").Trim().ToLowerInvariant();
                if (word == "hide") { hidden = true; Stops.Add((cell, "−")); continue; }
                if (word == "show") { hidden = false; Stops.Add((cell, "+")); continue; }
                if (word == "reset") { if (hidden) cell = origin; continue; }
                if (word == "wait" || word.StartsWith("wait:"))
                {
                    if (!hidden) Stops.Add((cell, word == "wait" ? "Ⅱ" : word.Substring(5)+"s"));
                    continue;
                }
                if (word is "random" or "toward" or "away") { Stops.Add((cell, "?")); continue; }
                var step = RpgRouteStep.Resolve(word, cell, origin, null);
                if (!hidden && step.Moves && step.Direction is { } direction)
                {
                    var next = cell + RpgDirections.Delta(direction);
                    Segments.Add((cell, next));
                    cell = next;
                }
            }
            End = cell;
        }
    }
}
