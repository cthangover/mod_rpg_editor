using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;

namespace Cthangover.Tools.RpgEditor.Tests.PaintRegions
{
    /// <summary>
    /// Persistent groups round-trip every field, isolate instance flags and retain ordinary one-step paste/foreign rules.
    /// </summary>
    public sealed class RpgMapStampTests
    {
        [ModTest("stamps/roundtrip_fresh_scopes_internal_links_and_single_undo")]
        static void Copies(ModTestContext ctx)
        {
            var source = new RpgPaintFixture(ctx).Document.Data.DeepClone() as JsonObject;
            ((JsonArray)source["Events"]).Add(new JsonObject
            {
                ["Id"] = "marker_2",
                ["X"] = 2,
                ["Y"] = 1,
                ["When"] = "rpg_fill_fixture/marker:seen == 1",
                ["Commands"] = new JsonArray("flag rpg_fill_fixture/marker:seen 1", "flag self.own 1",
                    "flag outside/key:seen 1", "flag prefixrpg_fill_fixture/marker:seen 1"),
                ["AuthorNote"] = "rpg_fill_fixture/marker:seen",
            });
            var document = new RpgPaintFixture(ctx, source).Document;
            var sign = document.AddPlacement("sign", "village_pack/decor/sign", 3, 1);
            ModAssert.NotNull(sign, "a real sprite joins the event group");
            ModAssert.True(document.SetPlacementField(sign, "Interact", new JsonObject
            {
                ["Commands"] = new JsonArray("flag rpg_fill_fixture/marker_2:ready 1"),
            }), "placement commands contain an internal explicit reference");
            var ids = new[]
            {
                "rpg_fill_fixture/marker",
                "rpg_fill_fixture/marker_2",
                sign,
            };
            var before = document.Data.ToJsonString();
            var placementBefore = document.AnyPlacement(sign).Data.ToJsonString();
            var clipboard = RpgPlacementClipboard.Content;
            var stamp = RpgMapStamp.Capture(document, ids, "linked_group");
            ModAssert.NotNull(stamp, "events and a sprite can be captured together");
            ModAssert.Equal(before, document.Data.ToJsonString(), "capture preserves every map field");
            ModAssert.True(ReferenceEquals(clipboard, RpgPlacementClipboard.Content), "capture does not replace Ctrl+C content");
            using var extra = JsonDocument.Parse("{\"values\":[1,2],\"enabled\":true}");
            stamp.ExtraFields = new Dictionary<string, JsonElement>
            {
                ["AuthorData"] = extra.RootElement.Clone(),
            };
            var serialized = JsonSerializer.Serialize(stamp, JsonOptionsProvider.ReadOptions);
            var restored = JsonSerializer.Deserialize<RpgMapStamp>(serialized, JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(stamp.Name, restored.Name, "library name survives JSON");
            ModAssert.Equal(stamp.SourceMap, restored.SourceMap, "source scope basis survives JSON");
            ModAssert.Equal(3, restored.Clips.Count, "all events and placements survive JSON");
            ModAssert.Equal(serialized, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "all clip coordinates, ids, overrides, commands, events and unknown metadata round-trip");
            var first = restored.Paste(document, 3, 3);
            ModAssert.NotNull(first, "the restored group pastes through the document");
            ModAssert.Equal(3, first.Count, "the instance contains every member");
            ModAssert.False(first[0] == ids[0] || first[1] == ids[1] || first[2] == ids[2], "every member gets a fresh identity");
            var secondEvent = document.EventOf(first[1]);
            ModAssert.Equal(first[0] + ":seen == 1", secondEvent.When, "internal condition targets this instance's event");
            ModAssert.Equal("flag " + first[0] + ":seen 1", secondEvent.Commands[0], "internal command targets this instance");
            ModAssert.Equal("flag self.own 1", secondEvent.Commands[1], "self remains relative to the fresh event scope");
            ModAssert.Equal("flag outside/key:seen 1", secondEvent.Commands[2], "external references remain unchanged");
            ModAssert.Equal("flag prefixrpg_fill_fixture/marker:seen 1", secondEvent.Commands[3],
                "scope-like suffixes of unrelated identifiers are untouched");
            JsonObject eventJson = null;
            foreach (var node in (JsonArray)document.Data["Events"])
            {
                if (node["Id"].ToString() == secondEvent.Id)
                {
                    eventJson = (JsonObject)node;
                }
            }

            ModAssert.NotNull(eventJson, "the pasted event retains its authored JSON");
            ModAssert.Equal("rpg_fill_fixture/marker:seen", eventJson["AuthorNote"].ToString(),
                "unknown metadata is preserved rather than rewritten as a script");
            ModAssert.Equal("flag " + first[1] + ":ready 1",
                document.AnyPlacement(first[2]).Data["Interact"]["Commands"][0].ToString(),
                "overlapping source names remap once without cascading between members");
            ModAssert.Equal(3, document.EventOf(first[0]).X, "first event anchors at the requested column");
            ModAssert.Equal(4, secondEvent.X, "second event retains its horizontal offset");
            ModAssert.Equal(3, secondEvent.Y, "second event retains its row");
            ModAssert.Equal(5, document.AnyPlacement(first[2]).Data["X"].GetValue<int>(), "sprite retains its offset");
            ModAssert.Equal(serialized, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "pasting never mutates the saved prototype");
            var second = restored.Paste(document, 0, 4);
            ModAssert.NotNull(second, "the same stamp can be reused");
            ModAssert.False(second[0] == first[0], "repeated instances do not share self flags");
            ModAssert.Equal(second[0] + ":seen == 1", document.EventOf(second[1]).When,
                "repeated instance references its own event, never the prior instance");
            ModAssert.True(document.Undo(), "one undo removes the whole second group");
            ModAssert.Null(document.EventOf(second[0]), "second group's first event is gone");
            ModAssert.Null(document.EventOf(second[1]), "second group's other event is gone");
            ModAssert.Null(document.AnyPlacement(second[2]), "second group's sprite is gone");
            ModAssert.NotNull(document.EventOf(first[0]), "the previous instance remains");
            ModAssert.True(document.Undo(), "one further undo removes the whole first group");
            ModAssert.Equal(before, document.Data.ToJsonString(), "both group undo operations restore exact map JSON");
            ModAssert.Equal(placementBefore, document.AnyPlacement(sign).Data.ToJsonString(), "original placement data is exact");
        }

        [ModTest("stamps/refusals_and_cross_map_remapping_are_readonly")]
        static void Boundaries(ModTestContext ctx)
        {
            var document = new RpgPaintFixture(ctx).Document;
            var selection = new[] { document.MapId + "/marker" };
            var stamp = RpgMapStamp.Capture(document, selection, "single_event");
            ModAssert.NotNull(stamp, "one event is a valid stamp");
            var before = document.Data.ToJsonString();
            ModAssert.Null(RpgMapStamp.Capture(null, selection, "missing"), "absent document cannot be captured");
            ModAssert.Null(RpgMapStamp.Capture(document, Array.Empty<string>(), "empty"), "empty selection is refused");
            ModAssert.Null(RpgMapStamp.Capture(document, selection, " "), "blank name is refused");
            ModAssert.Null(stamp.Paste(null, 0, 0), "absent destination is refused");
            ModAssert.Null(stamp.Paste(document, -1, 0), "off-map paste is refused atomically");
            ModAssert.Equal(before, document.Data.ToJsonString(), "refusals preserve authored data");
            ModAssert.False(document.CanUndo, "refusals create no history");
            var foreign = new RpgPaintFixture(ctx, foreign: true).Document;
            var foreignBefore = foreign.Data.ToJsonString();
            ModAssert.Null(stamp.Paste(foreign, 0, 0), "foreign destination retains the ordinary paste restriction");
            ModAssert.Equal(foreignBefore, foreign.Data.ToJsonString(), "foreign source stays exact");
            ModAssert.Equal(0, foreign.Changes().Count, "foreign refusal produces no patch");
            var source = document.Data.DeepClone() as JsonObject;
            source["Id"] = "stamp_destination";
            var other = new RpgPaintFixture(ctx, source).Document;
            stamp.Clips = new List<RpgPlacementClip>
            {
                new RpgPlacementClip
                {
                    LocalId = "marker",
                    X = 1,
                    Y = 1,
                    EventData = new JsonObject
                    {
                        ["Id"] = "marker",
                        ["When"] = "rpg_fill_fixture/marker:seen == 1",
                    },
                },
            };
            var copied = stamp.Paste(other, 2, 2);
            ModAssert.NotNull(copied, "group can be placed on another own map");
            ModAssert.Equal(copied[0] + ":seen == 1", other.EventOf(copied[0]).When,
                "cross-map condition uses the destination map and fresh local id");
            stamp.Clips.Add(stamp.Clips[0]);
            var changed = other.Data.ToJsonString();
            ModAssert.Null(stamp.Paste(other, 0, 0), "duplicate prototype identities are refused");
            ModAssert.Equal(changed, other.Data.ToJsonString(), "invalid group does not corrupt a previously placed instance");
            stamp.Clips = new List<RpgPlacementClip>
            {
                new RpgPlacementClip
                {
                    LocalId = "missing",
                    Object = "unloaded_fixture/decor/missing",
                    Fields = new JsonObject(),
                },
            };
            ModAssert.Null(stamp.Paste(other, 0, 0), "missing object refuses the complete group");
            ModAssert.Equal(changed, other.Data.ToJsonString(), "missing provider produces no partial instance");
            stamp.Clips = new List<RpgPlacementClip>
            {
                new RpgPlacementClip
                {
                    LocalId = "first",
                    X = int.MinValue,
                    EventData = new JsonObject(),
                },
                new RpgPlacementClip
                {
                    LocalId = "last",
                    X = int.MaxValue,
                    EventData = new JsonObject(),
                },
            };
            ModAssert.Null(stamp.Paste(other, 0, 0), "extreme offsets cannot overflow into an apparently valid destination");
            ModAssert.Equal(changed, other.Data.ToJsonString(), "offset rejection is atomic");
        }
    }
}
