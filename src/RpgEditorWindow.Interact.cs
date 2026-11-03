namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Mods.Resolvers;
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
            canvas.ArrowPressed += OnArrowPressed;
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
            if (document == null || !document.SelectionIds.Contains(placementId))
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
            if (!ApplyInteraction(pick.Placement, wanted))
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

        /// <summary>
        /// A press on a teleport arrow (plan 14.G18), with the Select tool and no target pick waiting. An incoming arrow selects its
        /// arrival: a drag moves the arrival cell. The head of an arrow to a cell of this map is the target handle: a drag moves the
        /// target. Any other press selects the arrow's placement (a door), and a drag moves the door from the cell it follows.
        /// Other tools and a waiting pick treat the press as the cell it is on.
        /// </summary>
        private void OnArrowPressed(int index, RpgArrowHandle handle, Vector2I cell, MouseButton button)
        {
            if (document == null || index < 0 || index >= canvas.Arrows.Count || button != MouseButton.Left
                || tool != RpgEditorTool.Select || targetPick != null)
            {
                if (cell.X >= 0 && cell.Y >= 0)
                    OnCellPressed(cell, button, false);
                return;
            }
            var arrow = canvas.Arrows[index];
            handleKind = RpgHandleKind.None;
            handlePlacement = null;
            dragOrigin = null;
            dragTo = null;
            lastPressCell = null;
            marked.Clear();
            boxFrom = null;
            boxTo = null;
            if (arrow.Incoming)
            {
                selectedId = null;
                arrivalId = arrow.Placement;
                handleKind = RpgHandleKind.Arrival;
                handlePlacement = arrow.Placement;
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/arrival_selected"), arrow.Placement), ToolStatusKind.Info);
                RefreshSelection();
                return;
            }
            if (arrow.Local && handle == RpgArrowHandle.Head)
            {
                selectedId = arrow.Placement;
                arrivalId = null;
                handleKind = RpgHandleKind.Target;
                handlePlacement = arrow.Placement;
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/target_selected"), arrow.Placement), ToolStatusKind.Info);
                RefreshSelection();
                return;
            }
            // The door: the arrow starts at its placement's cell, so the drag starts there and the door follows the pointer.
            selectedId = arrow.Placement;
            arrivalId = null;
            GrabPlacement(new Vector2I((int)Mathf.Floor(arrow.From.X), (int)Mathf.Floor(arrow.From.Y)));
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/picked"), arrow.Placement), ToolStatusKind.Info);
            RefreshSelection();
        }

        /// <summary>
        /// Ends the drag of a teleport handle (plan 14.G18). A handle writes only when the pointer has moved (a click changes
        /// nothing) and the released cell is walkable on this map; a blocked cell or one off the map is refused with its reason.
        /// The write is an ordinary change: one undo step, saved with the map's other files.
        /// </summary>
        private void FinishHandle(Vector2I cell)
        {
            var kind = handleKind;
            var placement = handlePlacement;
            var moved = dragTo != null;
            handleKind = RpgHandleKind.None;
            handlePlacement = null;
            dragTo = null;
            if (!moved || placement == null || cell.X < 0 || cell.Y < 0)
            {
                RebuildMarks();
                return;
            }
            var blocked = RpgTeleportLinks.BrokenOf(new RpgTeleport { Map = document.MapId, X = cell.X, Y = cell.Y }, KnownMaps()) != null;
            if (blocked)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/handle_blocked"), ToolStatusKind.Error);
                RebuildMarks();
                return;
            }
            bool written;
            if (document.HasOwnTeleport(placement))
                written = document.SetTeleportCell(placement, cell.X, cell.Y);
            else if (kind == RpgHandleKind.Target)
                written = WriteTargetOfObject(placement, cell);
            else
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/arrival_refused"), ToolStatusKind.Error);
                RebuildMarks();
                return;
            }
            if (!written)
            {
                RebuildMarks();
                return;
            }
            if (kind == RpgHandleKind.Arrival)
            {
                arrivalId = placement;
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/arrival_written"), cell.X, cell.Y, RpgMapDocument.MapOfPlacement(placement)), ToolStatusKind.Ok);
            }
            else
            {
                selectedId = placement;
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/target_written"), cell.X, cell.Y), ToolStatusKind.Ok);
            }
            AfterChange();
        }

        /// <summary>
        /// A target whose teleport comes from its object: the placement gets its own teleport to the cell, as the target pick writes
        /// it (the object's other interaction fields are cleared for the placement, see <see cref="RpgInteractEdit.OverrideOf"/>).
        /// </summary>
        private bool WriteTargetOfObject(string placement, Vector2I cell)
        {
            var current = scene?.ObjectOf(placement)?.Interact;
            var wanted = RpgInteractEdit.Make(RpgInteractKind.Map, current?.Trigger ?? RpgInteractTrigger.Use, null, 1,
                new RpgTeleport { Map = document.MapId, X = cell.X, Y = cell.Y, Dir = current?.Teleport?.Dir ?? RpgDirection.Down });
            return ApplyInteraction(placement, wanted);
        }

        /// <summary>The maps the editor knows: every map of the game, with this map as the document has it now.</summary>
        private Dictionary<string, RpgMap> KnownMaps()
        {
            var maps = Entities.Collect<RpgMap>();
            if (document != null)
                foreach (var record in document.MapRecords.Where(r => r.Data != null))
                    maps[record.Id] = RpgEditorScene.MapOf(record.Data);
            if (document != null && scene != null)
                maps[document.MapId] = scene.Map;
            return maps;
        }

        /// <summary>
        /// The placements of the other maps with the maps they belong to, as the document has them now: a teleport moved here
        /// shows at once, before the save. Placements the document does not hold are those the game has (saved, not in the document).
        /// </summary>
        private List<(RpgPlacement Placement, RpgMap Map)> PlacementsBeyond(string mapId, IReadOnlyDictionary<string, RpgMap> maps)
        {
            var result = new List<(RpgPlacement Placement, RpgMap Map)>();
            var known = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in document.PlacementRecords)
            {
                var owner = RpgMapDocument.MapOfPlacement(record.Id);
                if (record.Data == null || owner == null || owner == mapId)
                    continue;
                known.Add(record.Id);
                result.Add((RpgEditorScene.PlacementOf(record.Data), maps.TryGetValue(owner, out var map) ? map : null));
            }
            foreach (var placement in Entities.Collect<RpgPlacement>().Values)
            {
                var owner = RpgMapDocument.MapOfPlacement(placement.ID);
                if (owner == null || owner == mapId || known.Contains(placement.ID) || document.IsDeletedAny(placement.ID))
                    continue;
                result.Add((placement, maps.TryGetValue(owner, out var map) ? map : null));
            }
            foreach (var map in maps.Values.Where(m => m.ID != mapId))
                foreach (var entry in map.Events ?? new List<RpgMapEvent>())
                    result.Add((entry.AsPlacement(map.ID), map));
            return result;
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
