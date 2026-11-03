namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>A compact event form: rectangle, trigger, condition and the scenario itself; Apply commits one undo step.</summary>
    public partial class RpgEditorInspector
    {
        private void AddEvent(State state, RpgMapEvent entry)
        {
            var id = entry.Scope(state.Document.MapId);
            AddChild(ToolUi.Section("tools/rpg_editor/event_title"));
            AddChild(ToolUi.Hint(id));
            var x = EventNumber("EventX", entry.X, state.MapSize.X - 1, "tools/rpg_editor/placement_x", 0);
            var y = EventNumber("EventY", entry.Y, state.MapSize.Y - 1, "tools/rpg_editor/placement_y", 0);
            var width = EventNumber("EventWidth", entry.Width, state.MapSize.X, "tools/rpg_editor/event_width", 1);
            var height = EventNumber("EventHeight", entry.Height, state.MapSize.Y, "tools/rpg_editor/event_height", 1);
            var trigger = new OptionButton { Name = "EventTrigger" };
            trigger.AddItem(ToolUi.Tr("tools/rpg_editor/interact_trigger_use"));
            trigger.AddItem(ToolUi.Tr("tools/rpg_editor/interact_trigger_step"));
            trigger.Select(entry.Trigger == RpgInteractTrigger.Step ? 1 : 0);
            ToolUi.Field(this, "tools/rpg_editor/interact_trigger", trigger);
            var options = new VBoxContainer { Visible = entry.Teleport != null || entry.Solid || !string.IsNullOrEmpty(entry.When) || !string.IsNullOrEmpty(entry.Scenario) };
            var toggle = new CheckBox { Text = ToolUi.Tr("tools/rpg_editor/event_options"), ButtonPressed = options.Visible };
            toggle.Toggled += value => options.Visible = value;
            AddChild(toggle);
            AddChild(options);
            var when = new LineEdit { Name = "EventWhen", Text = entry.When ?? string.Empty };
            ToolUi.Field(options, "tools/rpg_editor/event_when", when);
            var solid = new CheckBox { Name = "EventSolid", Text = ToolUi.Tr("tools/rpg_editor/event_solid"), ButtonPressed = entry.Solid };
            options.AddChild(solid);
            var scenario = Picker("EventScenario", "tools/rpg_editor/interact_scenario_tip");
            FillScenarios(scenario, entry.Scenario);
            // Empty is always selectable, including when a scenario was previously assigned.
            scenario.AddItem(ToolUi.Tr("tools/rpg_editor/event_inline_only"));
            scenario.SetItemMetadata(scenario.ItemCount - 1, string.Empty);
            SelectValue(scenario, entry.Scenario);
            ToolUi.Field(options, "tools/rpg_editor/interact_scenario", scenario);
            var target = Picker("EventTargetMap", "tools/rpg_editor/interact_map_tip");
            FillEntities(target, MapKind, entry.Teleport?.Map);
            target.AddItem(ToolUi.Tr("tools/rpg_editor/event_no_teleport"));
            target.SetItemMetadata(target.ItemCount - 1, string.Empty);
            SelectValue(target, entry.Teleport?.Map);
            ToolUi.Field(options, "tools/rpg_editor/interact_map", target);
            var destination = new VBoxContainer { Visible = entry.Teleport != null };
            options.AddChild(destination);
            target.ItemSelected += _ => destination.Visible = PickedValue(target) != null;
            var tx = EventNumber("EventTargetX", entry.Teleport?.X ?? 0, 9999, "tools/rpg_editor/interact_x", 0, destination);
            var ty = EventNumber("EventTargetY", entry.Teleport?.Y ?? 0, 9999, "tools/rpg_editor/interact_y", 0, destination);
            var direction = new OptionButton { Name = "EventTargetDir" };
            foreach (var dir in Directions) direction.AddItem(ToolUi.Tr(DirKey(dir)));
            direction.Select(Array.IndexOf(Directions, entry.Teleport?.Dir ?? RpgDirection.Down));
            ToolUi.Field(destination, "tools/rpg_editor/interact_dir", direction);
            var pick = ToolUi.Button("tools/rpg_editor/interact_pick", () => TargetPickRequested?.Invoke(id, PickedValue(target)),
                ButtonKind.Secondary, "tools/rpg_editor/interact_pick_tip");
            pick.Name = "EventTargetPick";
            destination.AddChild(pick);
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/event_script_hint")));
            var script = new CodeEdit
            {
                Name = "EventScript", Text = string.Join("\n", entry.Commands ?? new()),
                CustomMinimumSize = new Vector2(0, 170), SizeFlagsHorizontal = SizeFlags.ExpandFill,
                WrapMode = TextEdit.LineWrappingMode.Boundary
            };
            AddChild(script);
            var error = new Label { Name = "EventError", AutowrapMode = TextServer.AutowrapMode.WordSmart };
            AddChild(error);
            var apply = ToolUi.Button("tools/rpg_editor/event_apply", () =>
            {
                if (x.Value + width.Value > state.MapSize.X || y.Value + height.Value > state.MapSize.Y)
                {
                    error.Text = string.Format(ToolUi.Tr("tools/rpg_editor/event_invalid"), entry.Id);
                    return;
                }
                EventChanged?.Invoke(id, new JsonObject
                {
                    ["X"] = (int)x.Value, ["Y"] = (int)y.Value, ["Width"] = (int)width.Value, ["Height"] = (int)height.Value,
                    ["Trigger"] = trigger.Selected == 1 ? "Step" : "Use", ["When"] = Text(when.Text),
                    ["Solid"] = solid.ButtonPressed ? JsonValue.Create(true) : null,
                    ["Scenario"] = Text(PickedValue(scenario)),
                    ["Teleport"] = PickedValue(target) == null ? null : new JsonObject
                    {
                        ["Map"] = PickedValue(target), ["X"] = (int)tx.Value, ["Y"] = (int)ty.Value,
                        ["Dir"] = Directions[direction.Selected].ToString()
                    },
                    ["Commands"] = string.IsNullOrWhiteSpace(script.Text) ? null
                        : new JsonArray(script.Text.Replace("\r", string.Empty).Split('\n').Select(l => (JsonNode)JsonValue.Create(l)).ToArray())
                });
            }, ButtonKind.Primary);
            apply.Name = "ApplyEvent";
            AddChild(apply);
            AddChild(ToolUi.Button("tools/rpg_editor/placement_delete", () => PlacementDeleted?.Invoke(id), ButtonKind.Danger));
        }

        private SpinBox EventNumber(string name, int value, int max, string label, int min, Container host = null)
        {
            var spin = ToolUi.Number(min, Math.Max(min, max), 1, null);
            spin.Name = name;
            spin.Value = value;
            ToolUi.Field(host ?? this, label, spin);
            return spin;
        }
    }
}
