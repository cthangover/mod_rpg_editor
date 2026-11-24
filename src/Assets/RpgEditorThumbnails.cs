using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.TileRender;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    using Cthangover.Core.Mods.Memory;

    /// <summary>
    /// Owns small RGBA8 palette textures, never GPU copies of full sheets. Visible rows borrow them;
    /// detach old row icons before SetVisible changes the pins. Only unpinned LRU entries are evicted.
    /// If visible rows alone exceed the budget, further rows remain selectable without an icon.
    /// </summary>
    public sealed class RpgEditorThumbnails : IDisposable, ICache
    {
        /// <summary>Default cap for derived RGBA8 miniatures; authored source images are never retained here.</summary>
        public const long DefaultBudgetBytes = 8 * 1024 * 1024;

        private readonly Dictionary<string, (Texture2D Texture, long Bytes, long Used)> entries = new();

        private readonly HashSet<string> visible = new(StringComparer.Ordinal);

        private long clock;
        private readonly ModMemoryScope memory;
        private readonly string storageName;
        private long hits;
        private long misses;
        private long evictions;

        /// <summary>Maximum retained miniature pixel bytes; does not include temporary CPU decode buffers.</summary>
        public long BudgetBytes { get; }

        /// <summary>Owned RGBA8 pixels currently uploaded, without parent sheets or mipmaps.</summary>
        public long CachedBytes { get; private set; }

        /// <summary>Number of owned miniature textures.</summary>
        public int Count
        {
            get
            {
                return entries.Count;
            }
        }

        /// <summary>CPU image decodes, useful for checking that inactive tabs do no image work.</summary>
        public int DecodeCount { get; private set; }

        /// <summary>Requests omitted because pinned rows filled the budget; their text/selection still works.</summary>
        public int RejectedCount { get; private set; }

        /// <summary>
        /// Creates an empty owner with a positive byte cap; visible pins are supplied before requesting rows.
        /// </summary>
        public RpgEditorThumbnails(long budgetBytes = DefaultBudgetBytes)
        {
            if (budgetBytes < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            }

            BudgetBytes = budgetBytes;
            memory = ModMemoryService.Shared.TryFor(typeof(RpgEditorThumbnails).Assembly);
            storageName = memory?.TrackInstance("editor_thumbnails", this);
        }

        /// <summary>Replaces the row pins after the previous list has detached every borrowed icon.</summary>
        public void SetVisible(IEnumerable<RpgObject> objects)
        {
            visible.Clear();
            if (objects != null)
            {
                foreach (var obj in objects)
                {
                    if (KeyOf(obj)is { } key)
                    {
                        visible.Add(key);
                    }
                }
            }

            RejectedCount = 0;
        }

        /// <summary>Borrowed miniature of the object's first visible image or metadata-defined idle frame.</summary>
        public Texture2D Get(RpgObject obj)
        {
            var key = KeyOf(obj);
            if (key == null)
            {
                return null;
            }

            if (entries.TryGetValue(key, out var found))
            {
                hits++;
                entries[key] = (found.Texture, found.Bytes, ++clock);
                return found.Texture;
            }

            misses++;
            var actor = obj.Form == RpgObjectForm.Actor;
            var path = PathOf(obj);
            DecodeCount++;
            using var image = TextureUtils.LoadImageFromModGroup(actor ? RpgTextures.CharsetGroup : RpgTextures.ImageGroup, path);
            if (image == null)
            {
                return null;
            }

            var rect = new Rect2I(0, 0, image.GetWidth(), image.GetHeight());
            if (actor)
            {
                var layout = WalkLayout(path, image.GetWidth(), image.GetHeight());
                if (layout == null)
                {
                    return null;
                }

                rect = new Rect2I(layout.IdleColumn * image.GetWidth() / layout.Columns, 0, image.GetWidth() / layout.Columns,
                    image.GetHeight() / TileWalkSheet.Rows);
            }
            else if (obj.Form == RpgObjectForm.Sprite && FirstPart(obj)?.Crop is { Length: 4 } crop)
            {
                rect = new Rect2I((int)crop[0], (int)crop[1], (int)crop[2], (int)crop[3]);
            }

            if (rect.Size.X < 1 || rect.Size.Y < 1 || !new Rect2I(0, 0, image.GetWidth(), image.GetHeight()).Encloses(rect))
            {
                GameLogger.Log("RPG_EDITOR", "Invalid thumbnail crop: " + path, LogLevel.Error);
                return null;
            }

            using var small = image.GetRegion(rect);
            var scale = Math.Min(1d, 56d / Math.Max(small.GetWidth(), small.GetHeight()));
            if (scale < 1)
            {
                small.Resize(Math.Max(1, (int)Math.Round(small.GetWidth() * scale)), Math.Max(1,
                    (int)Math.Round(small.GetHeight() * scale)), Image.Interpolation.Lanczos);
            }

            small.ClearMipmaps();
            small.Convert(Image.Format.Rgba8);
            var bytes = (long)small.GetWidth() * small.GetHeight() * 4;
            if (!MakeRoom(bytes))
            {
                RejectedCount++;
                return null;
            }

            var texture = ImageTexture.CreateFromImage(small);
            entries[key] = (texture, bytes, ++clock);
            CachedBytes += bytes;
            return texture;
        }

        /// <summary>
        /// Releases all thumbnails after the UI has detached them, including hidden row source records.
        /// </summary>
        public void Clear()
        {
            foreach (var entry in entries.Values)
            {
                entry.Texture.Dispose();
            }

            entries.Clear();
            visible.Clear();
            CachedBytes = 0;
            RejectedCount = 0;
        }

        /// <summary>Releases owned textures after the palette has detached its borrowed icon references.</summary>
        public void Dispose()
        {
            Clear();
            memory?.Untrack(storageName, this);
        }

        /// <summary>Reports the uploaded RGBA8 estimate and policy evictions on the editor thread.</summary>
        public StorageMetrics GetMetrics()
            => new(entries.Count, entries.Capacity, null, new CacheMetrics(hits, misses, evictions, null, null, BudgetBytes), CachedBytes);

        /// <summary>Lists miniature keys with their estimated size and palette pin status.</summary>
        public IReadOnlyList<MemoryPreviewEntry> ReadPreview(int offset, int limit)
        {
            var rows = new List<MemoryPreviewEntry>();
            foreach (var pair in entries)
            {
                if (offset-- > 0)
                    continue;
                rows.Add(new MemoryPreviewEntry(pair.Key,
                    FormattableString.Invariant($"bytes={pair.Value.Bytes}, pinned={visible.Contains(pair.Key)}")));
                if (rows.Count == limit)
                    break;
            }
            return rows;
        }

        void ICache.Clear()
        {
            foreach (var key in new List<string>(entries.Keys))
                if (!visible.Contains(key))
                    Drop(key);
        }

        private void Drop(string key)
        {
            var entry = entries[key];
            entries.Remove(key);
            CachedBytes -= entry.Bytes;
            entry.Texture.Dispose();
        }

        private bool MakeRoom(long bytes)
        {
            while (CachedBytes + bytes > BudgetBytes)
            {
                string oldest = null;
                var age = long.MaxValue;
                foreach (var pair in entries)
                {
                    if (!visible.Contains(pair.Key) && pair.Value.Used < age)
                    {
                        oldest = pair.Key;
                        age = pair.Value.Used;
                    }
                }

                if (oldest == null)
                {
                    return false;
                }

                Drop(oldest);
                evictions++;
            }

            return true;
        }

        private static RpgObjectPart FirstPart(RpgObject obj)
        {
            if (obj?.Parts != null)
            {
                foreach (var part in obj.Parts)
                {
                    if (!string.IsNullOrEmpty(part?.Image))
                    {
                        return part;
                    }
                }
            }

            return null;
        }

        private static string PathOf(RpgObject obj)
        {
            return obj?.Form switch
            {
                RpgObjectForm.Actor => obj.Charset,
                RpgObjectForm.Surface => obj.Surface?.Image,
                RpgObjectForm.Segment => obj.Segment?.Image,
                _ => FirstPart(obj)?.Image,
            };
        }

        private static string KeyOf(RpgObject obj)
        {
            var path = PathOf(obj);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            var crop = obj.Form == RpgObjectForm.Sprite ? FirstPart(obj)?.Crop : null;
            var suffix = crop == null ? "" : "/crop/" + string.Join(",", crop);
            return (obj.Form == RpgObjectForm.Actor ? "charset/" : "image/") + path + suffix;
        }

        private static TileWalkAnimation WalkLayout(string path, int width, int height)
        {
            var layout = new TileWalkAnimation();
            var normalized = ModPath.Normalize(path).TrimStart('/');
            if (normalized.StartsWith("charsets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(9);
            }

            if (Path.HasExtension(normalized))
            {
                normalized = normalized.Substring(0, normalized.Length - Path.GetExtension(normalized).Length);
            }

            try
            {
                if (Files.Collect("charsets").TryGetValue(normalized + ".walk.json", out var entry))
                {
                    var json = ModManager.Instance.ReadFileText(entry.ModId, entry.FullPath);
                    layout = JsonSerializer.Deserialize<TileWalkAnimation>(json, JsonOptionsProvider.ReadOptions)
                        ?? throw new ArgumentException("Walking sheet metadata is null.");
                }

                layout.Validate();
                if (width % layout.Columns != 0 || height % TileWalkSheet.Rows != 0)
                {
                    throw new ArgumentException("Walking sheet size must divide into Columns by four rows.");
                }

                return layout;
            }
            catch (Exception error)
            {
                GameLogger.Log("RPG", "walking sheet '" + path + "': invalid .walk.json: " + error.Message, LogLevel.Error);
                return null;
            }
        }
    }
}
