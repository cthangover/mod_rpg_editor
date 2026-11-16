using System;
using Cthangover.Core.Mods.Testing;
using Cthangover.Tools.Services;

namespace Cthangover.Tools.RpgEditor.Tests.ContextLinks
{
    /// <summary>Owns a temporary generated hero actor, restoring the exact original object file after every native probe.</summary>
    internal sealed class RpgContextFiles : IDisposable
    {
        private readonly string before;

        internal string Actor { get; }

        internal RpgContextFiles()
        {
            before = ModFileService.ReadText("rpg_plus_game", RpgCharacterActors.ObjectFile);
            try
            {
                Actor = RpgCharacterActors.Ensure("rpg_plus_game", "rpg_hero", "village/hero", "rpg_plus/character/hero");
                ModAssert.NotNull(Actor, "probe generates a valid actor for the existing fully specified hero");
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>Restores the original bytes, or removes the probe file if none existed.</summary>
        public void Dispose()
        {
            if (before == null)
            {
                ModFileService.DeleteFile("rpg_plus_game", RpgCharacterActors.ObjectFile);
            }
            else
            {
                ModFileService.WriteText("rpg_plus_game", RpgCharacterActors.ObjectFile, before);
            }
        }
    }
}
