namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using Godot;

    /// <summary>
    /// A teleport drawn over the map canvas (plan 14.G3). An outgoing teleport to a target on this map is an arrow from the
    /// cell of its placement to the target cell. A teleport to another map, and an incoming teleport from another map,
    /// cannot point at a cell here, so it is a short arrow that leaves the map (or enters it) at the cell, labelled with the
    /// other map. A broken target is drawn red, the same red as the list of links. Positions are cell-space points (cell
    /// centres are at <c>x + 0.5</c>); the canvas maps them into its view with <see cref="Draw"/>.
    /// </summary>
    public sealed class RpgTeleportArrow
    {
        /// <summary>Colour of a teleport that leaves the map and has a fine target.</summary>
        public static readonly Color OutgoingColor = new(0.55f, 0.85f, 1f, 0.95f);

        /// <summary>Colour of a teleport from another map that arrives here.</summary>
        public static readonly Color IncomingColor = new(0.55f, 1f, 0.6f, 0.95f);

        /// <summary>Colour of a teleport whose target is broken (no such map, a cell off the map or a blocked cell).</summary>
        public static readonly Color BrokenColor = new(1f, 0.35f, 0.3f, 0.95f);

        /// <summary>How far an arrow to another map reaches out of (or into) the map, in cells.</summary>
        public const float EdgeLength = 1.25f;

        private const float HeadSize = 9f;
        private const float LineWidth = 2f;
        private const float LabelSize = 12f;

        private RpgTeleportArrow(Vector2 from, Vector2 to, Color color, bool broken, string label)
        {
            From = from;
            To = to;
            Color = color;
            Broken = broken;
            Label = label;
        }

        /// <summary>Where the arrow starts, in cell space.</summary>
        public Vector2 From { get; }

        /// <summary>Where the arrowhead is, in cell space.</summary>
        public Vector2 To { get; }

        /// <summary>The colour the arrow is drawn in.</summary>
        public Color Color { get; }

        /// <summary>True when the teleport's target is broken; the arrow is red.</summary>
        public bool Broken { get; }

        /// <summary>The other map's id for an arrow that leaves or enters the map; null for an arrow between two cells here.</summary>
        public string Label { get; }

        /// <summary>
        /// The arrows of a map: the outgoing teleports of its placements (<paramref name="outgoing"/>, with their source cells) and
        /// the incoming ones from other maps (<paramref name="incoming"/>, with their arrival cells on this map).
        /// <paramref name="size"/> is the map's size in cells; it gives the direction of the arrows that leave the map.
        /// </summary>
        public static List<RpgTeleportArrow> Of(string mapId, Vector2I size, IEnumerable<RpgTeleportLink> outgoing, IEnumerable<RpgTeleportLink> incoming)
        {
            var result = new List<RpgTeleportArrow>();
            var centre = new Vector2(size.X / 2f, size.Y / 2f);
            foreach (var link in outgoing ?? Array.Empty<RpgTeleportLink>())
            {
                var from = new Vector2(link.FromX + 0.5f, link.FromY + 0.5f);
                var broken = link.Broken != null;
                if (string.Equals(link.Map, mapId, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(new RpgTeleportArrow(from, new Vector2(link.X + 0.5f, link.Y + 0.5f), broken ? BrokenColor : OutgoingColor, broken, null));
                    continue;
                }
                var outward = Outward(from, centre);
                result.Add(new RpgTeleportArrow(from, from + outward * EdgeLength, broken ? BrokenColor : OutgoingColor, broken, link.Map));
            }
            foreach (var link in incoming ?? Array.Empty<RpgTeleportLink>())
            {
                var arrival = new Vector2(link.X + 0.5f, link.Y + 0.5f);
                var outward = Outward(arrival, centre);
                result.Add(new RpgTeleportArrow(arrival + outward * EdgeLength, arrival, IncomingColor, false, link.Map));
            }
            return result;
        }

        /// <summary>
        /// Draws the arrow: the shaft, the head, and the label of an arrow to another map. <paramref name="toView"/> maps a
        /// cell-space point into the coordinates of <paramref name="target"/>.
        /// </summary>
        public void Draw(CanvasItem target, Func<Vector2, Vector2> toView)
        {
            var a = toView(From);
            var b = toView(To);
            target.DrawLine(a, b, Color, LineWidth, true);
            var dir = (b - a).Normalized();
            if (dir == Vector2.Zero)
                return;
            var side = new Vector2(-dir.Y, dir.X);
            target.DrawColoredPolygon(new[]
            {
                b,
                b - dir * HeadSize + side * (HeadSize * 0.5f),
                b - dir * HeadSize - side * (HeadSize * 0.5f),
            }, Color);
            if (!string.IsNullOrEmpty(Label))
                target.DrawString(ThemeDB.FallbackFont, (a + b) / 2f + side * LabelSize, Label, HorizontalAlignment.Left, -1, (int)LabelSize, Color);
        }

        /// <summary>The unit direction from the map's centre to a cell; up when the cell is the centre itself.</summary>
        private static Vector2 Outward(Vector2 cell, Vector2 centre)
        {
            var direction = (cell - centre).Normalized();
            return direction == Vector2.Zero ? new Vector2(0, -1) : direction;
        }
    }
}
