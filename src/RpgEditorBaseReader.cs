using System.Text.Json.Nodes;
using Cthangover.Tools.EntityEditor;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Captures the document's writable mod for foreign base reads. Later window selection changes cannot
    /// change which patch layer is excluded while the existing document computes its minimal edits.
    /// </summary>
    internal sealed class RpgEditorBaseReader
    {
        private readonly string mod;

        internal RpgEditorBaseReader(string modId)
        {
            mod = modId;
        }

        internal JsonObject Read(string kind, string id)
        {
            return ForeignEntities.BaseWithout(kind, id, mod, ForeignEntities.PatchFile(kind, mod));
        }
    }
}
