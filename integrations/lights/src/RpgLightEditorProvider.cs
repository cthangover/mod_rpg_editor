namespace Cthangover.Tools.RpgEditor.Lighting
{
    using Cthangover.Core.UI.Tool.LightEditor;
    /// <summary>The optional map adapter appears in the existing light editor without either engine depending on an editor.</summary>
    public class RpgLightEditorProvider : ILightEditorTargetProvider
    {
        public string Id => "rpg_map";
        public string LocaleKey => "tools/rpg_editor/light_target";
        public ILightEditorTarget Create() => new RpgLightEditorTarget();
    }
}
