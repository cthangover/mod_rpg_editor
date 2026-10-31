namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Runtime.CompilerServices;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// The state preview of a selected placement (plan 14.G11): the <c>self.</c> flags that its object's states and its
    /// interaction read, as check boxes, and the state that applies to those values. A check box sets the flag to 1 or 0
    /// for this preview only: the values live in the editor, per map document, and are never saved to a file or written
    /// into the game's story flags. The state is chosen the way the game chooses it (<see cref="RpgObjectResolve.ActiveState(RpgObject, Func{string, string, bool}, string)"/>),
    /// with each <c>self.&lt;name&gt;</c> of a condition replaced by its preview value (unset is 0) before
    /// <see cref="ScenarioCondition.Evaluate(string, string)"/> runs.
    /// <para>
    /// The canvas still draws the object's own look; the state switch is shown here. Placement-level <c>States$add</c>
    /// overrides are not part of the preview (the object's own states are).
    /// </para>
    /// </summary>
    public partial class RpgStatePreview : VBoxContainer
    {
        private static readonly Regex SelfToken = new(@"\bself\.([A-Za-z_][A-Za-z0-9_]*)");
        private static readonly Regex SelfCompare = new(@"\bself\.([A-Za-z_][A-Za-z0-9_]*)\s*(==|!=|<=|>=|<|>)\s*(-?\d+(?:\.\d+)?)");

        private static readonly ConditionalWeakTable<RpgMapDocument, Dictionary<string, Dictionary<string, string>>> Values = new();

        private readonly Label stateLabel;
        private readonly RpgObject effective;
        private readonly Dictionary<string, string> values;
        private readonly string placementId;

        /// <summary>Builds the preview of <paramref name="placementId"/> placed as <paramref name="objectId"/> on <paramref name="document"/>.</summary>
        public RpgStatePreview(RpgMapDocument document, string placementId, string objectId)
        {
            this.placementId = placementId;
            Name = "StatePreview";
            var store = Values.GetValue(document, _ => new Dictionary<string, Dictionary<string, string>>());
            if (!store.TryGetValue(placementId, out values))
            {
                values = new Dictionary<string, string>();
                store[placementId] = values;
            }
            RpgObject resolved = null;
            if (objectId != null)
                Entities.Collect<RpgObject>().TryGetValue(objectId, out resolved);
            effective = resolved == null ? null : RpgObjectResolve.Effective(resolved, null, null);

            AddChild(ToolUi.Section("tools/rpg_editor/preview_title"));
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/preview_hint")));
            stateLabel = ToolUi.Hint(string.Empty);
            stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            AddChild(stateLabel);

            var names = SelfNames(effective);
            if (names.Count == 0)
                AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/preview_no_flags")));
            foreach (var name in names)
            {
                var box = new CheckBox { Name = "Preview_" + name, Text = ScenarioCondition.SelfPrefix + "." + name, ButtonPressed = Value(name) == "1" };
                var flag = name;
                box.Toggled += on =>
                {
                    values[flag] = on ? "1" : "0";
                    Refresh();
                };
                AddChild(box);
            }
            if (names.Count > 0)
            {
                AddChild(ToolUi.Button("tools/rpg_editor/preview_reset", Reset, ButtonKind.Secondary, "tools/rpg_editor/preview_reset_tip"));
            }
            Refresh();
        }

        /// <summary>The text the preview shows as the state that applies (read by the window's tests).</summary>
        public string StateText => stateLabel?.Text ?? string.Empty;

        /// <summary>The preview value of a flag of this placement (unset reads as 0).</summary>
        private string Value(string name) => values.TryGetValue(name, out var value) ? value : "0";

        /// <summary>Sets every preview flag of the placement back to 0 and shows the base state again.</summary>
        private void Reset()
        {
            values.Clear();
            foreach (var child in GetChildren())
            {
                if (child is CheckBox box)
                    box.SetPressedNoSignal(false);
            }
            Refresh();
        }

        /// <summary>Re-chooses the active state from the preview values and shows it.</summary>
        private void Refresh()
        {
            if (stateLabel == null)
                return;
            if (effective == null)
            {
                stateLabel.Text = ToolUi.Tr("tools/rpg_editor/preview_no_object");
                return;
            }
            var index = RpgObjectResolve.ActiveState(effective, (when, scope) => Holds(when), placementId);
            stateLabel.Text = index >= 0 && effective.States[index]?.Id is { } id
                ? string.Format(ToolUi.Tr("tools/rpg_editor/preview_state"), id)
                : ToolUi.Tr("tools/rpg_editor/preview_base");
        }

        /// <summary>Evaluates a state condition with the placement's preview values in place of its <c>self.</c> names.</summary>
        private bool Holds(string when)
        {
            // The condition parser takes a flag on the left of a comparison, not a number: each self. comparison is decided
            // here, and the rest (a bare self. name) becomes a boolean literal before the game's parser runs.
            var text = SelfCompare.Replace(when, match => Compare(Value(match.Groups[1].Value), match.Groups[2].Value, match.Groups[3].Value) ? "true" : "false");
            text = SelfToken.Replace(text, match => Truthy(Value(match.Groups[1].Value)) ? "true" : "false");
            return ScenarioCondition.Evaluate(text, null);
        }

        /// <summary>Compares a preview value with a number of the condition (invariant culture); text values compare as text for == and !=.</summary>
        private static bool Compare(string value, string op, string literal)
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var left)
                && double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out var right))
            {
                return op switch
                {
                    "==" => left == right,
                    "!=" => left != right,
                    "<" => left < right,
                    ">" => left > right,
                    "<=" => left <= right,
                    _ => left >= right,
                };
            }
            return op == "==" ? value == literal : op == "!=" && value != literal;
        }

        /// <summary>A bare <c>self.</c> name holds when its preview value is set and not 0.</summary>
        private static bool Truthy(string value) => !string.IsNullOrEmpty(value) && value != "0";

        /// <summary>
        /// The command lines of a state's own <c>Interact</c>. A state keeps its components as JSON (<see cref="RpgObjectState.Components"/>),
        /// so the commands are read from the <c>Interact</c> entry; a state without one gives nothing.
        /// </summary>
        private static IEnumerable<string> StateCommands(RpgObjectState state)
        {
            if (state?.Components == null || !state.Components.TryGetValue("Interact", out var interact) || interact.ValueKind != JsonValueKind.Object)
                yield break;
            if (!interact.TryGetProperty("Commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
                yield break;
            foreach (var command in commands.EnumerateArray())
            {
                if (command.ValueKind == JsonValueKind.String && command.GetString() is { } text)
                    yield return text;
            }
        }

        /// <summary>The distinct <c>self.</c> names the object's states and interaction read, in order of appearance.</summary>
        public static List<string> SelfNames(RpgObject obj)
        {
            var result = new List<string>();
            if (obj == null)
                return result;
            var texts = new List<string>();
            if (obj.Interact?.Commands != null)
                texts.AddRange(obj.Interact.Commands);
            if (obj.States != null)
            {
                foreach (var state in obj.States)
                {
                    if (!string.IsNullOrEmpty(state?.When))
                        texts.Add(state.When);
                    texts.AddRange(StateCommands(state));
                }
            }
            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text))
                    continue;
                foreach (Match match in SelfToken.Matches(text))
                {
                    var name = match.Groups[1].Value;
                    if (!result.Contains(name))
                        result.Add(name);
                }
            }
            return result;
        }
    }
}
