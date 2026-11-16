using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Selection links remain utility controls; tool availability and source/save policy belong to the map window.</summary>
    internal sealed partial class RpgContextControls : ModWidget
    {
        private VBoxContainer content;

        internal Button ObjectButton { get; private set; }

        internal Button CharacterButton { get; private set; }

        internal Button ScenarioButton { get; private set; }

        protected override void Construct()
        {
            content = new VBoxContainer();
            AddChild(content);
            var row = new HFlowContainer();
            content.AddChild(row);
            ObjectButton = ToolUi.Button("tools/rpg_editor/context_object", null);
            ObjectButton.Name = "ContextObject";
            row.AddChild(ObjectButton);
            CharacterButton = ToolUi.Button("tools/rpg_editor/context_character", null);
            CharacterButton.Name = "ContextCharacter";
            row.AddChild(CharacterButton);
            ScenarioButton = ToolUi.Button("tools/rpg_editor/context_scenario", null);
            ScenarioButton.Name = "ContextScenario";
            row.AddChild(ScenarioButton);
            content.MinimumSizeChanged += UpdateSize;
            Resized += UpdateSize;
            UpdateSize();
        }

        private void UpdateSize()
        {
            content.Size = new Vector2(Size.X, content.GetCombinedMinimumSize().Y);
            CustomMinimumSize = content.GetCombinedMinimumSize();
        }

        protected override void Destruct()
        {
            content.MinimumSizeChanged -= UpdateSize;
            Resized -= UpdateSize;
            base.Destruct();
        }
    }
}
