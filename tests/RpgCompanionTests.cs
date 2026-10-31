namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The companion actor (plan 14.G7) and the state preview's flag names (plan 14.G11), without the window: the actor entry
    /// the palette writes into <c>objects/characters.json</c> (<see cref="RpgCharacterActors.Apply"/>) and the <c>self.</c>
    /// names an object's states and interaction read (<see cref="RpgStatePreview.SelfNames"/>).
    /// </summary>
    public class RpgCompanionTests
    {
        private const string Mod = "rpg_plus_game";
        private const string Hero = "rpg_companion";
        private const string OtherEntry = "rpg_plus_game/character/other";

        [ModTest("companion/names_follow_the_join_convention")]
        static void Names(ModTestContext ctx)
        {
            ModAssert.Equal("rpg_plus_game/character/rpg_companion", RpgCharacterActors.ObjectId(Mod, Hero), "the actor object id");
            ModAssert.Equal("rpg_companion_joined", RpgCharacterActors.JoinFlag(Hero), "the join flag is <id>_joined");
            ModAssert.Equal("scenarios/rpg_plus_game/party/rpg_companion.scenario", RpgCharacterActors.JoinScenarioPath(Mod, Hero),
                "the join scenario file is in the party folder");
            ModAssert.Equal("objects/characters.json", RpgCharacterActors.ObjectFile, "the actors are written into the characters object file");
        }

        [ModTest("companion/an_actor_that_joins_runs_the_join_and_hides_after_it")]
        static void Joins(ModTestContext ctx)
        {
            var root = RpgCharacterActors.Apply(EmptyFile(), Mod, Hero, "village/villager", "Lira", true);
            var entry = Entry(root, RpgCharacterActors.ObjectId(Mod, Hero));
            ModAssert.NotNull(entry, "the actor entry is written");
            ModAssert.Equal("actor", entry["Form"]?.GetValue<string>(), "its form is an actor");
            ModAssert.Equal("character", entry["Category"]?.GetValue<string>(), "its category is character");
            ModAssert.Equal("village/villager", entry["Charset"]?.GetValue<string>(), "its walking sheet is the character's");
            ModAssert.Equal("Lira", entry["Name"]?.GetValue<string>(), "its name is the character's");
            ModAssert.Equal("Use", entry["Interact"]?["Trigger"]?.GetValue<string>(), "talking to it uses it");
            ModAssert.Equal("rpg_plus_game/party/rpg_companion", entry["Interact"]?["Scenario"]?.GetValue<string>(), "the interaction runs the join scenario");
            var state = entry["States"]?.AsArray().FirstOrDefault();
            ModAssert.Equal("joined", state?["Id"]?.GetValue<string>(), "the joined state");
            ModAssert.Equal("rpg_companion_joined == 1", state?["When"]?.GetValue<string>(), "it applies once the join flag reads 1");
            ModAssert.True(state?["Hidden"]?.GetValue<bool>() == true, "and hides the placement");
        }

        [ModTest("companion/a_character_that_does_not_join_has_no_interaction")]
        static void NoJoin(ModTestContext ctx)
        {
            var root = RpgCharacterActors.Apply(EmptyFile(), Mod, Hero, "village/villager", null, false);
            var entry = Entry(root, RpgCharacterActors.ObjectId(Mod, Hero));
            ModAssert.NotNull(entry, "the actor entry is written without a join");
            ModAssert.Null(entry["Interact"], "no join, no interaction");
            ModAssert.Null(entry["States"], "no join, no hidden state");
            ModAssert.Null(entry["Name"], "an empty name leaves the name out");
        }

        [ModTest("companion/applying_twice_updates_one_entry_and_keeps_the_others")]
        static void Idempotent(ModTestContext ctx)
        {
            var file = EmptyFile();
            file["Items"]!.AsArray().Add(new JsonObject { ["Id"] = OtherEntry, ["Form"] = "actor", ["Charset"] = "village/hero" });
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", false);
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", true);
            ModAssert.True(file["Items"]!.AsArray().Count == 2, "one entry for the character, the other entry kept");
            ModAssert.NotNull(Entry(file, OtherEntry), "the other entry is still there");
            ModAssert.NotNull(Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"], "the second call adds the join");
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", false);
            ModAssert.Null(Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"], "a later call without a join takes it away again");
            ModAssert.True(file["Items"]!.AsArray().Count == 2, "still one entry per character after the change");
        }

        [ModTest("preview/self_names_come_from_the_interaction_and_the_states")]
        static void SelfNames(ModTestContext ctx)
        {
            var barrel = new RpgObject
            {
                ID = "test/barrel",
                Interact = new RpgInteract { Commands = new List<string> { "flag self.open 1", "action item.add id=money/coin count=2", "flag self.open 1" } },
                States = new List<RpgObjectState>
                {
                    new() { Id = "open", When = "self.open == 1 && self.looted == 0" },
                    new() { Id = "gone", When = "self.won == 1" },
                },
            };
            var names = RpgStatePreview.SelfNames(barrel);
            ModAssert.True(names.SequenceEqual(new[] { "open", "looted", "won" }), $"the distinct names in order of appearance, got [{string.Join(", ", names)}]");
        }

        [ModTest("preview/an_object_without_self_flags_has_none")]
        static void NoSelfNames(ModTestContext ctx)
        {
            var plain = new RpgObject
            {
                ID = "test/tree",
                Interact = new RpgInteract { Commands = new List<string> { "ptext \"A tree.\" key=test/tree" } },
                States = new List<RpgObjectState> { new() { Id = "season", When = "season == 2" } },
            };
            ModAssert.True(RpgStatePreview.SelfNames(plain).Count == 0, "no self. name, no preview check boxes");
            ModAssert.True(RpgStatePreview.SelfNames(null).Count == 0, "no object, no names");
        }

        /// <summary>A fresh object file: the kind and an empty item list, as a new mod gets it.</summary>
        private static JsonObject EmptyFile() => new() { ["Kind"] = "rpg_object", ["Items"] = new JsonArray() };

        private static JsonObject Entry(JsonObject root, string id)
            => root["Items"]?.AsArray().OfType<JsonObject>().FirstOrDefault(o => o["Id"]?.GetValue<string>() == id);
    }
}
