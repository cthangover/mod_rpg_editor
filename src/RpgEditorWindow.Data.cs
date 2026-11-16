using Cthangover.Mods.Rpg.Validation;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
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

        private bool ValidateDataBeforeSave()
        {
            var projections = new[]
            {
                "topdown",
                "isometric",
            };
            foreach (var projection in projections)
            {
                var report = RpgMapDataAudit.Check(document, projection, ObjectOf);
                var issue = RpgDataPolicy.FirstBlocking(report.Issues);
                if (issue == null)
                {
                    continue;
                }

                dataWidget.Show(report, true);
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/data_save_blocked"),
                    issue.Resource, issue.Field, RpgDataMessages.Explain(issue.Code)), ToolStatusKind.Error);
                return false;
            }

            return true;
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
