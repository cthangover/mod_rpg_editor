using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Collapsible ModWidget utility controls borrow no pictures; the window owns library and document policy.</summary>
    internal sealed partial class RpgStampControls : ModWidget
    {
        private VBoxContainer column;

        private CheckButton toggle;

        private VBoxContainer body;

        internal LineEdit NameInput { get; private set; }

        internal OptionButton Picker { get; private set; }

        internal Button CaptureButton { get; private set; }

        internal Button PasteButton { get; private set; }

        internal Label Details { get; private set; }

        protected override void Construct()
        {
            column = new VBoxContainer();
            AddChild(column);
            toggle = new CheckButton
            {
                Name = "StampToggle",
                Text = ToolUi.Tr("tools/rpg_editor/stamps"),
                TooltipText = ToolUi.Tr("tools/rpg_editor/stamps_tip"),
            };
            column.AddChild(toggle);
            body = new VBoxContainer { Visible = false };
            column.AddChild(body);
            var naming = new HBoxContainer();
            NameInput = new LineEdit
            {
                Name = "StampName",
                PlaceholderText = ToolUi.Tr("tools/rpg_editor/stamp_name"),
                TooltipText = ToolUi.Tr("tools/rpg_editor/stamp_name"),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                MaxLength = 64,
            };
            naming.AddChild(NameInput);
            CaptureButton = ToolUi.Button("tools/rpg_editor/stamp_capture", null);
            CaptureButton.Name = "CaptureStamp";
            naming.AddChild(CaptureButton);
            body.AddChild(naming);
            Picker = new OptionButton
            {
                Name = "StampPicker",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            body.AddChild(Picker);
            Details = new Label
            {
                Name = "StampDetails",
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(280, 0),
            };
            body.AddChild(Details);
            PasteButton = ToolUi.Button("tools/rpg_editor/stamp_paste", null);
            PasteButton.Name = "PasteStamp";
            body.AddChild(PasteButton);
            toggle.Toggled += OnToggled;
            column.MinimumSizeChanged += OnResized;
            Resized += OnResized;
            OnResized();
        }

        private void OnToggled(bool expanded)
        {
            body.Visible = expanded;
            CustomMinimumSize = GetChild<Control>(0).GetCombinedMinimumSize();
        }

        private void OnResized()
        {
            column.Size = new Vector2(Size.X, column.GetCombinedMinimumSize().Y);
            CustomMinimumSize = column.GetCombinedMinimumSize();
        }

        protected override void Destruct()
        {
            toggle.Toggled -= OnToggled;
            Resized -= OnResized;
            column.MinimumSizeChanged -= OnResized;
            base.Destruct();
        }
    }
}
