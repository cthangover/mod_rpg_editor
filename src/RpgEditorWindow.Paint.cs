using System.Collections.Generic;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Converts opt-in paint gestures into the existing single batch/undo transaction; scene and save policy remain unchanged.
    /// </summary>
    public partial class RpgEditorWindow
    {
        private RpgPaintMode paintMode;

        private Vector2I strokeOrigin;

        private OptionButton paintModePicker;

        private Control paintOptions;

        private void OnPaintModeChoice(long index)
        {
            if (index < 0 || index > (long)RpgPaintMode.Rectangle)
            {
                return;
            }

            paintMode = (RpgPaintMode)index;
            stroke = null;
            RebuildMarks();
        }

        private List<Vector2I> StrokeCells(Vector2I cell, MouseButton button)
        {
            strokeOrigin = cell;
            if (paintMode == RpgPaintMode.Brush || tool is not (RpgEditorTool.Paint or RpgEditorTool.Collision))
            {
                return new List<Vector2I> { cell };
            }

            if (tool == RpgEditorTool.Paint && button != MouseButton.Right && brushObject == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/choose_first"), ToolStatusKind.Info);
                return null;
            }

            var layer = tool == RpgEditorTool.Collision ? RpgStrokeLayer.Collision
                : brushForm == RpgObjectForm.Segment ? RpgStrokeLayer.Structure : RpgStrokeLayer.Ground;
            var region = paintMode == RpgPaintMode.Rectangle ? RpgPaintRegion.Rectangle(document, cell, cell)
                : RpgPaintRegion.Fill(document, layer, cell);
            if (!region.Complete)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/paint_fill_budget"), ToolStatusKind.Error);
                return null;
            }

            return new List<Vector2I>(region.Cells);
        }
        private bool UpdateRectangle(Vector2I cell)
        {
            if (stroke == null || paintMode != RpgPaintMode.Rectangle
                || tool is not (RpgEditorTool.Paint or RpgEditorTool.Collision))
            {
                return false;
            }

            var region = RpgPaintRegion.Rectangle(document, strokeOrigin, cell);
            stroke = region.Complete ? new List<Vector2I>(region.Cells) : null;
            if (!region.Complete)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/paint_rectangle_budget"), ToolStatusKind.Error);
            }

            RebuildMarks();
            return true;
        }
    }
}
