using System;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Rpg.Validation;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Explicit, collapsible data audit; scene replacement marks the retained report stale without per-frame scanning.</summary>
    internal sealed partial class RpgDataWidget : ModWidget
    {
        private readonly RpgEditorWindow host;

        private VBoxContainer content;

        private VBoxContainer body;

        private CheckButton toggle;

        private Button check;

        private Label summary;

        private ItemList rows;

        private Label details;

        private RpgMapDataReport report;

        private RpgEditorScene seen;

        private bool retired;

        internal event Action<RpgDataIssue> Selected;

        internal RpgDataWidget(RpgEditorWindow host)
        {
            this.host = host;
            Name = "MapDataValidation";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        protected override void Construct()
        {
            content = new VBoxContainer();
            AddChild(content);
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            toggle = new CheckButton
            {
                Name = "MapDataEnabled",
                Text = ToolUi.Tr("tools/rpg_editor/data_toggle"),
                TooltipText = ToolUi.Tr("tools/rpg_editor/data_tip"),
            };
            content.AddChild(toggle);
            body = new VBoxContainer
            {
                Visible = false,
            };
            content.AddChild(body);
            check = ToolUi.Button("tools/rpg_editor/data_run", OnCheck);
            check.Name = "MapDataCheck";
            body.AddChild(check);
            summary = ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/data_unchecked"));
            summary.Name = "MapDataSummary";
            body.AddChild(summary);
            rows = new ItemList
            {
                Name = "MapDataResults",
                CustomMinimumSize = new Vector2(0, 130),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Visible = false,
            };
            body.AddChild(rows);
            details = ToolUi.Hint("");
            details.Name = "MapDataDetails";
            body.AddChild(details);
            rows.ItemSelected += OnSelected;
            toggle.Toggled += OnToggle;
            content.MinimumSizeChanged += UpdateMinimum;
            SetProcess(false);
            UpdateMinimum();
        }

        /// <summary>Native layout is ready before the process gate and borrowed host references are observed.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
            SetProcess(toggle.ButtonPressed);
        }

        /// <summary>Compares scene identity only; checks and image decoding happen on explicit requests.</summary>
        public override void _Process(double delta)
        {
            if (retired)
            {
                return;
            }

            check.Disabled = host.Document == null;
            if (report != null && seen != host.Scene)
            {
                summary.Text = ToolUi.Tr("tools/rpg_editor/data_stale");
            }
        }

        internal void Show(RpgMapDataReport value, bool reveal = false)
        {
            EnsureConstructed();
            if (reveal)
            {
                toggle.ButtonPressed = true;
            }

            report = value;
            seen = host.Scene;
            rows.Clear();
            details.Text = "";
            var errors = 0;
            foreach (var issue in report.Issues)
            {
                var blocking = RpgDataPolicy.Blocks(issue.Code);
                if (blocking)
                {
                    errors++;
                }

                var severity = ToolUi.Tr(blocking ? "tools/rpg_editor/data_error" : "tools/rpg_editor/data_warning");
                rows.AddItem(severity + " · " + issue.Resource + " · " + issue.Field);
                rows.SetItemTooltip(rows.ItemCount - 1, Description(issue));
            }

            rows.Visible = rows.ItemCount > 0;
            summary.Text = string.Format(ToolUi.Tr("tools/rpg_editor/data_summary"),
                errors, report.Issues.Count - errors, report.ObjectsChecked, report.ImagesDecoded);
            if (!report.Complete)
            {
                summary.Text += "\n" + ToolUi.Tr("tools/rpg_editor/data_incomplete");
            }

            UpdateMinimum();
        }

        private string Description(RpgDataIssue issue)
        {
            return issue.Resource + " · " + issue.Field + "\n" + RpgDataMessages.Explain(issue.Code)
                + "\n" + issue.Detail + "\n" + issue.Source + "\n" + issue.Context
                + "\n" + string.Format(ToolUi.Tr("tools/rpg_editor/data_uses"), issue.Uses);
        }

        private void OnSelected(long index)
        {
            if (report == null || index < 0 || index >= report.Issues.Count)
            {
                return;
            }

            var issue = report.Issues[(int)index];
            details.Text = Description(issue);
            Selected?.Invoke(issue);
            UpdateMinimum();
        }

        private void OnCheck()
        {
            host.CheckData();
        }

        private void OnToggle(bool value)
        {
            body.Visible = value;
            SetProcess(value);
            check.Disabled = host.Document == null;
            UpdateMinimum();
        }

        private void UpdateMinimum()
        {
            var minimum = content.GetCombinedMinimumSize();
            if (CustomMinimumSize != minimum)
            {
                CustomMinimumSize = minimum;
            }
        }

        internal void Retire()
        {
            if (retired)
            {
                return;
            }

            retired = true;
            SetProcess(false);
            toggle.Toggled -= OnToggle;
            check.Pressed -= OnCheck;
            rows.ItemSelected -= OnSelected;
            content.MinimumSizeChanged -= UpdateMinimum;
            Selected = null;
            report = null;
            seen = null;
        }

        protected override void Destruct()
        {
            Retire();
            base.Destruct();
        }
    }
}
