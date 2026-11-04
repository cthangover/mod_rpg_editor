namespace Cthangover.Tools.RpgEditor.Lighting
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Settings;
    using Cthangover.Core.UI.Tool.LightEditor;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Rpg.Render;
    using Cthangover.Mods.Time;
    using Cthangover.Mods.TileRender;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Optional adapter: the existing 2D editor owns the controls and lights; this session supplies map projection and resource patching.</summary>
    public class RpgLightEditorTarget : ILightEditorTarget
    {
        private readonly VBoxContainer view = new() { Name = "RpgLightTarget", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        private readonly RpgTextures textures = new();
        private readonly RpgRenderSceneBuilder builder;
        private readonly List<string> mods = new();
        private readonly List<string> maps = new();
        private readonly OptionButton modPicker = new() { Name = "LightMapMod" };
        private readonly OptionButton mapPicker = new() { Name = "LightMap" };
        private readonly OptionButton projection = new() { Name = "LightMapProjection" };
        private readonly OptionButton lighting = new() { Name = "LightMapMode" };
        private readonly CheckButton night = new() { Name = "LightNightPreview", ButtonPressed = true };
        private readonly Label status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        private readonly ConfirmationDialog discard = new();
        private readonly RpgEditorCanvas canvas = new();
        private LightEditorController controller;
        private RpgMapDocument document;
        private RpgEditorScene scene;
        private Dictionary<string, RpgObject> objects;
        private List<(string Path, string Text)> files;
        private string mod;
        private string currentMap;
        private string baseline;
        private bool suppress;
        private int pendingMod;
        private int pendingMap;
        private bool changingMod;
        private bool dragging;
        private Vector2 pressPoint;
        private Vector2 pressLight;
        private bool disposed;

        public RpgLightEditorTarget()
        {
            builder = new RpgRenderSceneBuilder(textures.Image, textures.Charset);
            var sources = new HBoxContainer();
            sources.AddChild(ToolUi.Section("tools/rpg_editor/light_edit_in"));
            sources.AddChild(modPicker);
            sources.AddChild(mapPicker);
            modPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            mapPicker.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            view.AddChild(sources);
            var toolbar = new HBoxContainer();
            projection.AddItem(ToolUi.Tr("tools/rpg_editor/projection_top"));
            projection.AddItem(ToolUi.Tr("tools/rpg_editor/projection_iso"));
            projection.ItemSelected += OnProjection;
            toolbar.AddChild(projection);
            lighting.AddItem(ToolUi.Tr("tools/rpg_editor/light_cycle"));
            lighting.AddItem(ToolUi.Tr("tools/rpg_editor/light_day"));
            lighting.AddItem(ToolUi.Tr("tools/rpg_editor/light_night"));
            lighting.ItemSelected += OnLighting;
            toolbar.AddChild(lighting);
            night.Text = ToolUi.Tr("tools/rpg_editor/light_night_preview");
            night.TooltipText = ToolUi.Tr("tools/rpg_editor/light_night_preview_tip");
            night.Toggled += OnNightPreview;
            toolbar.AddChild(night);
            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/undo", Undo));
            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/redo", Redo));
            toolbar.AddChild(ToolUi.Button("tools/rpg_editor/fit", canvas.FitToView));
            view.AddChild(toolbar);
            view.AddChild(canvas);
            view.AddChild(status);
            view.AddChild(discard);
            discard.DialogText = ToolUi.Tr("tools/rpg_editor/light_discard");
            discard.Confirmed += ApplySource;
            discard.Canceled += RestoreSource;
            canvas.GuiInput += OnCanvasInput;
            canvas.Resized += canvas.FitToView;
            modPicker.ItemSelected += OnModPicked;
            mapPicker.ItemSelected += OnMapPicked;
        }

        public Control View => view;
        public event Action Changed;
        public event Action SelectionChanged;
        public bool IsDirty => document?.Changes().Count > 0;
        public bool ClampPosition => false;
        public float RadiusMinimum => 0.1f;
        public float RadiusMaximum => 50;
        public float RadiusStep => 0.1f;
        public string RadiusHintKey => "tools/rpg_editor/light_radius_tip";
        public string HintKey => "tools/rpg_editor/light_hint";
        public string SourceText => document?.Data?["Lights"]?.ToJsonString() ?? "[]";
        /// <summary>The map document, shared with the ordinary map editor's history and writer.</summary>
        public RpgMapDocument Document => document;
        /// <summary>The actual projected canvas, also used by the ordinary map editor.</summary>
        public RpgEditorCanvas Canvas => canvas;

        public void Open(LightEditorController value)
        {
            controller = value;
            controller.Limit = int.MaxValue; // Stored map sources are unlimited; the renderer selects the nearest shader slots.
            controller.NewRadius = 3;
            controller.UseIds = true;
            objects = Entities.Collect<RpgObject>();
            mods.AddRange(ModFileService.ListWritableMods());
            var current = RpgWorldHost.Current?.Session?.Map.ID;
            var selected = 0;
            for (var i = 0; i < mods.Count; i++)
            {
                modPicker.AddItem(mods[i]);
                if (ModFileService.ListFiles(mods[i], "maps", ".json").Any(path => System.IO.Path.GetFileNameWithoutExtension(path) == current)) selected = i;
            }
            pendingMod = selected;
            changingMod = true;
            ApplySource();
            if (current != null && maps.Contains(current)) OpenMap(mod, current);
            var iso = TileRendererRegistry.Instance.Ids.Contains(RpgEditorCanvas.Isometric);
            projection.SetItemDisabled(0, !TileRendererRegistry.Instance.Ids.Contains(RpgEditorCanvas.TopDown));
            projection.SetItemDisabled(1, !iso);
            projection.Select(iso ? 1 : 0);
            canvas.SetProjection(iso ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown);
        }

        private static List<(string Path, string Text)> ReadFiles(string modId)
        {
            var result = new List<(string Path, string Text)>();
            foreach (var path in ModFileService.ListFiles(modId, "", ".json"))
            {
                if (path.StartsWith("test_suites/", StringComparison.Ordinal)) continue;
                var text = ModFileService.ReadText(modId, path);
                if (text != null) result.Add((path, text));
            }
            return result;
        }
        private JsonObject BaseOf(string kind, string id) => ForeignEntities.BaseWithout(kind, id, mod, ForeignEntities.PatchFile(kind, mod));
        private RpgObject ResolveObject(string id) => id != null && objects.TryGetValue(id, out var value) ? value : null;
        private RpgObjectForm? FormOf(string id) => ResolveObject(id)?.Form;

        /// <summary>Opens an own or dependency map as the chosen writable mod, using normal entity resolution and foreign patches.</summary>
        public void OpenMap(string editedMod, string mapId)
        {
            mod = editedMod;
            controller.IdPrefix = mod + "/light_";
            files = ReadFiles(mod);
            document = RpgMapDocument.Open(mapId, mod, files, ForeignEntities.Scan("rpg_map", mod),
                ForeignEntities.Scan("rpg_placement", mod), BaseOf, FormOf);
            if (document == null) return;
            currentMap = mapId;
            ReadLights();
            scene = RpgEditorScene.From(document, ResolveObject);
            canvas.SetScene(builder.Build(scene.Map, scene.Cells, scene.Statics));
            canvas.FitToView();
            canvas.Actors.Clear();
            foreach (var actor in scene.Actors)
            {
                var look = builder.ActorLook(actor.Object, canvas.Scene.TileSize);
                if (look != null) canvas.Actors.Add(new TileActorPose { Id = actor.Id, Visible = true, Charset = true, Facing = 1,
                    Column = TileWalkSheet.Animation(look.Texture).IdleColumn, Position = new Vector2(actor.X, actor.Y), Look = look, Tint = Colors.White });
            }
            suppress = true;
            lighting.Select(scene.Map.LightMode == "day" ? 1 : scene.Map.LightMode == "night" ? 2 : 0);
            suppress = false;
            status.Text = string.Format(ToolUi.Tr("tools/rpg_editor/light_write_notice"), mod, currentMap);
            Refresh();
            SelectionChanged?.Invoke();
        }

        private void ReadLights()
        {
            var map = RpgEditorScene.MapOf(document.Data);
            var selection = controller.SelectedIndex;
            controller.ReplaceLights(map.Lights ?? new List<LightDef>(), selection);
            baseline = controller.ToJson();
        }
        public void Commit()
        {
            if (document == null) return;
            var json = controller.ToJson();
            if (json == baseline) return;
            document.SetProperty("Lights", JsonNode.Parse(json));
            baseline = json;
        }
        public void Refresh()
        {
            if (document == null || canvas.Scene == null) return;
            var map = RpgEditorScene.MapOf(document.Data);
            map.Lights = controller.Lights;
            canvas.PreviewAmbient = !night.ButtonPressed && (map.LightMode == "cycle" || map.LightMode == null)
                && Color.HtmlIsValid(map.Ambient) ? Color.FromHtml(map.Ambient) : null;
            canvas.PreviewTimeWeights = night.ButtonPressed ? new Vector4(0, 0, 0, 1)
                : LocationLighting.Weights(GameData.Instance?.Runtime?.Time?.Normalized ?? 12f, map.LightMode, false, true);
            canvas.LightMarkers.Clear();
            canvas.LightMarkers.AddRange(RpgMapLights.Of(map));
            canvas.SelectedLight = controller.SelectedIndex;
            canvas.PreviewLights.Clear();
            canvas.PreviewLights.AddRange(canvas.LightMarkers);
            if (scene != null)
                foreach (var entry in scene.Statics.Concat(scene.Actors))
                    canvas.PreviewLights.AddRange(RpgObjectLights.Of(entry.Object, new Vector2(entry.X, entry.Y), entry.Id));
            canvas.Refresh();
        }
        public bool Save(out string error)
        {
            error = null;
            if (document == null) { error = ToolUi.Tr("tools/rpg_editor/no_map"); return false; }
            Commit();
            foreach (var change in document.Changes())
            {
                var ok = change.Text == null ? ModFileService.DeleteFile(mod, change.Path) : ModFileService.WriteText(mod, change.Path, change.Text);
                if (!ok) { error = ToolUi.Tr("tools/common/readonly"); return false; }
            }
            document.MarkSaved();
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            SceneManager.ReloadContent();
            RpgWorldHost.Current?.RequestObjectRefresh();
            return true;
        }
        public bool HandleKey(InputEventKey key)
        {
            if (!key.CtrlPressed) return false;
            if (key.Keycode == Key.Z && !key.ShiftPressed) { Undo(); return true; }
            if (key.Keycode == Key.Y || (key.Keycode == Key.Z && key.ShiftPressed)) { Redo(); return true; }
            return false;
        }
        private void Undo()
        {
            if (document?.Undo() != true) return;
            ReadLights(); Refresh(); Changed?.Invoke();
        }
        private void Redo()
        {
            if (document?.Redo() != true) return;
            ReadLights(); Refresh(); Changed?.Invoke();
        }
        private void OnProjection(long index) { canvas.SetProjection(index == 1 ? RpgEditorCanvas.Isometric : RpgEditorCanvas.TopDown); Refresh(); }
        private void OnNightPreview(bool value) => Refresh();
        private void OnLighting(long index)
        {
            if (suppress || document == null) return;
            if (document.SetProperty("LightMode", JsonValue.Create(index == 1 ? "day" : index == 2 ? "night" : "cycle")))
            { Refresh(); Changed?.Invoke(); }
        }
        private void OnModPicked(long index) { pendingMod = (int)index; changingMod = true; AskSource(); }
        private void OnMapPicked(long index) { pendingMap = (int)index; changingMod = false; AskSource(); }
        private void AskSource() { if (IsDirty) discard.PopupCentered(); else ApplySource(); }
        private void RestoreSource() { modPicker.Select(mods.IndexOf(mod)); mapPicker.Select(maps.IndexOf(currentMap)); }
        private void ApplySource()
        {
            if (changingMod)
            {
                if (pendingMod < 0 || pendingMod >= mods.Count) return;
                mod = mods[pendingMod];
                modPicker.Select(pendingMod);
                maps.Clear();
                files = ReadFiles(mod);
                foreach (var source in files)
                {
                    if (JsonNode.Parse(source.Text) is not JsonObject root || root["Kind"]?.ToString() != "rpg_map" || root["Items"] is not JsonArray items) continue;
                    foreach (var item in items) if (item?["Id"]?.ToString() is { } id) maps.Add(id);
                }
                maps.AddRange(ForeignEntities.Scan("rpg_map", mod).Select(entry => entry.Id));
                var unique = maps.Distinct(StringComparer.Ordinal).OrderBy(IdOrder).ToList();
                maps.Clear(); maps.AddRange(unique);
                mapPicker.Clear();
                foreach (var id in maps) mapPicker.AddItem(id);
                pendingMap = maps.IndexOf(currentMap);
                if (pendingMap < 0) pendingMap = 0;
            }
            if (pendingMap >= 0 && pendingMap < maps.Count)
            {
                mapPicker.Select(pendingMap);
                OpenMap(mod, maps[pendingMap]);
            }
            else
            {
                document = null; scene = null; currentMap = null;
                canvas.Clear(); controller.ReplaceLights(new List<LightDef>());
                baseline = controller.ToJson();
                status.Text = ToolUi.Tr("tools/rpg_editor/no_map");
                SelectionChanged?.Invoke();
            }
            Changed?.Invoke();
        }
        private static string IdOrder(string id) => id;
        private Vector2 Normalized(Vector2 pointer)
        {
            var at = canvas.CellSpaceAt(pointer);
            return new Vector2(at.X / document.Width, at.Y / document.Height);
        }
        private void OnCanvasInput(InputEvent input)
        {
            if (document == null) return;
            if (input is InputEventMouseButton button && button.ButtonIndex == MouseButton.Left)
            {
                if (button.Pressed)
                {
                    var index = -1;
                    for (var i = 0; i < controller.Lights.Count; i++)
                    {
                        var light = controller.Lights[i];
                        var point = canvas.CanvasPointAt(new Vector2(light.X * document.Width, light.Y * document.Height));
                        if (point.DistanceSquaredTo(button.Position) <= 144) { index = i; break; }
                    }
                    if (index >= 0)
                    {
                        controller.Select(index); dragging = true; pressPoint = Normalized(button.Position);
                        pressLight = new Vector2(controller.Selected.X, controller.Selected.Y);
                        SelectionChanged?.Invoke();
                    }
                    else if (button.DoubleClick && controller.Add(Normalized(button.Position)) != null)
                    { Changed?.Invoke(); SelectionChanged?.Invoke(); }
                }
                else if (dragging) { dragging = false; Changed?.Invoke(); }
            }
            else if (input is InputEventMouseMotion motion && dragging && controller.Selected is { } selected)
            {
                var at = pressLight + Normalized(motion.Position) - pressPoint;
                selected.X = at.X; selected.Y = at.Y;
                Refresh();
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            canvas.GuiInput -= OnCanvasInput;
            canvas.Resized -= canvas.FitToView;
            canvas.Clear();
            builder.Dispose();
            textures.Dispose();
            Changed = null; SelectionChanged = null;
        }
    }
}
