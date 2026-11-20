using Cthangover.Core.UI.Tool;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// In-game toolbar entry of the location creator: a separate item of the tools menu where all
    /// the editors are, so a new RPG location can be started without touching the file selector.
    /// </summary>
    public class RpgMapCreateToolBoxButton : IToolBoxButton
    {
        /// <summary>Matches <see cref="RpgMapCreateToolProvider.Id"/>.</summary>
        public string ToolId => "rpg_map_creator";

        /// <summary>No icon: the button shows its label.</summary>
        public string IconPath => "";

        /// <summary>Button label key.</summary>
        public string LocaleKey => "tools/rpg_creator/title";

        /// <summary>Always shown.</summary>
        public bool IsVisible() => true;
    }
}
