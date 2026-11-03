namespace Cthangover.Tools.RpgEditor
{
    using Godot;

    /// <summary>Preview an event resize without mutating the document; commit the origin and footprint once on release.</summary>
    public partial class RpgEditorWindow
    {
        private Rect2I? resizePreview;
        private Rect2I resizeInitial;
        private Vector2 resizePointer;
        private int resizeCorner;

        private void OnResizePressed(int corner, Vector2 pointer)
        {
            if (tool != RpgEditorTool.Select || canvas.ResizeArea is not { } area) return;
            resizeInitial = area;
            resizePreview = area;
            resizePointer = pointer;
            resizeCorner = corner;
            dragOrigin = null;
            dragTo = null;
        }

        private void OnResizeDragged(Vector2 pointer)
        {
            if (resizePreview == null || document == null) return;
            resizePreview = RpgAreaResize.Bounds(resizeInitial, resizeCorner, pointer - resizePointer,
                new Vector2I(document.Width, document.Height));
            RebuildMarks();
        }

        private void OnResizeReleased(Vector2 pointer)
        {
            if (resizePreview == null) return;
            OnResizeDragged(pointer);
            var bounds = resizePreview.Value;
            resizePreview = null;
            if (bounds != resizeInitial && document.SetPlacementBounds(selectedId, bounds.Position.X, bounds.Position.Y,
                bounds.Size.X, bounds.Size.Y)) AfterChange();
            else RebuildMarks();
        }
    }
}
