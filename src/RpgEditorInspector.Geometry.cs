namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Tools.Common;
    using Godot;

    public partial class RpgEditorInspector
    {
        private SpinBox mapWidth;
        private SpinBox mapHeight;
        private OptionButton backdropPicker;
        private SpinBox backdropX;
        private SpinBox backdropY;
        private State geometryState;
        private string backdropId;
        private bool gridVisible = true;
        private bool fillingBackdrop;

        /// <summary>One explicit Apply of both map dimensions.</summary>
        public event Action<int, int> MapSizeChanged;

        /// <summary>One explicit Apply of this placement's native pixel offset in the current projection.</summary>
        public event Action<string, double, double> BackdropPositionChanged;

        /// <summary>
        /// Spin-box edits before Apply: the picture is shown at the typed offset while the document stays unchanged,
        /// so the user can place it by looking at the map instead of by numbers alone.
        /// </summary>
        public event Action<string, double, double> BackdropPreviewChanged;

        /// <summary>Preview-only cell line visibility.</summary>
        public event Action<bool> GridVisibilityChanged;

        private void AddMapGeometry(VBoxContainer box, State state)
        {
            geometryState = state;
            mapWidth = GeometryNumber(box, "MapWidth", "tools/rpg_editor/map_width", state.MapSize.X, 1, 4096);
            mapHeight = GeometryNumber(box, "MapHeight", "tools/rpg_editor/map_height", state.MapSize.Y, 1, 4096);
            var resize = ToolUi.Button("tools/rpg_editor/map_resize", ApplyMapSize, ButtonKind.Secondary);
            resize.Name = "MapResizeApply";
            box.AddChild(resize);
            box.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/map_resize_hint")));
            var grid = new CheckBox { Name = "MapGrid", Text = ToolUi.Tr("tools/rpg_editor/map_grid"), ButtonPressed = gridVisible };
            grid.Toggled += ToggleGrid;
            box.AddChild(grid);

            box.AddChild(ToolUi.Section("tools/rpg_editor/backdrop_title"));
            if (state.Backdrops.Count == 0)
            {
                box.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/backdrop_none")));
                return;
            }
            if (state.Backdrops.All(b => b.Id != backdropId)) backdropId = state.Backdrops[0].Id;
            backdropPicker = new OptionButton { Name = "MapBackdrop" };
            foreach (var backdrop in state.Backdrops)
            {
                backdropPicker.AddItem(backdrop.Id);
                if (backdrop.Id == backdropId) backdropPicker.Select(backdropPicker.ItemCount - 1);
            }
            ToolUi.Field(box, "tools/rpg_editor/backdrop_picture", backdropPicker);
            backdropX = GeometryNumber(box, "BackdropX", "tools/rpg_editor/backdrop_x", 0, -1000000, 1000000);
            backdropY = GeometryNumber(box, "BackdropY", "tools/rpg_editor/backdrop_y", 0, -1000000, 1000000);
            backdropScale = AddScale(box, "BackdropScale", backdropId, 1);
            backdropX.ValueChanged += PreviewBackdrop;
            backdropY.ValueChanged += PreviewBackdrop;
            backdropPicker.ItemSelected += PickBackdrop;
            SelectBackdrop(backdropPicker.Selected);
            var apply = ToolUi.Button("tools/rpg_editor/backdrop_apply", ApplyBackdropPosition, ButtonKind.Secondary);
            apply.Name = "BackdropApply";
            box.AddChild(apply);
            box.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/backdrop_hint")));
        }

        private static SpinBox GeometryNumber(VBoxContainer box, string name, string label, double value, double min, double max)
        {
            var spin = ToolUi.Number(min, max, 1, null);
            spin.Name = name;
            spin.Value = value;
            ToolUi.Field(box, label, spin);
            return spin;
        }

        private void ApplyMapSize() => MapSizeChanged?.Invoke((int)mapWidth.Value, (int)mapHeight.Value);

        /// <summary>A hand pick also drops the unapplied preview of the previous picture: the canvas shows the stored places again.</summary>
        private void PickBackdrop(long index)
        {
            SelectBackdrop(index);
            PreviewBackdrop(0);
        }

        private void SelectBackdrop(long index)
        {
            if (index < 0 || index >= geometryState.Backdrops.Count) return;
            var backdrop = geometryState.Backdrops[(int)index];
            backdropId = backdrop.Id;
            backdropScale.SetPlacement(backdropId, geometryState.ScaleOf?.Invoke(backdropId) ?? backdrop.Object.Scale);
            var tileSize = RpgEditorScene.MapOf(geometryState.Document.Data).TileSize;
            var position = RpgBackdropEdit.Position(backdrop.Object, geometryState.Projection, tileSize);
            // Filling the boxes is not an edit by the user: no preview of the stored position.
            fillingBackdrop = true;
            backdropX.Value = position.X;
            backdropY.Value = position.Y;
            fillingBackdrop = false;
        }

        /// <summary>Shows the picture at the current spin-box numbers; the document is written only by Apply.</summary>
        private void PreviewBackdrop(double value)
        {
            if (fillingBackdrop) return;
            BackdropPreviewChanged?.Invoke(backdropId, backdropX.Value, backdropY.Value);
        }

        private void ApplyBackdropPosition() => BackdropPositionChanged?.Invoke(backdropId, backdropX.Value, backdropY.Value);

        private void ToggleGrid(bool visible)
        {
            gridVisible = visible;
            GridVisibilityChanged?.Invoke(visible);
        }
    }
}
