namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Tools.Services;

    /// <summary>Owns fresh PNG inputs and import targets; no existing asset is overwritten or deleted by the fixture.</summary>
    internal sealed class RpgImportDataFiles : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "cthangover-f11-import-" + Guid.NewGuid().ToString("N"));

        private readonly List<string> targets = new();

        internal RpgImportDataFiles()
        {
            Directory.CreateDirectory(directory);
        }

        internal string Source(string name, byte[] bytes)
        {
            var path = Path.Combine(directory, name + ".png");
            File.WriteAllBytes(path, bytes);
            return path;
        }

        internal string Target(string name)
        {
            var path = ModFileService.GetWritablePath("rpg_editor__data_validation", "rpg_images/container/" + name + ".png");
            if (path == null || File.Exists(path))
            {
                throw new InvalidOperationException("The import fixture needs a fresh target: " + name);
            }

            targets.Add(path);
            return path;
        }

        /// <summary>Closes after the window releases its preview; only files explicitly owned by this fixture are removed.</summary>
        public void Dispose()
        {
            foreach (var path in targets)
            {
                File.Delete(path);
            }

            Directory.Delete(directory, true);
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
        }
    }
}
