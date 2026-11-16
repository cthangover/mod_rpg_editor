using System;
using System.Collections.Generic;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Tools.Services;

namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    /// <summary>Restores only named fixture files and resolver snapshots after native save tests, including exceptional exits.</summary>
    internal sealed class RpgValidationFiles : IDisposable
    {
        private readonly string mod;

        private readonly Dictionary<string, string> original = new(StringComparer.Ordinal);

        internal RpgValidationFiles(string mod, IEnumerable<string> paths)
        {
            this.mod = mod;
            foreach (var path in paths)
            {
                original.Add(path, ModFileService.ReadText(mod, path));
            }
        }

        /// <summary>Releases the test's file mutations before another case can observe them through a resolver cache.</summary>
        public void Dispose()
        {
            foreach (var file in original)
            {
                if (file.Value == null)
                {
                    if (ModFileService.ReadText(mod, file.Key) != null && !ModFileService.DeleteFile(mod, file.Key))
                    {
                        throw new InvalidOperationException("Cannot restore fixture: " + mod + "/" + file.Key);
                    }
                }
                else
                {
                    if (!ModFileService.WriteText(mod, file.Key, file.Value))
                    {
                        throw new InvalidOperationException("Cannot restore fixture: " + mod + "/" + file.Key);
                    }
                }
            }

            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<FileListResolver>()?.ClearCache();
        }
    }
}
