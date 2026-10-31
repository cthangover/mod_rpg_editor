namespace Cthangover.Tools.RpgEditor.Tests
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Godot;

    /// <summary>
    /// The interactions the inspector writes on a placement (plan 14.G2): a picked kind reads back as that kind, the game reads the
    /// pick over the object's own interaction (checked through the game's own merge, <see cref="RpgEditorScene.Effective"/>), a
    /// write is one undo step, and nothing is written for an unchanged pick. Foreign placements are written as a patch.
    /// </summary>
    public class RpgInteractEditTests
    {
        private const string Chest = "village_pack/chest";
        private const string Trigger = "village_pack/marker/trigger";
        private const string Elixir = "rpg_plus/elixir";
        private const string Shop = "rpg_plus/merchant";
        private const string Dialog = "rpg_plus/woodland/elder";
        private const string Door = "rpg_elder_house";
        private const string MapOfVillage = "rpg_village";
        private const string Merchant = "rpg_village/merchant";
        private const string Oak = "rpg_village/oak_small_1";
        private const string PatchPlacements = "patches/rpg_placement/my_mod.json";
        private const string GiveLine = "action item.add id=rpg_plus/elixir count=2";

        [ModTest("interact/picked_kinds_read_back_as_their_kind")]
        static void ReadBack(ModTestContext ctx)
        {
            var item = RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null);
            ModAssert.True(RpgInteractKind.Item == RpgInteractEdit.KindOf(item), "an item pick is an item");
            ModAssert.Equal(Elixir, RpgInteractEdit.ItemOf(item), "its item id reads back");
            ModAssert.Equal(2, RpgInteractEdit.CountOf(item), "its count reads back");
            ModAssert.Equal(GiveLine, item.Commands[0], "its command line is the palette's spelling");

            var shop = RpgInteractEdit.Make(RpgInteractKind.Shop, RpgInteractTrigger.Use, Shop, 1, null);
            ModAssert.True(RpgInteractKind.Shop == RpgInteractEdit.KindOf(shop), "a shop pick is a shop");
            ModAssert.Equal(Shop, RpgInteractEdit.ShopOf(shop), "its shop id reads back");

            var talk = RpgInteractEdit.Make(RpgInteractKind.Dialog, RpgInteractTrigger.Use, Dialog, 1, null);
            ModAssert.True(RpgInteractKind.Dialog == RpgInteractEdit.KindOf(talk), "a dialog pick is a dialog");

            var door = RpgInteractEdit.Make(RpgInteractKind.Map, RpgInteractTrigger.Step, null, 1,
                new RpgTeleport { Map = Door, X = 3, Y = 4, Dir = RpgDirection.Up });
            ModAssert.True(RpgInteractKind.Map == RpgInteractEdit.KindOf(door), "a map pick is a teleport");
            ModAssert.True(RpgInteractTrigger.Step == door.Trigger, "its trigger is the one picked");
            ModAssert.True(RpgDirection.Up == door.Teleport.Dir, "its facing is the one picked");

            ModAssert.Null(RpgInteractEdit.Make(RpgInteractKind.None, RpgInteractTrigger.Use, null, 1, null), "the kind None makes nothing");
            ModAssert.True(RpgInteractKind.None == RpgInteractEdit.KindOf(null), "no interaction is the kind None");

            var custom = new RpgInteract { Commands = new List<string> { "flag self.open 1", GiveLine } };
            ModAssert.True(RpgInteractKind.Custom == RpgInteractEdit.KindOf(custom), "two commands are a custom interaction, kept as written");
            ModAssert.True(RpgInteractKind.Custom == RpgInteractEdit.KindOf(new RpgInteract { Scenario = Dialog, Commands = new List<string> { GiveLine } }), "a scenario with commands is custom too");
        }

        [ModTest("interact/override_removes_what_the_object_had")]
        static void Override(ModTestContext ctx)
        {
            var objectInteract = new RpgInteract
            {
                Trigger = RpgInteractTrigger.Use,
                Scenario = Dialog,
                Commands = new List<string> { "flag self.open 1" },
            };
            var door = RpgInteractEdit.Make(RpgInteractKind.Map, RpgInteractTrigger.Step, null, 1,
                new RpgTeleport { Map = Door, X = 3, Y = 4, Dir = RpgDirection.Up });
            var json = RpgInteractEdit.OverrideOf(door, objectInteract) as JsonObject;
            ModAssert.NotNull(json, "a wanted interaction is written as an object");
            ModAssert.Equal("Step", json["Trigger"]?.ToString(), "the trigger is written");
            ModAssert.True(json.ContainsKey("Scenario") && json["Scenario"] == null, "the object's scenario is removed by null");
            ModAssert.True(json.ContainsKey("Commands") && json["Commands"] == null, "the object's commands are removed by null");
            ModAssert.Equal(Door, json["Teleport"]?["Map"]?.ToString(), "the teleport's map");
            ModAssert.Equal(3, json["Teleport"]?["X"]?.GetValue<int>() ?? -1, "the teleport's column");
            ModAssert.Equal("Up", json["Teleport"]?["Dir"]?.ToString(), "the teleport's facing, as the reader spells it");

            var item = RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null);
            var plain = RpgInteractEdit.OverrideOf(item, null) as JsonObject;
            ModAssert.False(plain.ContainsKey("Scenario") || plain.ContainsKey("Teleport"), "an object with no interaction gets no removals");
            ModAssert.Equal(GiveLine, plain["Commands"]?[0]?.ToString(), "the item's command is written");
            ModAssert.Null(RpgInteractEdit.OverrideOf(null, objectInteract), "nothing wanted is no override (the caller writes null)");
        }

        [ModTest("interact/the_game_reads_the_pick_over_the_objects_own")]
        static void GameReadsPick(ModTestContext ctx)
        {
            var objects = RpgEditorFixture.ObjectLookup();
            var doc = RpgEditorFixture.OwnMap();
            var id = doc.AddPlacement("chest", Chest, 1, 1);
            ModAssert.NotNull(id, "a chest is placed on the own map");
            ModAssert.NotNull(objects(Chest)?.Interact, "the chest object has its own interaction (the test is about its removal)");

            ModAssert.True(RpgInteractEdit.Apply(doc, id, RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null), objects),
                "the item pick is written");
            var read = EffectiveInteract(doc, id, objects);
            ModAssert.True(RpgInteractKind.Item == RpgInteractEdit.KindOf(read), "the game reads an item interaction");
            ModAssert.Null(read.Scenario, "the chest's own scenario is gone for this placement");
            ModAssert.Equal(GiveLine, read.Commands[0], "the game reads the picked command");
            ModAssert.True(RpgInteractTrigger.Use == read.Trigger, "the game reads the picked trigger");

            ModAssert.True(RpgInteractEdit.Apply(doc, id, RpgInteractEdit.Make(RpgInteractKind.Map, RpgInteractTrigger.Step, null, 1,
                new RpgTeleport { Map = Door, X = 3, Y = 4, Dir = RpgDirection.Right }), objects), "the door pick is written");
            read = EffectiveInteract(doc, id, objects);
            ModAssert.True(RpgInteractKind.Map == RpgInteractEdit.KindOf(read), "the game reads a teleport");
            ModAssert.Equal(Door, read.Teleport.Map, "the teleport's map");
            ModAssert.Equal(4, read.Teleport.Y, "the teleport's row");
            ModAssert.Null(read.Commands, "the chest's own commands are gone for this placement");

            ModAssert.True(RpgInteractEdit.Apply(doc, id, null, objects), "no interaction is written");
            ModAssert.Null(EffectiveInteract(doc, id, objects), "the game reads no interaction on this placement");
        }

        [ModTest("interact/a_write_is_one_undo_step_and_an_unchanged_pick_writes_nothing")]
        static void UndoAndSame(ModTestContext ctx)
        {
            var objects = RpgEditorFixture.ObjectLookup();
            var doc = RpgEditorFixture.OwnMap();
            var before = doc.Placements.First(r => r.Id == "my_map/sign").Data.ToJsonString();
            var item = RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null);

            ModAssert.True(RpgInteractEdit.Apply(doc, "my_map/sign", item, objects), "the pick is written to an own placement");
            var text = RpgEditorFixture.TextOf(doc.Changes(), "placements/my_map/events.json");
            ModAssert.NotNull(text, "the own placement file is written");
            ModAssert.True(text.Contains("\"Interact\"") && text.Contains(GiveLine), "the file holds the chosen interaction");
            ModAssert.False(RpgInteractEdit.Apply(doc, "my_map/sign", item, objects), "the same pick again changes nothing");
            ModAssert.False(RpgInteractEdit.Apply(doc, "my_map/well", null, objects), "no interaction on an object with none changes nothing");
            ModAssert.False(RpgInteractEdit.Apply(doc, "other_map/well", item, objects), "a placement of another map is not written from here");

            ModAssert.True(doc.Undo(), "the pick is an undo step");
            ModAssert.Equal(before, doc.Placements.First(r => r.Id == "my_map/sign").Data.ToJsonString(), "undo restores the placement as it was");
            ModAssert.Equal(0, doc.Changes().Count, "back at the saved state: nothing is written");
        }

        [ModTest("interact/pick_on_a_foreign_placement_without_interaction_is_a_patch")]
        static void ForeignPatch(ModTestContext ctx)
        {
            var objects = RpgEditorFixture.ObjectLookup();
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.GameEntities(game);
            var oakBefore = placements.First(p => p.Id == Oak).Written.ToJsonString();
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);

            ModAssert.True(RpgInteractEdit.Apply(doc, Oak, RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null), objects),
                "the item pick is written on a foreign placement that has no interaction of its own");
            var changes = doc.Changes();
            ModAssert.False(changes.Any(c => c.Path.StartsWith("placements/", StringComparison.Ordinal)), "the owner's placement file is never written");
            var entry = PatchEntry(RpgEditorFixture.TextOf(changes, PatchPlacements), Oak);
            ModAssert.NotNull(entry, "the patch entry of the placement is written");
            ModAssert.Equal(GiveLine, entry["Interact"]?["Commands"]?[0]?.ToString(), "the patch holds the picked command");
            ModAssert.Equal(oakBefore, placements.First(p => p.Id == Oak).Written.ToJsonString(), "the owner's entry is byte-identical as written");
        }

        [ModTest("interact/foreign_placement_whose_owner_sets_an_interaction_refuses_a_patch_that_drops_it")]
        static void ForeignRefused(ModTestContext ctx)
        {
            var objects = RpgEditorFixture.ObjectLookup();
            var game = RpgEditorFixture.ReadModJson(RpgEditorFixture.GameMod);
            var (maps, placements) = RpgEditorFixture.GameEntities(game);
            var doc = RpgEditorFixture.Open(new (string Path, string Text)[0], maps, MapOfVillage, placements);

            ModAssert.False(RpgInteractEdit.Apply(doc, Merchant, RpgInteractEdit.Make(RpgInteractKind.Item, RpgInteractTrigger.Use, Elixir, 2, null), objects),
                "the owner's interaction keeps a field the new one drops, so the pick is refused");
            ModAssert.False(RpgInteractEdit.Apply(doc, Merchant, null, objects), "no interaction cannot be written over the owner's interaction either");
            ModAssert.Equal(0, doc.Changes().Count, "a refused pick writes nothing");
        }

        [ModTest("arrows/outgoing_arrows_leave_their_placement_and_broken_targets_are_red")]
        static void Arrows(ModTestContext ctx)
        {
            var same = new RpgTeleportLink { Placement = "my_map/door", FromX = 1, FromY = 1, Map = "my_map", X = 3, Y = 2 };
            var broken = new RpgTeleportLink { Placement = "my_map/gate", FromX = 0, FromY = 2, Map = "my_map", X = 9, Y = 9, Broken = RpgTeleportLink.BrokenCell };
            var other = new RpgTeleportLink { Placement = "my_map/exit", FromX = 3, FromY = 0, Map = "elsewhere", X = 0, Y = 0 };
            var incoming = new RpgTeleportLink { Placement = "elsewhere/door", Map = "elsewhere", X = 2, Y = 2 };

            var arrows = RpgTeleportArrow.Of("my_map", new Vector2I(4, 3), new[] { same, broken, other }, new[] { incoming });
            ModAssert.Equal(4, arrows.Count, "one arrow per link, outgoing and incoming");
            ModAssert.True(new Vector2(1.5f, 1.5f) == arrows[0].From, "an arrow starts at the placement's cell centre");
            ModAssert.True(new Vector2(3.5f, 2.5f) == arrows[0].To, "a teleport to this map ends at the target cell centre");
            ModAssert.False(arrows[0].Broken, "a target on the map is not broken");
            ModAssert.Null(arrows[0].Label, "an arrow between two cells here has no label");
            ModAssert.True(RpgTeleportArrow.OutgoingColor == arrows[0].Color, "a fine target is drawn in the outgoing colour");
            ModAssert.True(arrows[1].Broken, "a target off the map is broken");
            ModAssert.True(RpgTeleportArrow.BrokenColor == arrows[1].Color, "a broken target is drawn red");
            ModAssert.Equal("elsewhere", arrows[2].Label, "an arrow to another map is labelled with that map");
            ModAssert.True(arrows[2].To != arrows[2].From, "an arrow to another map leaves the map");
            ModAssert.True(RpgTeleportArrow.IncomingColor == arrows[3].Color, "an incoming teleport has its own colour");
            ModAssert.True(new Vector2(2.5f, 2.5f) == arrows[3].To, "an incoming arrow ends on the arrival cell");
        }

        private static RpgInteract EffectiveInteract(RpgMapDocument doc, string id, Func<string, RpgObject> objects)
        {
            var record = doc.Placements.First(r => r.Id == id);
            var placement = RpgEditorScene.PlacementOf(record.Data);
            return RpgEditorScene.Effective(objects(placement.Object), RpgEditorScene.MapOf(doc.Data), placement)?.Interact;
        }

        private static JsonObject PatchEntry(string text, string id)
        {
            if (text == null || JsonNode.Parse(text) is not JsonObject root || root["Items"] is not JsonArray items)
                return null;
            return items.OfType<JsonObject>().FirstOrDefault(item => item["Id"]?.ToString() == id);
        }
    }
}
