namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Authoring gestures share the same batch document operations; the default brush retains existing input behavior.</summary>
    internal enum RpgPaintMode
    {
        /// <summary>The pointer records the ordinary freehand stroke.</summary>
        Brush,

        /// <summary>The first cell selects a complete equal-value cardinal component, committed on release.</summary>
        Fill,

        /// <summary>Both corners bound an inclusive clipped area; release commits the entire rectangle.</summary>
        Rectangle,
    }
}
