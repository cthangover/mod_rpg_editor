using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor.Tests.Preview
{
    /// <summary>Detached variants and scoped state inputs must change effective data without authoring or gameplay writes.</summary>
    public sealed class RpgEditorPreviewTests
    {
        private RpgObject lookup;

        private const string SourceJson = "{\"Id\":\"preview/prop\",\"Form\":\"sprite\",\"Solid\":true,"
            + "\"Parts\":[{\"Id\":\"body\",\"Image\":\"cache_probe/red\"}],"
            + "\"Variants\":{\"winter\":{\"Parts\":[{\"Id\":\"body\",\"Image\":\"cache_probe/blue\"}],\"Solid\":false},"
            + "\"summer\":{\"Parts\":[{\"Id\":\"body\",\"Image\":\"cache_probe/summer\"}],\"Solid\":true}},"
            + "\"States\":[{\"Id\":\"opened\",\"When\":\"self.open == 1\",\"Solid\":false,"
            + "\"Parts\":[{\"Id\":\"body\",\"Image\":\"cache_probe/open\"}]}]}";

        [ModTest("effective_preview/variant_overrides_and_added_last_state")]
        static void Merge(ModTestContext ctx)
        {
            var source = Source();
            var serialized = JsonSerializer.Serialize(source, JsonOptionsProvider.ReadOptions);
            var restored = JsonSerializer.Deserialize<RpgObject>(serialized, JsonOptionsProvider.ReadOptions);
            ModAssert.Equal(serialized, JsonSerializer.Serialize(restored, JsonOptionsProvider.ReadOptions),
                "source variants, state components and parts survive the complete JSON round trip");
            ModAssert.Equal(source.ID, restored.ID, "object id survives JSON");
            ModAssert.Equal(2, restored.Variants.Count, "both variant patches survive JSON");
            ModAssert.Equal(1, restored.States.Count, "base state survives JSON");
            var doc = Document("winter");
            var map = RpgEditorScene.MapOf(doc.Data);
            var preview = RpgEditorPreview.ForDocument(doc);
            var left = new RpgPlacement
            {
                ID = "preview_map/left",
                Object = source.ID,
                X = 1,
                Y = 1,
            };
            var winter = preview.Resolve(source, map, left);
            ModAssert.Equal("cache_probe/blue", winter.Parts[0].Image, "map variant supplies the effective picture");
            ModAssert.False(winter.IsSolid, "map variant supplies effective collision");
            var right = new RpgPlacement
            {
                ID = "preview_map/right",
                Object = source.ID,
                Variant = "summer",
                X = 2,
                Y = 1,
                Overrides = new Dictionary<string, JsonElement>
                {
                    ["Parts$add"] = JsonSerializer.SerializeToElement(new[]
                    {
                        new RpgObjectPart
                        {
                            Id = "body",
                            Image = "cache_probe/placement",
                        },
                    }, JsonOptionsProvider.ReadOptions),
                    ["States$add"] = JsonSerializer.Deserialize<JsonElement>(
                        "[{\"Id\":\"gone\",\"When\":\"self.gone == 1\",\"Hidden\":true}]"),
                },
            };
            var placed = preview.Resolve(source, map, right);
            ModAssert.True(placed.IsSolid, "placement variant takes precedence over the map variant");
            ModAssert.Equal("cache_probe/placement", placed.Parts[0].Image, "keyed placement override follows the variant");
            ModAssert.Equal(2, placed.States.Count, "placement-added states remain available to preview");
            preview.Set(right.ID, "open", true);
            preview.Set(right.ID, "gone", true);
            var gone = preview.Resolve(source, map, right);
            ModAssert.True(gone.Hidden, "last eligible placement-added state wins");
            ModAssert.Equal("cache_probe/placement", gone.Parts[0].Image,
                "a state replaces only its named components, without accumulating the earlier state");
            ModAssert.Equal(serialized, JsonSerializer.Serialize(source, JsonOptionsProvider.ReadOptions),
                "variant, override and state resolution leave the source catalogue unchanged");
        }

        [ModTest("effective_preview/scoped_canvas_collision_and_document_isolation")]
        static void Scoped(ModTestContext ctx)
        {
            new RpgEditorPreviewTests().CheckScoped();
        }

        private void CheckScoped()
        {
            lookup = Source();
            var doc = Document(null);
            var sourceBefore = JsonSerializer.Serialize(lookup, JsonOptionsProvider.ReadOptions);
            var mapBefore = doc.Data.ToJsonString();
            var placementsBefore = new List<string>();
            foreach (var record in doc.Placements)
            {
                placementsBefore.Add(record.Data.ToJsonString());
            }

            var preview = RpgEditorPreview.ForDocument(doc);
            ModAssert.True(ReferenceEquals(preview, RpgEditorPreview.ForDocument(doc)),
                "inspector and canvas share one document-local preview owner");
            var initial = RpgEditorScene.From(doc, ObjectOf, preview.Resolve);
            ModAssert.False(initial.Walk.IsWalkable(1, 1), "closed left placement blocks its actual canvas cell");
            ModAssert.False(initial.Walk.IsWalkable(2, 1), "closed right placement blocks independently");
            preview.Set("preview_map/left", "open", true);
            var opened = RpgEditorScene.From(doc, ObjectOf, preview.Resolve);
            ModAssert.Equal("cache_probe/open", opened.ObjectOf("preview_map/left").Parts[0].Image,
                "the scene uses the active state picture");
            ModAssert.True(opened.Walk.IsWalkable(1, 1), "state collision reaches the effective walk grid");
            ModAssert.False(opened.Walk.IsWalkable(2, 1), "the other placement keeps its own closed state");
            ModAssert.Equal("cache_probe/red", opened.ObjectOf("preview_map/right").Parts[0].Image,
                "the other placement retains the base picture");
            ModAssert.False(ReferenceEquals(lookup, opened.ObjectOf("preview_map/right")),
                "even an unchanged result is detached from shared catalogue data");
            preview.Set("preview_map/right", "open", true);
            preview.Reset("preview_map/left");
            var reset = RpgEditorScene.From(doc, ObjectOf, preview.Resolve);
            ModAssert.False(reset.Walk.IsWalkable(1, 1), "reset restores this placement's base collision");
            ModAssert.True(reset.Walk.IsWalkable(2, 1), "reset preserves another placement's preview inputs");
            ModAssert.False(RpgEditorPreview.ForDocument(Document(null)).IsSet("preview_map/right", "open"),
                "a different document starts with independent inputs");
            ModAssert.Equal(sourceBefore, JsonSerializer.Serialize(lookup, JsonOptionsProvider.ReadOptions),
                "presentation never mutates catalogue fields");
            ModAssert.Equal(mapBefore, doc.Data.ToJsonString(), "presentation never mutates map fields");
            var index = 0;
            foreach (var record in doc.Placements)
            {
                ModAssert.Equal(placementsBefore[index++], record.Data.ToJsonString(), "placement JSON remains byte-identical");
            }

            ModAssert.False(doc.CanUndo, "preview creates no authoring history entries");
            ModAssert.Equal(0, doc.Changes().Count, "preview creates no saved file changes");
        }

        private RpgObject ObjectOf(string id)
        {
            return id == lookup.ID ? lookup : null;
        }

        [ModTest("effective_preview/unset_zero_and_case_insensitive_state_commands")]
        static void Inputs(ModTestContext ctx)
        {
            var source = Source();
            source.States = new List<RpgObjectState>
            {
                new RpgObjectState
                {
                    Id = "closed",
                    When = "self.open == 0",
                    Components = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                        "{\"interact\":{\"commands\":[\"flag self.changed 1\"]}}"),
                },
            };
            var doc = Document(null);
            var preview = RpgEditorPreview.ForDocument(doc);
            const string id = "preview_map/left";
            ModAssert.Equal(-1, preview.StateIndex(source, id), "unset is distinct from an explicit zero in runtime conditions");
            preview.Set(id, "open", false);
            ModAssert.Equal(0, preview.StateIndex(source, id), "an unchecked explicit value supplies zero to the parser");
            ModAssert.False(preview.IsSet(id, "open"), "zero is not checked");
            preview.Set(id, "open", true);
            ModAssert.True(preview.IsSet(id, "open"), "one is checked");
            ModAssert.Equal(-1, preview.StateIndex(source, id), "one no longer matches the zero condition");
            preview.Reset(id);
            ModAssert.Equal(-1, preview.StateIndex(source, id), "reset removes the input instead of keeping an explicit zero");
            ModAssert.Null(preview.Resolve(null, RpgEditorScene.MapOf(doc.Data), null), "an unknown source has no presentation");
            var names = RpgStatePreview.SelfNames(source);
            ModAssert.Equal(2, names.Count, "conditions and case-insensitive interaction command fields both supply inputs");
            ModAssert.Equal("open", names[0], "condition input retains discovery order");
            ModAssert.Equal("changed", names[1], "lowercase state commands retain their input");
        }

        private static RpgObject Source()
        {
            return JsonSerializer.Deserialize<RpgObject>(SourceJson, JsonOptionsProvider.ReadOptions);
        }

        private static RpgMapDocument Document(string variant)
        {
            var map = new JsonObject
            {
                ["Id"] = "preview_map",
                ["Width"] = 4,
                ["Height"] = 3,
                ["TileSize"] = 32,
                ["Legend"] = new JsonObject(),
                ["Ground"] = new JsonArray("....", "....", "...."),
                ["Structure"] = new JsonArray("....", "....", "...."),
                ["Collision"] = new JsonArray("....", "....", "...."),
                ["Variant"] = variant,
            };
            var envelope = new JsonObject
            {
                ["Kind"] = "rpg_map",
                ["Items"] = new JsonArray(map),
            };
            var placements = new JsonArray();
            placements.Add(JsonSerializer.SerializeToNode(new RpgPlacement
            {
                ID = "preview_map/left",
                Object = "preview/prop",
                X = 1,
                Y = 1,
            }, JsonOptionsProvider.ReadOptions));
            placements.Add(JsonSerializer.SerializeToNode(new RpgPlacement
            {
                ID = "preview_map/right",
                Object = "preview/prop",
                X = 2,
                Y = 1,
            }, JsonOptionsProvider.ReadOptions));
            var placementFile = new JsonObject
            {
                ["Kind"] = "rpg_placement",
                ["Items"] = placements,
            };
            var files = new List<(string Path, string Text)>
            {
                ("maps/preview_map.json", envelope.ToJsonString()),
                ("placements/preview_map.json", placementFile.ToJsonString()),
            };
            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            return RpgMapDocument.Open("preview_map", "preview_owner", files, foreign, foreign, BaseOf, FormOf);
        }

        private static JsonObject BaseOf(string kind, string id)
        {
            return null;
        }

        private static RpgObjectForm? FormOf(string id)
        {
            return id == "preview/prop" ? RpgObjectForm.Sprite : null;
        }
    }
}
