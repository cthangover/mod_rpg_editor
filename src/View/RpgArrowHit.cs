namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using Godot;

    /// <summary>
    /// The hit test of the teleport arrows on the canvas (plan 14.G18): a press within a few pixels of an arrow's head or of
    /// its line. A head within <see cref="HeadTolerance"/> is a head hit; otherwise a line within <see cref="ShaftTolerance"/>
    /// is a shaft hit. Among several arrows the nearest hit wins. Pure: the arrows come in as points of the same space as the
    /// pointer (canvas pixels in the window), so the rule is testable without a canvas.
    /// </summary>
    public static class RpgArrowHit
    {
        /// <summary>How far from the head a press still hits it, in pixels.</summary>
        public const float HeadTolerance = 10f;

        /// <summary>How far from the line a press still hits it, in pixels.</summary>
        public const float ShaftTolerance = 6f;

        /// <summary>The index of the arrow under <paramref name="point"/> with the default tolerances, or -1; <paramref name="handle"/> is what was hit.</summary>
        public static int Pick(IReadOnlyList<(Vector2 From, Vector2 To)> arrows, Vector2 point, out RpgArrowHandle handle)
            => Pick(arrows, point, HeadTolerance, ShaftTolerance, out handle);

        /// <summary>The index of the arrow under <paramref name="point"/>, or -1; <paramref name="handle"/> is what was hit.</summary>
        public static int Pick(IReadOnlyList<(Vector2 From, Vector2 To)> arrows, Vector2 point, float headTolerance, float shaftTolerance, out RpgArrowHandle handle)
        {
            handle = RpgArrowHandle.None;
            var best = -1;
            var bestScore = float.MaxValue;
            for (var i = 0; i < arrows.Count; i++)
            {
                var head = point.DistanceTo(arrows[i].To);
                float score;
                RpgArrowHandle hit;
                if (head <= headTolerance)
                {
                    score = head;
                    hit = RpgArrowHandle.Head;
                }
                else
                {
                    var shaft = DistanceToSegment(point, arrows[i].From, arrows[i].To);
                    if (shaft > shaftTolerance)
                        continue;
                    score = shaft;
                    hit = RpgArrowHandle.Shaft;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                    handle = hit;
                }
            }
            return best;
        }

        /// <summary>The distance from a point to the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
        public static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var length = ab.LengthSquared();
            if (length <= 0f)
                return point.DistanceTo(a);
            var t = Mathf.Clamp((point - a).Dot(ab) / length, 0f, 1f);
            return point.DistanceTo(a + ab * t);
        }
    }
}
