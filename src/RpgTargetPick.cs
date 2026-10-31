namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// A teleport whose target cell waits for a click on the canvas (plan 14.G3): the placement that carries it, the map it is
    /// on, and the target map. When the target is another map, the window opens that map and the next click on it picks the
    /// cell; the click then reopens <see cref="SourceMap"/> and writes the target into the placement.
    /// </summary>
    public sealed class RpgTargetPick
    {
        /// <summary>Creates the pick of <paramref name="placement"/> on <paramref name="sourceMap"/> whose target is <paramref name="targetMap"/>.</summary>
        public RpgTargetPick(string placement, string sourceMap, string targetMap)
        {
            Placement = placement;
            SourceMap = sourceMap;
            TargetMap = targetMap;
        }

        /// <summary>Id of the placement that carries the teleport.</summary>
        public string Placement { get; }

        /// <summary>Map the placement is on: the map the author edits, which the pick returns to.</summary>
        public string SourceMap { get; }

        /// <summary>Map the target cell is picked on.</summary>
        public string TargetMap { get; }
    }
}
