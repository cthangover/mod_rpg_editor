namespace Cthangover.Tools.RpgEditor
{
    using Godot;

    public partial class RpgEditorCanvas
    {
        // Lines follow the logical map size, independent of a background sprite's footprint.
        // Draw whole rows/columns, keeping large maps linear in width + height.
        private void PaintMapGrid(Node2D target)
        {
            if (!ShowGrid || scene == null) return;
            var color = new Color(0.65f, 0.85f, 1f, 0.18f);
            var edge = new Color(0.45f, 0.85f, 1f, 0.8f);
            for (var x = 0; x <= scene.Size.X; x++)
                target.DrawLine(view.ProjectToView(new Vector3(x, 0, 0)),
                    view.ProjectToView(new Vector3(x, scene.Size.Y, 0)),
                    x == 0 || x == scene.Size.X ? edge : color);
            for (var y = 0; y <= scene.Size.Y; y++)
                target.DrawLine(view.ProjectToView(new Vector3(0, y, 0)),
                    view.ProjectToView(new Vector3(scene.Size.X, y, 0)),
                    y == 0 || y == scene.Size.Y ? edge : color);
        }
    }
}
