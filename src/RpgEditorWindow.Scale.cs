namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg.Model;

    public partial class RpgEditorWindow
    {
        private void OnScalePreviewChanged(string id, double value)
        {
            if (scene?.ObjectOf(id) is not { } obj || !double.IsFinite(value)) return;
            // Effective objects may be borrowed from the shared catalogue: preview only this placement's copy.
            var preview = JsonSerializer.SerializeToNode(obj, JsonOptionsProvider.ReadOptions)
                .Deserialize<RpgObject>(JsonOptionsProvider.ReadOptions);
            preview.Scale = (float)Math.Clamp(value, 0.01, 100);
            foreach (var pose in canvas.Actors)
                if (pose.Id == id) pose.Look = builder.ActorLook(preview, scene.Map.TileSize);
            var statics = new List<(string Id, RpgObject Object, int X, int Y)>(scene.Statics);
            for (var i = 0; i < statics.Count; i++)
                if (statics[i].Id == id) statics[i] = (id, preview, statics[i].X, statics[i].Y);
            ShowBackdropPreview(statics);
        }

        private void OnScaleChanged(string id, double value)
        {
            if (document == null || !double.IsFinite(value)) return;
            if (!document.SetPlacementField(id, "Scale", JsonValue.Create((float)Math.Clamp(value, 0.01, 100)))) return;
            // The preview already updated the scene. Keep the inspector and the ongoing input alive.
            SetDirty();
            RebuildScene(false);
        }
    }
}
