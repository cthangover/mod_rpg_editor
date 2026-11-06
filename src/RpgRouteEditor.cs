namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Mods.Rpg;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>Edits the selected actor's placement route; changes use the map document's undo/save path.</summary>
    public partial class RpgRouteEditor : VBoxContainer
    {
        private readonly string placement;
        private readonly TextEdit steps = new();
        private readonly OptionButton command = new();
        private readonly SpinBox speed = new() { MinValue = 1, MaxValue = 12, Step = 0.1 };
        private readonly SpinBox interval = new() { MinValue = 0.1, MaxValue = 60, Step = 0.1 };
        private readonly SpinBox pause = new() { MinValue = 0.1, MaxValue = 600, Step = 0.1, Value = 5 };
        private readonly CheckBox repeat = new();
        private readonly Label status = new();
        private readonly RpgMoveRoute original;
        private static readonly string[] Commands = { "up", "down", "left", "right", "wait", "hide", "reset", "show" };
        /// <summary>Validated route override, ready to be written as one undo step.</summary>
        public event Action<string, JsonNode> RouteChanged;

        /// <summary>Creates controls for one actor, including its inherited route when no override exists.</summary>
        public RpgRouteEditor(string id, RpgMoveRoute route)
        {
            Name = "ActorRoute";
            placement = id;
            original = route;
            AddChild(ToolUi.Section("tools/rpg_editor/route_title"));
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/route_help")));
            steps.CustomMinimumSize = new Vector2(0, 130);
            steps.Text = string.Join("\n", route?.Steps ?? new());
            AddChild(steps);
            foreach (var value in Commands) command.AddItem(value);
            AddChild(command);
            var add = new Button { Text = ToolUi.Tr("tools/rpg_editor/route_add") };
            add.Name = "AddRouteInstruction";
            add.Pressed += AddCommand;
            AddChild(add);
            ToolUi.Field(this, "tools/rpg_editor/route_pause", pause);
            var wait = new Button { Text = ToolUi.Tr("tools/rpg_editor/route_add_pause") };
            wait.Pressed += AddPause;
            AddChild(wait);
            speed.Value = route?.Speed ?? 1.3;
            interval.Value = route?.Every ?? 1.2;
            ToolUi.Field(this, "tools/rpg_editor/route_speed", speed);
            ToolUi.Field(this, "tools/rpg_editor/route_interval", interval);
            repeat.Text = ToolUi.Tr("tools/rpg_editor/route_repeat");
            repeat.ButtonPressed = route?.Repeat ?? true;
            AddChild(repeat);
            var save = new Button { Text = ToolUi.Tr("tools/rpg_editor/route_apply") };
            save.Pressed += Apply;
            AddChild(save);
            AddChild(status);
            InitializeVariants();
        }

        private void AddCommand() => Append(Commands[command.Selected]);
        private void AddPause() => Append("wait:" + pause.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        private void Append(string instruction)
        {
            steps.Text = steps.Text.TrimEnd() + (steps.Text.Trim().Length == 0 ? "" : "\n") + instruction;
            VariantChanged();
        }
        private void Apply()
        {
            var values = steps.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            foreach (var value in Preview.Steps.Concat(Preview.Variants.SelectMany(v => v)))
            {
                if (Commands.Contains(value) || value is "random" or "toward" or "away" or "face_up" or "face_down" or "face_left" or "face_right") continue;
                if (value.StartsWith("wait:") && double.TryParse(value.Substring(5), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds) && double.IsFinite(seconds) && seconds >= 0.1 && seconds <= 600) continue;
                status.Text = ToolUi.Tr("tools/rpg_editor/route_invalid") + ": " + value;
                return;
            }
            var route = System.Text.Json.JsonSerializer.SerializeToNode(Preview).AsObject();
            var list = new JsonArray();
            foreach (var value in values) list.Add(value);
            if (Variant < 0) route["Steps"] = list;
            route["Speed"] = speed.Value;
            route["Every"] = interval.Value;
            route["Repeat"] = repeat.ButtonPressed;
            route["RetryBlocked"] = true;
            RouteChanged?.Invoke(placement, route);
        }
    }
}
