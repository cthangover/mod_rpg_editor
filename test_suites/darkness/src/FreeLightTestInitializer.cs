namespace Cthangover.Tools.RpgEditor.Tests.FreeLights
{
    using Cthangover.Core.Mods;
    /// <summary>Mounts the independent wall-light regression only with its explicit test flag.</summary>
    public class FreeLightTestInitializer : IModInitializer
    {
        private bool added;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (added) return;
            added = true;
            ModRoot.AddDeferred(new FreeLightTestDriver());
        }
    }
}
