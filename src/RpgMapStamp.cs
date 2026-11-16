using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Persistent authoring groups reuse document copy/paste without changing the session clipboard.
    /// Every paste reserves fresh event/placement scopes before rewriting internal script references,
    /// then uses one existing document transaction. Source definitions and runtime flags are never written.
    /// </summary>
    public sealed class RpgMapStamp
    {
        /// <summary>Author's library name; file-name validation belongs to the saving utility.</summary>
        public string Name { get; set; }

        /// <summary>Original map identity, needed to distinguish internal explicit scopes from external references.</summary>
        public string SourceMap { get; set; }

        /// <summary>Copied events/placements with relative layout and detached JSON overrides; no native resources.</summary>
        public List<RpgPlacementClip> Clips { get; set; } = new();

        /// <summary>Unrecognized library metadata survives a full JSON load/save cycle.</summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement> ExtraFields { get; set; }

        /// <summary>
        /// Captures the selected public document clips without adding history or changing Ctrl+C content.
        /// Empty selections and malformed or duplicate local identities return null.
        /// </summary>
        public static RpgMapStamp Capture(RpgMapDocument document, IEnumerable<string> selection, string name)
        {
            if (document == null)
            {
                return null;
            }

            var stamp = new RpgMapStamp
            {
                Name = name,
                SourceMap = document.MapId,
                Clips = document.ClipOf(selection),
            };
            return stamp.Usable() ? stamp : null;
        }

        /// <summary>
        /// Creates one group at the clipped group's top-left anchor using normal paste validation and undo.
        /// Internal When/Commands scopes are remapped in a detached copy; self flags remain self-relative.
        /// Foreign destinations, malformed stamps and ordinary paste refusals return null without a write.
        /// </summary>
        public List<string> Paste(RpgMapDocument document, int x, int y)
        {
            if (document == null || document.IsForeign || !Usable())
            {
                return null;
            }

            var minX = int.MaxValue;
            var minY = int.MaxValue;
            foreach (var clip in Clips)
            {
                minX = Math.Min(minX, clip.X);
                minY = Math.Min(minY, clip.Y);
            }

            foreach (var clip in Clips)
            {
                var column = (long)x + clip.X - minX;
                var row = (long)y + clip.Y - minY;
                if (column < 0 || row < 0 || column >= document.Width || row >= document.Height)
                {
                    return null;
                }
            }

            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var placement in document.Placements)
            {
                occupied.Add(placement.Id);
            }

            foreach (var entry in document.Events)
            {
                occupied.Add(entry.Scope(document.MapId));
            }

            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in Clips)
            {
                var wanted = document.MapId + "/" + clip.LocalId;
                var id = wanted;
                for (var suffix = 2; occupied.Contains(id); suffix++)
                {
                    id = wanted + "_" + suffix;
                }

                occupied.Add(id);
                names.Add(SourceMap + "/" + clip.LocalId, id);
            }

            var scopes = new RpgStampScopes(names);
            var copies = new List<RpgPlacementClip>();
            foreach (var clip in Clips)
            {
                var id = names[SourceMap + "/" + clip.LocalId];
                copies.Add(new RpgPlacementClip
                {
                    LocalId = id.Substring(document.MapId.Length + 1),
                    Object = clip.Object,
                    X = clip.X,
                    Y = clip.Y,
                    Fields = scopes.Copy(clip.Fields),
                    EventData = scopes.Copy(clip.EventData),
                });
            }

            return document.Paste(copies, x, y);
        }

        internal bool Usable()
        {
            if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(SourceMap)
                || Clips == null || Clips.Count == 0 || Clips.Count > 256)
            {
                return false;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var clip in Clips)
            {
                if (clip == null || string.IsNullOrWhiteSpace(clip.LocalId) || clip.LocalId.Contains('/')
                    || clip.LocalId.Contains(':') || !names.Add(clip.LocalId)
                    || clip.EventData == null && (clip.Fields == null || string.IsNullOrWhiteSpace(clip.Object)))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
