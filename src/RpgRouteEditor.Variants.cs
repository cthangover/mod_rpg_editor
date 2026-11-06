namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Mods.Rpg;
    using Cthangover.Tools.Common;
    using Godot;

    public partial class RpgRouteEditor
    {
        private readonly OptionButton variantPicker = new() { Name = "RouteVariant" };
        private RpgMoveRoute draft;
        private int selectedVariant;
        private bool switchingVariant;
        public string Placement => placement;
        public int Variant => draft.Variants.Count == 0 ? -1 : selectedVariant;
        public IReadOnlyList<string> SelectedSteps => steps.Text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        public RpgMoveRoute Preview { get { StoreVariant(); return draft; } }
        public event Action<RpgRouteEditor> PreviewChanged;
        public event Action<RpgRouteEditor> DrawingRequested;

        private void InitializeVariants()
        {
            draft = original == null ? new RpgMoveRoute() : JsonSerializer.Deserialize<RpgMoveRoute>(JsonSerializer.Serialize(original));
            draft.Variants ??= new();
            FillVariantPicker();
            AddChild(variantPicker); MoveChild(variantPicker, 2);
            variantPicker.ItemSelected += ChooseVariant;
            var add = ToolUi.Button("tools/rpg_editor/route_new_variant", NewVariant);
            AddChild(add); MoveChild(add, 3);
            var draw = ToolUi.Button("tools/rpg_editor/route_draw", RequestDrawing);
            draw.Name = "DrawRoute";
            AddChild(draw); MoveChild(draw, 4);
            var clear = ToolUi.Button("tools/rpg_editor/route_clear", ClearVariant);
            AddChild(clear); MoveChild(clear, 5);
            var commands = new CheckBox { Text = ToolUi.Tr("tools/rpg_editor/route_commands") };
            commands.Toggled += ShowInstructions;
            AddChild(commands); MoveChild(commands, 6);
            if (draft.Variants.Count > 0) steps.Text = string.Join("\n", draft.Variants[0]);
            steps.Visible = false;
            command.Visible = false;
            GetNode<Button>("AddRouteInstruction").Visible = false;
            steps.TextChanged += VariantChanged;
        }

        /// <summary>Updates the unsaved route draft after a map waypoint has been connected through walkable cells.</summary>
        public void AppendInstructions(IEnumerable<string> instructions)
        {
            steps.Text = string.Join("\n", SelectedSteps.Concat(instructions));
            VariantChanged();
        }
        private void RequestDrawing() => DrawingRequested?.Invoke(this);
        private void ClearVariant() { steps.Text = ""; VariantChanged(); }
        private void ShowInstructions(bool visible)
        {
            steps.Visible = visible;
            command.Visible = visible;
            GetNode<Button>("AddRouteInstruction").Visible = visible;
        }
        private void VariantChanged() { if (!switchingVariant) PreviewChanged?.Invoke(this); }
        private void StoreVariant()
        {
            if (draft.Variants.Count == 0) draft.Steps = SelectedSteps.ToList();
            else draft.Variants[selectedVariant] = SelectedSteps.ToList();
        }
        private void FillVariantPicker()
        {
            variantPicker.Clear();
            if (draft.Variants.Count == 0) variantPicker.AddItem(ToolUi.Tr("tools/rpg_editor/route_single"));
            else for (var i = 0; i < draft.Variants.Count; i++) variantPicker.AddItem(string.Format(ToolUi.Tr("tools/rpg_editor/route_variant"), i+1));
            variantPicker.Select(selectedVariant);
        }
        private void ChooseVariant(long index)
        {
            StoreVariant(); selectedVariant = (int)index;
            switchingVariant = true;
            steps.Text = string.Join("\n", draft.Variants.Count == 0 ? draft.Steps : draft.Variants[selectedVariant]);
            switchingVariant = false;
            VariantChanged();
        }
        private void NewVariant()
        {
            StoreVariant();
            if (draft.Variants.Count == 0) draft.Variants.Add(new List<string>(draft.Steps));
            draft.Variants.Add(new List<string>());
            selectedVariant = draft.Variants.Count-1;
            FillVariantPicker();
            switchingVariant = true; steps.Text = ""; switchingVariant = false;
            VariantChanged();
        }
    }
}
