using Cthangover.Core.UI.Tool;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Registers the RPG map editor as a tool: the kernel's tool factory finds
    /// every <see cref="IToolProvider"/>. The in-game toolbar opens it through
    /// <see cref="RpgMapEditorToolBoxButton"/>.
    /// </summary>
    public class RpgMapEditorToolProvider : IToolProvider
    {
        /// <summary>Tool id.</summary>
        public string Id => "rpg_map_editor";

        /// <summary>Display name key.</summary>
        public string LocaleKey => "tools/rpg_editor/title";

        /// <summary>Creates the window.</summary>
        public Window CreateWindow() => new RpgMapEditorWindow();
    }
}
