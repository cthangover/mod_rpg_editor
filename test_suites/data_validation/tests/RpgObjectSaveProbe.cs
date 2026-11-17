namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    using Cthangover.Tools.RpgObjectEditor;

    /// <summary>Counts the real object's Save preflight while retaining all validation and file-writing behavior.</summary>
    public sealed partial class RpgObjectSaveProbe : RpgObjectEditorWindow
    {
        /// <summary>One entry is expected for each Save route, including refused attempts before any disk operation.</summary>
        public int Checks { get; private set; }

        /// <summary>Observes entry to the shared hook and delegates every decision to the real object editor.</summary>
        protected override bool ValidateForSave()
        {
            Checks++;
            return base.ValidateForSave();
        }
    }
}
