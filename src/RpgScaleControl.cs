namespace Cthangover.Tools.RpgEditor
{
    using System;
    using Godot;

    /// <summary>Live picture scale with one committed change per slider drag; controls survive canvas updates.</summary>
    public partial class RpgScaleControl : HBoxContainer
    {
        private readonly HSlider slider;
        private readonly SpinBox number;
        private bool dragging;

        /// <summary>The placement edited by this row, which may also be selected as a backdrop.</summary>
        public string Placement { get; private set; }

        /// <summary>Redraws during every edit, before a slider drag is committed.</summary>
        public event Action<string, double> PreviewChanged;

        /// <summary>Writes a numeric edit or a completed slider drag into the document.</summary>
        public event Action<string, double> Committed;

        public RpgScaleControl(string placement, double value)
        {
            Placement = placement;
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            slider = new HSlider { Name = "Slider", MinValue = 0.01, MaxValue = 100, Step = 0.01,
                ExpEdit = true, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(100, 0) };
            number = new SpinBox { Name = "Number", MinValue = 0.01, MaxValue = 100, Step = 0.01,
                Value = value, UpdateOnTextChanged = true, CustomMinimumSize = new Vector2(100, 0) };
            AddChild(slider);
            AddChild(number);
            slider.ValueChanged += OnSliderChanged;
            slider.DragStarted += OnDragStarted;
            slider.DragEnded += OnDragEnded;
            number.ValueChanged += OnNumberChanged;
        }

        /// <summary>Changes the backdrop target without replacing controls.</summary>
        public void SetPlacement(string id, double value)
        {
            Placement = id;
            SetValue(value);
        }

        /// <summary>Synchronizes a second row for the same placement without emitting another edit.</summary>
        public void SetValue(double value)
        {
            slider.SetValueNoSignal(value);
            number.SetValueNoSignal(value);
        }

        private void OnDragStarted() => dragging = true;

        private void OnDragEnded(bool changed)
        {
            dragging = false;
            if (changed) Committed?.Invoke(Placement, slider.Value);
        }

        private void OnSliderChanged(double value)
        {
            number.SetValueNoSignal(value);
            PreviewChanged?.Invoke(Placement, value);
            if (!dragging) Committed?.Invoke(Placement, value);
        }

        private void OnNumberChanged(double value)
        {
            slider.SetValueNoSignal(value);
            PreviewChanged?.Invoke(Placement, value);
            Committed?.Invoke(Placement, value);
        }
    }
}
