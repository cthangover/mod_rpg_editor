namespace Cthangover.Tools.RpgEditor.Tests.Geometry
{
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts the grid and pixel-position UI regression only for its explicit launch flag.</summary>
    public class RpgGeometryTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued) return;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg == "--itest=rpg_editor_geometry")
                {
                    queued = true;
                    ModRoot.AddDeferred(new RpgGeometryTestDriver());
                    return;
                }
        }
    }
}
