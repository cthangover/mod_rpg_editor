using System.Collections.Generic;
using Cthangover.Mods.Rpg.Validation;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>A serializable snapshot of bounded map checks; incomplete work is never described as an exhaustive audit.</summary>
    public sealed class RpgMapDataReport
    {
        /// <summary>Coalesced findings; repeated uses retain one selectable instance and an explicit count.</summary>
        public List<RpgDataIssue> Issues { get; set; } = new();

        /// <summary>False when the map exceeds the explicit cell budget or a malformed input could not be resolved.</summary>
        public bool Complete { get; set; } = true;

        /// <summary>Number of effective object families checked, excluding unused legend entries.</summary>
        public int ObjectsChecked { get; set; }

        /// <summary>Image decodes performed by this snapshot's metadata cache.</summary>
        public int ImagesDecoded { get; set; }

        /// <summary>Unused authored legend entries, explicitly separated from used but missing resources.</summary>
        public int UnusedLegendEntries { get; set; }
    }
}
