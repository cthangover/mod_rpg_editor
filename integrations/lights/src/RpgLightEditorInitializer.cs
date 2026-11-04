namespace Cthangover.Tools.RpgEditor.Lighting
{
    using Cthangover.Core.Mods;
    using Cthangover.Core.UI.Tool.LightEditor;
    /// <summary>Registration belongs to this optional integration and is released before its assembly unloads.</summary>
    public class RpgLightEditorInitializer : IModInitializer, IModUnloadAware
    {
        private RpgLightEditorProvider provider;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (provider != null) return;
            provider = new RpgLightEditorProvider();
            LightEditorTargets.Register(provider);
        }
        public void OnModUnloading() { if (provider != null) LightEditorTargets.Unregister(provider); provider = null; }
        public void OnModUnloaded(string modId) { }
    }
}
