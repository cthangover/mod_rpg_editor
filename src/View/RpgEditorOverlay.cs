namespace Cthangover.Tools.RpgEditor
{
    using System;
    using Godot;

    /// <summary>
    /// The editor's marks (footprints, selection, ghost) drawn over the map. It is a child of the canvas' world node,
    /// last in order, so it draws above the game's renderer and in the same coordinates (cells are projected by the
    /// renderer's <c>ProjectToView</c>). <see cref="Painter"/> does the drawing; the canvas owns what is drawn.
    /// </summary>
    public partial class RpgEditorOverlay : Node2D
    {
        /// <summary>Draws the marks; set by the canvas.</summary>
        public Action<Node2D> Painter { get; set; }

        /// <inheritdoc />
        public override void _Draw() => Painter?.Invoke(this);
    }
}
