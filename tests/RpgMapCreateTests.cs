namespace Cthangover.Tools.RpgEditor.Tests
{
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Utils;

    /// <summary>The location creator's file side: the new-map template and where map files are found.</summary>
    public class RpgMapCreateTests
    {
        [ModTest("create/template_builds_an_openable_map_of_the_given_size")]
        static void Template(ModTestContext ctx)
        {
            var text = RpgMapTemplate.FileText("new_loc", 7, 5, atmosphere: null);
            var doc = RpgEditorFixture.OpenOwn("new_loc", RpgEditorFixture.OwnMod, new[] { ("maps/new_loc.json", text) });
            ModAssert.Equal(7, doc.Width, "width");
            ModAssert.Equal(5, doc.Height, "height");
            ModAssert.Equal("Down", doc.Data["Spawn"]["Dir"].GetValue<string>(), "spawn faces down");
            ModAssert.Equal(1L, (long)doc.Data["Spawn"]["X"].GetValue<int>(), "spawn sits in the corner");
            ModAssert.True(doc.Legend.Count == 0, "legend starts empty");
            ModAssert.Null(doc.Data["Atmosphere"], "no atmosphere when none was chosen");
            ModAssert.True(doc.Resize(8, 6), "the empty map is editable like any other");
        }

        [ModTest("create/template_carries_the_chosen_atmosphere")]
        static void Atmosphere(ModTestContext ctx)
        {
            var text = RpgMapTemplate.FileText("new_loc", 4, 3, atmosphere: "winter_snow");
            var doc = RpgEditorFixture.OpenOwn("new_loc", RpgEditorFixture.OwnMod, new[] { ("maps/new_loc.json", text) });
            ModAssert.Equal("winter_snow", doc.Data["Atmosphere"].GetValue<string>(), "the whole-map profile is written");
            ModAssert.True(doc.Data["Effects"] == null, "the mask stays out of a fresh map");
        }

        [ModTest("create/findfile_resolves_map_files_of_writable_mods")]
        static void FindFile(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "map files are listed through the loaded mods");
            var found = RpgMapFiles.FindFile("rpg_village");
            ModAssert.True(found.HasValue, "the game's village map is found");
            ModAssert.Equal("rpg_plus_game", found.Value.Mod, "owner mod");
            ModAssert.Equal("maps/rpg_village.json", found.Value.Path, "mod-relative path");
            ModAssert.False(RpgMapFiles.FindFile("no_such_location_xyz").HasValue, "a missing id resolves to nothing");
        }
    }
}
