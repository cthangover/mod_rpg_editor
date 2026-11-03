namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;

    /// <summary>
    /// One copied placement (plan 14.G9): its object, its cell as it stood on the copied map, and its other fields (the
    /// <c>Interact</c>, <c>Variant</c>… overrides) without its id, object and cell. A paste makes a placement of these.
    /// </summary>
    public sealed class RpgPlacementClip
    {
        /// <summary>The copied placement's local id (the part after the map id), used as the start of the new id.</summary>
        public string LocalId { get; init; }

        /// <summary>The object the placement shows.</summary>
        public string Object { get; init; }

        /// <summary>Column of the copied placement on its map.</summary>
        public int X { get; init; }

        /// <summary>Row of the copied placement on its map.</summary>
        public int Y { get; init; }

        /// <summary>The other fields of the placement (a copy; no id, object or cell, no <c>$</c> markers).</summary>
        public JsonObject Fields { get; init; }

        /// <summary>For a map-owned event, its compact data instead of object overrides; paste creates another event.</summary>
        public JsonObject EventData { get; init; }
    }

    /// <summary>
    /// The placements copied with Ctrl+C, kept for the session (a paste on another map of the same session works too).
    /// Plain data: the window fills it from the document and a paste reads it through the document.
    /// </summary>
    public static class RpgPlacementClipboard
    {
        private static IReadOnlyList<RpgPlacementClip> content = new List<RpgPlacementClip>();

        /// <summary>What the last copy holds (empty before any copy).</summary>
        public static IReadOnlyList<RpgPlacementClip> Content => content;

        /// <summary>Replaces the clipboard with the copied placements.</summary>
        public static void Set(IEnumerable<RpgPlacementClip> clips) => content = clips.ToList();
    }
}
