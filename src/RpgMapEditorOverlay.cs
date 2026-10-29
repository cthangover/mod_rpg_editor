namespace Cthangover.Tools.RpgEditor
{
    using Godot;
    /// <summary>Authoring marks render above the shared world, without intercepting canvas input.</summary>
    public partial class RpgMapEditorOverlay : Node2D
    {
        private readonly RpgMapEditorCanvas canvas;
        public RpgMapEditorOverlay(RpgMapEditorCanvas canvas) { this.canvas = canvas; ZIndex = 10; }
        public override void _Draw() => canvas.DrawOverlays(this);
    }
}
