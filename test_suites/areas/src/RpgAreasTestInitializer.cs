namespace Cthangover.Tools.RpgEditor.Tests.Areas
{
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Isolated map fixture, active only for the area and collision UI regression.</summary>
    public class RpgAreasTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued) return;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg == "--itest=rpg_editor_areas")
                {
                    queued = true;
                    ModRoot.AddDeferred(new RpgAreasTestDriver());
                    return;
                }
        }
    }
}
