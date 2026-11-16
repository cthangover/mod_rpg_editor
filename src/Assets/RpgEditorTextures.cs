using System;
using System.Collections.Generic;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.TileRender;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Editor-only full-size picture cache. A successful scene rebuild pins its requested images;
    /// EndScene runs after the previous canvas is detached and evicts only inactive LRU entries.
    /// Current scene/ghost/drag textures may exceed the budget: active data is never freed under a
    /// renderer. Byte counts conservatively use RGBA8 dimensions, excluding builder-owned ground.
    /// </summary>
    public sealed class RpgEditorTextures : IDisposable
    {
        /// <summary>
        /// Total retained source-image target; current scene and previews remain pinned if they exceed it.
        /// </summary>
        public const long DefaultBudgetBytes = 64 * 1024 * 1024;

        private readonly Dictionary<string, (TileAssetCache Owner, Texture2D Texture, long Bytes, long Used)> entries = new();

        private readonly HashSet<string> scene = new(StringComparer.Ordinal);

        private HashSet<string> nextScene;

        private readonly Dictionary<string, HashSet<string>> previewKeys = new();

        private readonly Dictionary<string, AtlasTexture> regions = new();

        private string previewScope;

        private long clock;

        /// <summary>Cache retention limit; active scene and preview images are pinned outside eviction.</summary>
        public long BudgetBytes { get; }

        /// <summary>Conservative pixel-byte estimate for all owned source textures.</summary>
        public long CachedBytes { get; private set; }

        /// <summary>Conservative bytes that cannot be evicted while the current scene/previews use them.</summary>
        public long PinnedBytes
        {
            get
            {
                long sum = 0;
                foreach (var pair in entries)
                {
                    if (IsPinned(pair.Key))
                    {
                        sum += pair.Value.Bytes;
                    }
                }

                return sum;
            }
        }

        /// <summary>Owned source-image count, excluding crop wrappers.</summary>
        public int Count
        {
            get
            {
                return entries.Count;
            }
        }

        /// <summary>Creates an empty editor owner; the byte estimate assumes RGBA8 pixels for every source.</summary>
        public RpgEditorTextures(long budgetBytes = DefaultBudgetBytes)
        {
            if (budgetBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            }

            BudgetBytes = budgetBytes;
        }

        /// <summary>Borrowed object image; calls between BeginScene/EndScene pin the next scene's data.</summary>
        public Texture2D Image(string path)
        {
            return Resolve(RpgTextures.ImageGroup, path);
        }

        /// <summary>Borrowed walking sheet with the runtime's validated layout metadata.</summary>
        public Texture2D Charset(string path)
        {
            return Resolve(RpgTextures.CharsetGroup, path);
        }

        /// <summary>
        /// Starts a replacement generation while preserving old scene pins; a failed build never commits new
        /// pins.
        /// </summary>
        public void BeginScene()
        {
            nextScene = new HashSet<string>(StringComparer.Ordinal);
        }

        /// <summary>Trims after the old presentation has synchronously released its texture references.</summary>
        public void EndScene()
        {
            if (nextScene != null)
            {
                scene.Clear();
                scene.UnionWith(nextScene);
                nextScene = null;
            }

            while (CachedBytes > BudgetBytes)
            {
                string oldest = null;
                var age = long.MaxValue;
                foreach (var pair in entries)
                {
                    if (!IsPinned(pair.Key) && pair.Value.Used < age)
                    {
                        oldest = pair.Key;
                        age = pair.Value.Used;
                    }
                }

                if (oldest == null)
                {
                    break;
                }

                var entry = entries[oldest];
                entries.Remove(oldest);
                CachedBytes -= entry.Bytes;
                entry.Owner.Dispose();
            }
        }

        /// <summary>
        /// Full-resolution ghost/drag crop. Detach the previous borrowed result before replacing this slot.
        /// The cache owns AtlasTexture wrappers as well as their source; palette miniatures never enter here.
        /// </summary>
        public Texture2D Preview(string slot, RpgObject obj)
        {
            ReleasePreview(slot);
            previewKeys[slot] = new HashSet<string>(StringComparer.Ordinal);
            previewScope = slot;
            try
            {
                var texture = RpgEditorPalette.ThumbnailOf(obj, Image, Charset);
                if (texture is AtlasTexture region)
                {
                    regions[slot] = region;
                }

                return texture;
            }
            finally
            {
                previewScope = null;
            }
        }

        /// <summary>Unpins one detached ghost/drag result and releases its owned crop wrapper.</summary>
        public void ReleasePreview(string slot)
        {
            if (regions.Remove(slot, out var region))
            {
                region.Atlas = null;
                region.Dispose();
            }

            previewKeys.Remove(slot);
        }

        /// <summary>Call after canvas/ghost teardown; releases all wrappers before their source textures.</summary>
        public void Dispose()
        {
            foreach (var region in regions.Values)
            {
                region.Atlas = null;
                region.Dispose();
            }

            regions.Clear();
            previewKeys.Clear();
            scene.Clear();
            nextScene = null;
            foreach (var entry in entries.Values)
            {
                entry.Owner.Dispose();
            }

            entries.Clear();
            CachedBytes = 0;
        }

        private bool IsPinned(string key)
        {
            if (scene.Contains(key) || nextScene?.Contains(key) == true)
            {
                return true;
            }

            foreach (var keys in previewKeys.Values)
            {
                if (keys.Contains(key))
                {
                    return true;
                }
            }

            return false;
        }

        private Texture2D Resolve(string group, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var key = group + "/" + path;
            if (previewScope != null)
            {
                previewKeys[previewScope].Add(key);
            }
            else
            {
                (nextScene ?? scene).Add(key);
            }

            if (entries.TryGetValue(key, out var entry))
            {
                entries[key] = (entry.Owner, entry.Texture, entry.Bytes, ++clock);
                return entry.Texture;
            }

            var owner = new TileAssetCache();
            Texture2D texture;
            try
            {
                texture = owner.Resolve(group, path);
            }
            catch
            {
                owner.Dispose();
                throw;
            }

            var bytes = texture == null ? 0 : (long)texture.GetWidth() * texture.GetHeight() * 4;
            entries[key] = (owner, texture, bytes, ++clock);
            CachedBytes += bytes;
            return texture;
        }
    }
}
