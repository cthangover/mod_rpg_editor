namespace Cthangover.Tools.RpgEditor
{
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The picture-less sprites of a map (plan 14.G18): a trigger, a door or a marker has no picture (<c>Parts: []</c>), so the
    /// editor draws it as a cell mark and picks it first. A door is a picture-less sprite with a teleport of its own.
    /// </summary>
    public static class RpgMarkers
    {
        /// <summary>True for a sprite without any picture part: it is only a marker on the map.</summary>
        public static bool IsPictureless(RpgObject obj)
            => obj != null && obj.Form == RpgObjectForm.Sprite && (obj.Parts == null || obj.Parts.Count == 0);

        /// <summary>True for a picture-less marker that leads elsewhere (its effective interaction has a teleport).</summary>
        public static bool IsDoor(RpgObject obj) => IsPictureless(obj) && obj.Interact?.Teleport != null;
    }
}
