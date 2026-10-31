namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// What a placement's <c>Interact</c> does, as the novice picks it in the inspector (plan 14.G2). <see cref="Custom"/>
    /// is an interaction the pickers cannot express (several commands, a scenario with commands): it is shown and kept as
    /// written until another kind is picked.
    /// </summary>
    public enum RpgInteractKind
    {
        /// <summary>No interaction on this placement.</summary>
        None,

        /// <summary>Gives one item: the command <c>action item.add id=… count=…</c>.</summary>
        Item,

        /// <summary>Teleports the player to a cell of a map (a door, a map exit).</summary>
        Map,

        /// <summary>Opens a dialog scenario (an NPC talks).</summary>
        Dialog,

        /// <summary>Opens a shop: the command <c>action trade.open shop=…</c>.</summary>
        Shop,

        /// <summary>An interaction the pickers do not describe; it stays as written.</summary>
        Custom,
    }
}
