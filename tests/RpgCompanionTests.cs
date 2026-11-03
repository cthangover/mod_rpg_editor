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

        [ModTest("companion/join_lines_match_the_join_scenario_text")]
        static void JoinLines(ModTestContext ctx)
        {
            // characters_editor writes "flag <id>_joined 1 / action party.add id=<id> / end" (CompanionScenario.Text).
            var lines = RpgCharacterActors.JoinLines(Hero);
            ModAssert.True(lines.SequenceEqual(new[] { "flag rpg_companion_joined 1", "action party.add id=rpg_companion" }),
                "the join lines are the scenario's lines without end");
        }

        [ModTest("companion/join_after_an_own_dialog_keeps_the_dialog")]
        static void JoinAfterDialog(ModTestContext ctx)
        {
            var dialog = "rpg_plus_game/dialog/lira";
            var file = EmptyFile();
            file["Items"]!.AsArray().Add(new JsonObject
            {
                ["Id"] = RpgCharacterActors.ObjectId(Mod, Hero),
                ["Form"] = "actor",
                ["Interact"] = new JsonObject { ["Trigger"] = "Use", ["Scenario"] = dialog },
            });
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", true);
            var interact = Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"];
            ModAssert.Equal(dialog, interact?["Scenario"]?.GetValue<string>(), "the dialog stays the scenario, it plays first");
            ModAssert.True(CommandsOf(interact).SequenceEqual(RpgCharacterActors.JoinLines(Hero)), "the join is appended after the dialog");
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", true);
            ModAssert.True(CommandsOf(Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"]).Count == 2, "a second join does not repeat the lines");

            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", false);
            var left = Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"];
            ModAssert.Equal(dialog, left?["Scenario"]?.GetValue<string>(), "unticking the join keeps the dialog");
            ModAssert.Null(left?["Commands"], "and removes only the join lines");
        }

        [ModTest("companion/inline_commands_of_an_actor_survive_the_join")]
        static void InlineCommands(ModTestContext ctx)
        {
            var file = EmptyFile();
            file["Items"]!.AsArray().Add(new JsonObject
            {
                ["Id"] = RpgCharacterActors.ObjectId(Mod, Hero),
                ["Form"] = "actor",
                ["Interact"] = new JsonObject { ["Trigger"] = "Use", ["Commands"] = new JsonArray("ptext \"Hi.\" key=lira/hi") },
            });
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", true);
            var commands = CommandsOf(Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"]);
            ModAssert.True(commands.SequenceEqual(new[] { "ptext \"Hi.\" key=lira/hi", "flag rpg_companion_joined 1", "action party.add id=rpg_companion" }),
                "the own line first, the join after it");
            RpgCharacterActors.Apply(file, Mod, Hero, "village/villager", "Lira", false);
            var kept = CommandsOf(Entry(file, RpgCharacterActors.ObjectId(Mod, Hero))?["Interact"]);
            ModAssert.True(kept.SequenceEqual(new[] { "ptext \"Hi.\" key=lira/hi" }), "unticking keeps the own line only");
        }

        private static List<string> CommandsOf(JsonNode interact)
            => (interact?["Commands"] as JsonArray)?.Select(c => c.GetValue<string>()).ToList() ?? new List<string>();

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

        [ModTest("companion/an_actor_object_says_when_its_character_joins")]
        static void JoinOfObject(ModTestContext ctx)
        {
            var id = RpgCharacterActors.ObjectId(Mod, Hero);
            var key = Mod + "/party/" + Hero;
            var byScenario = new RpgInteract { Trigger = RpgInteractTrigger.Use, Scenario = key };
            ModAssert.True(RpgCharacterActors.TryJoinOf(id, byScenario, out var scenario, out var lines), "an actor whose interaction runs the join joins");
            ModAssert.Equal(key, scenario, "its join scenario is the party scenario");
            ModAssert.True(lines.SequenceEqual(RpgCharacterActors.JoinLines(Hero)), "its join lines are the join's lines");
            ModAssert.True(RpgCharacterActors.TryJoinOf(id, new RpgInteract { Commands = new List<string>(lines) }, out _, out _),
                "the join lines alone are a join too");
            ModAssert.False(RpgCharacterActors.TryJoinOf(id, new RpgInteract { Scenario = Mod + "/dialog/lira" }, out _, out _),
                "a dialog without the join lines is not a join");
            ModAssert.False(RpgCharacterActors.TryJoinOf(id, null, out _, out _), "an actor without an interaction does not join");
            ModAssert.False(RpgCharacterActors.TryJoinOf("village_pack/marker/trigger", byScenario, out _, out _), "another object is not an actor");
            ModAssert.False(RpgCharacterActors.TryJoinOf(Mod + "/character/", byScenario, out _, out _), "an actor without a character id does not join");
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
