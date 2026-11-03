namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.UI.Tool;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Contracts;
    using Godot;

    /// <summary>Drawing an event is a single map edit. Objects use their own form, opened in the map's content mod.</summary>
    public partial class RpgEditorWindow
    {
        private void FinishEvent()
        {
            var a = boxFrom;
            var b = boxTo ?? a;
            boxFrom = null;
            boxTo = null;
            if (a == null || b == null) return;
            selectedId = document.AddEvent(Math.Min(a.Value.X, b.Value.X), Math.Min(a.Value.Y, b.Value.Y),
                Math.Abs(a.Value.X - b.Value.X) + 1, Math.Abs(a.Value.Y - b.Value.Y) + 1);
            if (selectedId == null) { RebuildMarks(); return; }
            marked.Clear();
            SelectTool(RpgEditorTool.Select);
            AfterChange();
        }

        private void OnEventChanged(string id, JsonObject changes)
        {
            if (document?.SetEvent(id, changes) == true) AfterChange();
        }

        private bool ApplyInteraction(string id, RpgInteract wanted)
        {
            if (document.EventOf(id) == null) return RpgInteractEdit.Apply(document, id, wanted, ObjectOf);
            var node = System.Text.Json.JsonSerializer.SerializeToNode(wanted) as JsonObject;
            return node != null && document.SetEvent(id, node);
        }

        private IEnumerable<(string Kind, string Id)> EventReferences()
            => document.Events.SelectMany(e => RpgInteractReferences.Of(new JsonObject
            {
                ["Scenario"] = e.Scenario,
                ["Commands"] = e.Commands == null ? null : new JsonArray(e.Commands.Select(l => (JsonNode)JsonValue.Create(l)).ToArray()),
                ["Teleport"] = e.Teleport == null ? null : new JsonObject { ["Map"] = e.Teleport.Map }
            }));

        private void CreateMapObject()
        {
            if (document == null) return;
            var provider = ToolFactory.Instance.Get("rpg_object_editor");
            if (provider == null) return;
            var window = provider.CreateWindow();
            ToolWindow.ShowWindow(window);
            if (window is IEntityCreationEditor editor) editor.CreateNewInMod(modId);
        }
    }
}
