namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Core.UI.Tool;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Effects.Atmosphere;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// The location creator of the RPG map editor: a separate entry of the tools menu (next to the
    /// editors themselves) with the basic settings of a new location — the writable mod it lands
    /// in, its id, the size in cells and the atmosphere effect — and the Create button.
    /// <para/>
    /// Create writes the map file (<c>maps/&lt;id&gt;.json</c>) from <see cref="RpgMapTemplate"/>
    /// and the scene file that makes the location enterable, refreshes the game's caches so the
    /// running game sees the new location at once, then opens the RPG map editor on the new file —
    /// where the drawing starts. An existing id is refused, never overwritten.
    /// </summary>
    public partial class RpgMapCreateWindow : ToolWindow
    {
        private const string Category = "RPG_CREATOR";

        /// <summary>The editor tool the created location opens in (see <see cref="RpgEditorToolProvider"/>).</summary>
        private const string EditorToolId = "rpg_map_editor";

        private readonly List<string> mods = new();

        private OptionButton modPicker;

        private LineEdit idEdit;

        private SpinBox widthSpin;

        private SpinBox heightSpin;

        private OptionButton effectPicker;

        private Label pathPreview;

        private Button createButton;

        /// <summary>Builds the form; the editor opens on the location the moment Create succeeds.</summary>
        public RpgMapCreateWindow() : base("tools/rpg_creator/title")
        {
            BuildUi();
            LoadMods();
            LoadEffects();
            OnInputChanged();
        }

        // ── UI construction ────────────────────────────────────────

        private void BuildUi()
        {
            var center = new CenterContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            };
            AddChild(center);

            var column = new VBoxContainer { CustomMinimumSize = new Vector2(460, 0) };
            column.AddThemeConstantOverride("separation", UiTokens.Space2);
            center.AddChild(column);

            column.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_creator/intro")));

            modPicker = new OptionButton { TooltipText = ToolUi.Tr("tools/common/mod_tip") };
            modPicker.ItemSelected += _ => OnInputChanged();
            ToolUi.Field(column, "tools/rpg_creator/mod", modPicker);

            idEdit = ToolUi.Text("new_location", _ => OnInputChanged());
            idEdit.AddThemeFontOverride("font", ToolUi.Mono());
            idEdit.TextSubmitted += _ => OnCreatePressed();
            ToolUi.Field(column, "tools/rpg_creator/name", idEdit, "tools/rpg_creator/name_tip");

            var sizeRow = new HBoxContainer();
            sizeRow.AddThemeConstantOverride("separation", UiTokens.Space2);
            widthSpin = ToolUi.Number(1, 256, 1, _ => OnInputChanged());
            widthSpin.Value = RpgMapTemplate.DefaultWidth;
            heightSpin = ToolUi.Number(1, 256, 1, _ => OnInputChanged());
            heightSpin.Value = RpgMapTemplate.DefaultHeight;
            sizeRow.AddChild(widthSpin);
            sizeRow.AddChild(heightSpin);
            ToolUi.Field(column, "tools/rpg_creator/size", sizeRow, "tools/rpg_creator/size_tip");

            effectPicker = new OptionButton { TooltipText = ToolUi.Tr("tools/rpg_creator/effects_tip") };
            ToolUi.Field(column, "tools/rpg_creator/effects", effectPicker);

            pathPreview = ToolUi.Hint(string.Empty);
            column.AddChild(pathPreview);

            createButton = ToolUi.Button("tools/rpg_creator/create", OnCreatePressed, ButtonKind.Primary, "tools/rpg_creator/create_tip");
            column.AddChild(createButton);

            SetHint(ToolUi.Tr("tools/rpg_creator/hint"));
        }

        /// <summary>Writable mods, the ones with maps first — the same order the file selector offers.</summary>
        private void LoadMods()
        {
            var writable = ModFileService.ListWritableMods();
            mods.Clear();
            mods.AddRange(writable.Where(HasMaps));
            mods.AddRange(writable.Where(m => !HasMaps(m)));

            modPicker.Clear();
            foreach (var mod in mods)
                modPicker.AddItem(HasMaps(mod) ? mod : mod + "  ·");

            if (mods.Count == 0)
            {
                createButton.Disabled = true;
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
                return;
            }

            modPicker.Select(0);
        }

        /// <summary>The atmosphere profiles the editor's Effects tool paints, "none" first.</summary>
        private void LoadEffects()
        {
            effectPicker.Clear();
            effectPicker.AddItem(ToolUi.Tr("tools/rpg_creator/effects_none"));
            effectPicker.SetItemMetadata(0, string.Empty);
            foreach (var profile in AtmosphereCatalog.Collect(required: false).Keys.OrderBy(Identity, StringComparer.Ordinal))
            {
                effectPicker.AddItem(profile);
                effectPicker.SetItemMetadata(effectPicker.ItemCount - 1, profile);
            }

            effectPicker.Select(0);
        }

        private static string Identity(string value)
        {
            return value;
        }

        private static bool HasMaps(string modId)
        {
            return ModFileService.ListFiles(modId, RpgMapFiles.MapDir, RpgMapFiles.MapExtension).Count > 0;
        }

        // ── input ──────────────────────────────────────────────────

        private void OnInputChanged()
        {
            UpdatePreview();
            var id = idEdit.Text.Trim();
            if (id.Length == 0)
            {
                idEdit.RemoveThemeColorOverride("font_color");
                return;
            }

            if (SceneFileService.ValidName(id) == id)
            {
                idEdit.RemoveThemeColorOverride("font_color");
                SetStatus(string.Empty);
            }
            else
            {
                idEdit.AddThemeColorOverride("font_color", UiTokens.DangerText);
                SetStatus(ToolUi.Tr("tools/rpg_creator/error_name"), ToolStatusKind.Error);
            }
        }

        private void UpdatePreview()
        {
            var mod = SelectedMod();
            var id = idEdit == null ? null : idEdit.Text.Trim();
            pathPreview.Text = mod == null || string.IsNullOrEmpty(id)
                ? string.Empty
                : string.Format(ToolUi.Tr("tools/rpg_creator/target"), mod, RpgMapFiles.PathOf(id));
        }

        private string SelectedMod()
        {
            return modPicker != null && modPicker.Selected >= 0 && modPicker.Selected < mods.Count ? mods[modPicker.Selected] : null;
        }

        /// <summary>The picked atmosphere profile, or null for "none".</summary>
        private string SelectedProfile()
        {
            return effectPicker.Selected > 0 && effectPicker.Selected < effectPicker.ItemCount
                ? effectPicker.GetItemMetadata(effectPicker.Selected).AsString()
                : null;
        }

        // ── creation ───────────────────────────────────────────────

        private void OnCreatePressed()
        {
            var modId = SelectedMod();
            if (modId == null)
            {
                SetStatus(ToolUi.Tr("tools/common/no_writable_mods"), ToolStatusKind.Error);
                return;
            }

            var id = idEdit.Text.Trim();
            if (SceneFileService.ValidName(id) != id)
            {
                SetStatus(ToolUi.Tr("tools/rpg_creator/error_name"), ToolStatusKind.Error);
                return;
            }

            if (RpgMapFiles.FindFile(id) is { } existing)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_creator/error_exists"), id, existing.Mod), ToolStatusKind.Error);
                return;
            }

            var takenScene = SceneFileService.Scan().FirstOrDefault(e => string.Equals(e.Name, id, StringComparison.OrdinalIgnoreCase));
            if (takenScene != null)
            {
                SetStatus(string.Format(ToolUi.Tr("tools/rpg_creator/error_scene"), id, takenScene.ModId), ToolStatusKind.Error);
                return;
            }

            var width = (int)widthSpin.Value;
            var height = (int)heightSpin.Value;
            var atmosphere = SelectedProfile();
            var path = RpgMapFiles.PathOf(id);

            if (!ModFileService.WriteText(modId, path, RpgMapTemplate.FileText(id, width, height, atmosphere)))
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }

            RpgMapFiles.WriteScene(modId, id);
            RpgMapFiles.RefreshGameData();
            GameLogger.Log(Category, $"created location '{id}' in '{modId}' ({width}×{height}, atmosphere '{atmosphere ?? "none"}')");

            if (ToolFactory.Instance.Get(EditorToolId)?.CreateWindow() is not RpgEditorWindow editor)
            {
                SetStatus(ToolUi.Tr("tools/rpg_creator/error_editor"), ToolStatusKind.Error);
                return;
            }

            ToolWindow.ShowWindow(editor);
            editor.OpenMapFile(modId, path);
            QueueFree();
        }
    }
}
