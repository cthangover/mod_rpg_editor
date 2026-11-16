using System;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Owns the geometry diagnostic row; the overlay supplies read-only results and owns the walking figure.</summary>
    internal sealed partial class RpgGeometryControls : ModWidget
    {
        private VBoxContainer content;

        private CheckButton toggle;

        private Button step;

        private Button play;

        private Button reset;

        private Label details;

        private string summary;

        internal event Action<bool> EnabledChanged;

        internal event Action StepRequested;

        internal event Action PlayRequested;

        internal event Action ResetRequested;

        internal RpgGeometryControls()
        {
            Name = "GeometryControls";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        protected override void Construct()
        {
            content = new VBoxContainer();
            AddChild(content);
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var row = new HBoxContainer();
            content.AddChild(row);
            toggle = new CheckButton
            {
                Name = "GeometryEnabled",
                Text = ToolUi.Tr("tools/rpg_editor/geometry_toggle"),
                TooltipText = ToolUi.Tr("tools/rpg_editor/geometry_toggle_tip"),
            };
            toggle.Toggled += OnEnabled;
            row.AddChild(toggle);
            step = ToolUi.Button("tools/rpg_editor/geometry_step", OnStep,
                tooltipKey: "tools/rpg_editor/geometry_step_tip");
            step.Name = "GeometryStep";
            row.AddChild(step);
            play = ToolUi.Button("tools/rpg_editor/geometry_play", OnPlay,
                tooltipKey: "tools/rpg_editor/geometry_play_tip");
            play.Name = "GeometryPlay";
            row.AddChild(play);
            reset = ToolUi.Button("tools/rpg_editor/geometry_reset", OnReset);
            reset.Name = "GeometryReset";
            row.AddChild(reset);
            details = new Label
            {
                Name = "GeometryDetails",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
                Visible = false,
            };
            content.AddChild(details);
            SetSummary(ToolUi.Tr("tools/rpg_editor/geometry_select"), false, false);
            content.MinimumSizeChanged += UpdateMinimum;
            UpdateMinimum();
        }

        internal void SetSummary(string text, bool available, bool running)
        {
            if (summary != text)
            {
                summary = text;
                details.Text = text;
            }
            step.Disabled = !toggle.ButtonPressed || !available || running;
            play.Disabled = !toggle.ButtonPressed || !available;
            reset.Disabled = !toggle.ButtonPressed || !available;
            play.Text = ToolUi.Tr(running ? "tools/rpg_editor/geometry_stop" : "tools/rpg_editor/geometry_play");
        }

        private void OnEnabled(bool enabled)
        {
            details.Visible = enabled;
            EnabledChanged?.Invoke(enabled);
            UpdateMinimum();
        }

        private void OnStep()
        {
            StepRequested?.Invoke();
        }

        private void OnPlay()
        {
            PlayRequested?.Invoke();
        }

        private void OnReset()
        {
            ResetRequested?.Invoke();
        }

        private void UpdateMinimum()
        {
            var minimum = content.GetCombinedMinimumSize();
            if (CustomMinimumSize != minimum)
            {
                CustomMinimumSize = minimum;
            }
        }

        /// <summary>Refreshes the utility minimum after the native theme and container widths become available.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
            UpdateMinimum();
        }

        protected override void Destruct()
        {
            content.MinimumSizeChanged -= UpdateMinimum;
            toggle.Toggled -= OnEnabled;
            step.Pressed -= OnStep;
            play.Pressed -= OnPlay;
            reset.Pressed -= OnReset;
            EnabledChanged = null;
            StepRequested = null;
            PlayRequested = null;
            ResetRequested = null;
            base.Destruct();
        }
    }
}
