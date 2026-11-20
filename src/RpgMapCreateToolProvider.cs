using Cthangover.Core.UI.Tool;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Registers the location creator as its own entry of the tools menu (the same list the RPG
    /// map editor sits in). It writes a new map file from the basic settings and hands it to
    /// <see cref="RpgEditorWindow"/> for drawing.
    /// </summary>
    public class RpgMapCreateToolProvider : IToolProvider
    {
        /// <summary>Tool id, referenced by the toolbox button.</summary>
        public string Id => "rpg_map_creator";

        /// <summary>Display name key.</summary>
        public string LocaleKey => "tools/rpg_creator/title";

        /// <summary>Creates the creation window.</summary>
        public Window CreateWindow() => new RpgMapCreateWindow();
    }
}
