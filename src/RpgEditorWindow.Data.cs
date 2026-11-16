using Cthangover.Mods.Rpg.Validation;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Connects structural checks to the unsaved document; it shares the ordinary selection and geometry overlay.</summary>
    public partial class RpgEditorWindow
    {
        private RpgDataWidget dataWidget;

        private void InstallDataCheck(Control parent)
        {
            dataWidget = new RpgDataWidget(this);
            parent.AddChild(dataWidget);
            dataWidget.EnsureConstructed();
            dataWidget.Selected += SelectDataIssue;
        }

        /// <summary>Checks used resources and effective variants/states without saving, changing history or running interactions.</summary>
        public RpgMapDataReport CheckData()
        {
            var report = document == null ? new RpgMapDataReport { Complete = false }
                : RpgMapDataAudit.Check(document, canvas.Projection, ObjectOf);
            dataWidget?.Show(report);
            return report;
        }

        private void SelectDataIssue(RpgDataIssue issue)
        {
            if (string.IsNullOrEmpty(issue.Placement) || document?.AnyPlacement(issue.Placement) == null)
            {
                return;
            }

            selectedId = issue.Placement;
            marked.Clear();
            RefreshSelection();
            if (FindChild("GeometryEnabled", true, false) is CheckButton geometry)
            {
                geometry.ButtonPressed = true;
            }
        }

        private void ReleaseDataCheck()
        {
            if (GodotObject.IsInstanceValid(dataWidget))
            {
                dataWidget.Selected -= SelectDataIssue;
                dataWidget.Retire();
            }
        }
    }
}
