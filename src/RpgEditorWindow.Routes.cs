namespace Cthangover.Tools.RpgEditor
{
    using System.Linq;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Godot;

    public partial class RpgEditorWindow
    {
        private RpgRouteEditor routePreview;
        private RpgRouteEditor routeDrawing;

        private void OnRoutePreview(RpgRouteEditor editor) { routePreview = editor; RebuildMarks(); }
        private void BeginRouteDrawing(RpgRouteEditor editor)
        {
            routeDrawing = routeDrawing == editor ? null : editor;
            routePreview = editor;
            dragOrigin = null;
            dragTo = null;
            SetStatus(ToolUi.Tr(routeDrawing == null ? "tools/rpg_editor/route_draw_done" : "tools/rpg_editor/route_draw_help"), ToolStatusKind.Info);
            RebuildMarks();
        }
        private bool DrawRouteCell(Vector2I cell, MouseButton button)
        {
            if (!GodotObject.IsInstanceValid(routeDrawing) || routeDrawing.Placement != selectedId) { routeDrawing = null; return false; }
            if (button == MouseButton.Right) { routeDrawing = null; RebuildMarks(); return true; }
            if (button != MouseButton.Left || scene == null) return true;
            var at = scene.Editables.FirstOrDefault(p => p.Id == selectedId);
            var trace = new RpgRouteTrace(new Vector2I(at.X, at.Y), routeDrawing.SelectedSteps);
            var path = RpgRouteSketch.Connect(trace.End, cell, c => scene.Walk.IsWalkable(c.X, c.Y));
            if (path == null) SetStatus(ToolUi.Tr("tools/rpg_editor/route_unreachable"), ToolStatusKind.Error);
            else routeDrawing.AppendInstructions(path);
            return true;
        }
        private void UpdateRouteOverlay()
        {
            canvas.Routes.Clear();
            if (scene == null) return;
            foreach (var at in scene.Editables)
            {
                if (at.Object.Form != RpgObjectForm.Actor) continue;
                var preview = GodotObject.IsInstanceValid(routePreview) && routePreview.Placement == selectedId && at.Id == selectedId;
                var route = preview ? routePreview.Preview : at.Object.Route;
                if (route == null) continue;
                var selectedVariant = preview ? routePreview.Variant : route.Variants?.Count > 0 ? 0 : -1;
                var choices = route.Variants?.Count > 0 ? route.Variants : new System.Collections.Generic.List<System.Collections.Generic.List<string>> { route.Steps };
                for (var i = 0; i < choices.Count; i++)
                {
                    if (at.Id != selectedId && i > 0) continue;
                    var selected = at.Id == selectedId && (selectedVariant < 0 || selectedVariant == i);
                    var color = selected ? new Color(1,.8f,.2f,1) : at.Id == selectedId ? new Color(.4f,.8f,1,.25f) : new Color(.4f,.8f,1,.12f);
                    canvas.Routes.Add((new RpgRouteTrace(new Vector2I(at.X, at.Y), choices[i]), color, selected));
                }
            }
            if (GodotObject.IsInstanceValid(routeDrawing)) { canvas.ArrowsInteractive = false; canvas.ResizeArea = null; }
        }
    }
}
