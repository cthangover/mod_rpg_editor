namespace Cthangover.Tools.RpgEditor.Tests.Events
{
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts the UI authoring check only under its explicit test-suite flag.</summary>
    public class RpgEventsTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued) return;
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg == "--itest=rpg_map_events")
                {
                    queued = true;
                    ModRoot.AddDeferred(new RpgEventsTestDriver());
                }
        }
    }
}
