namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// What a drag of a teleport handle moves (plan 14.G18): nothing (a placement drag or none), the target cell of a teleport
    /// on this map (the arrowhead of its arrow), or the arrival cell of a teleport from another map (its incoming arrow).
    /// </summary>
    public enum RpgHandleKind
    {
        /// <summary>No handle is dragged.</summary>
        None,

        /// <summary>The target cell of the teleport of a placement on this map.</summary>
        Target,

        /// <summary>The arrival cell on this map of a teleport of another map's placement.</summary>
        Arrival,
    }
}
