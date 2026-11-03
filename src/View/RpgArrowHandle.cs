namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Which part of a teleport arrow a press on the canvas hit (plan 14.G18).</summary>
    public enum RpgArrowHandle
    {
        /// <summary>No arrow under the pointer.</summary>
        None,

        /// <summary>The line of the arrow.</summary>
        Shaft,

        /// <summary>The head: the target cell of a teleport on this map, or the arrival cell of an incoming one.</summary>
        Head,
    }
}
