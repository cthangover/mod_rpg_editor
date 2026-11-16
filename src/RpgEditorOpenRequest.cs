namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Retains one requested destination across the discard dialog. Separate requests keep their own paths;
    /// cancellation still restores the window's current selection through its existing named callback.
    /// </summary>
    internal sealed class RpgEditorOpenRequest
    {
        private readonly RpgEditorWindow window;
        private readonly string mod;
        private readonly string path;

        internal RpgEditorOpenRequest(RpgEditorWindow owner, string destinationMod, string destinationPath)
        {
            window = owner;
            mod = destinationMod;
            path = destinationPath;
        }

        internal void Open()
        {
            window.OpenConfirmed(mod, path);
        }
    }
}
