using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Selected placement's detached state inputs, including map/placement variants and added states.
    /// Changes request a canvas rebuild without document edits, story flags or interaction execution.
    /// The document-local owner survives inspector rebuilds and isolates different placements.
    /// </summary>
    public partial class RpgStatePreview : ModWidget
    {
        private static readonly Regex SelfToken = new(@"\bself\.([A-Za-z_][A-Za-z0-9_]*)");
        private readonly string placementId;
        private readonly RpgEditorPreview preview;
        private readonly RpgObject effective;
        private readonly Dictionary<string, CheckBox> boxes = new(StringComparer.Ordinal);
        private Label stateLabel;
        private VBoxContainer content;
        /// <summary>Requests presentation refresh only; the host must preserve this inspector while rebuilding the canvas.</summary>
        public event Action Changed;
        /// <summary>Reads the document's actual placement overrides and map variant before creating any controls.</summary>
        public RpgStatePreview(RpgMapDocument document, string placementId, string objectId)
        {
            this.placementId = placementId;
            preview = RpgEditorPreview.ForDocument(document);
            Name = "StatePreview";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            RpgObject resolved = null;
            if (objectId != null)
            {
                Entities.Collect<RpgObject>().TryGetValue(objectId, out resolved);
            }

            RpgPlacement placement = null;
            foreach (var record in document.Placements)
            {
                if (record.Id == placementId)
                {
                    placement = RpgEditorScene.PlacementOf(record.Data);
                    break;
                }
            }

            effective = RpgObjectResolve.Effective(resolved, RpgEditorScene.MapOf(document.Data).Variant, placement);
        }

        /// <summary>Localized chosen-state text for the same effective object and detached values as the canvas.</summary>
        public string StateText
        {
            get
            {
                EnsureConstructed();
                return stateLabel.Text;
            }
        }

        /// <summary>Completes utility construction after native attachment; repeated access uses the same controls.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
        }

        protected override void Construct()
        {
            content = new VBoxContainer();
            AddChild(content);
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            content.AddChild(ToolUi.Section("tools/rpg_editor/preview_title"));
            content.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/preview_hint")));
            stateLabel = ToolUi.Hint(string.Empty);
            stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            content.AddChild(stateLabel);
            var names = SelfNames(effective);
            if (names.Count == 0)
            {
                content.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/preview_no_flags")));
            }

            foreach (var name in names)
            {
                var box = new CheckBox
                {
                    Name = "Preview_" + name,
                    Text = "self." + name,
                    ButtonPressed = preview.IsSet(placementId, name),
                };
                boxes[name] = box;
                box.Toggled += OnToggled;
                content.AddChild(box);
            }

            if (names.Count > 0)
            {
                content.AddChild(ToolUi.Button("tools/rpg_editor/preview_reset", Reset, ButtonKind.Secondary,
                    "tools/rpg_editor/preview_reset_tip"));
            }

            content.MinimumSizeChanged += UpdateMinimum;
            UpdateMinimum();
            Refresh();
        }

        private void OnToggled(bool enabled)
        {
            foreach (var pair in boxes)
            {
                preview.Set(placementId, pair.Key, pair.Value.ButtonPressed);
            }

            Refresh();
            Changed?.Invoke();
        }

        private void Reset()
        {
            preview.Reset(placementId);
            foreach (var box in boxes.Values)
            {
                box.SetPressedNoSignal(false);
            }

            Refresh();
            Changed?.Invoke();
        }

        private void Refresh()
        {
            if (effective == null)
            {
                stateLabel.Text = ToolUi.Tr("tools/rpg_editor/preview_no_object");
                return;
            }

            var index = preview.StateIndex(effective, placementId);
            stateLabel.Text = index >= 0 && effective.States[index]?.Id is { } id
                ? string.Format(ToolUi.Tr("tools/rpg_editor/preview_state"), id)
                : ToolUi.Tr("tools/rpg_editor/preview_base");
        }

        private void UpdateMinimum()
        {
            var minimum = content.GetCombinedMinimumSize();
            if (CustomMinimumSize != minimum)
            {
                CustomMinimumSize = minimum;
            }
        }

        protected override void Destruct()
        {
            content.MinimumSizeChanged -= UpdateMinimum;
            foreach (var box in boxes.Values)
            {
                box.Toggled -= OnToggled;
            }

            Changed = null;
            base.Destruct();
        }

        private static IEnumerable<string> StateCommands(RpgObjectState state)
        {
            if (state?.Components == null)
            {
                yield break;
            }

            foreach (var pair in state.Components)
            {
                if (!pair.Key.Equals("Interact", StringComparison.OrdinalIgnoreCase)
                    || pair.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                foreach (var property in pair.Value.EnumerateObject())
                {
                    if (!property.Name.Equals("Commands", StringComparison.OrdinalIgnoreCase)
                        || property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    foreach (var command in property.Value.EnumerateArray())
                    {
                        if (command.ValueKind == JsonValueKind.String && command.GetString() is { } text)
                        {
                            yield return text;
                        }
                    }
                }
            }
        }

        /// <summary>The distinct <c>self.</c> names the object's states and interaction read, in order of appearance.</summary>
        public static List<string> SelfNames(RpgObject obj)
        {
            var result = new List<string>();
            if (obj == null)
            {
                return result;
            }

            var texts = new List<string>();
            if (obj.Interact?.Commands != null)
            {
                texts.AddRange(obj.Interact.Commands);
            }

            if (obj.States != null)
            {
                foreach (var state in obj.States)
                {
                    if (!string.IsNullOrEmpty(state?.When))
                    {
                        texts.Add(state.When);
                    }

                    texts.AddRange(StateCommands(state));
                }
            }

            foreach (var text in texts)
            {
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                foreach (Match match in SelfToken.Matches(text))
                {
                    var name = match.Groups[1].Value;
                    if (!result.Contains(name))
                    {
                        result.Add(name);
                    }
                }
            }

            return result;
        }
    }
}
