using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor.Tests.DepthProbe
{
    /// <summary>Loads neutral fixture data through its mounted provider; no files or global catalogue objects are edited.</summary>
    internal sealed class RpgGeometryFixture
    {
        private readonly IReadOnlyDictionary<string, RpgObject> objects;

        internal RpgMapDocument Document { get; }

        internal RpgGeometryFixture(ModTestContext ctx)
        {
            objects = Entities.Collect<RpgObject>();
            var files = new List<(string Path, string Text)>
            {
                ("maps/geometry_fixture.json", ctx.ReadFileText("maps/geometry_fixture.json")),
                ("placements/geometry_fixture/objects.json", ctx.ReadFileText("placements/geometry_fixture/objects.json")),
            };
            var foreign = Array.Empty<(string Id, string OwnerMod, string File, JsonObject Written)>();
            Document = RpgMapDocument.Open("geometry_fixture", ctx.ModId, files, foreign, foreign, BaseOf, FormOf);
            ModAssert.NotNull(Document, "neutral geometry fixture document opens");
        }

        internal RpgEditorScene Scene()
        {
            return RpgEditorScene.From(Document, ObjectOf, RpgEditorPreview.ForDocument(Document).Resolve);
        }

        internal RpgObject ObjectOf(string id)
        {
            return id != null && objects.TryGetValue(id, out var obj) ? obj : null;
        }

        private RpgObjectForm? FormOf(string id)
        {
            return ObjectOf(id)?.Form;
        }

        private static JsonObject BaseOf(string kind, string id)
        {
            return null;
        }
    }
}
