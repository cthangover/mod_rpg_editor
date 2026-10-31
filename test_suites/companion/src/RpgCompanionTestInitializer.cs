using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests
{
    /// <summary>Adds the companion test driver when <c>--itest=rpg_companion_edit</c> is given.</summary>
    public class RpgCompanionTestInitializer : IModInitializer
    {
        private static bool driverQueued;

        /// <summary>Nothing per mod.</summary>
        public void OnModLoaded(string modId)
        {
        }

        /// <summary>Queues the driver once.</summary>
        public void OnModResourcesReady()
        {
            var args = OS.GetCmdlineUserArgs();
            if (driverQueued || !args.Any(a => a == "--itest=rpg_companion_edit"))
                return;

            driverQueued = true;
            GameLogger.Log("TEST", "RpgCompanionTestInitializer: activating rpg_companion_edit");
            ModRoot.AddDeferred(new RpgCompanionTestDriver());
        }
    }
}
