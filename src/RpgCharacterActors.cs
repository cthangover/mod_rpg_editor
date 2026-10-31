namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Tools.Services;

    /// <summary>
    /// The actor object of a character on RPG maps (plan 14.G7): a character is placed on a map as the rpg_object
    /// <c>&lt;mod&gt;/character/&lt;id&gt;</c>, an actor with the character's walking sheet, written into the map's own mod in
    /// <c>objects/characters.json</c>. A character with a join scenario (written by <c>characters_editor</c> in its
    /// "Joins the party" block, <c>scenarios/&lt;mod&gt;/party/&lt;id&gt;.scenario</c>) gets the interaction that runs it, and
    /// a state that hides the placement once the join flag is set: a companion walks with the hero, not on its cell.
    /// <para>
    /// The object is shared by every placement of the character, so a re-choice in the palette after the join is
    /// switched on or off updates all of them. The names of the join scenario and flag are a contract with
    /// <c>characters_editor</c> (<c>CompanionScenario</c>): change both together.
    /// </para>
    /// </summary>
    public static class RpgCharacterActors
    {
        /// <summary>The object file of a mod that holds the character actors (other objects of the file are kept).</summary>
        public const string ObjectFile = "objects/characters.json";

        /// <summary>The object category of character actors.</summary>
        public const string Category = "character";

        /// <summary>The state name the placement takes once the character has joined.</summary>
        public const string JoinedState = "joined";

        private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

        /// <summary>The object id of a character's actor in a mod.</summary>
        public static string ObjectId(string mod, string characterId) => mod + "/" + Category + "/" + characterId;

        /// <summary>The scenario path (in the <c>scenarios</c> group) of a character's join, without the extension.</summary>
        public static string JoinScenarioKey(string mod, string characterId) => mod + "/party/" + characterId;

        /// <summary>The file of the join scenario, relative to the mod.</summary>
        public static string JoinScenarioPath(string mod, string characterId) => "scenarios/" + JoinScenarioKey(mod, characterId) + ".scenario";

        /// <summary>The story flag the join scenario sets: a placement hides once it reads 1.</summary>
        public static string JoinFlag(string characterId) => characterId + "_" + JoinedState;

        /// <summary>True when the mod has the join scenario of the character.</summary>
        public static bool JoinsParty(string mod, string characterId)
            => !string.IsNullOrEmpty(mod) && !string.IsNullOrEmpty(characterId)
                && ModFileService.ReadText(mod, JoinScenarioPath(mod, characterId)) != null;

        /// <summary>
        /// Writes (or brings up to date) the actor object of a character in <paramref name="mod"/> and returns its id; null
        /// when the mod is not writable or its object file cannot be read. The entry's interaction and hidden state follow
        /// <see cref="JoinsParty"/>; other entries of the file are left as they are.
        /// </summary>
        public static string Ensure(string mod, string characterId, string charset, string name)
        {
            var root = Load(mod);
            if (root == null)
                return null;
            Apply(root, mod, characterId, charset, name, JoinsParty(mod, characterId));
            var text = root.ToJsonString(WriteOptions).Replace("\r\n", "\n") + "\n";
            if (!ModFileService.WriteText(mod, ObjectFile, text))
                return null;
            ClearCaches();
            return ObjectId(mod, characterId);
        }

        /// <summary>
        /// Sets the actor entry of a character in the object file <paramref name="root"/> (pure JSON, no file access): the entry
        /// is found by its id or appended. <paramref name="joins"/> gives it the join interaction and the hidden state; without
        /// it those fields are removed. Other entries of the file are not touched.
        /// </summary>
        public static JsonObject Apply(JsonObject root, string mod, string characterId, string charset, string name, bool joins)
        {
            if (root["Items"] is not JsonArray items)
            {
                items = new JsonArray();
                root["Items"] = items;
            }
            var id = ObjectId(mod, characterId);
            var entry = items.OfType<JsonObject>().FirstOrDefault(o => string.Equals(o["Id"]?.GetValue<string>(), id, StringComparison.Ordinal));
            if (entry == null)
            {
                entry = new JsonObject();
                items.Add(entry);
            }
            entry["Id"] = id;
            entry["Form"] = "actor";
            entry["Category"] = Category;
            entry["Charset"] = charset;
            if (string.IsNullOrEmpty(name))
                entry.Remove("Name");
            else
                entry["Name"] = name;
            if (joins)
            {
                entry["Interact"] = new JsonObject { ["Trigger"] = "Use", ["Scenario"] = JoinScenarioKey(mod, characterId) };
                entry["States"] = new JsonArray(new JsonObject { ["Id"] = JoinedState, ["When"] = JoinFlag(characterId) + " == 1", ["Hidden"] = true });
            }
            else
            {
                entry.Remove("Interact");
                entry.Remove("States");
            }
            return root;
        }

        /// <summary>The object file as a JSON object; a new one when the mod has none, null when it is not valid JSON.</summary>
        private static JsonObject Load(string mod)
        {
            var text = ModFileService.ReadText(mod, ObjectFile);
            if (text == null)
                return new JsonObject { ["Kind"] = "rpg_object", ["Items"] = new JsonArray() };
            try
            {
                return JsonNode.Parse(text) as JsonObject;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Makes the game and the editors read the changed files: the entity and patch caches are dropped.</summary>
        private static void ClearCaches()
        {
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
        }
    }
}
