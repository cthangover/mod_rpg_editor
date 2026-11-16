using System;
using Cthangover.Core.Characters;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.UI.Tool;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Contracts;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Context links open independent optional editors, explaining source and save destination before any navigation.</summary>
    public partial class RpgEditorWindow
    {
        private RpgContextControls contextControls;

        private string contextObject;

        private string contextCharacter;

        private string contextScenario;

        private void InstallContext(Control parent)
        {
            contextControls = new RpgContextControls();
            parent.AddChild(contextControls);
            contextControls.EnsureConstructed();
            contextControls.ObjectButton.Pressed += OpenContextObject;
            contextControls.CharacterButton.Pressed += OpenContextCharacter;
            contextControls.ScenarioButton.Pressed += OpenContextScenario;
            RefreshContext();
        }

        private void ReleaseContext()
        {
            if (GodotObject.IsInstanceValid(contextControls))
            {
                contextControls.ObjectButton.Pressed -= OpenContextObject;
                contextControls.CharacterButton.Pressed -= OpenContextCharacter;
                contextControls.ScenarioButton.Pressed -= OpenContextScenario;
            }
        }

        private void RefreshContext()
        {
            if (!GodotObject.IsInstanceValid(contextControls))
            {
                return;
            }

            var selected = selectedId == null ? null : scene?.ObjectOf(selectedId);
            contextObject = tool == RpgEditorTool.Paint ? brushObject : tool == RpgEditorTool.Place ? placeObject
                : document?.AnyPlacement(selectedId)?.Data?["Object"]?.ToString();
            var current = tool is RpgEditorTool.Paint or RpgEditorTool.Place ? ObjectOf(contextObject) : selected;
            contextScenario = current?.Interact?.Scenario;
            var mapEvent = document?.EventOf(selectedId);
            if (mapEvent != null && tool is not (RpgEditorTool.Paint or RpgEditorTool.Place))
            {
                contextScenario = mapEvent.Scenario;
            }

            contextCharacter = null;
            var marker = "/" + RpgCharacterActors.Category + "/";
            var offset = contextObject?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (offset >= 0)
            {
                var id = contextObject.Substring(offset + marker.Length);
                if (Entities.Collect<CharacterInfo>().ContainsKey(id))
                {
                    contextCharacter = id;
                }
            }

            ExplainEntity(contextControls.ObjectButton, "rpg_object_editor", "rpg_object", contextObject);
            ExplainEntity(contextControls.CharacterButton, "characters_editor", "character", contextCharacter);
            var source = ScenarioSource(contextScenario);
            contextControls.ScenarioButton.Disabled = source == null || ToolFactory.Instance.Get("scenario_editor") == null;
            contextControls.ScenarioButton.TooltipText = source == null ? ToolUi.Tr("tools/rpg_editor/context_no_target")
                : string.Format(ToolUi.Tr("tools/rpg_editor/context_scenario_destination"),
                    contextScenario, source.Value.Mod + "/" + source.Value.Path);
            if (ToolFactory.Instance.Get("scenario_editor") == null)
            {
                contextControls.ScenarioButton.TooltipText += "\n" + ToolUi.Tr("tools/rpg_editor/context_tool_absent");
            }
        }

        private void ExplainEntity(Button button, string editor, string kind, string id)
        {
            var source = EntitySource(kind, id);
            var reachable = source != null && (source.Value.Mod == modId || ForeignEntities.Dependencies(modId).Contains(source.Value.Mod));
            button.Disabled = !reachable || !ModFileService.IsWritable(modId) || ToolFactory.Instance.Get(editor) == null;
            if (source == null)
            {
                button.TooltipText = ToolUi.Tr("tools/rpg_editor/context_no_target");
            }
            else
            {
                var destination = source.Value.Mod == modId ? modId + "/" + source.Value.Path
                    : modId + "/" + ForeignEntities.PatchFile(kind, modId);
                button.TooltipText = string.Format(ToolUi.Tr("tools/rpg_editor/context_entity_destination"),
                    id, source.Value.Mod + "/" + source.Value.Path, destination);
                if (!reachable)
                {
                    button.TooltipText += "\n" + ToolUi.Tr("tools/rpg_editor/context_dependency");
                }
            }

            if (ToolFactory.Instance.Get(editor) == null)
            {
                button.TooltipText += "\n" + ToolUi.Tr("tools/rpg_editor/context_tool_absent");
            }
        }

        private static (string Mod, string Path)? EntitySource(string kind, string id)
        {
            var explanation = ForeignEntities.Explain(kind, id);
            if (explanation?.Fields != null && explanation.Fields.TryGetValue("Id", out var origin))
            {
                return (origin.ModId, origin.File);
            }

            return null;
        }

        private static (string Mod, string Path)? ScenarioSource(string id)
        {
            var owner = ModDependencies.ProviderOf(ModDependencies.ScenarioKind, id);
            if (owner == null)
            {
                return null;
            }

            var path = "scenarios/" + id + ".scenario";
            return ModFileService.ReadText(owner, path) == null ? null : (owner, path);
        }

        private void OpenContextObject()
        {
            if (!contextControls.ObjectButton.Disabled)
            {
                OpenContextEntity("rpg_object_editor", contextObject);
            }
        }

        private void OpenContextCharacter()
        {
            if (!contextControls.CharacterButton.Disabled)
            {
                OpenContextEntity("characters_editor", contextCharacter);
            }
        }

        private void OpenContextEntity(string toolId, string id)
        {
            var created = ToolFactory.Instance.Get(toolId)?.CreateWindow();
            if (created == null)
            {
                return;
            }

            if (created is not EntityEditorWindow editor)
            {
                created.Free();
                SetStatus(ToolUi.Tr("tools/rpg_editor/context_tool_absent"), ToolStatusKind.Error);
                return;
            }

            ToolWindow.ShowWindow(editor);
            editor.OpenMod(modId);
            var record = editor.Document?.FindAny(id);
            if (record == null)
            {
                editor.QueueFree();
                SetStatus(ToolUi.Tr("tools/rpg_editor/context_no_target"), ToolStatusKind.Error);
                return;
            }

            editor.Select(record);
        }

        private void OpenContextScenario()
        {
            if (contextControls.ScenarioButton.Disabled || ScenarioSource(contextScenario) is not { } source)
            {
                return;
            }

            var created = ToolFactory.Instance.Get("scenario_editor")?.CreateWindow();
            if (created == null)
            {
                return;
            }

            if (created is not IScenarioResourceEditor editor)
            {
                created.Free();
                SetStatus(ToolUi.Tr("tools/rpg_editor/context_tool_absent"), ToolStatusKind.Error);
                return;
            }

            ToolWindow.ShowWindow(created);
            editor.OpenScenario(source.Mod, source.Path);
        }
    }
}
