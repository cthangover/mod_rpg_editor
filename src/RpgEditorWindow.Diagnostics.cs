using System.Collections.Generic;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Connects read-only diagnostics to unsaved map and placement records, including edits to incoming links.</summary>
    public partial class RpgEditorWindow
    {
        internal RpgMapDiagnostics DiagnoseMap()
        {
            if (scene == null)
            {
                return new RpgMapDiagnostics(null);
            }

            var maps = KnownMaps();
            var beyond = PlacementsBeyond(scene.Map.ID, maps);
            var targets = new RpgDiagnosticScenes(scene, maps, beyond, ObjectOf);
            var arrivals = new List<(string Id, Vector2I Cell)>();
            foreach (var link in RpgTeleportLinks.Incoming(scene.Map.ID, beyond, ObjectOf))
            {
                arrivals.Add((link.Placement, new Vector2I(link.X, link.Y)));
            }

            return new RpgMapDiagnostics(scene, targets.Resolve, arrivals);
        }
    }
}
