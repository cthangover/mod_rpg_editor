namespace Cthangover.Tools.RpgEditor
{
    using Cthangover.Core.Characters;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Common;

    /// <summary>
    /// The characters tab of the palette (plan 14.G7): a chosen character is placed as its actor object, which is written
    /// into the map's mod first (<see cref="RpgCharacterActors"/>). The placement then works like any actor's: a join
    /// scenario of the character is already in that object, so the placed character joins when talked to.
    /// </summary>
    public partial class RpgEditorWindow
    {
        private void OnCharacterChosen(string characterId)
        {
            if (document == null)
                return;
            if (document.IsForeign)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/refused"), ToolStatusKind.Error);
                return;
            }
            var charset = RpgPlayerLook.CharsetOfId(characterId);
            if (charset == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/character_no_sheet"), ToolStatusKind.Error);
                return;
            }
            var name = Entities.Collect<CharacterInfo>().TryGetValue(characterId, out var info) ? info?.Name : null;
            var objectId = RpgCharacterActors.Ensure(modId, characterId, charset, name);
            if (objectId == null)
            {
                SetStatus(ToolUi.Tr("tools/common/readonly"), ToolStatusKind.Error);
                return;
            }
            objectTable = Entities.Collect<RpgObject>();
            palette?.Rebuild(objectTable, textures.Image, textures.Charset);
            ChooseObject(objectId, RpgObjectForm.Actor);
            var joins = RpgCharacterActors.JoinsParty(modId, characterId);
            SetStatus(string.Format(ToolUi.Tr(joins ? "tools/rpg_editor/character_chosen_joins" : "tools/rpg_editor/character_chosen"), characterId), ToolStatusKind.Ok);
        }
    }
}
