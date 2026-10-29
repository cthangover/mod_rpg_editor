namespace Cthangover.Tools.RpgEditor.Tests.Authoring
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts the authoring driver only for its explicit test flag.</summary>
    public class RpgAuthoringInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=rpg_authoring")) return;
            queued = true;
            ModRoot.AddDeferred(new RpgAuthoringDriver());
        }
    }
}
