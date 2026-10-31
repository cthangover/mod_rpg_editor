namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// The parts of <see cref="RpgEditorWindow"/> that serve the placement interactions and the teleport arrows (plan 14.G2,
    /// 14.G3, 14.G5): the inspector's interaction changes are written through <see cref="RpgInteractEdit"/>, a teleport's
    /// target cell is picked on the canvas, and the arrows of the map are kept in step with the document.
    /// <para/>
    /// <b>Picking a cell on another map.</b> The window edits one map at a time, so a pick on another map opens that map,
    /// waits for the click, reopens the map the teleport is on and writes the target there; that write is an ordinary
    /// unsaved change. A pick is refused while the map has unsaved changes, because opening the other map would discard them.
    /// Escape ends a pick and reopens the map it started from.
    /// </summary>
    public partial class RpgEditorWindow
    {
        private RpgTargetPick targetPick;

        /// <summary>Connects the inspector's interaction and pick events; called once while the window is built.</summary>
        private void WireInteractParts()
        {
            inspector.InteractChanged += OnInteractChanged;
            inspector.TargetPickRequested += BeginTargetPick;
        }

        private void OnInteractChanged(string placementId, RpgInteract wanted)
        {
            if (document != null && RpgInteractEdit.Apply(document, placementId, wanted, ObjectOf))
            {
                selectedId = placementId;
                AfterChange();
            }
        }

        /// <summary>
        /// Starts the pick of a teleport's target cell on <paramref name="targetMap"/>. On this map the next click picks it; on
        /// another map the map is opened first (refused while this map has unsaved changes).
        /// </summary>
        private void BeginTargetPick(string placementId, string targetMap)
        {
            if (document == null || document.Placements.All(r => r.Id != placementId))
                return;
            if (string.IsNullOrEmpty(targetMap))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/pick_no_target"), ToolStatusKind.Error);
                return;
            }
            if (string.Equals(targetMap, document.MapId, StringComparison.OrdinalIgnoreCase))
            {
                targetPick = new RpgTargetPick(placementId, document.MapId, targetMap);
                SetStatus(ToolUi.Tr("tools/rpg_editor/pick_same_map"), ToolStatusKind.Info);
                return;
            }
            if (_dirty)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/pick_save_first"), ToolStatusKind.Error);
                return;
            }
            if (FindMapFile(targetMap) is not { } found)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/link_no_file"), targetMap), ToolStatusKind.Error);
                return;
            }
            targetPick = new RpgTargetPick(placementId, document.MapId, targetMap);
            OpenFile(found.Mod, found.Path);
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/pick_on_map"), targetMap), ToolStatusKind.Info);
        }

        /// <summary>
        /// A click that picks a waiting teleport target: the cell becomes the target of the placement, keeping its trigger and
        /// facing. Returns false when no pick waits, so the click is an ordinary edit; a pick on a map the author has left is dropped.
        /// </summary>
        private bool TryPickTargetCell(Vector2I cell)
        {
            if (targetPick == null || document == null)
                return false;
            var pick = targetPick;
            targetPick = null;
            if (!string.Equals(pick.TargetMap, document.MapId, StringComparison.OrdinalIgnoreCase))
                return false;

            if (!string.Equals(pick.SourceMap, document.MapId, StringComparison.OrdinalIgnoreCase))
            {
                if (FindMapFile(pick.SourceMap) is not { } source)
                {
                    SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/link_no_file"), pick.SourceMap), ToolStatusKind.Error);
                    return true;
                }
                OpenFile(source.Mod, source.Path);
                if (document == null || !string.Equals(document.MapId, pick.SourceMap, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var current = scene?.ObjectOf(pick.Placement)?.Interact;
            var wanted = RpgInteractEdit.Make(RpgInteractKind.Map, current?.Trigger ?? RpgInteractTrigger.Use, null, 1,
                new RpgTeleport { Map = pick.TargetMap, X = cell.X, Y = cell.Y, Dir = current?.Teleport?.Dir ?? RpgDirection.Down });
            if (!RpgInteractEdit.Apply(document, pick.Placement, wanted, ObjectOf))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/pick_not_written"), ToolStatusKind.Error);
                return true;
            }
            selectedId = pick.Placement;
            AfterChange();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/pick_done"), pick.TargetMap, cell.X, cell.Y), ToolStatusKind.Ok);
            return true;
        }

        /// <summary>Escape during a pick: the pick ends and the map it started from is shown again. False when no pick waits.</summary>
        private bool CancelTargetPick()
        {
            if (targetPick == null)
                return false;
            var pick = targetPick;
            targetPick = null;
            if (document != null && !string.Equals(pick.SourceMap, document.MapId, StringComparison.OrdinalIgnoreCase)
                && FindMapFile(pick.SourceMap) is { } back)
                OpenFile(back.Mod, back.Path);
            SetStatus(ToolUi.Tr("tools/rpg_editor/pick_cancelled"), ToolStatusKind.Info);
            return true;
        }

        /// <summary>The variant names the loaded objects define, sorted: the choices of the map's variant picker.</summary>
        private List<string> VariantNames()
            => objectTable.Values
                .Where(o => o.Variants != null)
                .SelectMany(o => o.Variants.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToList();

        /// <summary>The interaction of the selected placement as the game reads it, or null.</summary>
        private RpgInteract InteractOfSelected()
            => selectedId == null ? null : scene?.ObjectOf(selectedId)?.Interact;

        /// <summary>Draws the teleport arrows of the map: the outgoing links from the placements and the incoming ones.</summary>
        private void UpdateArrows(string mapId, IEnumerable<RpgTeleportLink> outgoing, IEnumerable<RpgTeleportLink> incoming)
        {
            canvas.Arrows.Clear();
            canvas.Arrows.AddRange(RpgTeleportArrow.Of(mapId, new Vector2I(scene.Map.Width, scene.Map.Height), outgoing, incoming));
            canvas.Refresh();
        }
    }
}
