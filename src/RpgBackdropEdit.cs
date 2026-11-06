namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// Pixel positioning for painted scenery stored as a sprite placement. Converts only the current
    /// projection's rectangular background parts into native pixels, before map and editor zoom.
    /// The result overrides this placement's parts; shared object definitions, footprints and the
    /// parts limited to the alternate projection remain unchanged.
    /// </summary>
    public static class RpgBackdropEdit
    {
        /// <summary>The first background rectangle visible in this projection, or null for ordinary scenery.</summary>
        public static RpgObjectPart Part(RpgObject obj, string projection)
            => obj?.Form == RpgObjectForm.Sprite ? obj.Parts?.FirstOrDefault(p => Matches(obj, p, projection)) : null;

        /// <summary>Top-left offset of the first background part, in native map pixels.</summary>
        public static Vector2 Position(RpgObject obj, string projection, int tileSize)
        {
            var at = Part(obj, projection)?.At;
            return at?.Length == 2 ? new Vector2(at[0], at[1]) * Math.Max(1, tileSize) : Vector2.Zero;
        }

        /// <summary>
        /// Copies all parts and moves the current projection's background rectangles together to the
        /// requested pixel offset. Null means no supported part, invalid numbers or no change.
        /// </summary>
        public static JsonArray PartsAt(RpgObject obj, string projection, int tileSize, double x, double y)
        {
            if (Part(obj, projection) == null || tileSize < 1 || !double.IsFinite(x) || !double.IsFinite(y)) return null;
            var current = Position(obj, projection, tileSize);
            var dx = (x - current.X) / tileSize;
            var dy = (y - current.Y) / tileSize;
            if (Math.Abs(dx) < 0.0000001 && Math.Abs(dy) < 0.0000001) return null;
            var parts = JsonSerializer.SerializeToNode(obj.Parts, JsonOptionsProvider.WriteIndentedIgnoreNullOptions).AsArray();
            for (var i = 0; i < obj.Parts.Count; i++)
            {
                if (!Matches(obj, obj.Parts[i], projection)) continue;
                var at = obj.Parts[i].At;
                parts[i]["At"] = new JsonArray((float)((at?.Length == 2 ? at[0] : 0) + dx),
                    (float)((at?.Length == 2 ? at[1] : 0) + dy));
            }
            return parts;
        }

        private static bool Matches(RpgObject obj, RpgObjectPart part, string projection)
            => part != null && (string.IsNullOrEmpty(part.Only) || part.Only == projection)
                && (part.Geometry == RpgPartGeometry.Upright
                    || projection == RpgEditorCanvas.TopDown && part.Geometry == RpgPartGeometry.Ground)
                && (part.DrawOrder ?? obj.DrawOrder ?? (part.Geometry == RpgPartGeometry.Ground
                    ? TileDrawOrder.BelowCharacters : TileDrawOrder.ByPosition)) == TileDrawOrder.BelowCharacters;
    }
}
