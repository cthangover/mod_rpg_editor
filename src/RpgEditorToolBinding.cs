using System;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Keeps each toolbar button's Pressed signal authoritative for its tool choice.
    /// The button group supplies visual exclusivity; activating a button also works through
    /// the same public signal without depending on a separate group notification.
    /// </summary>
    internal sealed class RpgEditorToolBinding : IDisposable
    {
        private readonly RpgEditorWindow window;

        private readonly Button button;

        private readonly RpgEditorTool tool;

        internal RpgEditorToolBinding(RpgEditorWindow owner, Button control, RpgEditorTool choice)
        {
            window = owner;
            button = control;
            tool = choice;
            button.Pressed += Activate;
        }

        private void Activate()
        {
            window.SelectTool(tool);
        }

        /// <summary>Disconnects the borrowed control before the owning window is released; it never frees the button.</summary>
        public void Dispose()
        {
            if (GodotObject.IsInstanceValid(button))
            {
                button.Pressed -= Activate;
            }
        }
    }
}
