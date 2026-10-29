using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.UI;
using Cthangover.Core.UI.Theming;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.TileRender;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Editor of RPG tile maps (<c>maps/*.json</c>, kind <c>rpg_map</c>).
    /// Layout: toolbar (file, save, layer, brush, grid, zoom, undo/redo, play),
    /// the tile palette, the game's characters, items and all map appearances as tabs on the left, the
    /// map in the middle, the map's and the selected event's properties on the right.
    /// A character is put on the map as an NPC or an enemy, an item as a pickup:
    /// pick it, press the button, click a cell (<see cref="RpgMapDocument.AddCharacter"/>,
    /// <see cref="RpgMapDocument.AddItem"/>).
    /// <para/>
    /// Layers are the game's: back, main, front, the collision layer (codes
    /// over the tiles' own solidity) and events. Every edit is undoable
    /// (Ctrl+Z / Ctrl+Y); Ctrl+S saves. A new file becomes a map filled with the
    /// first tile of the first tileset, and the first save also writes
    /// <c>scenes/&lt;id&gt;.json</c> when the mod has no such scene, because the
    /// map id is the scene name; the game re-reads its scenes right away
    /// (<see cref="SceneManager.ReloadContent"/>), so "Play" works on a new map
    /// without a mod reload.
    /// <para/>
    /// Saving writes the map file and the mod's events file of the map,
    /// <c>map_events/&lt;id&gt;/events.json</c> (<see cref="RpgMapDocument"/> explains
    /// own and locked events).
    /// <para/>
    /// After a save the map is put into the running game's map cache, and a
    /// world showing it is remounted, so the edit is visible at once; "Play"
    /// saves and moves the player onto the map at its spawn.
    /// </summary>
    public partial class RpgMapEditorWindow : ToolWindow
    {
        private const string MapDir = "maps";

        private readonly RpgTextures textures = new();
        private readonly Dictionary<RpgEditLayer, Button> layerButtons = new();
        private readonly Dictionary<RpgEditTool, Button> toolButtons = new();

        private ModFileSelector fileSelector;
        private TabContainer sideTabs;
        private RpgTilePalette palette;
        private OptionButton atlasPicker;
        private readonly List<string> atlasIds = new();
        private string paletteSource;
        private OptionButton rendererPicker;
        private SpinBox itemCount;
        private Func<Vector2I, RpgMapEvent> placeAt;
        private string placedKey;
        private string placedName;
        private RpgMapEditorCanvas canvas;
        private RpgMapPropertiesPanel mapPanel;
        private RpgEventPropertiesPanel eventPanel;
        private CheckBox gridCheck;
        private Label zoomLabel;

        private RpgMapDocument document;
        private string modId;
        private string path;
        private bool isNew;
        private string mapBaseline;
        private RpgMapEvent pendingEventSource;

        /// <summary>Builds the window and opens the first map found.</summary>
        public RpgMapEditorWindow() : base("tools/rpg_editor/title")
        {
            CthangoverTheme.Apply(this);
            BuildUi();
            FocusEntered += RefreshTilesetOnFocus;
            SetHint(ToolUi.Tr("tools/rpg_editor/hint"));
            SelectLayer(RpgEditLayer.Back);
            SelectTool(RpgEditTool.Pencil);
            fileSelector.Refresh();
        }

        /// <summary>Child pickers release their preview regions on exit before the window releases the shared source images.</summary>
        protected override void Cleanup()
        {
            placeAt = null;
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

            var leftColumn = new VBoxContainer();
            sideTabs = new TabContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            leftColumn.AddChild(sideTabs);

            var paletteColumn = new VBoxContainer();
            paletteColumn.AddChild(ToolUi.Section("tools/rpg_editor/section_palette"));
            atlasPicker = new OptionButton { Name = "AtlasPicker", TooltipText = ToolUi.Tr("tools/rpg_editor/atlas_tip") };
            atlasPicker.ItemSelected += OnAtlasSelected;
            paletteColumn.AddChild(atlasPicker);
            palette = new RpgTilePalette();
            palette.TileSelected += OnPaletteTile;
            palette.StampSelected += OnPaletteStamp;
            palette.CollisionSelected += OnPaletteCollision;
            paletteColumn.AddChild(palette);
            sideTabs.AddChild(paletteColumn);
            sideTabs.SetTabTitle(0, ToolUi.Tr("tools/rpg_editor/tab_tiles"));

            var characters = new RpgEntityPicker("tools/rpg_editor/character_search", "tools/rpg_editor/characters_hint") { Name = "Characters" };
            characters.Source = LoadCharacters;
            characters.AddAction("tools/rpg_editor/place_npc", "tools/rpg_editor/place_npc_tip", PlaceNpc);
            characters.AddAction("tools/rpg_editor/place_enemy", "tools/rpg_editor/place_enemy_tip", PlaceEnemy);
            sideTabs.AddChild(characters);
            sideTabs.SetTabTitle(1, ToolUi.Tr("tools/rpg_editor/tab_characters"));

            var items = new RpgEntityPicker("tools/rpg_editor/item_search", "tools/rpg_editor/items_hint") { Name = "Items" };
            items.Source = LoadItems;
            itemCount = ToolUi.Number(1, 999, 1, null);
            itemCount.Value = 1;
            itemCount.TooltipText = ToolUi.Tr("tools/rpg_editor/item_count_tip");
            items.AddOption(new Label { Text = ToolUi.Tr("tools/rpg_editor/item_count") });
            items.AddOption(itemCount);
            items.AddAction("tools/rpg_editor/place_item", "tools/rpg_editor/place_item_tip", PlaceItem);
            sideTabs.AddChild(items);
            sideTabs.SetTabTitle(2, ToolUi.Tr("tools/rpg_editor/tab_items"));

            var appearances = new RpgEntityPicker("tools/rpg_editor/appearance_search", "tools/rpg_editor/appearances_hint") { Name = "Appearances" };
            appearances.Source = LoadAppearances;
            appearances.AddAction("tools/rpg_editor/place_appearance", "tools/rpg_editor/place_appearance_tip", PlaceAppearance);
            sideTabs.AddChild(appearances);
            sideTabs.SetTabTitle(3, ToolUi.Tr("tools/rpg_editor/tab_appearances"));
            sideTabs.TabChanged += OnSideTabChanged;
            main.AddChild(CreateSidebar(leftColumn, 400));

            var center = new HSplitContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            main.AddChild(center);

            canvas = new RpgMapEditorCanvas();
            canvas.EditStarting += RecordUndo;
            canvas.Changed += OnDocumentChanged;
            canvas.EventSelected += OnEventSelected;
            canvas.TilePicked += OnTilePicked;
            canvas.CellChosen += OnCellChosen;
            center.AddChild(Scroll(canvas));

            var properties = new VBoxContainer();
            mapPanel = new RpgMapPropertiesPanel();
            mapPanel.EditStarting += RecordUndo;
            mapPanel.Changed += OnDocumentChanged;
            mapPanel.TilesetChanged += LoadTileset;
            mapPanel.ResizeRequested += OnResizeRequested;
            properties.AddChild(mapPanel);
            eventPanel = new RpgEventPropertiesPanel();
            eventPanel.EditStarting += RecordUndo;
            eventPanel.Changed += OnDocumentChanged;
            eventPanel.DeleteRequested += DeleteSelectedEvent;
            eventPanel.EditSourceRequested += EditEventSource;
            properties.AddChild(eventPanel);
            center.AddChild(CreateSidebar(properties, 340));
        }

        private HBoxContainer BuildToolbar()
        {
            var toolbar = CreateToolbar();
            fileSelector = new ModFileSelector(MapDir, ".json");
            fileSelector.FileRequested += OnFileRequested;
            toolbar.AddChild(fileSelector);
            toolbar.AddChild(ToolUi.Button("tools/common/save", OnSavePressed, ButtonKind.Primary, "tools/common/save_tip"));
            toolbar.AddChild(ToolUi.Divider());

            AddLayerButton(toolbar, RpgEditLayer.Back, "tools/rpg_editor/layer_back");
            AddLayerButton(toolbar, RpgEditLayer.Main, "tools/rpg_editor/layer_main");
            AddLayerButton(toolbar, RpgEditLayer.Front, "tools/rpg_editor/layer_front");
            AddLayerButton(toolbar, RpgEditLayer.Collision, "tools/rpg_editor/layer_collision");
            AddLayerButton(toolbar, RpgEditLayer.Events, "tools/rpg_editor/layer_events");
            toolbar.AddChild(ToolUi.Divider());

            AddToolButton(toolbar, RpgEditTool.Pencil, "tools/rpg_editor/tool_pencil");
            AddToolButton(toolbar, RpgEditTool.Eraser, "tools/rpg_editor/tool_eraser");
            AddToolButton(toolbar, RpgEditTool.Fill, "tools/rpg_editor/tool_fill");
            toolbar.AddChild(ToolUi.Divider());

            rendererPicker = new OptionButton { Name = "RendererPicker" };
            var wantedRenderer = Cthangover.Core.Games.ActiveGame.GetSetting("tile.renderer") ?? "topdown";
            foreach (var arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--editor-renderer=")) wantedRenderer = arg.Substring(18);
            foreach (var id in TileRendererRegistry.Instance.Ids)
            {
                var index = rendererPicker.ItemCount;
                var label = id switch
                {
                    "topdown" => ToolUi.Tr("tools/rpg_editor/view_topdown"),
                    "isometric" => ToolUi.Tr("tools/rpg_editor/view_isometric"),
                    _ => id
                };
                rendererPicker.AddItem(label);
                rendererPicker.SetItemMetadata(index, id);
                if (id == wantedRenderer) rendererPicker.Select(index);
            }
            rendererPicker.ItemSelected += OnRendererSelected;
            toolbar.AddChild(rendererPicker);
            gridCheck = new CheckBox { Text = ToolUi.Tr("tools/rpg_editor/grid"), ButtonPressed = true };
            gridCheck.Toggled += OnGridToggled;
            toolbar.AddChild(gridCheck);
            var zoomOut = UiKit.IconButton("−", ToolUi.Tr("tools/rpg_editor/zoom_out"));
            zoomOut.Pressed += OnZoomOut;
            toolbar.AddChild(zoomOut);
            zoomLabel = new Label { CustomMinimumSize = new Vector2(48, 0), HorizontalAlignment = HorizontalAlignment.Center };
            toolbar.AddChild(zoomLabel);
            var zoomIn = UiKit.IconButton("+", ToolUi.Tr("tools/rpg_editor/zoom_in"));
            zoomIn.Pressed += OnZoomIn;
            toolbar.AddChild(zoomIn);
            toolbar.AddChild(ToolUi.Divider());

            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/undo", Undo, ButtonKind.Secondary, "tools/rpg_editor/undo_tip"));
            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/redo", Redo, ButtonKind.Secondary, "tools/rpg_editor/redo_tip"));
            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/play", Play, ButtonKind.Secondary, "tools/rpg_editor/play_tip"));
            return toolbar;
        }

        private void AddLayerButton(HBoxContainer toolbar, RpgEditLayer layer, string key)
        {
            var button = ToolUi.Button(key, null);
            button.ToggleMode = true;
            button.Pressed += () => SelectLayer(layer);
            layerButtons[layer] = button;
            toolbar.AddChild(button);
        }

        private void AddToolButton(HBoxContainer toolbar, RpgEditTool tool, string key)
        {
            var button = ToolUi.Button(key, null);
            button.ToggleMode = true;
            button.Pressed += () => SelectTool(tool);
            toolButtons[tool] = button;
            toolbar.AddChild(button);
        }

        private static ScrollContainer Scroll(Control content)
        {
            var scroll = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
            content.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(content);
            return scroll;
        }

        // ── file ───────────────────────────────────────────────────

        private void OnFileRequested(string mod, string file)
        {
            RunDiscardingChanges(() => Open(mod, file), () => fileSelector.Show(modId, path));
        }

        private void Open(string mod, string file)
        {
            modId = mod;
            path = file;
            document = null;
            isNew = false;
            MarkClean();

            if (mod == null)
            {
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
            }
            else if (file == null)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/common/no_files"), mod));
            }
            else
            {
                try
                {
                    var text = ModFileService.ReadText(mod, file);
                    isNew = string.IsNullOrEmpty(text);
                    document = isNew ? CreateDocument(file) : LoadDocument(mod, text);
                    SetStatus(string.Format(ToolUi.Tr(isNew ? "tools/common/new_file_status" : "tools/common/loaded"), mod + "/" + file));
                }
                catch (Exception ex)
                {
                    SetStatus($"{ToolUi.Tr("tools/common/load_error")}: {ex.Message}", ToolStatusKind.Error);
                    GameLogger.Log("RPG_EDITOR", $"load failed for '{mod}/{file}': {ex.Message}", LogLevel.Error);
                }
            }

            fileSelector.Show(modId, path);
            canvas.SelectedEvent = null;
            BindDocument();
            mapBaseline = document?.ToJson();
        }

        /// <summary>
        /// The map with the editor's events file of the same mod; every other event
        /// the game shows on the map is locked. An own event hides a locked one of the
        /// same id (another mod replacing it), so the author sees and edits their own.
        /// </summary>
        private static RpgMapDocument LoadDocument(string mod, string text, string eventsMod = null, string file = null)
        {
            var id = RpgMapDocument.Load(text).Map.ID;
            var eventsPath = file ?? RpgMapDocument.EventsPath(id);
            eventsMod ??= mod;
            var document = RpgMapDocument.Load(text, ModFileService.ReadText(eventsMod, eventsPath));
            document.EventsModId = eventsMod;
            document.EventsFile = eventsPath;
            var own = new HashSet<string>(document.Events.Select(e => e.Id), StringComparer.OrdinalIgnoreCase);
            document.SetLockedEvents(RpgMapEvents.Of(id)
                .Where(e => !(e.ModId == eventsMod && e.File == eventsPath) && !own.Contains(e.Id))
                .ToList());
            return document;
        }

        private static RpgMapDocument CreateDocument(string file)
        {
            var tileset = Entities.Collect<RpgTileset>().Keys.OrderBy(k => k).FirstOrDefault();
            return RpgMapDocument.Create(Path.GetFileNameWithoutExtension(file), tileset, 20, 15, 0);
        }

        /// <summary>Ctrl+S.</summary>
        protected override void OnSaveShortcut() => Save();

        private void OnSavePressed() => Save();

        private bool Save()
        {
            if (document == null || path == null)
            {
                SetStatus(ToolUi.Tr("tools/common/no_file_to_save"), ToolStatusKind.Error);
                return false;
            }

            var problems = document.Validate();
            if (problems.Count > 0)
            {
                SetStatus(string.Format(ToolUi.Tr(problems[0].Key), problems[0].Argument), ToolStatusKind.Error);
                return false;
            }

            var json = document.ToJson();
            var eventsPath = document.EventsFile ?? RpgMapDocument.EventsPath(document.Map.ID);
            var eventsMod = document.EventsModId ?? modId;
            var writeEvents = document.Events.Count > 0 || !string.IsNullOrEmpty(ModFileService.ReadText(eventsMod, eventsPath));
            if (((isNew || json != mapBaseline) && !ModFileService.WriteText(modId, path, json))
                || (writeEvents && !ModFileService.WriteText(eventsMod, eventsPath, document.EventsToJson())))
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return false;
            }
            RpgMapEvents.Invalidate();
            mapBaseline = json;

            var sceneNote = isNew ? EnsureScene() : null;
            if (sceneNote != null)
                SceneManager.ReloadContent();
            isNew = false;
            MarkClean();
            PublishToGame(json);
            SetStatus(string.Format(ToolUi.Tr("tools/common/saved"), modId + "/" + path) + (sceneNote ?? string.Empty), ToolStatusKind.Ok);
            return true;
        }

        /// <summary>Writes <c>scenes/&lt;id&gt;.json</c> for a new map when the mod has none; returns a status suffix.</summary>
        private string EnsureScene()
        {
            var scene = new SceneFileEntry { ModId = modId, Name = document.Map.ID, RelativePath = SceneFileEntry.PathFor(document.Map.ID) };
            if (!string.IsNullOrEmpty(ModFileService.ReadText(modId, scene.RelativePath)))
                return null;
            return SceneFileService.Write(scene)
                ? " · " + string.Format(ToolUi.Tr("tools/rpg_editor/scene_created"), scene.RelativePath)
                : null;
        }

        private static void PublishToGame(string json)
        {
            var copy = RpgMapDocument.Load(json).Map;
            RpgMapFactory.Instance.Publish(copy);
            var host = RpgWorldHost.Current;
            if (host?.View?.Map?.ID == copy.ID)
                host.Mount(copy.ID);
        }

        private void Play()
        {
            if (!Save())
                return;
            var sceneManager = SceneContextNode.FindNode<SceneManager>("SceneManager");
            if (sceneManager == null || !sceneManager.HasScene(document.Map.ID))
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/play_no_scene"), document.Map.ID), ToolStatusKind.Error);
                return;
            }
            var spawn = document.Map.Spawn;
            RpgWorldHost.Teleport(new RpgTeleport { Map = document.Map.ID, X = spawn.X, Y = spawn.Y, Dir = spawn.Dir });
        }

        // ── editing ────────────────────────────────────────────────

        private void BindDocument()
        {
            if (document != null)
                foreach (var mapEvent in document.Events)
                {
                    mapEvent.ModId = document.EventsModId ?? modId;
                    mapEvent.File = document.EventsFile ?? RpgMapDocument.EventsPath(document.Map.ID);
                }
            canvas.Document = document;
            mapPanel.Bind(document);
            OnEventSelected(canvas.SelectedEvent);
            mapPanel.Visible = canvas.ActiveLayer != RpgEditLayer.Events;
            eventPanel.Visible = canvas.ActiveLayer == RpgEditLayer.Events;
            LoadTileset();
        }

        /// <summary>
        /// The canvas paints with the map's composed tileset (its own plus the borrowed ones, <see cref="RpgTilesetComposer"/>);
        /// the palette shows one source atlas at a time, picked above it. Picking a tileset the map does not use yet
        /// appends it to <see cref="RpgMap.Tilesets"/>, so its objects can be put on this map.
        /// </summary>
        private void LoadTileset()
        {
            var tileset = document == null ? null : RpgTilesetFactory.Instance.ForMap(document.Map);
            canvas.Tileset = tileset;
            canvas.Atlas = tileset == null ? null : textures.Tileset(tileset.Image);
            FillAtlasPicker();
            ShowPaletteSource();
            canvas.Refresh();
            UpdateZoomLabel();
        }

        private void RefreshTilesetOnFocus()
        {
            if (document != null)
                LoadTileset();
        }

        private List<string> MapTilesets()
        {
            var result = new List<string> { document?.Map.Tileset ?? string.Empty };
            result.AddRange(document?.Map.Tilesets ?? new List<string>());
            return result;
        }

        private void FillAtlasPicker()
        {
            atlasPicker.Clear();
            atlasIds.Clear();
            if (document == null)
                return;
            var used = MapTilesets();
            if (paletteSource == null || !used.Contains(paletteSource))
                paletteSource = used[0];
            foreach (var id in used)
            {
                atlasPicker.AddItem(id == used[0] ? id : "+ " + id);
                atlasIds.Add(id);
            }
            foreach (var id in Entities.Collect<RpgTileset>().Keys.OrderBy(k => k).Where(k => !used.Contains(k)))
            {
                atlasPicker.AddItem(string.Format(ToolUi.Tr("tools/rpg_editor/atlas_add"), id));
                atlasIds.Add(id);
            }
            atlasPicker.Select(atlasIds.IndexOf(paletteSource));
        }

        private void ShowPaletteSource()
        {
            var slot = MapTilesets().IndexOf(paletteSource);
            var source = document == null || slot < 0 ? null : RpgTilesetFactory.Instance.Get(paletteSource);
            palette.SetTileset(source, source == null ? null : textures.Tileset(source.Image), Math.Max(0, slot) * RpgTilesetComposer.SlotSize);
        }

        private void OnAtlasSelected(long index)
        {
            if (document == null || index < 0 || index >= atlasIds.Count)
                return;
            paletteSource = atlasIds[(int)index];
            if (!MapTilesets().Contains(paletteSource))
            {
                RecordUndo();
                document.Map.Tilesets ??= new List<string>();
                document.Map.Tilesets.Add(paletteSource);
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/atlas_added"), paletteSource));
            }
            LoadTileset();
        }

        private void RecordUndo()
        {
            document?.BeginChange();
            SetDirty();
        }

        private void OnDocumentChanged()
        {
            SetDirty();
            canvas.Refresh();
            eventPanel.RefreshCell();
        }

        private void Undo() => StepHistory(document != null && document.Undo());

        private void Redo() => StepHistory(document != null && document.Redo());

        private void StepHistory(bool stepped)
        {
            if (!stepped)
                return;
            // The document holds a new map object now: find the selection again by id.
            var selectedId = canvas.SelectedEvent?.Id;
            canvas.SelectedEvent = document.Events.FirstOrDefault(e => e.Id == selectedId);
            SetDirty();
            BindDocument();
        }

        private void OnResizeRequested(int width, int height)
        {
            if (document == null || (width == document.Grid.Width && height == document.Grid.Height))
                return;
            RecordUndo();
            document.Resize(width, height);
            mapPanel.Bind(document);
            canvas.Refresh();
        }

        private void DeleteSelectedEvent()
        {
            if (document == null || canvas.SelectedEvent == null || !document.Events.Contains(canvas.SelectedEvent))
                return;
            RecordUndo();
            document.RemoveEvent(canvas.SelectedEvent);
            canvas.SelectedEvent = null;
            eventPanel.Bind(null);
            canvas.Refresh();
        }

        private void OnEventSelected(RpgMapEvent mapEvent)
        {
            var own = mapEvent != null && document.Events.Contains(mapEvent);
            if (own)
            {
                mapEvent.ModId = document.EventsModId ?? modId;
                mapEvent.File = document.EventsFile ?? RpgMapDocument.EventsPath(document.Map.ID);
            }
            eventPanel.Bind(mapEvent, mapEvent != null && !own);
        }

        private void EditEventSource(RpgMapEvent mapEvent)
        {
            if (!ModFileService.IsWritable(mapEvent?.ModId) || string.IsNullOrEmpty(mapEvent.File))
                return;
            pendingEventSource = mapEvent;
            RunDiscardingChanges(OpenEventSource);
        }

        private void OpenEventSource()
        {
            var source = pendingEventSource;
            var text = ModFileService.ReadText(modId, path);
            if (source == null || string.IsNullOrEmpty(text))
                return;
            document = LoadDocument(modId, text, source.ModId, source.File);
            canvas.SelectedEvent = document.Events.FirstOrDefault(e => e.Id == source.Id);
            MarkClean();
            BindDocument();
            mapBaseline = document.ToJson();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/editing_event_source"), source.ModId, source.File));
        }

        private void OnPaletteTile(int index)
        {
            canvas.SelectedTile = index;
            canvas.SelectedStamp = RpgTileStamp.Single(index);
            if (canvas.ActiveLayer > RpgEditLayer.Front)
                SelectLayer(RpgEditLayer.Main);
        }

        private void OnPaletteStamp(RpgTileStamp stamp)
        {
            canvas.SelectedStamp = stamp;
            canvas.QueueRedraw();
        }

        private void OnPaletteCollision(char code) => canvas.SelectedCollision = code;

        private void OnSideTabChanged(long tab)
        {
            if (sideTabs.GetCurrentTabControl() is RpgEntityPicker picker)
                picker.Reload();
        }

        private List<RpgPickerEntry> LoadCharacters() => RpgCharacterCatalog.Load(textures);

        private List<RpgPickerEntry> LoadItems() => RpgItemCatalog.Load(textures);

        private List<RpgPickerEntry> LoadAppearances() => RpgAppearanceCatalog.Load(textures);

        private void PlaceAppearance(RpgPickerEntry entry)
        {
            var id = "npc_" + new string(entry.Look.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
            BeginPlacing(entry, "tools/rpg_editor/placed_npc", cell => document.AddCharacter(cell.X, cell.Y, id, entry.Look, false));
        }

        private void PlaceNpc(RpgPickerEntry entry)
            => BeginPlacing(entry, "tools/rpg_editor/placed_npc", cell => document.AddCharacter(cell.X, cell.Y, entry.Id, entry.Look, false));

        private void PlaceEnemy(RpgPickerEntry entry)
            => BeginPlacing(entry, "tools/rpg_editor/placed_enemy", cell => document.AddCharacter(cell.X, cell.Y, entry.Id, entry.Look, true));

        private void PlaceItem(RpgPickerEntry entry)
        {
            var count = (int)itemCount.Value;
            var line = string.Format(ToolUi.Tr("tools/rpg_editor/item_found_line"), entry.Name, count);
            BeginPlacing(entry, "tools/rpg_editor/placed_item", cell => document.AddItem(cell.X, cell.Y, entry.Id, entry.Look, count, line));
        }

        /// <summary>Something is to be placed: switch to the events layer and wait for a cell; <paramref name="place"/> makes the event.</summary>
        private void BeginPlacing(RpgPickerEntry entry, string doneKey, Func<Vector2I, RpgMapEvent> place)
        {
            if (document == null)
                return;
            placeAt = place;
            placedKey = doneKey;
            placedName = entry.Name;
            SelectLayer(RpgEditLayer.Events);
            canvas.Placing = true;
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/place_status"), entry.Name));
        }

        private void OnCellChosen(Vector2I? cell)
        {
            if (cell == null || placeAt == null || document == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/place_cancelled"));
                return;
            }
            RecordUndo();
            var mapEvent = placeAt(cell.Value);
            canvas.SelectedEvent = mapEvent;
            eventPanel.Bind(mapEvent);
            OnDocumentChanged();
            SetStatus(string.Format(ToolUi.Tr(placedKey), placedName, mapEvent.Id), ToolStatusKind.Ok);
        }

        private void OnTilePicked(int index)
        {
            canvas.SelectedTile = index;
            canvas.SelectedStamp = RpgTileStamp.Single(index);
            var source = MapTilesets().ElementAtOrDefault(RpgTilesetComposer.Slot(index));
            if (source != null && source != paletteSource)
            {
                paletteSource = source;
                atlasPicker.Select(atlasIds.IndexOf(source));
                ShowPaletteSource();
            }
            palette.Select(index);
        }

        private void SelectLayer(RpgEditLayer layer)
        {
            canvas.ActiveLayer = layer;
            mapPanel.Visible = layer != RpgEditLayer.Events;
            eventPanel.Visible = layer == RpgEditLayer.Events;
            if (layer != RpgEditLayer.Events)
                canvas.Placing = false;
            foreach (var pair in layerButtons)
                pair.Value.SetPressedNoSignal(pair.Key == layer);
            palette.SetCollisionMode(layer == RpgEditLayer.Collision);
            foreach (var button in toolButtons.Values)
                button.Disabled = layer == RpgEditLayer.Events;
            canvas.Refresh();
        }

        private void SelectTool(RpgEditTool tool)
        {
            canvas.Tool = tool;
            foreach (var pair in toolButtons)
                pair.Value.SetPressedNoSignal(pair.Key == tool);
        }

        private void OnRendererSelected(long index)
        {
            canvas.SetRenderer(rendererPicker.GetItemMetadata((int)index).AsString());
            UpdateZoomLabel();
        }

        private void OnGridToggled(bool on)
        {
            canvas.ShowGrid = on;
            canvas.QueueRedraw();
        }

        private void OnZoomIn()
        {
            canvas.SetZoom(canvas.Zoom * 1.25f);
            UpdateZoomLabel();
        }

        private void OnZoomOut()
        {
            canvas.SetZoom(canvas.Zoom * 0.8f);
            UpdateZoomLabel();
        }

        private void UpdateZoomLabel() => zoomLabel.Text = $"{canvas.Zoom * 100f:0}%";

        /// <summary>Shortcuts: Ctrl+Z / Ctrl+Y undo and redo, 1–5 pick the layer, B/E/G the brush, Del deletes the selected event.</summary>
        protected override bool HandleKey(InputEventKey key)
        {
            if (key.CtrlPressed && key.Keycode == Key.Z)
            {
                if (key.ShiftPressed)
                    Redo();
                else
                    Undo();
                return true;
            }
            if (key.CtrlPressed && key.Keycode == Key.Y)
            {
                Redo();
                return true;
            }
            if (key.CtrlPressed || key.AltPressed)
                return false;

            switch (key.Keycode)
            {
                case Key.Key1: SelectLayer(RpgEditLayer.Back); return true;
                case Key.Key2: SelectLayer(RpgEditLayer.Main); return true;
                case Key.Key3: SelectLayer(RpgEditLayer.Front); return true;
                case Key.Key4: SelectLayer(RpgEditLayer.Collision); return true;
                case Key.Key5: SelectLayer(RpgEditLayer.Events); return true;
                case Key.B: SelectTool(RpgEditTool.Pencil); return true;
                case Key.E: SelectTool(RpgEditTool.Eraser); return true;
                case Key.G: SelectTool(RpgEditTool.Fill); return true;
                case Key.Delete: DeleteSelectedEvent(); return true;
            }
            return false;
        }
    }
}
