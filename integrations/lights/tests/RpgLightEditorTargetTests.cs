namespace Cthangover.Tools.RpgEditor.Lighting.Tests
{
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.UI.Tool.LightEditor;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Exercises the optional adapter through its public registry and actual map persistence path.</summary>
    public class RpgLightEditorTargetTests
    {
        [ModTest("light_target/registered_provider_creates_independent_views")]
        static void Registered(ModTestContext ctx)
        {
            var provider = LightEditorTargets.All.Single(p => p.Id == "rpg_map");
            using var first = provider.Create();
            using var second = provider.Create();
            ctx.SandboxRoot.AddChild(first.View);
            ctx.SandboxRoot.AddChild(second.View);
            ModAssert.False(ReferenceEquals(first.View, second.View), "each editor owns a separate view");
            ModAssert.False(first.ClampPosition, "wall sources can lie outside the ground rectangle");
            ModAssert.True(first.RadiusMinimum > 0 && first.RadiusMaximum > first.RadiusMinimum, "map radius has a usable cell range");
            ModAssert.Equal("[]", first.SourceText, "an unopened target has no sources");
            ModAssert.False(first.Save(out var error), "an unopened target cannot report a successful save");
            ModAssert.True(!string.IsNullOrEmpty(error), "missing map is explained");
        }

        [ModTest("light_target/map_sources_save_roundtrip_and_undo")]
        static void Persist(ModTestContext ctx)
        {
            const string path = "maps/light_target_contract.json";
            const string mapId = "light_target_contract";
            ModAssert.True(ctx.ReadFileText(path) == null, "fixture must not overwrite an author file");
            try
            {
                ModAssert.True(ModFileService.WriteText(ctx.ModId, path,
                    "{\"Kind\":\"rpg_map\",\"Items\":[{\"Id\":\"light_target_contract\",\"Width\":4,\"Height\":4}]}"), "create an isolated map");
                using var target = new RpgLightEditorTarget();
                ctx.SandboxRoot.AddChild(target.View);
                var controller = new LightEditorController();
                target.Open(controller);
                target.OpenMap(ctx.ModId, mapId);
                ModAssert.NotNull(target.Document, "own map opens through the adapter");
                for (var i = 0; i < 11; i++)
                    ModAssert.NotNull(controller.Add(new Vector2(-0.25f, 1.25f)), "map sources are not limited to background shader slots");
                target.Commit();
                ModAssert.True(target.IsDirty, "shared-controller edits reach the map document");
                using var undo = new InputEventKey { CtrlPressed = true, Keycode = Key.Z };
                ModAssert.True(target.HandleKey(undo) && !target.IsDirty && controller.Lights.Count == 0, "one undo restores the map and controller");
                using var redo = new InputEventKey { CtrlPressed = true, Keycode = Key.Y };
                ModAssert.True(target.HandleKey(redo) && target.IsDirty && controller.Lights.Count == 11, "redo restores all source data");
                ModAssert.True(target.Save(out var error), "map saves: " + error);
                ModAssert.False(target.IsDirty, "successful save clears the document changes");
                using var saved = JsonDocument.Parse(ctx.ReadFileText(path));
                var map = JsonSerializer.Deserialize<RpgMap>(saved.RootElement.GetProperty("Items")[0].GetRawText(), JsonOptionsProvider.ReadOptions);
                ModAssert.Equal(11, map.Lights.Count, "all sources survive serialization");
                ModAssert.Equal(11, map.Lights.Select(l => l.Id).Distinct().Count(), "stable source ids are unique");
                ModAssert.True(map.Lights.All(l => l.Id.StartsWith(ctx.ModId + "/light_") && l.X == -0.25f && l.Y == 1.25f && l.Radius == 3), "owner, coordinates and cell radius survive save");
                target.OpenMap(ctx.ModId, mapId);
                ModAssert.True(controller.Lights.Count == 11 && !target.IsDirty, "reopening reads the persisted map without pending changes");
            }
            finally
            {
                ModFileService.DeleteFile(ctx.ModId, path);
                SceneManager.ReloadContent();
            }
        }
    }
}
