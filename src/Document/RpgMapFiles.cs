namespace Cthangover.Tools.RpgEditor
{
    using System.IO;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Schemas;
    using Cthangover.Tools.Services;

    /// <summary>
    /// File-side helpers shared by the RPG map editor and the location creator: where a map's
    /// file lives, the scene file that makes a map enterable, and the refresh the running game
    /// needs after new files appear on disk.
    /// </summary>
    public static class RpgMapFiles
    {
        private const string Category = "RPG_EDITOR";

        /// <summary>Folder of map files inside a mod.</summary>
        public const string MapDir = "maps";

        /// <summary>Extension of map files.</summary>
        public const string MapExtension = ".json";

        /// <summary>The mod-relative path of the map file with this id.</summary>
        public static string PathOf(string mapId)
        {
            return MapDir + "/" + mapId + MapExtension;
        }

        /// <summary>The writable mod and the path of the map file with this id, or null.</summary>
        public static (string Mod, string Path)? FindFile(string mapId)
        {
            if (string.IsNullOrEmpty(mapId))
                return null;

            foreach (var mod in ModFileService.ListWritableMods())
            {
                foreach (var path in ModFileService.ListFiles(mod, MapDir, MapExtension))
                {
                    if (Path.GetFileNameWithoutExtension(path) == mapId)
                        return (mod, path);
                }
            }

            return null;
        }

        /// <summary>
        /// Writes the logical scene of a new map (<c>scenes/&lt;id&gt;.json</c>) when no file is
        /// there yet: without a scene the game cannot enter the map. The first known background
        /// becomes the scene's backdrop. Existing scene files are never overwritten.
        /// </summary>
        public static bool WriteScene(string modId, string mapId)
        {
            var scenePath = SceneFileEntry.PathFor(mapId);
            if (ModFileService.ReadText(modId, scenePath) != null)
                return true;

            var entry = new SceneFileEntry
            {
                ModId = modId,
                RelativePath = scenePath,
                Name = mapId,
            };
            var backgrounds = ModResourceService.GetBackgroundIds();
            if (backgrounds.Count > 0)
                entry.Backgrounds.Add(backgrounds[0]);

            if (SceneFileService.Write(entry))
                return true;

            GameLogger.Log(Category, $"scene '{mapId}' of '{modId}' could not be written", LogLevel.Error);
            return false;
        }

        /// <summary>
        /// Makes freshly written map files visible to the running game: the resolvers forget
        /// their caches, the entity index re-reads the mods (teleport pickers among them) and
        /// the scene content reloads.
        /// </summary>
        public static void RefreshGameData()
        {
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
            GameEntityIndex.Refresh();
            SceneManager.ReloadContent();
        }
    }
}
