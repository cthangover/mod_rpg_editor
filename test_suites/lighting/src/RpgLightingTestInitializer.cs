namespace Cthangover.Tools.RpgEditor.Tests.Lighting
{
    using Cthangover.Core.Mods;
    /// <summary>This driver and its resource fixture mount only for the explicit light editor test suite.</summary>
    public class RpgLightingTestInitializer : IModInitializer
    {
        private bool added;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (added) return;
            added = true;
            ModRoot.AddDeferred(new RpgLightingTestDriver());
        }
    }
}
