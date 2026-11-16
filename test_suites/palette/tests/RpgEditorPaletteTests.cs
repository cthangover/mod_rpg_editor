using System.Collections.Generic;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    /// <summary>Exercises lazy tab images through native rows while preserving search and stable-id selection.</summary>
    public sealed class RpgEditorPaletteTests
    {
        private string chosenId;

        private RpgObjectForm chosenForm;

        [ModTest("palette_images/tabs_filter_selection_and_rebuild")]
        static void Tabs(ModTestContext ctx)
        {
            new RpgEditorPaletteTests().Run(ctx);
        }

        private void Run(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var images = new RpgEditorThumbnails();
            var objects = new Dictionary<string, RpgObject>
            {
                [fixture.Red.ID] = fixture.Red,
                [fixture.Blue.ID] = fixture.Blue,
                [fixture.Actor.ID] = fixture.Actor,
            };
            var palette = new RpgEditorPalette(objects, images);
            // Native tab-change signals require a live scene-tree host, as in the real tool window.
            ((SceneTree)Engine.GetMainLoop()).Root.AddChild(palette);
            try
            {
                palette.EnsureConstructed();
                palette.ObjectChosen += OnChosen;
                ModAssert.Equal(5, palette.GetTabCount(), "four shapes and characters retain their tabs");
                ModAssert.Equal(2, palette.Count(RpgObjectForm.Sprite), "inactive sprite metadata is immediately available");
                ModAssert.Equal(1, palette.Count(RpgObjectForm.Actor), "inactive actor metadata is immediately available");
                ModAssert.Equal(0, images.DecodeCount, "an empty surface tab decodes no inactive images");
                palette.Select(RpgObjectForm.Sprite, fixture.Red.ID);
                var list = palette.GetNode<FilteredList>("PaletteTabs/Palette_Sprite");
                var search = list.GetChild<LineEdit>(0);
                var rows = list.GetChild<ItemList>(1);
                ModAssert.Equal(2, images.DecodeCount, "only the active sprite tab decodes");
                ModAssert.Equal(fixture.Red.ID, list.SelectedId, "silent selection uses the stable id");
                ModAssert.Null(chosenId, "programmatic selection does not publish a user choice");
                search.Text = "blue";
                search.EmitSignal(LineEdit.SignalName.TextChanged, search.Text);
                ModAssert.Equal(1, rows.ItemCount, "search narrows the active native rows");
                ModAssert.Equal(fixture.Blue.ID, rows.GetItemTooltip(0), "filtered row retains its source id");
                rows.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                ModAssert.Equal(fixture.Blue.ID, chosenId, "user selection publishes the filtered stable id");
                ModAssert.True(chosenForm == RpgObjectForm.Sprite, "user selection publishes its actual form");
                palette.Select(RpgObjectForm.Actor, fixture.Actor.ID);
                ModAssert.Equal(3, images.DecodeCount, "actor tab decodes its own idle miniature only on activation");
                ModAssert.Equal(fixture.Blue.ID, list.SelectedId, "inactive tab remembers its selection");
                palette.CurrentTab = 2;
                ModAssert.Equal(3, images.DecodeCount, "a warm tab reuses derived textures");
                ModAssert.Equal(1, rows.ItemCount, "tab switching preserves the search text");
                ModAssert.NotNull(rows.GetItemIcon(0), "filtered row borrows a derived miniature");
                palette.Rebuild(objects);
                ModAssert.Equal(2, palette.CurrentTab, "rebuild preserves the active tab");
                ModAssert.Equal(fixture.Blue.ID, list.SelectedId, "rebuild preserves a surviving stable id");
                ModAssert.Equal(1, rows.ItemCount, "rebuild preserves the filter");
                ModAssert.Equal(5, images.DecodeCount, "rebuild refreshes only the active tab images");
                palette.ReleaseIcons();
                ModAssert.Null(rows.GetItemIcon(0), "release detaches native icon borrowers before disposal");
                images.Clear();
                ModAssert.Equal(0, images.CachedBytes, "detached palette permits complete image-owner cleanup");
            }
            finally
            {
                palette.ObjectChosen -= OnChosen;
                palette.ReleaseIcons();
                palette.Free();
            }
        }

        private void OnChosen(string id, RpgObjectForm form)
        {
            chosenId = id;
            chosenForm = form;
        }
    }
}
