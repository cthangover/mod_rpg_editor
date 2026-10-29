using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>One row of an <see cref="RpgEntityPicker"/>: a character, item or map appearance.</summary>
    public class RpgPickerEntry
    {
        /// <summary>Entity id for character/item entries; a charset path for appearance-only entries.</summary>
        public string Id { get; init; }

        /// <summary>Display name in the current language (the id when the entity has none).</summary>
        public string Name { get; init; }

        /// <summary>How it looks on the map: a charset path for a character, an item picture path for an item; null for none.</summary>
        public string Look { get; init; }

        /// <summary>Picture of the row, or null.</summary>
        public Texture2D Icon { get; init; }

        /// <summary>True for an entry-owned atlas preview; false for a texture borrowed from the window cache.</summary>
        public bool OwnsIcon { get; init; }
    }
}
