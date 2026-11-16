using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Mods.Effects.Atmosphere;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Interface.Theming;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Owns map-editor utility layout through ModWidget. The window borrows controls and keeps document,
    /// selection and history policy; image owners are borrowed and are retired by the window after UI detach.
    /// </summary>
    internal sealed partial class RpgEditorWorkbench : ModWidget
    {
        private static readonly (RpgEditorTool Id, string Key, string Tip)[] tools = new[]
        {
            (RpgEditorTool.Select, "tools/rpg_editor/tool_select", "tools/rpg_editor/tool_select_tip"),
            (RpgEditorTool.Event, "tools/rpg_editor/tool_event", "tools/rpg_editor/tool_event_tip"),
            (RpgEditorTool.Paint, "tools/rpg_editor/tool_paint", "tools/rpg_editor/tool_paint_tip"),
            (RpgEditorTool.Place, "tools/rpg_editor/tool_place", "tools/rpg_editor/tool_place_tip"),
            (RpgEditorTool.Collision, "tools/rpg_editor/tool_collision", "tools/rpg_editor/tool_collision_tip"),
            (RpgEditorTool.Spawn, "tools/rpg_editor/tool_spawn", "tools/rpg_editor/tool_spawn_tip"),
            (RpgEditorTool.Effects, "tools/rpg_editor/tool_effects", "tools/rpg_editor/tool_effects_tip"),
        };

        private readonly IReadOnlyDictionary<string, RpgObject> objectTable;

        private readonly RpgEditorThumbnails thumbnails;

        private readonly ButtonGroup toolGroup;

        private readonly Dictionary<RpgEditorTool, Button> toolButtons = new();

        private VBoxContainer content;

        internal RpgEditorPalette Palette { get; private set; }

        internal RpgEditorCanvas Canvas { get; private set; }

        internal RpgGeometryControls Geometry { get; private set; }

        internal RpgEditorInspector Inspector { get; private set; }

        internal VBoxContainer Diagnostics { get; private set; }

        internal ModFileSelector Selector { get; private set; }

        internal OptionButton ProjectionPicker { get; private set; }

        internal OptionButton CollisionPicker { get; private set; }

        internal OptionButton PaintModePicker { get; private set; }

        internal Control PaintOptions { get; private set; }

        internal OptionButton EffectPicker { get; private set; }

        internal Button PlayButton { get; private set; }

        internal Button UndoButton { get; private set; }

        internal Button RedoButton { get; private set; }

        internal Button SaveButton { get; private set; }

        internal Button NewObjectButton { get; private set; }

        internal Button FitButton { get; private set; }

        internal IReadOnlyDictionary<RpgEditorTool, Button> ToolButtons
        {
            get
            {
                return toolButtons;
            }
        }

        internal RpgEditorWorkbench(IReadOnlyDictionary<string, RpgObject> objects, RpgEditorThumbnails images, ButtonGroup group)
        {
            objectTable = objects;
            thumbnails = images;
            toolGroup = group;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
        }

        protected override void Construct()
        {
            content = RpgEditorWindow.FillContainer();
            AddChild(content);
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            // Long translated toolbar labels must not push the Inspector outside the window.
            var toolbarScroll = new ScrollContainer
            {
                Name = "RpgToolbarScroll",
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
                VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            toolbarScroll.AddChild(BuildToolbar());
            content.AddChild(toolbarScroll);
            PaintOptions = BuildPaintOptions();
            content.AddChild(PaintOptions);
            Geometry = new RpgGeometryControls();
            content.AddChild(Geometry);
            Geometry.EnsureConstructed();
            var main = new HSplitContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            content.AddChild(main);
            var left = new VBoxContainer();
            left.AddChild(ToolUi.Section("tools/rpg_editor/palette_title"));
            Palette = new RpgEditorPalette(objectTable, thumbnails);
            left.AddChild(Palette);
            Palette.EnsureConstructed();
            NewObjectButton = ToolUi.Button("tools/rpg_editor/new_object", null, ButtonKind.Primary);
            NewObjectButton.Name = "NewMapObject";
            NewObjectButton.Disabled = Cthangover.Core.UI.Tool.ToolFactory.Instance.Get("rpg_object_editor") == null;
            left.AddChild(NewObjectButton);
            main.AddChild(RpgEditorWindow.Sidebar(left, 300));
            Canvas = new RpgEditorCanvas();
            main.AddChild(Canvas);
            var right = new VBoxContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            Diagnostics = new VBoxContainer();
            right.AddChild(Diagnostics);
            var scroll = new ScrollContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ExpandFill,
                HorizontalScrollMode = ScrollContainer.ScrollMode.Auto
            };
            Inspector = new RpgEditorInspector
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            scroll.AddChild(Inspector);
            right.AddChild(scroll);
            main.AddChild(RpgEditorWindow.Sidebar(right, 320));
            content.MinimumSizeChanged += UpdateMinimum;
            UpdateMinimum();
        }

        private HBoxContainer BuildPaintOptions()
        {
            var row = new HBoxContainer
            {
                Name = "PaintOptions",
                Visible = false,
            };
            row.AddChild(new Label
            {
                Text = ToolUi.Tr("tools/rpg_editor/paint_mode_label"),
            });
            PaintModePicker = new OptionButton
            {
                Name = "PaintMode",
                TooltipText = ToolUi.Tr("tools/rpg_editor/paint_mode_tip"),
            };
            PaintModePicker.AddItem(ToolUi.Tr("tools/rpg_editor/paint_brush"), (int)RpgPaintMode.Brush);
            PaintModePicker.AddItem(ToolUi.Tr("tools/rpg_editor/paint_fill"), (int)RpgPaintMode.Fill);
            PaintModePicker.Select(0);
            row.AddChild(PaintModePicker);
            return row;
        }

        private HBoxContainer BuildToolbar()
        {
            var bar = RpgEditorWindow.Toolbar();
            Selector = new ModFileSelector("maps", ".json");
            bar.AddChild(Selector);
            SaveButton = ToolUi.Button("tools/rpg_editor/save", null, ButtonKind.Primary, "tools/rpg_editor/save_tip");
            bar.AddChild(SaveButton);
            PlayButton = ToolUi.Button("tools/rpg_editor/play", null, ButtonKind.Primary, "tools/rpg_editor/play_tip");
            bar.AddChild(PlayButton);
            UndoButton = ToolUi.Button("tools/rpg_editor/undo", null, ButtonKind.Secondary, "tools/rpg_editor/undo_tip");
            RedoButton = ToolUi.Button("tools/rpg_editor/redo", null, ButtonKind.Secondary, "tools/rpg_editor/redo_tip");
            bar.AddChild(UndoButton);
            bar.AddChild(RedoButton);
            bar.AddChild(ToolUi.Divider());
            foreach (var (id, key, tip) in tools)
            {
                var button = new Button
                {
                    Text = ToolUi.Tr(key),
                    TooltipText = ToolUi.Tr(tip),
                    ToggleMode = true,
                    ButtonGroup = toolGroup,
                    FocusMode = Control.FocusModeEnum.None
                };
                toolButtons[id] = button;
                bar.AddChild(button);
            }

            CollisionPicker = new OptionButton
            {
                TooltipText = ToolUi.Tr("tools/rpg_editor/collision_tip")
            };
            CollisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_auto"), 0);
            CollisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_block"), 1);
            CollisionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/collision_open"), 2);
            CollisionPicker.Select(1);
            bar.AddChild(CollisionPicker);
            EffectPicker = new OptionButton
            {
                Name = "EffectProfile",
                TooltipText = ToolUi.Tr("tools/rpg_editor/effect_profile_tip")
            };
            foreach (var profile in AtmosphereCatalog.Collect(required: false).Keys.OrderBy(Identity, StringComparer.Ordinal))
            {
                EffectPicker.AddItem(profile);
                EffectPicker.SetItemMetadata(EffectPicker.ItemCount - 1, profile);
            }

            if (EffectPicker.ItemCount > 0)
            {
                EffectPicker.Select(0);
            }
            else
            {
                EffectPicker.AddItem(ToolUi.Tr("tools/rpg_editor/effect_profiles_none"));
                EffectPicker.SetItemDisabled(0, true);
                EffectPicker.Disabled = true;
                EffectPicker.TooltipText = ToolUi.Tr("tools/rpg_editor/effect_profiles_none_tip");
                toolButtons[RpgEditorTool.Effects].Disabled = true;
                toolButtons[RpgEditorTool.Effects].TooltipText = EffectPicker.TooltipText;
            }

            bar.AddChild(EffectPicker);
            bar.AddChild(ToolUi.Divider());
            ProjectionPicker = new OptionButton
            {
                TooltipText = ToolUi.Tr("tools/rpg_editor/projection_tip")
            };
            ProjectionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/projection_top"), 0);
            ProjectionPicker.AddItem(ToolUi.Tr("tools/rpg_editor/projection_iso"), 1);
            bar.AddChild(ProjectionPicker);
            FitButton = ToolUi.Button("tools/rpg_editor/fit", null, ButtonKind.Secondary, "tools/rpg_editor/fit_tip");
            bar.AddChild(FitButton);
            return bar;
        }

        /// <summary>Refreshes layout minimum after native themes and child controls enter the tree.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
            UpdateMinimum();
        }

        private void UpdateMinimum()
        {
            var minimum = content.GetCombinedMinimumSize();
            if (CustomMinimumSize != minimum)
            {
                CustomMinimumSize = minimum;
            }
        }

        private static string Identity(string value)
        {
            return value;
        }

        protected override void Destruct()
        {
            content.MinimumSizeChanged -= UpdateMinimum;
            base.Destruct();
        }
    }
}
