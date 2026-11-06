namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using System.Text.Json.Nodes;

    public sealed partial class RpgMapDocument
    {
        /// <summary>
        /// Resizes the grids from their fixed origin as one undo step. Existing cells keep their coordinates;
        /// new cells are empty. Refuses to strand a placement, event or spawn outside the new bounds.
        /// Independent lights retain their cell positions despite their normalized storage coordinates.
        /// Foreign maps produce only a patch in the edited mod.
        /// </summary>
        public bool Resize(int width, int height)
        {
            if (Data == null || width < 1 || height < 1 || (long)width * height > 1048576
                || width == Width && height == Height)
                return false;
            if (Data["Spawn"] is JsonObject spawn
                && (IntOf(spawn, "X") >= width || IntOf(spawn, "Y") >= height))
                return false;
            foreach (var placement in Placements.Where(p => !IsDeleted(p.Id)))
                if (IntOf(placement.Data, "X") >= width || IntOf(placement.Data, "Y") >= height)
                    return false;
            foreach (var entry in Events)
                if ((long)entry.X + entry.Width > width || (long)entry.Y + entry.Height > height)
                    return false;

            var previousWidth = Width;
            var previousHeight = Height;
            Begin();
            foreach (var key in new[] { "Ground", "Structure", "Collision" })
            {
                if (Data[key] is not JsonArray rows) continue;
                var resized = new JsonArray();
                for (var y = 0; y < height; y++)
                {
                    var text = y < rows.Count ? rows[y]?.GetValue<string>() ?? string.Empty : string.Empty;
                    resized.Add(text.Length > width ? text.Substring(0, width) : text.PadRight(width, '.'));
                }
                Data[key] = resized;
            }
            if (Data["Lights"] is JsonArray lights)
                foreach (var light in lights.OfType<JsonObject>())
                {
                    if (light["x"] is JsonNode x) light["x"] = x.GetValue<double>() * previousWidth / width;
                    if (light["y"] is JsonNode y) light["y"] = y.GetValue<double>() * previousHeight / height;
                }
            Data["Width"] = width;
            Data["Height"] = height;
            if (IsForeign) maps.CommitForeign(Map);
            return true;
        }
    }
}
