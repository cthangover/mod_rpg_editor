using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Cthangover.Core.Events;
using Cthangover.Core.Games;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Effects.Atmosphere;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.Rpg.Render;
using Cthangover.Mods.Schemas;
using Cthangover.Mods.TileRender;
using Cthangover.Tools.Common;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Contracts;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// The RPG map editor (plan 12.4) on the model v2: a palette of objects by shape, the map canvas in the game's own
    /// projections (switchable while editing), selection and moves of placements, the map's properties, teleport links,
    /// and — when <c>rpg__battles</c> is mounted — the encounters. Every change goes through <see cref="RpgMapDocument"/>,
    /// so undo and redo (Ctrl+Z, Ctrl+Y / Ctrl+Shift+Z) and the byte-stable save are the document's. Ctrl+S saves.
    /// <para/>
    /// A new map is made with the file selector's "+": it is written on the first save with its scene file, so the running
    /// game knows the map at once (<see cref="SceneManager.ReloadContent"/>).
    /// </summary>
    public partial class RpgEditorWindow : ToolWindow, IMapPickupEditor, IAtmosphereMaskEditor
    {
        private const string Category = "RPG_EDITOR";

        private const string MapDir = "maps";

        private const string MapExtension = ".json";

        private const string BattlesIntegration = "rpg__battles";

        private const string TestSuites = "test_suites/";

        private const int NewWidth = 16;

        private const int NewHeight = 12;

        private static readonly Color PaintFill = new(0.55f, 0.85f, 1f, 0.45f);

        private static readonly Color EraseFill = new(1f, 0.45f, 0.4f, 0.45f);

        private static readonly Color SelectFill = new(1f, 0.85f, 0.25f, 0.45f);

        private static readonly Color FootprintLine = new(1f, 1f, 1f, 0.22f);

        private static readonly Color HoverLine = new(1f, 1f, 1f, 0.9f);

        private static readonly Color SpawnFill = new(0.4f, 1f, 0.5f, 0.5f);

        private static readonly Color DoorFill = new(0.95f, 0.65f, 0.2f, 0.8f);

        private static readonly Color MarkerFill = new(0.75f, 0.85f, 0.95f, 0.65f);

        private static readonly Color GhostFill = new(1f, 1f, 1f, 0.3f);

        private static readonly Color GhostBlocked = new(1f, 0.3f, 0.3f, 0.7f);

        private readonly RpgEditorTextures textures = new();

        private readonly RpgEditorThumbnails thumbnails = new();

        private RpgStatePreview statePreview;

        private RpgGeometryOverlay geometryOverlay;

        private readonly RpgRenderSceneBuilder builder;

        private readonly ButtonGroup toolGroup = new();

        private readonly Dictionary<RpgEditorTool, Button> toolButtons = new();

        private Dictionary<string, RpgObject> objectTable = new(StringComparer.OrdinalIgnoreCase);

        private RpgEditorPalette palette;

        private RpgEditorCanvas canvas;

        private RpgEditorInspector inspector;

        private ModFileSelector selector;

        private OptionButton projectionPicker;

        private OptionButton collisionPicker;

        private OptionButton effectPicker;

        private Button playButton;

        private Button undoButton;

        private Button redoButton;

        private Button saveButton;

        private RpgMapDocument document;

        private RpgEditorScene scene;

        private string modId;

        private string mapPath;

        private bool isNewMap;

        private bool encountersShown;

        private RpgEditorTool tool = RpgEditorTool.Select;

        private string brushObject;

        private RpgObjectForm brushForm = RpgObjectForm.Surface;

        private string placeObject;

        private Texture2D ghostTexture;

        private string selectedId;

        private string collisionCode = RpgCollision.Block.ToString();

        private List<Vector2I> stroke;

        private MouseButton strokeButton;

        private Vector2I? dragOrigin;
        // The cell under the mouse while a selected placement is dragged, and its picture for the ghost.

        private Vector2I? dragTo;

        private Texture2D dragTexture;

        private Vector2I? hover;

        private readonly HashSet<string> marked = new(StringComparer.Ordinal);

        private Vector2I? boxFrom;

        private Vector2I? boxTo;

        private Vector2I? lastCell;

        private string effectProfile;
        // The cell of the previous press with the Select tool: a repeat press on it takes the next placement under it (plan 14.G18).

        private Vector2I? lastPressCell;
        // The handle a drag moves: a teleport target on this map or an arrival from another map (plan 14.G18).

        private RpgHandleKind handleKind;

        private string handlePlacement;
        // The placement that holds the arrival selected by a press on an incoming arrow (its teleport comes from another map).

        private string arrivalId;

        /// <summary>
        /// Builds the window and opens the first writable mod's first map (or the choice of the selector).
        /// </summary>
        public RpgEditorWindow() : base("tools/rpg_editor/title")
        {
            builder = new RpgRenderSceneBuilder(textures.Image, textures.Charset);
            objectTable = Entities.Collect<RpgObject>();
            encountersShown = ModManager.Instance?.GetMod(BattlesIntegration) != null;
            BuildUi();
            ModEventBus.Subscribe<EntitiesSavedEvent>(OnEntitiesSaved);
            SetHint(ToolUi.Tr("tools/rpg_editor/hint"));
            // The first map is chosen here, not by the selector's first refresh: the map the running game shows wins.
            selector.Refresh();
            var current = FindMapFile(RpgWorldHost.Current?.Session?.Map?.ID);
            if (current is { } found)
            {
                selector.Show(found.Mod, found.Path);
                OpenFile(found.Mod, found.Path);
            }
            else if (selector.CurrentMod != null)
            {
                OpenFile(selector.CurrentMod, selector.CurrentPath);
            }

            selector.FileRequested += OnFileRequested;
            SelectTool(RpgEditorTool.Select);
            UpdateButtons();
        }

        /// <summary>The open document (null when no map is open).</summary>
        public RpgMapDocument Document
        {
            get
            {
                return document;
            }
        }

        /// <summary>The scene of the open map as the game reads it (null when no map is open).</summary>
        public RpgEditorScene Scene
        {
            get
            {
                return scene;
            }
        }

        /// <summary>The canvas (the map view).</summary>
        public RpgEditorCanvas Canvas
        {
            get
            {
                return canvas;
            }
        }

        /// <summary>The palette.</summary>
        public RpgEditorPalette Palette
        {
            get
            {
                return palette;
            }
        }

        /// <summary>The inspector column.</summary>
        public RpgEditorInspector Inspector
        {
            get
            {
                return inspector;
            }
        }

        /// <summary>
        /// Opens a map file the way the file selector does (asks first when there are unsaved changes).
        /// </summary>
        public void OpenMapFile(string mod, string path)
        {
            OnFileRequested(mod, path);
        }

        /// <summary>
        /// Opens the map and chooses the pickup object (category <c>pickup</c>) of <paramref name = "itemId"/>: the one whose id ends
        /// like the item's last segment (<c>rpg_plus/amber</c> → <c>rpg_plus/pickup/amber</c>). Null when the item has no pickup
        /// object; the item editor then writes one and asks again (plan 14.G4). The whole map is fitted to the canvas.
        /// </summary>
        public string PreparePickup(string modId, string mapPath, string itemId)
        {
            OpenMapFile(modId, mapPath);
            var name = itemId?.Split('/').Last();
            RpgObject chosen = null;
            if (name != null)
            {
                foreach (var candidate in objectTable.Values)
                {
                    if (candidate.Category != "pickup" || candidate.Form == null
                        || !candidate.ID.EndsWith("/" + name, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (chosen == null || StringComparer.Ordinal.Compare(candidate.ID, chosen.ID) < 0)
                    {
                        chosen = candidate;
                    }
                }
            }
            if (chosen == null)
            {
                return null;
            }

            ChooseObject(chosen.ID, chosen.Form.Value);
            canvas?.FitToView();
            return chosen.ID;
        }

        /// <summary>The collision code the Collision tool paints: <c>.</c>, <c>#</c> or <c>o</c>.</summary>
        public void SetCollisionCode(char code)
        {
            collisionCode = code.ToString();
            collisionPicker?.Select(code == RpgCollision.Block ? 1 : code == RpgCollision.Open ? 2 : 0);
        }

        /// <summary>The atmosphere profile the Effects tool paints, or null.</summary>
        public string EffectProfile
        {
            get
            {
                return effectProfile;
            }
        }

        /// <summary>
        /// Selects the effects tool and the atmosphere profile <paramref name = "profileId"/> (plan 14.G10);
        /// the effects editor's button calls it.
        /// </summary>
        public void ShowMaskTool(string profileId)
        {
            for (var i = 0; effectPicker != null && i < effectPicker.ItemCount; i++)
            {
                if (effectPicker.GetItemMetadata(i).AsString() == profileId)
                {
                    effectPicker.Select(i);
                    effectProfile = profileId;
                    break;
                }
            }

            SelectTool(RpgEditorTool.Effects);
        }

        /// <summary>The placements of this map marked by a box or Ctrl+A (their ids, in file order).</summary>
        public IReadOnlyList<string> Marked
        {
            get
            {
                return document == null ? new List<string>() : document.SelectionIds.Where(marked.Contains).ToList();
            }
        }

        /// <summary>
        /// "Play from here" (plan 14.G1): the running game enters the open map at the cell of the last click on the canvas (the
        /// spawn point before any click). The game reads the saved map, so unsaved changes are saved first.
        /// </summary>
        private void PlayFromHere()
        {
            if (document == null || scene == null)
            {
                return;
            }

            if (RpgWorldHost.Current == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/play_no_game"), ToolStatusKind.Error);
                return;
            }

            if ((_dirty || isNewMap) && !Save())
            {
                return;
            }

            if (!Entities.Collect<RpgMap>().ContainsKey(document.MapId))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/play_not_loaded"), ToolStatusKind.Error);
                return;
            }

            var cell = lastCell ?? new Vector2I(scene.Map.Spawn?.X ?? 0, scene.Map.Spawn?.Y ?? 0);
            RpgWorldHost.Teleport(new RpgTeleport
            {
                Map = document.MapId,
                X = cell.X,
                Y = cell.Y,
                Dir = RpgDirection.Down
            });
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/play_started"), document.MapId, cell.X, cell.Y), ToolStatusKind.Ok);
            GameLogger.Log(Category, $"play from '{document.MapId}' at {cell.X},{cell.Y}");
        }

        /// <summary>Ctrl+A: marks every placement of the map (plan 14.G9).</summary>
        private bool SelectAll()
        {
            if (document == null)
            {
                return false;
            }

            marked.Clear();
            foreach (var id in document.SelectionIds)
            {
                marked.Add(id);
            }

            selectedId = null;
            RefreshSelection();
            return true;
        }

        /// <summary>
        /// The placements an action acts on: the marked ones in file order, else the selected one, else none.
        /// </summary>
        private List<string> ActedIds()
        {
            if (document == null)
            {
                return new List<string>();
            }

            if (marked.Count > 0)
            {
                return Marked.ToList();
            }

            return selectedId == null ? new List<string>() : new List<string>
            {
                selectedId
            };
        }

        /// <summary>Ctrl+C: copies the acted-on placements to the clipboard (plan 14.G9).</summary>
        private void CopySelection()
        {
            var clips = document == null ? new List<RpgPlacementClip>() : document.ClipOf(ActedIds());
            if (clips.Count == 0)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/copy_none"), ToolStatusKind.Error);
                return;
            }

            RpgPlacementClipboard.Set(clips);
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/copied"), clips.Count), ToolStatusKind.Info);
        }

        /// <summary>
        /// Ctrl+V: pastes the clipboard with its first cell on the hovered cell (else the last click); the
        /// copies are marked.
        /// </summary>
        private void PasteClipboard()
        {
            if (document == null)
            {
                return;
            }

            if (RpgPlacementClipboard.Content.Count == 0)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/paste_none"), ToolStatusKind.Error);
                return;
            }

            var at = hover ?? lastCell ?? Vector2I.Zero;
            var ids = document.Paste(RpgPlacementClipboard.Content, at.X, at.Y);
            if (ids == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/paste_refused"), ToolStatusKind.Error);
                return;
            }

            marked.Clear();
            foreach (var id in ids)
            {
                marked.Add(id);
            }

            selectedId = null;
            AfterChange();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/pasted"), ids.Count), ToolStatusKind.Ok);
        }

        /// <summary>Delete: removes the acted-on placements in one step (plan 14.G9).</summary>
        private void DeleteSelection()
        {
            if (document == null)
            {
                return;
            }

            if (marked.Count == 0)
            {
                if (selectedId != null)
                {
                    DeletePlacement(selectedId);
                }

                return;
            }

            if (!document.DeletePlacements(marked.ToList()))
            {
                return;
            }

            marked.Clear();
            selectedId = null;
            AfterChange();
        }

        /// <summary>
        /// Marks the placements inside the dragged box (inclusive corners) and clears the single selection.
        /// </summary>
        private void FinishBox()
        {
            var from = boxFrom;
            var to = boxTo ?? boxFrom;
            boxFrom = null;
            boxTo = null;
            marked.Clear();
            if (from is { } a && to is { } b && scene != null)
            {
                var x0 = Math.Min(a.X, b.X);
                var y0 = Math.Min(a.Y, b.Y);
                var x1 = Math.Max(a.X, b.X);
                var y1 = Math.Max(a.Y, b.Y);
                foreach (var (id, _, x, y) in scene.Editables)
                {
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1)
                    {
                        marked.Add(id);
                    }
                }
            }

            selectedId = null;
            RefreshSelection();
        }

        /// <summary>Drops marks of placements that no longer exist (after an undo, a delete or a paste).</summary>
        private void PruneMarked()
        {
            if (document == null)
            {
                marked.Clear();
                return;
            }

            marked.RemoveWhere(IsMarkedUnavailable);
        }

        /// <summary>The tool a click does.</summary>
        public RpgEditorTool Tool
        {
            get
            {
                return tool;
            }
        }

        /// <summary>The sprite or actor the Place tool puts, or null.</summary>
        public string PlaceObject
        {
            get
            {
                return placeObject;
            }
        }

        /// <summary>The selected placement's id, or null.</summary>
        public string SelectedId
        {
            get
            {
                return selectedId;
            }
        }

        /// <summary>
        /// The placement of the arrival selected on the canvas (its teleport comes from another map), or null.
        /// </summary>
        public string ArrivalId
        {
            get
            {
                return arrivalId;
            }
        }

        /// <summary>True when the rpg__battles integration is mounted, so the encounters section is shown.</summary>
        public bool EncountersShown
        {
            get
            {
                return encountersShown;
            }
        }

        /// <summary>The map id of the open document (its scene name), or null.</summary>
        public string MapId
        {
            get
            {
                return document?.MapId;
            }
        }

        /// <summary>Releases the textures and the builder; the canvas releases its scene.</summary>
        protected override void Cleanup()
        {
            ModEventBus.Unsubscribe<EntitiesSavedEvent>(OnEntitiesSaved);
            toolGroup.Pressed -= OnToolButtonPressed;
            DetachStatePreview();
            geometryOverlay?.Retire();
            canvas?.Clear();
            if (canvas != null)
            {
                canvas.GhostTexture = null;
            }

            ghostTexture = null;
            dragTexture = null;
            palette?.ReleaseIcons();
            thumbnails.Dispose();
            builder.Dispose();
            textures.Dispose();
            base.Cleanup();
        }

        // ── layout ─────────────────────────────────────────────────

        private void BuildUi()
        {
            var view = new RpgEditorWorkbench(objectTable, thumbnails, toolGroup);
            AddChild(view);
            view.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            try
            {
                view.EnsureConstructed();
                palette = view.Palette;
                canvas = view.Canvas;
                geometryOverlay = new RpgGeometryOverlay(this, view.Geometry);
                canvas.AddChild(geometryOverlay);
                geometryOverlay.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
                geometryOverlay.EnsureConstructed();
                inspector = view.Inspector;
                selector = view.Selector;
                projectionPicker = view.ProjectionPicker;
                collisionPicker = view.CollisionPicker;
                effectPicker = view.EffectPicker;
                playButton = view.PlayButton;
                undoButton = view.UndoButton;
                redoButton = view.RedoButton;
                saveButton = view.SaveButton;
                foreach (var pair in view.ToolButtons)
                {
                    toolButtons[pair.Key] = pair.Value;
                }
                toolGroup.Pressed += OnToolButtonPressed;
                palette.ObjectChosen += OnObjectChosen;
                palette.CharacterChosen += OnCharacterChosen;
                canvas.CellPressed += OnCellPressed;
                canvas.CellDragged += OnCellDragged;
                canvas.CellReleased += OnCellReleased;
                canvas.Hovered += OnHovered;
                canvas.ResizePressed += OnResizePressed;
                canvas.ResizeDragged += OnResizeDragged;
                canvas.ResizeReleased += OnResizeReleased;
                inspector.PlacementMoved += OnPlacementMoved;
                inspector.RouteChanged += OnRouteChanged;
                inspector.RouteDrawingRequested += BeginRouteDrawing;
                inspector.RoutePreviewChanged += OnRoutePreview;
                inspector.PlacementDeleted += OnDeleteRequested;
                inspector.PlacementReset += OnPlacementReset;
                inspector.PropertyChanged += OnPropertyChanged;
                inspector.MapSizeChanged += OnMapSizeChanged;
                inspector.BackdropPositionChanged += OnBackdropPositionChanged;
                inspector.BackdropPreviewChanged += OnBackdropPreviewChanged;
                inspector.ScalePreviewChanged += OnScalePreviewChanged;
                inspector.ScaleChanged += OnScaleChanged;
                inspector.GridVisibilityChanged += OnGridVisibilityChanged;
                inspector.EventChanged += OnEventChanged;
                inspector.SpawnPickRequested += OnSpawnRequested;
                inspector.EncountersChanged += OnEncountersChanged;
                inspector.LinkOpened += OnLinkOpened;
                view.NewObjectButton.Pressed += CreateMapObject;
                view.FitButton.Pressed += OnFit;
                saveButton.Pressed += OnSaveRequested;
                undoButton.Pressed += OnUndoRequested;
                redoButton.Pressed += OnRedoRequested;
                playButton.Pressed += PlayFromHere;
                collisionPicker.ItemSelected += OnCollisionChoice;
                effectPicker.ItemSelected += OnEffectChoice;
                projectionPicker.ItemSelected += OnProjectionChoice;
                if (!effectPicker.Disabled && effectPicker.ItemCount > 0)
                {
                    effectProfile = effectPicker.GetItemMetadata(0).AsString();
                }
                WireInteractParts();
                UseGameProjection();
            }
            catch
            {
                RemoveChild(view);
                view.Free();
                throw;
            }
        }



        /// <summary>
        /// Opens the editor in the projection the game draws its maps with, so painted collision lines up with
        /// the game.
        /// </summary>
        private void UseGameProjection()
        {
            var id = ActiveGame.GetSetting("tile.renderer");
            if ((id != RpgEditorCanvas.Isometric && id != RpgEditorCanvas.TopDown) || !RpgEditorCanvas.HasProjection(id))
            {
                return;
            }

            canvas.SetProjection(id);
            projectionPicker.Select(id == RpgEditorCanvas.Isometric ? 1 : 0);
        }

        // ── files ──────────────────────────────────────────────────

        private void OnFileRequested(string mod, string path)
        {
            if (document != null && (mod != modId || path != mapPath))
            {
                ConfirmOpen(mod, path);
                return;
            }

            OpenFile(mod, path);
        }

        private void OpenFile(string mod, string path)
        {
            CloseDocument();
            objectTable = Entities.Collect<RpgObject>();
            if (mod == null || path == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/no_map"), ToolStatusKind.Info);
                UpdateButtons();
                return;
            }

            var id = Path.GetFileNameWithoutExtension(path);
            var existing = ModFileService.ReadText(mod, path) != null;
            if (!existing && SceneFileService.ValidName(id) != id)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/bad_name"), ToolStatusKind.Error);
                selector.Show(modId, mapPath);
                return;
            }

            var opened = existing ? OpenDocument(mod, path, id) : CreateDocument(mod, path, id);
            if (opened == null)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/cannot_open"), id, mod), ToolStatusKind.Error);
                UpdateButtons();
                return;
            }

            modId = mod;
            mapPath = path;
            document = opened.Value.Document;
            isNewMap = opened.Value.New;
            selectedId = null;
            SetTool(RpgEditorTool.Select);
            RebuildScene();
            selector.Show(modId, mapPath);
            SetStatus(string.Format(ToolUi.Tr(isNewMap ? "tools/rpg_editor/status_new" : "tools/rpg_editor/status_opened"), id, mod),
                ToolStatusKind.Info);
            GameLogger.Log(Category, $"opened map '{id}' of '{mod}'" + (isNewMap ? " (new)" : string.Empty));
        }

        private (RpgMapDocument Document, bool New)? OpenDocument(string mod, string path, string id)
        {
            var files = OwnFiles(mod);
            var doc = RpgMapDocument.Open(id, mod, files, ForeignOf("rpg_map", mod), ForeignOf("rpg_placement", mod), BaseOf(mod), FormOf);
            return doc == null ? null : (doc, false);
        }

        private (RpgMapDocument Document, bool New)? CreateDocument(string mod, string path, string id)
        {
            var template = new JsonObject
            {
                ["Id"] = id,
                ["Width"] = NewWidth,
                ["Height"] = NewHeight,
                ["Zoom"] = 2,
                ["Spawn"] = new JsonObject
                {
                    ["X"] = 1,
                    ["Y"] = 1,
                    ["Dir"] = "Down"
                },
                ["Legend"] = new JsonObject(),
            };
            var files = OwnFiles(mod).ToList();
            files.Add((path, EntityDocument.Serialize(new JsonObject
            {
                ["Kind"] = "rpg_map",
                ["Items"] = new JsonArray(template)
            })));
            var doc = RpgMapDocument.Open(id, mod, files, ForeignOf("rpg_map", mod), ForeignOf("rpg_placement", mod), BaseOf(mod), FormOf);
            return doc == null ? null : (doc, true);
        }

        private void CloseDocument()
        {
            document = null;
            scene = null;
            selectedId = null;
            stroke = null;
            dragOrigin = null;
            dragTo = null;
            resizePreview = null;
            lastPressCell = null;
            handleKind = RpgHandleKind.None;
            handlePlacement = null;
            arrivalId = null;
            marked.Clear();
            boxFrom = null;
            boxTo = null;
            canvas?.Clear();
            inspector?.Show(new RpgEditorInspector.State());
        }

        /// <summary>Every JSON file of the mod except its test suites: the document takes the kinds it edits.</summary>
        private static List<(string Path, string Text)> OwnFiles(string mod)
        {
            var result = new List<(string Path, string Text)>();
            foreach (var path in ModFileService.ListFiles(mod, string.Empty, ".json"))
            {
                if (path.StartsWith(TestSuites, StringComparison.Ordinal))
                {
                    continue;
                }

                var text = ModFileService.ReadText(mod, path);
                if (text != null)
                {
                    result.Add((path, text));
                }
            }

            return result;
        }

        private static List<(string Id, string OwnerMod, string File, JsonObject Written)> ForeignOf(string kind, string mod)
        {
            return ForeignEntities.Scan(kind, mod).Where(IsGameEntity).ToList();
        }

        private static Func<string, string, JsonObject> BaseOf(string mod)
        {
            return new RpgEditorBaseReader(mod).Read;
        }

        private Func<string, RpgObjectForm?> FormOf
        {
            get
            {
                return ObjectFormOf;
            }
        }

        private RpgObject ObjectOf(string id)
        {
            return id != null && objectTable.TryGetValue(id, out var value) ? value : null;
        }

        private void OnLinkOpened(string mapId, int x, int y)
        {
            if (FindMapFile(mapId) is not { } found)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/link_no_file"), mapId), ToolStatusKind.Error);
                return;
            }

            ConfirmOpen(found.Mod, found.Path);
        }

        /// <summary>The writable mod and the path of a map file with this id, or null.</summary>
        private static (string Mod, string Path)? FindMapFile(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
            {
                return null;
            }

            foreach (var mod in ModFileService.ListWritableMods())
            {
                foreach (var path in ModFileService.ListFiles(mod, MapDir, MapExtension))
                {
                    if (Path.GetFileNameWithoutExtension(path) == mapId)
                    {
                        return (mod, path);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Saves the document: changed files, the new map's file and scene (first save), the caches, and the
        /// running game.
        /// </summary>
        public bool Save()
        {
            if (document == null)
            {
                return false;
            }

            var problems = document.Validate();
            if (problems.Count > 0)
            {
                SetStatus(string.Format(ToolUi.Tr(problems[0].Key), problems[0].Argument), ToolStatusKind.Error);
                return false;
            }

            var files = document.Changes();
            if (isNewMap)
            {
                var text = EntityDocument.Serialize(new JsonObject
                {
                    ["Kind"] = "rpg_map",
                    ["Items"] = new JsonArray(document.Map.Data.DeepClone())
                });
                files.RemoveAll(IsCurrentMapFile);
                files.Add((mapPath, text));
            }

            foreach (var (path, text) in files)
            {
                var written = text == null ? ModFileService.DeleteFile(modId, path) : ModFileService.WriteText(modId, path, text);
                if (!written)
                {
                    SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                    return false;
                }
            }

            if (isNewMap)
            {
                WriteSceneOf(document.MapId);
            }

            // Objects of other mods (painted surfaces, placed sprites) and the scenarios, maps, items and shops that the
            // placements' interactions name need that mod in depends (plan 14.G16).
            var objectIds = new List<string>(document.Legend.Values);
            var interactRefs = new List<(string Kind, string Id)>();
            foreach (var record in document.Placements)
            {
                if (record.IsForeign)
                {
                    continue;
                }

                if (record.Data?["Object"] is JsonValue)
                {
                    objectIds.Add(record.Data["Object"].GetValue<string>());
                }

                interactRefs.AddRange(RpgInteractReferences.Of(record.Data?["Interact"] as JsonObject));
            }

            var objectRefs = objectIds.Distinct(StringComparer.Ordinal).Select(ObjectReference);
            // Changing a foreign placement writes a patch in this map's writable mod.
            var placementRefs = new List<(string Kind, string Id)>();
            foreach (var record in document.PlacementRecords)
            {
                if (record.IsForeign)
                {
                    placementRefs.Add(("rpg_placement", record.Id));
                }
            }

            var added = ModDependencies.EnsureReferenced(modId,
                objectRefs.Concat(interactRefs).Concat(placementRefs).Concat(EventReferences()).ToList());
            document.MarkSaved();
            isNewMap = false;
            MarkClean();
            ClearCaches();
            // The teleport pickers list the maps of the game from this index: a new map must be in it at once (plan 14.G18).
            GameEntityIndex.Refresh();
            SceneManager.ReloadContent();
            GameLogger.Log(Category, $"saved map '{document.MapId}' of '{modId}': {files.Count} file(s)");
            var savedText = string.Format(ToolUi.Tr("tools/rpg_editor/status_saved"), document.MapId, files.Count);
            SetStatus(added.Count == 0 ? savedText : savedText + " " + ModDependencies.Notice(added), ToolStatusKind.Ok);
            UpdateButtons();
            return true;
        }

        private void WriteSceneOf(string mapId)
        {
            var scenePath = SceneFileEntry.PathFor(mapId);
            if (ModFileService.ReadText(modId, scenePath) != null)
            {
                return;
            }

            var backgrounds = ModResourceService.GetBackgroundIds();
            var entry = new SceneFileEntry
            {
                ModId = modId,
                RelativePath = scenePath,
                Name = mapId
            };
            if (backgrounds.Count > 0)
            {
                entry.Backgrounds.Add(backgrounds[0]);
            }

            if (!SceneFileService.Write(entry))
            {
                GameLogger.Log(Category, $"scene '{mapId}' of '{modId}' could not be written", LogLevel.Error);
            }
        }

        private static void ClearCaches()
        {
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
        }

        // ── changes ────────────────────────────────────────────────

        /// <summary>Steps back one change; the map and its placements step back together.</summary>
        public bool Undo()
        {
            if (document == null || !document.Undo())
            {
                return false;
            }

            AfterChange();
            return true;
        }

        /// <summary>Steps forward again.</summary>
        public bool Redo()
        {
            if (document == null || !document.Redo())
            {
                return false;
            }

            AfterChange();
            return true;
        }

        private void AfterChange()
        {
            if (selectedId != null && !document.SelectionIds.Contains(selectedId))
            {
                selectedId = null;
            }

            PruneMarked();
            SetDirty();
            RebuildScene();
        }

        private void ChangeProperty(string key, JsonNode value)
        {
            if (document == null)
            {
                return;
            }

            if (document.SetProperty(key, value))
            {
                AfterChange();
            }
        }

        private void OnPropertyChanged(string key, JsonNode value)
        {
            ChangeProperty(key, value);
        }

        private void OnPlacementMoved(string id, int x, int y)
        {
            if (document != null && document.MovePlacement(id, x, y))
            {
                AfterChange();
            }
        }

        private void OnPlacementReset(string id)
        {
            if (document != null && document.ResetPlacement(id))
            {
                AfterChange();
            }
        }

        private void DeletePlacement(string id)
        {
            if (document == null || !document.DeletePlacement(id))
            {
                return;
            }

            if (selectedId == id)
            {
                selectedId = null;
            }

            AfterChange();
        }

        // ── tools ──────────────────────────────────────────────────

        /// <summary>Makes <paramref name = "next"/> the tool a click does (and shows it on the toolbar).</summary>
        public void SelectTool(RpgEditorTool next)
        {
            SetTool(next);
            if (toolButtons.TryGetValue(next, out var button))
            {
                button.SetPressedNoSignal(true);
            }
        }

        private void SetTool(RpgEditorTool next)
        {
            tool = next;
            stroke = null;
            dragOrigin = null;
            dragTo = null;
            resizePreview = null;
            lastPressCell = null;
            handleKind = RpgHandleKind.None;
            handlePlacement = null;
            if (toolButtons.TryGetValue(next, out var button))
            {
                button.SetPressedNoSignal(true);
            }

            RebuildMarks();
        }

        /// <summary>
        /// Chooses an object from the palette: a surface or a segment paints, a sprite or an actor is placed.
        /// </summary>
        public void ChooseObject(string id, RpgObjectForm form)
        {
            var obj = ObjectOf(id);
            if (obj == null)
            {
                return;
            }

            canvas.GhostTexture = null;
            ghostTexture = textures.Preview("ghost", obj);
            switch (form)
            {
                case RpgObjectForm.Surface:
                case RpgObjectForm.Segment:
                    brushObject = id;
                    brushForm = form;
                    SelectTool(RpgEditorTool.Paint);
                    break;
                default:
                    placeObject = id;
                    SelectTool(RpgEditorTool.Place);
                    break;
            }

            palette?.Select(form, id);
            RebuildMarks();
        }

        private void OnObjectChosen(string id, RpgObjectForm form)
        {
            ChooseObject(id, form);
        }

        /// <summary>Switches the renderer; the view stays where it is.</summary>
        public void SetProjection(string id)
        {
            canvas?.SetProjection(id);
            var index = id == RpgEditorCanvas.Isometric ? 1 : 0;
            if (projectionPicker != null && projectionPicker.Selected != index)
            {
                projectionPicker.Select(index);
            }

            // A projection switch discards an unapplied background preview as well as its form values.
            RebuildScene();
        }

        // ── canvas input ───────────────────────────────────────────

        private void OnCellPressed(Vector2I cell, MouseButton button, bool doubleClick)
        {
            if (document == null)
            {
                return;
            }

            lastCell = cell;
            if (DrawRouteCell(cell, button))
            {
                return;
            }

            if (button == MouseButton.Left && TryPickTargetCell(cell))
            {
                return;
            }

            if (button == MouseButton.Right)
            {
                if (tool is RpgEditorTool.Paint or RpgEditorTool.Collision or RpgEditorTool.Effects)
                {
                    BeginStroke(cell, button);
                }
                else if (tool == RpgEditorTool.Select)
                {
                    selectedId = null;
                    marked.Clear();
                }

                RefreshSelection();
                return;
            }

            switch (tool)
            {
                case RpgEditorTool.Event:
                    if (button == MouseButton.Left)
                    {
                        boxFrom = cell;
                        boxTo = cell;
                        RebuildMarks();
                    }

                    break;
                case RpgEditorTool.Select:
                    PressSelect(cell);
                    break;
                case RpgEditorTool.Paint:
                case RpgEditorTool.Collision:
                case RpgEditorTool.Effects:
                    BeginStroke(cell, button);
                    break;
                case RpgEditorTool.Place:
                    PlaceAt(cell);
                    break;
                case RpgEditorTool.Spawn:
                    if (document.SetSpawn(cell.X, cell.Y))
                    {
                        AfterChange();
                    }

                    break;
            }
        }

        private void BeginStroke(Vector2I cell, MouseButton button)
        {
            stroke = new List<Vector2I>
            {
                cell
            };
            strokeButton = button;
            RebuildMarks();
        }

        private void OnCellDragged(Vector2I cell, MouseButton button)
        {
            if (DrawRouteCell(cell, button))
            {
                return;
            }

            if (handleKind != RpgHandleKind.None)
            {
                dragTo = cell;
                RebuildMarks();
                return;
            }

            if (tool == RpgEditorTool.Select && selectedId != null && dragOrigin != null)
            {
                dragTo = cell;
                RebuildMarks();
                return;
            }

            if (boxFrom != null)
            {
                boxTo = cell;
                RebuildMarks();
                return;
            }

            if (stroke != null && !stroke.Contains(cell))
            {
                stroke.Add(cell);
                RebuildMarks();
            }
        }

        private void OnCellReleased(Vector2I cell, MouseButton button)
        {
            if (GodotObject.IsInstanceValid(routeDrawing))
            {
                return;
            }

            if (document == null)
            {
                return;
            }

            if (handleKind != RpgHandleKind.None)
            {
                FinishHandle(cell);
                return;
            }

            if (boxFrom != null)
            {
                if (tool == RpgEditorTool.Event)
                {
                    FinishEvent();
                    return;
                }

                FinishBox();
                return;
            }

            if (stroke != null)
            {
                var cells = stroke;
                var erase = strokeButton == MouseButton.Right;
                stroke = null;
                var changed = tool switch
                {
                    RpgEditorTool.Collision => document.PaintCollision(cells.Select(CellCoordinates),
                        erase ? RpgCollision.Open : collisionCode[0]),
                    RpgEditorTool.Effects => document.PaintEffect(effectProfile, cells.Select(CellCoordinates), erase),
                    _ => PaintCells(cells, erase),
                };
                if (changed)
                {
                    AfterChange();
                }
                else
                {
                    RebuildMarks();
                }

                return;
            }

            if (tool == RpgEditorTool.Select && cell.X >= 0 && cell.Y >= 0 && DragTarget(cell) is { } target)
            {
                dragTo = null;
                if (document.MovePlacement(selectedId, target.X, target.Y))
                {
                    AfterChange();
                }
            }

            dragOrigin = null;
            if (dragTo != null)
            {
                dragTo = null;
                RebuildMarks();
            }
        }

        /// <summary>
        /// Where the dragged placement lands when the mouse is over <paramref name = "cell"/>: its origin moves by the
        /// mouse's offset from the pressed cell, so an object held by any of its cells does not jump. Null when no
        /// placement is dragged or it would stay where it is.
        /// </summary>
        private Vector2I? DragTarget(Vector2I cell)
        {
            if (selectedId == null || dragOrigin is not { } origin || scene == null)
            {
                return null;
            }

            var at = scene.Editables.FirstOrDefault(IsSelectedEditable);
            if (at.Id == null)
            {
                return null;
            }

            var target = RpgDragOffset.Landing(new Vector2I(at.X, at.Y), origin, cell);
            return target == new Vector2I(at.X, at.Y) ? null : target;
        }

        private bool PaintCells(IEnumerable<Vector2I> cells, bool erase)
        {
            var layer = brushForm == RpgObjectForm.Segment ? RpgStrokeLayer.Structure : RpgStrokeLayer.Ground;
            var list = cells.Select(CellCoordinates);
            if (erase)
            {
                return document.Paint(layer, list, null);
            }

            if (brushObject == null)
            {
                return false;
            }

            return document.Paint(layer, list, brushObject);
        }

        private void PlaceAt(Vector2I cell)
        {
            if (placeObject == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/choose_first"), ToolStatusKind.Error);
                return;
            }

            var local = placeObject.Split('/').Last().Replace(' ', '_');
            var id = document.AddPlacement("new_" + local, placeObject, cell.X, cell.Y);
            if (id == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/refused"), ToolStatusKind.Error);
                return;
            }

            selectedId = id;
            AfterChange();
        }

        private void OnHovered(Vector2I? cell)
        {
            hover = cell;
            RebuildMarks();
        }

        private void RefreshSelection()
        {
            if (selectedId != null && (document == null || !document.SelectionIds.Contains(selectedId)))
            {
                selectedId = null;
            }

            PruneMarked();
            RebuildMarks();
            RefreshInspector();
        }

        /// <summary>
        /// A press with the Select tool (plan 14.G18): the placement under the cell is selected; a repeat press on the same cell
        /// takes the next placement under it, in the order of <see cref="RpgPickCycle"/> (markers first). A press on empty ground
        /// starts a box (plan 14.G9). The status line names the selected placement.
        /// </summary>
        private void PressSelect(Vector2I cell)
        {
            var ordered = RpgPickCycle.Order(CandidatesAt(cell));
            var again = lastPressCell == cell;
            lastPressCell = cell;
            arrivalId = null;
            selectedId = again ? RpgPickCycle.Next(ordered, selectedId) : ordered.FirstOrDefault();
            GrabPlacement(cell);
            marked.Clear();
            // A press on empty ground starts a box (plan 14.G9); a press on a placement selects it and may drag it.
            boxFrom = selectedId == null ? cell : null;
            boxTo = boxFrom;
            if (selectedId != null)
            {
                SetStatus(PickedText(ordered), ToolStatusKind.Info);
            }

            RefreshSelection();
        }

        /// <summary>
        /// The placements of this map that cover a cell, with what the pick order needs: a picture-less marker,
        /// the origin is the cell.
        /// </summary>
        private List<(string Id, bool Marker, bool Origin)> CandidatesAt(Vector2I cell)
        {
            var result = new List<(string Id, bool Marker, bool Origin)>();
            if (scene == null)
            {
                return result;
            }

            foreach (var (id, obj, x, y) in scene.Editables)
            {
                if (cell.X < x || cell.Y < y || cell.X >= x + obj.Width || cell.Y >= y + obj.Height)
                {
                    continue;
                }

                result.Add((id, RpgMarkers.IsPictureless(obj), x == cell.X && y == cell.Y));
            }

            return result;
        }

        /// <summary>
        /// Makes the selected placement the one a drag from <paramref name = "pressed"/> moves; its picture is
        /// the ghost.
        /// </summary>
        private void GrabPlacement(Vector2I pressed)
        {
            dragOrigin = selectedId == null ? null : pressed;
            dragTo = null;
            canvas.GhostTexture = null;
            dragTexture = textures.Preview("drag", selectedId != null ? scene?.ObjectOf(selectedId) : null);
        }

        /// <summary>
        /// The status line for the selected placement; a stack of several says which one of how many it is.
        /// </summary>
        private string PickedText(List<string> ordered)
        {
            if (ordered.Count <= 1)
            {
                return string.Format(ToolUi.Tr("tools/rpg_editor/picked"), selectedId);
            }

            return string.Format(ToolUi.Tr("tools/rpg_editor/picked_cycle"), selectedId, ordered.IndexOf(selectedId) + 1, ordered.Count);
        }

        // ── rebuilding what is shown ───────────────────────────────

        /// <summary>Rebuilds the scene from the document (after a change) and shows it.</summary>
        private void RebuildScene(bool refreshInspector = true)
        {
            if (refreshInspector)
            {
                previewBackdropId = null;
            }

            if (document == null)
            {
                canvas?.Clear();
                RefreshInspector();
                UpdateButtons();
                return;
            }

            scene = RpgEditorScene.From(document, ObjectOf, RpgEditorPreview.ForDocument(document).Resolve);
            textures.BeginScene();
            var tileScene = builder.Build(scene.Map, scene.Cells, scene.Statics);
            canvas.Actors.Clear();
            foreach (var actor in scene.Actors)
            {
                if (actor.Object.Hidden)
                {
                    continue;
                }

                var look = builder.ActorLook(actor.Object, tileScene.TileSize);
                if (look == null)
                {
                    continue;
                }

                canvas.Actors.Add(new TileActorPose
                {
                    Id = actor.Id,
                    Visible = true,
                    Charset = true,
                    Facing = (int)actor.Object.Facing,
                    Column = actor.Object.Column == TileWalkSheet.IdleColumn
                        ? TileWalkSheet.Animation(look.Texture).IdleColumn : actor.Object.Column,
                    Position = new Vector2(actor.X, actor.Y),
                    Look = look,
                    Tint = Colors.White,
                });
            }

            canvas.SetScene(tileScene);
            if (selectedId != null && !document.SelectionIds.Contains(selectedId))
            {
                selectedId = null;
            }

            RebuildMarks();
            if (refreshInspector)
            {
                RefreshInspector();
            }
            else if (previewBackdropId != null)
            {
                ShowBackdropPreview(scene.Statics);
            }

            textures.EndScene();
            if (textures.PinnedBytes > textures.BudgetBytes)
            {
                SetHint(string.Format(ToolUi.Tr("tools/rpg_editor/active_texture_budget"), textures.PinnedBytes / 1048576d,
                    textures.BudgetBytes / 1048576d));
            }

            UpdateButtons();
        }

        private void RebuildMarks()
        {
            if (canvas == null)
            {
                return;
            }

            var marks = canvas.Marks;
            marks.Clear();
            canvas.ResizeArea = null;
            canvas.ArrowsInteractive = tool == RpgEditorTool.Select;
            if (scene != null)
            {
                foreach (var (id, obj, x, y) in scene.Editables)
                    marks.Add(new CanvasRect(x, y, obj.Width, obj.Height, FootprintLine, false));
                // A picture-less marker (a trigger, a door) is a filled cell over whatever stands there (plan 14.G18).
                foreach (var (id, obj, x, y) in scene.Statics.Concat(scene.Events))
                {
                    if (RpgMarkers.IsPictureless(obj))
                    {
                        marks.Add(new CanvasRect(x, y, obj.Width, obj.Height, RpgMarkers.IsDoor(obj) ? DoorFill : MarkerFill, true));
                    }
                }

                RpgEffectsLayer.AddMarks(scene.Map, marks);
                foreach (var (id, obj, x, y) in scene.Editables)
                {
                    if (marked.Contains(id))
                    {
                        marks.Add(new CanvasRect(x, y, obj.Width, obj.Height, SelectFill, true));
                    }
                }

                if (boxFrom is { } boxA && boxTo is { } boxB)
                {
                    var bx = Math.Min(boxA.X, boxB.X);
                    var by = Math.Min(boxA.Y, boxB.Y);
                    marks.Add(new CanvasRect(bx, by, Math.Abs(boxA.X - boxB.X) + 1, Math.Abs(boxA.Y - boxB.Y) + 1, SelectFill, true));
                }

                if (selectedId != null && scene.ObjectOf(selectedId) is { } selected)
                {
                    var at = scene.Editables.FirstOrDefault(IsSelectedEditable);
                    var bounds = resizePreview ?? new Rect2I(at.X, at.Y, selected.Width, selected.Height);
                    marks.Add(new CanvasRect(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y, SelectFill, true));
                    marks.Add(new CanvasRect(bounds.Position.X, bounds.Position.Y, bounds.Size.X, bounds.Size.Y, HoverLine, false));
                    if (tool == RpgEditorTool.Select && RpgMarkers.IsPictureless(selected))
                    {
                        canvas.ResizeArea = bounds;
                    }
                }

                if (scene.Map != null)
                {
                    marks.Add(new CanvasRect(scene.Map.Spawn?.X ?? 0, scene.Map.Spawn?.Y ?? 0, 1, 1, SpawnFill, true));
                }

                if (tool == RpgEditorTool.Collision)
                {
                    RpgCollisionOverlay.AddMarks(scene, marks, stroke,
                        strokeButton == MouseButton.Right ? RpgCollision.Open : collisionCode[0]);
                }

                if (stroke != null && tool != RpgEditorTool.Collision)
                {
                    var fill = strokeButton == MouseButton.Right || (tool == RpgEditorTool.Collision
                        && collisionCode == RpgCollision.Block.ToString()) ? EraseFill
                            : tool == RpgEditorTool.Effects ? RpgEffectsLayer.MaskFill : PaintFill;
                    foreach (var cell in stroke)
                    {
                        marks.Add(new CanvasRect(cell.X, cell.Y, 1, 1, fill, true));
                    }
                }
            }

            canvas.GhostRect = null;
            canvas.GhostTexture = null;
            if (scene != null && hover is { } at2 && document != null)
            {
                if (tool == RpgEditorTool.Place && placeObject != null && ObjectOf(placeObject) is { } placing)
                {
                    canvas.GhostRect = new CanvasRect(at2.X, at2.Y, placing.Width, placing.Height, Colors.White, false);
                    canvas.GhostTexture = ghostTexture;
                    canvas.GhostTint = scene.Blocks(placing, at2.X, at2.Y) ? new Color(1f, 0.35f, 0.35f, 0.6f) : new Color(1, 1, 1, 0.6f);
                }
                else if (tool is RpgEditorTool.Paint or RpgEditorTool.Collision or RpgEditorTool.Spawn
                    or RpgEditorTool.Place or RpgEditorTool.Effects or RpgEditorTool.Event)
                {
                    marks.Add(new CanvasRect(at2.X, at2.Y, 1, 1, HoverLine, false));
                }
            }

            // The ghost of a drag follows the mouse, so the move is not made blind: a placement or a door lands on its ghost,
            // a teleport handle shows the cell it would write (red when that cell is blocked or off the map, plan 14.G18).
            if (scene != null && dragTo is { } over && handleKind != RpgHandleKind.None)
            {
                var blocked = RpgTeleportLinks.BrokenOf(new RpgTeleport
                {
                    Map = document.MapId,
                    X = over.X,
                    Y = over.Y
                }, KnownMaps()) != null;
                marks.Add(new CanvasRect(over.X, over.Y, 1, 1, blocked ? GhostBlocked : GhostFill, true));
                marks.Add(new CanvasRect(over.X, over.Y, 1, 1, HoverLine, false));
            }
            else if (scene != null && dragTo is { } moveOver && DragTarget(moveOver) is { } landing
                && scene.ObjectOf(selectedId) is { } moving)
            {
                canvas.GhostRect = new CanvasRect(landing.X, landing.Y, moving.Width, moving.Height, Colors.White, false);
                canvas.GhostTexture = dragTexture;
                canvas.GhostTint = new Color(1, 1, 1, 0.6f);
                marks.Add(new CanvasRect(landing.X, landing.Y, moving.Width, moving.Height, GhostFill, true));
                marks.Add(new CanvasRect(landing.X, landing.Y, moving.Width, moving.Height, HoverLine, false));
            }

            UpdateRouteOverlay();
            canvas.Refresh();
        }

        private void RefreshInspector()
        {
            DetachStatePreview();
            routePreview = null;
            routeDrawing = null;
            if (inspector == null)
            {
                return;
            }

            if (document == null || scene == null)
            {
                inspector.Show(new RpgEditorInspector.State
                {
                    Encounters = false
                });
                return;
            }

            var mapId = document.MapId;
            var placements = new List<RpgPlacement>();
            foreach (var record in document.Placements)
            {
                if (RpgMapDocument.MapOfPlacement(record.Id) == mapId && !document.IsDeleted(record.Id))
                {
                    placements.Add(RpgEditorScene.PlacementOf(record.Data));
                }
            }

            var maps = KnownMaps();
            var outgoing = RpgTeleportLinks.Outgoing(mapId, placements, scene.Map, ObjectOf, maps);
            var incoming = RpgTeleportLinks.Incoming(mapId, PlacementsBeyond(mapId, maps), ObjectOf);
            if (arrivalId != null && incoming.All(IsNotArrival))
            {
                arrivalId = null;
            }

            UpdateArrows(mapId, outgoing, incoming);
            var selectedObject = selectedId == null ? null : scene.ObjectOf(selectedId)?.ID;
            inspector.Show(new RpgEditorInspector.State
            {
                Document = document,
                Projection = canvas.Projection,
                Backdrops = scene.Statics.Where(HasBackdrop).Select(BackdropEntry).ToList(),
                Placement = selectedId,
                Arrival = incoming.FirstOrDefault(IsArrival),
                ObjectId = selectedObject,
                ScaleOf = CurrentScaleOf,
                PictureScale = selectedId != null && document.EventOf(selectedId) == null
                    && scene.ObjectOf(selectedId) is { } selectedPicture
                    && !RpgMarkers.IsPictureless(selectedPicture) ? selectedPicture.Scale : null,
                Route = selectedId == null ? null : scene.ObjectOf(selectedId)?.Route,
                IsActor = selectedId != null && scene.ObjectOf(selectedId)?.Form == RpgObjectForm.Actor,
                Outgoing = outgoing,
                Incoming = incoming,
                MapSize = new Vector2I(scene.Map.Width, scene.Map.Height),
                Encounters = encountersShown,
                Interact = InteractOfSelected(),
                ObjectInteract = selectedObject == null ? null : ObjectOf(selectedObject)?.Interact,
                Variants = VariantNames(),
            });
            statePreview = inspector.FindChild("StatePreview", true, false) as RpgStatePreview;
            if (statePreview != null)
            {
                statePreview.EnsureConstructed();
                statePreview.Changed += OnStatePreviewChanged;
            }

            UpdateRouteOverlay();
            canvas.Refresh();
        }

        private void OnStatePreviewChanged()
        {
            RebuildScene(false);
        }

        private void DetachStatePreview()
        {
            if (statePreview != null)
            {
                statePreview.Changed -= OnStatePreviewChanged;
                statePreview = null;
            }
        }

        private void UpdateButtons()
        {
            if (undoButton == null)
            {
                return;
            }

            undoButton.Disabled = document == null || !document.CanUndo;
            redoButton.Disabled = document == null || !document.CanRedo;
            saveButton.Disabled = document == null;
            collisionPicker.Disabled = document == null;
            effectPicker.Disabled = document == null;
            playButton.Disabled = document == null;
            projectionPicker.Disabled = document == null;
        }

        // ── keys ───────────────────────────────────────────────────

        /// <summary>Ctrl+S saves.</summary>
        protected override void OnSaveShortcut()
        {
            Save();
        }

        /// <summary>
        /// Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo, Delete removes the selected placement, Escape clears
        /// the selection.
        /// </summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.CtrlPressed)
            {
                switch (key.Keycode)
                {
                    case Key.Z when key.ShiftPressed:
                    case Key.Y:
                        return Redo();
                    case Key.Z:
                        return Undo();
                    case Key.A:
                        return SelectAll();
                    case Key.C:
                        CopySelection();
                        return true;
                    case Key.V:
                        PasteClipboard();
                        return true;
                }

                return false;
            }

            switch (key.Keycode)
            {
                case Key.Delete when marked.Count > 0 || selectedId != null:
                    DeleteSelection();
                    return true;
                case Key.Escape when targetPick != null:
                    CancelTargetPick();
                    return true;
                case Key.Escape when GodotObject.IsInstanceValid(routeDrawing):
                    routeDrawing = null;
                    RebuildMarks();
                    return true;
                case Key.Escape:
                    selectedId = null;
                    resizePreview = null;
                    arrivalId = null;
                    handleKind = RpgHandleKind.None;
                    dragTo = null;
                    stroke = null;
                    marked.Clear();
                    boxFrom = null;
                    boxTo = null;
                    RefreshSelection();
                    return true;
            }

            return false;
        }

        internal static VBoxContainer FillContainer()
        {
            return CreateFillContainer();
        }

        internal static HBoxContainer Toolbar()
        {
            return CreateToolbar();
        }

        internal static PanelContainer Sidebar(VBoxContainer content, int width)
        {
            return CreateSidebar(content, width);
        }

        private void OnToolButtonPressed(BaseButton button)
        {
            foreach (var pair in toolButtons)
            {
                if (pair.Value == button)
                {
                    SelectTool(pair.Key);
                    return;
                }
            }
        }

        private void OnDeleteRequested(string id)
        {
            DeletePlacement(id);
        }

        private void OnSpawnRequested()
        {
            SelectTool(RpgEditorTool.Spawn);
        }

        private void OnEncountersChanged(JsonNode value)
        {
            ChangeProperty("Encounters", value);
        }

        private void OnSaveRequested()
        {
            Save();
        }

        private void OnUndoRequested()
        {
            Undo();
        }

        private void OnRedoRequested()
        {
            Redo();
        }

        private void OnFit()
        {
            canvas?.FitToView();
        }

        private void OnCollisionChoice(long index)
        {
            collisionCode = index switch
            {
                1 => "#",
                2 => "o",
                _ => "."
            };
        }

        private void OnEffectChoice(long index)
        {
            effectProfile = effectPicker.GetItemMetadata((int)index).AsString();
        }

        private void OnProjectionChoice(long index)
        {
            SetProjection(index == 1 ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown);
        }

        private bool IsMarkedUnavailable(string id)
        {
            return document.IsDeleted(id) || !document.SelectionIds.Contains(id);
        }

        private static bool IsGameEntity((string Id, string OwnerMod, string File, JsonObject Written) row)
        {
            return !row.File.StartsWith(TestSuites, StringComparison.Ordinal);
        }

        private RpgObjectForm? ObjectFormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }

        private bool IsCurrentMapFile((string Path, string Text) file)
        {
            return file.Path == mapPath;
        }

        private static (string Kind, string Id) ObjectReference(string id)
        {
            return ("rpg_object", id);
        }

        private static (int X, int Y) CellCoordinates(Vector2I cell)
        {
            return (cell.X, cell.Y);
        }

        private bool IsSelectedEditable((string Id, RpgObject Object, int X, int Y) placed)
        {
            return placed.Id == selectedId;
        }

        private bool IsNotArrival(RpgTeleportLink link)
        {
            return link.Placement != arrivalId;
        }

        private bool IsArrival(RpgTeleportLink link)
        {
            return link.Placement == arrivalId;
        }

        private bool HasBackdrop((string Id, RpgObject Object, int X, int Y) placed)
        {
            return RpgBackdropEdit.Part(placed.Object, canvas.Projection) != null;
        }

        private static (string Id, RpgObject Object) BackdropEntry((string Id, RpgObject Object, int X, int Y) placed)
        {
            return (placed.Id, placed.Object);
        }

        private float CurrentScaleOf(string id)
        {
            return scene?.ObjectOf(id)?.Scale ?? 1;
        }

        private void ConfirmOpen(string mod, string path)
        {
            var request = new RpgEditorOpenRequest(this, mod, path);
            RunDiscardingChanges(request.Open, RestoreFileSelection);
        }

        internal void OpenConfirmed(string mod, string path)
        {
            OpenFile(mod, path);
        }

        private void RestoreFileSelection()
        {
            selector.Show(modId, mapPath);
        }
    }
}
