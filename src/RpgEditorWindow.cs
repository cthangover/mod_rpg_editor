namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
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
    using Cthangover.Mods.TileRender;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Contracts;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>What a click on the canvas does.</summary>
    public enum RpgEditorTool
    {
        /// <summary>Select a placement; drag it to move it; Delete removes it.</summary>
        Select,

        /// <summary>Paint the chosen surface (Ground) or segment (Structure); right button erases.</summary>
        Paint,

        /// <summary>Place the chosen sprite or actor on the cell clicked.</summary>
        Place,

        /// <summary>Paint collision codes over cells: <c>.</c> as the objects decide, <c>#</c> blocked, <c>o</c> forced open.</summary>
        Collision,

        /// <summary>Set the map's spawn point on the cell clicked.</summary>
        Spawn,

        /// <summary>Paint or erase (right button) the effect mask of the chosen atmosphere profile over cells.</summary>
        Effects,
    }

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

        private readonly RpgTextures textures = new();
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
        private string collisionCode = RpgCollision.Auto.ToString();
        private List<Vector2I> stroke;
        private MouseButton strokeButton;
        private Vector2I? dragOrigin;
        private Vector2I? hover;
        private readonly HashSet<string> marked = new(StringComparer.Ordinal);
        private Vector2I? boxFrom;
        private Vector2I? boxTo;
        private Vector2I? lastCell;
        private string effectProfile;

        /// <summary>Builds the window and opens the first writable mod's first map (or the choice of the selector).</summary>
        public RpgEditorWindow() : base("tools/rpg_editor/title")
        {
            builder = new RpgRenderSceneBuilder(textures.Image, textures.Charset);
            objectTable = Entities.Collect<RpgObject>();
            encountersShown = ModManager.Instance?.GetMod(BattlesIntegration) != null;
            BuildUi();
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
        public RpgMapDocument Document => document;

        /// <summary>The scene of the open map as the game reads it (null when no map is open).</summary>
        public RpgEditorScene Scene => scene;

        /// <summary>The canvas (the map view).</summary>
        public RpgEditorCanvas Canvas => canvas;

        /// <summary>The palette.</summary>
        public RpgEditorPalette Palette => palette;

        /// <summary>The inspector column.</summary>
        public RpgEditorInspector Inspector => inspector;

        /// <summary>Opens a map file the way the file selector does (asks first when there are unsaved changes).</summary>
        public void OpenMapFile(string mod, string path) => OnFileRequested(mod, path);

        /// <summary>
        /// Opens the map and chooses the pickup object (category <c>pickup</c>) of <paramref name="itemId"/>: the one whose id ends
        /// like the item's last segment (<c>rpg_plus/amber</c> → <c>rpg_plus/pickup/amber</c>). Null when the item has no pickup
        /// object; the item editor then writes one and asks again (plan 14.G4). The whole map is fitted to the canvas.
        /// </summary>
        public string PreparePickup(string modId, string mapPath, string itemId)
        {
            OpenMapFile(modId, mapPath);
            var name = itemId?.Split('/').Last();
            var chosen = name == null ? null : objectTable.Values
                .Where(o => o.Category == "pickup" && o.Form != null && o.ID.EndsWith("/" + name, StringComparison.Ordinal))
                .OrderBy(o => o.ID, StringComparer.Ordinal)
                .FirstOrDefault();
            if (chosen == null)
                return null;
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
        public string EffectProfile => effectProfile;

        /// <summary>Selects the effects tool and the atmosphere profile <paramref name="profileId"/> (plan 14.G10); the effects editor's button calls it.</summary>
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
            => document == null ? new List<string>() : document.Placements.Select(r => r.Id).Where(marked.Contains).ToList();

        /// <summary>
        /// "Play from here" (plan 14.G1): the running game enters the open map at the cell of the last click on the canvas (the
        /// spawn point before any click). The game reads the saved map, so unsaved changes are saved first.
        /// </summary>
        private void PlayFromHere()
        {
            if (document == null || scene == null)
                return;
            if (RpgWorldHost.Current == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/play_no_game"), ToolStatusKind.Error);
                return;
            }
            if ((_dirty || isNewMap) && !Save())
                return;
            if (!Entities.Collect<RpgMap>().ContainsKey(document.MapId))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/play_not_loaded"), ToolStatusKind.Error);
                return;
            }
            var cell = lastCell ?? new Vector2I(scene.Map.Spawn?.X ?? 0, scene.Map.Spawn?.Y ?? 0);
            RpgWorldHost.Teleport(new RpgTeleport { Map = document.MapId, X = cell.X, Y = cell.Y, Dir = RpgDirection.Down });
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/play_started"), document.MapId, cell.X, cell.Y), ToolStatusKind.Ok);
            GameLogger.Log(Category, $"play from '{document.MapId}' at {cell.X},{cell.Y}");
        }

        /// <summary>Ctrl+A: marks every placement of the map (plan 14.G9).</summary>
        private bool SelectAll()
        {
            if (document == null)
                return false;
            marked.Clear();
            foreach (var record in document.Placements.Where(r => !document.IsDeleted(r.Id)))
                marked.Add(record.Id);
            selectedId = null;
            RefreshSelection();
            return true;
        }

        /// <summary>The placements an action acts on: the marked ones in file order, else the selected one, else none.</summary>
        private List<string> ActedIds()
        {
            if (document == null)
                return new List<string>();
            if (marked.Count > 0)
                return Marked.ToList();
            return selectedId == null ? new List<string>() : new List<string> { selectedId };
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

        /// <summary>Ctrl+V: pastes the clipboard with its first cell on the hovered cell (else the last click); the copies are marked.</summary>
        private void PasteClipboard()
        {
            if (document == null)
                return;
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
                marked.Add(id);
            selectedId = null;
            AfterChange();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/pasted"), ids.Count), ToolStatusKind.Ok);
        }

        /// <summary>Delete: removes the acted-on placements in one step (plan 14.G9).</summary>
        private void DeleteSelection()
        {
            if (document == null)
                return;
            if (marked.Count == 0)
            {
                if (selectedId != null)
                    DeletePlacement(selectedId);
                return;
            }
            if (!document.DeletePlacements(marked.ToList()))
                return;
            marked.Clear();
            selectedId = null;
            AfterChange();
        }

        /// <summary>Marks the placements inside the dragged box (inclusive corners) and clears the single selection.</summary>
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
                foreach (var (id, _, x, y) in scene.Statics.Concat(scene.Actors))
                {
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1)
                        marked.Add(id);
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
            marked.RemoveWhere(id => document.IsDeleted(id) || !document.Placements.Any(r => r.Id == id));
        }

        /// <summary>The tool a click does.</summary>
        public RpgEditorTool Tool => tool;

        /// <summary>The sprite or actor the Place tool puts, or null.</summary>
        public string PlaceObject => placeObject;

        /// <summary>The selected placement's id, or null.</summary>
        public string SelectedId => selectedId;

        /// <summary>True when the rpg__battles integration is mounted, so the encounters section is shown.</summary>
        public bool EncountersShown => encountersShown;

        /// <summary>The map id of the open document (its scene name), or null.</summary>
        public string MapId => document?.MapId;

        /// <summary>Releases the textures and the builder; the canvas releases its scene.</summary>
        protected override void Cleanup()
        {
            canvas?.Clear();
            builder.Dispose();
            textures.Dispose();
            base.Cleanup();
        }

        // ── layout ─────────────────────────────────────────────────

        private void BuildUi()
        {
            var outer = CreateFillContainer();
            AddChild(outer);
            outer.AddChild(BuildToolbar());

            var main = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            outer.AddChild(main);

            var left = new VBoxContainer();
            left.AddChild(ToolUi.Section("tools/rpg_editor/palette_title"));
            palette = new RpgEditorPalette(objectTable, textures.Image, textures.Charset);
            palette.ObjectChosen += OnObjectChosen;
            palette.CharacterChosen += OnCharacterChosen;
            left.AddChild(palette);
            main.AddChild(CreateSidebar(left, 300));

            canvas = new RpgEditorCanvas();
            canvas.CellPressed += OnCellPressed;
            canvas.CellDragged += OnCellDragged;
            canvas.CellReleased += OnCellReleased;
            canvas.Hovered += OnHovered;
            main.AddChild(canvas);

            var right = new VBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            inspector = new RpgEditorInspector { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            inspector.PlacementMoved += OnPlacementMoved;
            inspector.PlacementDeleted += id => DeletePlacement(id);
            inspector.PlacementReset += OnPlacementReset;
            inspector.PropertyChanged += OnPropertyChanged;
            inspector.SpawnPickRequested += () => SelectTool(RpgEditorTool.Spawn);
            inspector.EncountersChanged += table => ChangeProperty("Encounters", table);
            inspector.LinkOpened += OnLinkOpened;
            WireInteractParts();
            scroll.AddChild(inspector);
            right.AddChild(scroll);
            main.AddChild(CreateSidebar(right, 320));
        }

        private HBoxContainer BuildToolbar()
        {
            var bar = CreateToolbar();
            selector = new ModFileSelector(MapDir, MapExtension);
            bar.AddChild(selector);
            saveButton = ToolUi.Button("tools/rpg_editor/save", () => Save(), ButtonKind.Primary, "tools/rpg_editor/save_tip");
            bar.AddChild(saveButton);
            playButton = ToolUi.Button("tools/rpg_editor/play", PlayFromHere, ButtonKind.Primary, "tools/rpg_editor/play_tip");
            bar.AddChild(playButton);
            undoButton = ToolUi.Button("tools/rpg_editor/undo", () => Undo(), ButtonKind.Secondary, "tools/rpg_editor/undo_tip");
            redoButton = ToolUi.Button("tools/rpg_editor/redo", () => Redo(), ButtonKind.Secondary, "tools/rpg_editor/redo_tip");
            bar.AddChild(undoButton);
            bar.AddChild(redoButton);
            bar.AddChild(ToolUi.Divider());
            foreach (var (id, key, tip) in new[]
            {
                (RpgEditorTool.Select, "tools/rpg_editor/tool_select", "tools/rpg_editor/tool_select_tip"),
                (RpgEditorTool.Paint, "tools/rpg_editor/tool_paint", "tools/rpg_editor/tool_paint_tip"),
                (RpgEditorTool.Place, "tools/rpg_editor/tool_place", "tools/rpg_editor/tool_place_tip"),
                (RpgEditorTool.Collision, "tools/rpg_editor/tool_collision", "tools/rpg_editor/tool_collision_tip"),
                (RpgEditorTool.Spawn, "tools/rpg_editor/tool_spawn", "tools/rpg_editor/tool_spawn_tip"),
                (RpgEditorTool.Effects, "tools/rpg_editor/tool_effects", "tools/rpg_editor/tool_effects_tip"),
            })
            {
                var button = new Button { Text = ToolUi.Tr(key), TooltipText = ToolUi.Tr(tip), ToggleMode = true, ButtonGroup = toolGroup, FocusMode = Control.FocusModeEnum.None };
                var target = id;
                button.Pressed += () => SelectTool(target);
                toolButtons[id] = button;
                bar.AddChild(button);
            }
            collisionPicker = new OptionButton { TooltipText = ToolUi.Tr("tools/rpg_editor/collision_tip") };
            collisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_auto"), 0);
            collisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_block"), 1);
            collisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_open"), 2);
            collisionPicker.ItemSelected += index => collisionCode = index switch { 1 => "#", 2 => "o", _ => "." };
            bar.AddChild(collisionPicker);
            effectPicker = new OptionButton { Name = "EffectProfile", TooltipText = ToolUi.Tr("tools/rpg_editor/effect_profile_tip") };
            effectPicker.ItemSelected += index => effectProfile = effectPicker.GetItemMetadata((int)index).AsString();
            foreach (var profile in AtmosphereCatalog.Collect().Keys.OrderBy(k => k, StringComparer.Ordinal))
            {
                effectPicker.AddItem(profile);
                effectPicker.SetItemMetadata(effectPicker.ItemCount - 1, profile);
            }
            if (effectPicker.ItemCount > 0)
            {
                effectPicker.Select(0);
                effectProfile = effectPicker.GetItemMetadata(0).AsString();
            }
            bar.AddChild(effectPicker);
            bar.AddChild(ToolUi.Divider());
            projectionPicker = new OptionButton { TooltipText = ToolUi.Tr("tools/rpg_editor/projection_tip") };
            projectionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/projection_top"), 0);
            projectionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/projection_iso"), 1);
            projectionPicker.ItemSelected += index => SetProjection(index == 1 ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown);
            bar.AddChild(projectionPicker);
            bar.AddChild(ToolUi.Button("tools/rpg_editor/fit", () => canvas?.FitToView(), ButtonKind.Secondary, "tools/rpg_editor/fit_tip"));
            return bar;
        }

        // ── files ──────────────────────────────────────────────────

        private void OnFileRequested(string mod, string path)
        {
            if (document != null && (mod != modId || path != mapPath))
            {
                RunDiscardingChanges(() => OpenFile(mod, path), () => selector.Show(modId, mapPath));
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
            SetStatus(string.Format(ToolUi.Tr(isNewMap ? "tools/rpg_editor/status_new" : "tools/rpg_editor/status_opened"), id, mod), ToolStatusKind.Info);
            GameLogger.Log(Category, $"opened map '{id}' of '{mod}'" + (isNewMap ? " (new)" : string.Empty));
        }

        private (RpgMapDocument Document, bool New)? OpenDocument(string mod, string path, string id)
        {
            var files = OwnFiles(mod);
            var doc = RpgMapDocument.Open(id, mod, files, ForeignOf("rpg_map", mod), ForeignOf("rpg_placement", mod),
                BaseOf(mod), FormOf);
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
                ["Spawn"] = new JsonObject { ["X"] = 1, ["Y"] = 1, ["Dir"] = "Down" },
                ["Legend"] = new JsonObject(),
            };
            var files = OwnFiles(mod).ToList();
            files.Add((path, EntityDocument.Serialize(new JsonObject { ["Kind"] = "rpg_map", ["Items"] = new JsonArray(template) })));
            var doc = RpgMapDocument.Open(id, mod, files, ForeignOf("rpg_map", mod), ForeignOf("rpg_placement", mod),
                BaseOf(mod), FormOf);
            return doc == null ? null : (doc, true);
        }

        private void CloseDocument()
        {
            document = null;
            scene = null;
            selectedId = null;
            stroke = null;
            dragOrigin = null;
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
                    continue;
                var text = ModFileService.ReadText(mod, path);
                if (text != null)
                    result.Add((path, text));
            }
            return result;
        }

        private static List<(string Id, string OwnerMod, string File, JsonObject Written)> ForeignOf(string kind, string mod)
            => ForeignEntities.Scan(kind, mod).Where(e => !e.File.StartsWith(TestSuites, StringComparison.Ordinal)).ToList();

        private static Func<string, string, JsonObject> BaseOf(string mod)
            => (kind, id) => ForeignEntities.BaseWithout(kind, id, mod, ForeignEntities.PatchFile(kind, mod));

        private Func<string, RpgObjectForm?> FormOf
            => id => ObjectOf(id)?.Form;

        private RpgObject ObjectOf(string id)
            => id != null && objectTable.TryGetValue(id, out var value) ? value : null;

        private void OnLinkOpened(string mapId, int x, int y)
        {
            if (FindMapFile(mapId) is not { } found)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/link_no_file"), mapId), ToolStatusKind.Error);
                return;
            }
            RunDiscardingChanges(() => OpenFile(found.Mod, found.Path), () => selector.Show(modId, mapPath));
        }

        /// <summary>The writable mod and the path of a map file with this id, or null.</summary>
        private static (string Mod, string Path)? FindMapFile(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return null;
            foreach (var mod in ModFileService.ListWritableMods())
            {
                var path = ModFileService.ListFiles(mod, MapDir, MapExtension).FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == mapId);
                if (path != null)
                    return (mod, path);
            }
            return null;
        }

        /// <summary>Saves the document: changed files, the new map's file and scene (first save), the caches, and the running game.</summary>
        public bool Save()
        {
            if (document == null)
                return false;
            var problems = document.Validate();
            if (problems.Count > 0)
            {
                SetStatus(string.Format(ToolUi.Tr(problems[0].Key), problems[0].Argument), ToolStatusKind.Error);
                return false;
            }
            var files = document.Changes();
            if (isNewMap)
            {
                var text = EntityDocument.Serialize(new JsonObject { ["Kind"] = "rpg_map", ["Items"] = new JsonArray(document.Map.Data.DeepClone()) });
                files.RemoveAll(f => f.Path == mapPath);
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
                WriteSceneOf(document.MapId);
            document.MarkSaved();
            isNewMap = false;
            MarkClean();
            ClearCaches();
            SceneManager.ReloadContent();
            GameLogger.Log(Category, $"saved map '{document.MapId}' of '{modId}': {files.Count} file(s)");
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/status_saved"), document.MapId, files.Count), ToolStatusKind.Ok);
            UpdateButtons();
            return true;
        }

        private void WriteSceneOf(string mapId)
        {
            var scenePath = SceneFileEntry.PathFor(mapId);
            if (ModFileService.ReadText(modId, scenePath) != null)
                return;
            var backgrounds = ModResourceService.GetBackgroundIds();
            var entry = new SceneFileEntry { ModId = modId, RelativePath = scenePath, Name = mapId };
            if (backgrounds.Count > 0)
                entry.Backgrounds.Add(backgrounds[0]);
            if (!SceneFileService.Write(entry))
                GameLogger.Log(Category, $"scene '{mapId}' of '{modId}' could not be written", LogLevel.Error);
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
                return false;
            AfterChange();
            return true;
        }

        /// <summary>Steps forward again.</summary>
        public bool Redo()
        {
            if (document == null || !document.Redo())
                return false;
            AfterChange();
            return true;
        }

        private void AfterChange()
        {
            if (selectedId != null && !document.Placements.Any(r => r.Id == selectedId))
                selectedId = null;
            PruneMarked();
            SetDirty();
            RebuildScene();
        }

        private void ChangeProperty(string key, JsonNode value)
        {
            if (document == null)
                return;
            if (document.SetProperty(key, value))
                AfterChange();
        }

        private void OnPropertyChanged(string key, JsonNode value) => ChangeProperty(key, value);

        private void OnPlacementMoved(string id, int x, int y)
        {
            if (document != null && document.MovePlacement(id, x, y))
                AfterChange();
        }

        private void OnPlacementReset(string id)
        {
            if (document != null && document.ResetPlacement(id))
                AfterChange();
        }

        private void DeletePlacement(string id)
        {
            if (document == null || !document.DeletePlacement(id))
                return;
            if (selectedId == id)
                selectedId = null;
            AfterChange();
        }

        // ── tools ──────────────────────────────────────────────────

        /// <summary>Makes <paramref name="next"/> the tool a click does (and shows it on the toolbar).</summary>
        public void SelectTool(RpgEditorTool next)
        {
            SetTool(next);
            if (toolButtons.TryGetValue(next, out var button))
                button.SetPressedNoSignal(true);
        }

        private void SetTool(RpgEditorTool next)
        {
            tool = next;
            stroke = null;
            dragOrigin = null;
            if (toolButtons.TryGetValue(next, out var button))
                button.SetPressedNoSignal(true);
            RebuildMarks();
        }

        /// <summary>Chooses an object from the palette: a surface or a segment paints, a sprite or an actor is placed.</summary>
        public void ChooseObject(string id, RpgObjectForm form)
        {
            var obj = ObjectOf(id);
            if (obj == null)
                return;
            ghostTexture = RpgEditorPalette.ThumbnailOf(obj, textures.Image, textures.Charset);
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

        private void OnObjectChosen(string id, RpgObjectForm form) => ChooseObject(id, form);

        /// <summary>Switches the renderer; the view stays where it is.</summary>
        public void SetProjection(string id)
        {
            canvas?.SetProjection(id);
            var index = id == RpgEditorCanvas.Isometric ? 1 : 0;
            if (projectionPicker != null && projectionPicker.Selected != index)
                projectionPicker.Select(index);
            RebuildMarks();
        }

        // ── canvas input ───────────────────────────────────────────

        private void OnCellPressed(Vector2I cell, MouseButton button, bool doubleClick)
        {
            if (document == null)
                return;
            lastCell = cell;
            if (button == MouseButton.Left && TryPickTargetCell(cell))
                return;
            if (button == MouseButton.Right)
            {
                if (tool is RpgEditorTool.Paint or RpgEditorTool.Collision or RpgEditorTool.Effects)
                    BeginStroke(cell, button);
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
                case RpgEditorTool.Select:
                    selectedId = PlacementAt(cell);
                    dragOrigin = selectedId == null ? null : cell;
                    marked.Clear();
                    // A press on empty ground starts a box (plan 14.G9); a press on a placement selects it and may drag it.
                    boxFrom = selectedId == null ? cell : null;
                    boxTo = boxFrom;
                    RefreshSelection();
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
                        AfterChange();
                    break;
            }
        }

        private void BeginStroke(Vector2I cell, MouseButton button)
        {
            stroke = new List<Vector2I> { cell };
            strokeButton = button;
            RebuildMarks();
        }

        private void OnCellDragged(Vector2I cell, MouseButton button)
        {
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
            if (document == null)
                return;
            if (boxFrom != null)
            {
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
                    RpgEditorTool.Collision => document.PaintCollision(cells.Select(c => (c.X, c.Y)), collisionCode[0]),
                    RpgEditorTool.Effects => document.PaintEffect(effectProfile, cells.Select(c => (c.X, c.Y)), erase),
                    _ => PaintCells(cells, erase),
                };
                if (changed)
                    AfterChange();
                else
                    RebuildMarks();
                return;
            }
            if (tool == RpgEditorTool.Select && selectedId != null && dragOrigin is { } origin && cell != origin && cell.X >= 0 && cell.Y >= 0)
            {
                if (document.MovePlacement(selectedId, cell.X, cell.Y))
                    AfterChange();
            }
            dragOrigin = null;
        }

        private bool PaintCells(IEnumerable<Vector2I> cells, bool erase)
        {
            var layer = brushForm == RpgObjectForm.Segment ? RpgStrokeLayer.Structure : RpgStrokeLayer.Ground;
            var list = cells.Select(c => (c.X, c.Y));
            if (erase)
                return document.Paint(layer, list, null);
            if (brushObject == null)
                return false;
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
            if (selectedId != null && (document == null || !document.Placements.Any(r => r.Id == selectedId)))
                selectedId = null;
            PruneMarked();
            RebuildMarks();
            RefreshInspector();
        }

        /// <summary>The placement of this map under a cell: the one whose origin is the cell, else the one whose footprint covers it.</summary>
        public string PlacementAt(Vector2I cell)
        {
            if (scene == null)
                return null;
            var ids = scene.Statics.Select(s => (s.Id, s.X, s.Y)).Concat(scene.Actors.Select(a => (a.Id, a.X, a.Y))).ToList();
            var origin = ids.FirstOrDefault(p => p.X == cell.X && p.Y == cell.Y);
            if (origin.Id != null)
                return origin.Id;
            foreach (var (id, x, y) in ids)
            {
                var obj = scene.ObjectOf(id);
                if (obj != null && cell.X >= x && cell.Y >= y && cell.X < x + obj.Width && cell.Y < y + obj.Height)
                    return id;
            }
            return null;
        }

        // ── rebuilding what is shown ───────────────────────────────

        /// <summary>Rebuilds the scene from the document (after a change) and shows it.</summary>
        private void RebuildScene()
        {
            if (document == null)
            {
                canvas?.Clear();
                RefreshInspector();
                UpdateButtons();
                return;
            }
            scene = RpgEditorScene.From(document, id => objectTable.TryGetValue(id, out var o) ? o : null);
            var tileScene = builder.Build(scene.Map, scene.Cells, scene.Statics);
            canvas.Actors.Clear();
            foreach (var actor in scene.Actors)
            {
                var look = builder.ActorLook(actor.Object, tileScene.TileSize);
                if (look == null)
                    continue;
                canvas.Actors.Add(new TileActorPose
                {
                    Id = actor.Id,
                    Visible = true,
                    Charset = true,
                    Facing = 1,
                    Column = TileWalkSheet.IdleColumn,
                    Position = new Vector2(actor.X, actor.Y),
                    Look = look,
                    Tint = Colors.White,
                });
            }
            canvas.SetScene(tileScene);
            if (selectedId != null && !document.Placements.Any(r => r.Id == selectedId))
                selectedId = null;
            RebuildMarks();
            RefreshInspector();
            UpdateButtons();
        }

        private void RebuildMarks()
        {
            if (canvas == null)
                return;
            var marks = canvas.Marks;
            marks.Clear();
            if (scene != null)
            {
                foreach (var (id, obj, x, y) in scene.Statics.Concat(scene.Actors))
                    marks.Add(new CanvasRect(x, y, obj.Width, obj.Height, FootprintLine, false));
                RpgEffectsLayer.AddMarks(scene.Map, marks);
                foreach (var (id, obj, x, y) in scene.Statics.Concat(scene.Actors))
                {
                    if (marked.Contains(id))
                        marks.Add(new CanvasRect(x, y, obj.Width, obj.Height, SelectFill, true));
                }
                if (boxFrom is { } boxA && boxTo is { } boxB)
                {
                    var bx = Math.Min(boxA.X, boxB.X);
                    var by = Math.Min(boxA.Y, boxB.Y);
                    marks.Add(new CanvasRect(bx, by, Math.Abs(boxA.X - boxB.X) + 1, Math.Abs(boxA.Y - boxB.Y) + 1, SelectFill, true));
                }
                if (selectedId != null && scene.ObjectOf(selectedId) is { } selected)
                {
                    var at = scene.Statics.Concat(scene.Actors).FirstOrDefault(p => p.Id == selectedId);
                    marks.Add(new CanvasRect(at.X, at.Y, selected.Width, selected.Height, SelectFill, true));
                    marks.Add(new CanvasRect(at.X, at.Y, selected.Width, selected.Height, HoverLine, false));
                }
                if (scene.Map != null)
                    marks.Add(new CanvasRect(scene.Map.Spawn?.X ?? 0, scene.Map.Spawn?.Y ?? 0, 1, 1, SpawnFill, true));
                if (stroke != null)
                {
                    var fill = strokeButton == MouseButton.Right || (tool == RpgEditorTool.Collision && collisionCode == RpgCollision.Block.ToString()) ? EraseFill
                        : tool == RpgEditorTool.Effects ? RpgEffectsLayer.MaskFill : PaintFill;
                    foreach (var cell in stroke)
                        marks.Add(new CanvasRect(cell.X, cell.Y, 1, 1, fill, true));
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
                else if (tool is RpgEditorTool.Paint or RpgEditorTool.Collision or RpgEditorTool.Spawn or RpgEditorTool.Place or RpgEditorTool.Effects)
                {
                    marks.Add(new CanvasRect(at2.X, at2.Y, 1, 1, HoverLine, false));
                }
            }
            canvas.Refresh();
        }

        private void RefreshInspector()
        {
            if (inspector == null)
                return;
            if (document == null || scene == null)
            {
                inspector.Show(new RpgEditorInspector.State { Encounters = false });
                return;
            }
            var mapId = document.MapId;
            var placements = document.Placements
                .Where(r => RpgMapDocument.MapOfPlacement(r.Id) == mapId && !document.IsDeleted(r.Id))
                .Select(r => RpgEditorScene.PlacementOf(r.Data))
                .ToList();
            var maps = Entities.Collect<RpgMap>();
            maps[mapId] = scene.Map;
            var outgoing = RpgTeleportLinks.Outgoing(mapId, placements, scene.Map, id => ObjectOf(id), maps);
            var others = Entities.Collect<RpgPlacement>()
                .Values
                .Where(p => RpgMapDocument.MapOfPlacement(p.ID) != mapId && RpgMapDocument.MapOfPlacement(p.ID) != null)
                .Select(p => (Placement: p, Map: maps.TryGetValue(RpgMapDocument.MapOfPlacement(p.ID), out var m) ? m : null))
                .ToList();
            var incoming = RpgTeleportLinks.Incoming(mapId, others, id => ObjectOf(id));
            UpdateArrows(mapId, outgoing, incoming);
            var selectedObject = selectedId == null ? null : scene.ObjectOf(selectedId)?.ID;
            inspector.Show(new RpgEditorInspector.State
            {
                Document = document,
                Placement = selectedId,
                ObjectId = selectedObject,
                Outgoing = outgoing,
                Incoming = incoming,
                MapSize = new Vector2I(scene.Map.Width, scene.Map.Height),
                Encounters = encountersShown,
                Interact = InteractOfSelected(),
                Variants = VariantNames(),
            });
        }

        private void UpdateButtons()
        {
            if (undoButton == null)
                return;
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
        protected override void OnSaveShortcut() => Save();

        /// <summary>Ctrl+Z undoes, Ctrl+Y and Ctrl+Shift+Z redo, Delete removes the selected placement, Escape clears the selection.</summary>
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
                case Key.Escape:
                    selectedId = null;
                    stroke = null;
                    marked.Clear();
                    boxFrom = null;
                    boxTo = null;
                    RefreshSelection();
                    return true;
            }
            return false;
        }

    }
}
