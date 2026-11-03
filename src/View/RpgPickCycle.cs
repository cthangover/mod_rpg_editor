namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using System.Linq;

    /// <summary>
    /// The order in which a press on a cell picks the placements covering it (plan 14.G18). A picture-less marker standing on
    /// the cell comes first (a door or a trigger is what the author means to reach under a building), then the other markers,
    /// then the placements whose origin is the cell, then the rest of the footprints. A repeat press on the same cell takes the
    /// next one in this order and wraps around. Pure: the candidates come in as ids with their flags.
    /// </summary>
    public static class RpgPickCycle
    {
        /// <summary>The candidates in pick order (stable: equal ranks keep the order they came in).</summary>
        /// <param name="candidates">Each placement covering the cell: id, whether it is a picture-less marker, whether its origin is the cell.</param>
        public static List<string> Order(IEnumerable<(string Id, bool Marker, bool Origin)> candidates)
            => candidates.OrderBy(c => Rank(c.Marker, c.Origin)).Select(c => c.Id).ToList();

        /// <summary>The id after <paramref name="current"/> in <paramref name="ordered"/>, wrapping; the first one when the current is not in the list; null for an empty list.</summary>
        public static string Next(IReadOnlyList<string> ordered, string current)
        {
            if (ordered == null || ordered.Count == 0)
                return null;
            var at = -1;
            for (var i = 0; i < ordered.Count; i++)
            {
                if (current != null && ordered[i] == current)
                {
                    at = i;
                    break;
                }
            }
            return at < 0 ? ordered[0] : ordered[(at + 1) % ordered.Count];
        }

        private static int Rank(bool marker, bool origin)
        {
            if (marker && origin)
                return 0;
            if (marker)
                return 1;
            return origin ? 2 : 3;
        }
    }
}
