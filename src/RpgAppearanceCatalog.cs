using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Mods.Rpg;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// All map charsets supplied by the loaded mods, including NPC artwork without
    /// a character entity. Paths stay distinct even when two mods use the same file name.
    /// These entries describe appearance only and must never be used as battle enemy ids.
    /// </summary>
    public static class RpgAppearanceCatalog
    {
        /// <summary>Reads charset images anew and builds standing-frame previews for the picker.</summary>
        public static List<RpgPickerEntry> Load(RpgTextures textures)
        {
            var files = Files.Collect(RpgTextures.CharsetGroup);
            return (files?.Keys.AsEnumerable() ?? Enumerable.Empty<string>())
                .Where(IsImage)
                .Select(file => Path.ChangeExtension(file, null))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new RpgPickerEntry
                {
                    Id = path,
                    Name = Path.GetFileName(path),
                    Look = path,
                    Icon = RpgCharacterCatalog.FrontFrame(textures.Charset(path)),
                    OwnsIcon = true
                })
                .ToList();
        }

        private static bool IsImage(string path)
            => ModConfig.Instance.GetTextureExtensionSet().Contains(Path.GetExtension(path));
    }
}
