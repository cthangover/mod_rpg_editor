namespace Cthangover.Tools.RpgEditor.Tutorial
{
    using System;
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounts <see cref="TutorialPlayDriver"/> for <c>--itest=tutorial_first_game_play</c> (a game run of rpg_plus).</summary>
    public class TutorialPlayInitializer : IModInitializer
    {
        private static bool queued;

        /// <summary>Nothing per mod.</summary>
        public void OnModLoaded(string modId)
        {
        }

        /// <summary>Queues the driver once; <c>--shots=&lt;dir&gt;</c> names the folder for the pictures.</summary>
        public void OnModResourcesReady()
        {
            var args = OS.GetCmdlineUserArgs();
            if (queued || !args.Contains("--itest=tutorial_first_game_play"))
                return;
            queued = true;
            var shots = args.Where(a => a.StartsWith("--shots=", StringComparison.Ordinal))
                .Select(a => a.Substring("--shots=".Length))
                .FirstOrDefault();
            ModRoot.AddDeferred(new TutorialPlayDriver(shots));
        }
    }
}
