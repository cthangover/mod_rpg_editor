namespace Cthangover.Tools.RpgEditor.Tests.Catalog
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Utils;

    /// <summary>Owns a scratch overlay and observes the real runtime diagnostics without suppressing their log records.</summary>
    public sealed class OptionalCatalogProbe : IDisposable
    {
        private const string Overlay = "optional_catalog_test";
        private readonly string root;
        private readonly FolderModFileProvider provider;
        private readonly List<string> errors = new();
        /// <summary>Only diagnostics for the test's isolated entity kind.</summary>
        public IReadOnlyList<string> Errors => errors;

        /// <summary>Mounts the per-case scratch directory; disposal always unmounts it and unsubscribes.</summary>
        public OptionalCatalogProbe(string directory)
        {
            root = directory;
            provider = new FolderModFileProvider(root, Overlay);
            ModOverlays.Mount(Overlay, provider);
            ModManager.Instance.GetResolver<EntityResolver>().ClearCache();
            GameLogger.LogEmitted += Record;
        }

        /// <summary>Writes one definition before the first collection of this cache generation.</summary>
        public void Write(string text) => File.WriteAllText(Path.Combine(root, "profiles.json"), text);
        /// <summary>Starts a new discovery generation through the public resolver contract.</summary>
        public void ClearCache() => ModManager.Instance.GetResolver<EntityResolver>().ClearCache();

        /// <summary>Releases the overlay before invalidating discovery, even after a failed assertion.</summary>
        public void Dispose()
        {
            GameLogger.LogEmitted -= Record;
            ModOverlays.Unmount(Overlay);
            ClearCache();
            provider.Dispose();
        }

        private void Record(string category, string message, LogLevel level)
        {
            if (level == LogLevel.Error && message.Contains("optional_catalog_sample", StringComparison.Ordinal))
                errors.Add(message);
        }
    }
}
