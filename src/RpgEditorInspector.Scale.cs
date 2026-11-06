namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using Cthangover.Tools.Common;
    using Godot;

    public partial class RpgEditorInspector
    {
        private readonly List<RpgScaleControl> scaleControls = new();
        private RpgScaleControl backdropScale;

        /// <summary>Live picture-only preview, preserving the active control and its focus.</summary>
        public event Action<string, double> ScalePreviewChanged;

        /// <summary>A scale edit to persist as a placement override.</summary>
        public event Action<string, double> ScaleChanged;

        private RpgScaleControl AddScale(VBoxContainer box, string name, string id, double value)
        {
            var control = new RpgScaleControl(id, value) { Name = name };
            control.PreviewChanged += PreviewScale;
            control.Committed += CommitScale;
            scaleControls.Add(control);
            box.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/picture_scale")));
            control.TooltipText = ToolUi.Tr("tools/rpg_editor/picture_scale_tip");
            box.AddChild(control);
            return control;
        }

        private void PreviewScale(string id, double value)
        {
            foreach (var control in scaleControls)
                if (control.Placement == id) control.SetValue(value);
            ScalePreviewChanged?.Invoke(id, value);
        }

        private void CommitScale(string id, double value) => ScaleChanged?.Invoke(id, value);
    }
}
