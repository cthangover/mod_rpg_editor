namespace Cthangover.Tools.RpgEditor
{
    /// <summary>What a click on the canvas does.</summary>
    public enum RpgEditorTool
    {
        /// <summary>Select a placement; drag it to move it; Delete removes it.</summary>
        Select,

        /// <summary>Draw a new event rectangle directly on the map.</summary>
        Event,

        /// <summary>Paint the chosen surface (Ground) or segment (Structure); right button erases.</summary>
        Paint,

        /// <summary>Place the chosen sprite or actor on the cell clicked.</summary>
        Place,

        /// <summary>
        /// Paint collision codes over cells: <c>.</c> as the objects decide, <c>#</c> blocked, <c>o</c> forced
        /// open.
        /// </summary>
        Collision,

        /// <summary>Set the map's spawn point on the cell clicked.</summary>
        Spawn,

        /// <summary>
        /// Paint or erase (right button) the effect mask of the chosen atmosphere profile over cells.
        /// </summary>
        Effects,
    }
}
