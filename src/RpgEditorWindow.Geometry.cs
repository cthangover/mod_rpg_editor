namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;

    public partial class RpgEditorWindow
    {
        private string previewBackdropId;
        private double previewBackdropX;
        private double previewBackdropY;
        private void OnMapSizeChanged(int width, int height)
        {
            if (document == null || width == document.Width && height == document.Height) return;
            if (!document.Resize(width, height))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/map_resize_refused"), ToolStatusKind.Error);
                return;
            }
            AfterChange();
            canvas.FitToView();
        }

        private void OnBackdropPositionChanged(string id, double x, double y)
        {
            if (scene == null || document == null) return;
            var parts = RpgBackdropEdit.PartsAt(scene.ObjectOf(id), canvas.Projection, scene.Map.TileSize, x, y);
            if (parts != null && document.SetPlacementField(id, "Parts", parts)) AfterChange();
        }

        /// <summary>
        /// Shows the picture at the spin-box offset before Apply. The scene's statics get a copy of the object with the
        /// moved parts, so the canvas redraws while the document is still unchanged and Apply stays the one undo step.
        /// The inspector is not rebuilt here, so the numbers the user is typing keep their values.
        /// </summary>
        private void OnBackdropPreviewChanged(string id, double x, double y)
        {
            if (scene == null || document == null || canvas == null) return;
            previewBackdropId = id;
            previewBackdropX = x;
            previewBackdropY = y;
            ShowBackdropPreview(scene.Statics);
        }

        private void ShowBackdropPreview(IEnumerable<(string Id, RpgObject Object, int X, int Y)> source)
        {
            var statics = new List<(string Id, RpgObject Object, int X, int Y)>(source);
            var obj = statics.Find(p => p.Id == previewBackdropId).Object;
            var parts = RpgBackdropEdit.PartsAt(obj, canvas.Projection, scene.Map.TileSize, previewBackdropX, previewBackdropY);
            if (parts != null)
            {
                for (var i = 0; i < statics.Count; i++)
                {
                    if (statics[i].Id != previewBackdropId) continue;
                    statics[i] = (previewBackdropId, WithParts(statics[i].Object, parts), statics[i].X, statics[i].Y);
                    break;
                }
            }
            canvas.SetScene(builder.Build(scene.Map, scene.Cells, statics));
        }

        /// <summary>
        /// A copy of the object with other parts. The JSON round trip is the one the resolver uses for states, so the
        /// copy keeps every property; the scene's own object stays unchanged.
        /// </summary>
        private static RpgObject WithParts(RpgObject obj, JsonArray parts)
        {
            var copy = JsonSerializer.SerializeToNode(obj, JsonOptionsProvider.ReadOptions).Deserialize<RpgObject>(JsonOptionsProvider.ReadOptions);
            copy.Parts = parts.Deserialize<List<RpgObjectPart>>(JsonOptionsProvider.ReadOptions);
            return copy;
        }

        private void OnGridVisibilityChanged(bool visible)
        {
            canvas.ShowGrid = visible;
            canvas.Refresh();
        }
    }
}
