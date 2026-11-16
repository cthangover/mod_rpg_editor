using Cthangover.Core.Mods.Testing;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    /// <summary>
    /// Checks visible pixels and ownership limits, including a nonlegacy sheet and a pinned cache under
    /// pressure.
    /// </summary>
    public sealed class RpgEditorThumbnailsTests
    {
        [ModTest("palette_images/crop_and_sheet_idle_keep_pixels")]
        static void Pixels(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var cache = new RpgEditorThumbnails();
            cache.SetVisible(new[]
            {
                fixture.Cropped,
                fixture.Actor
            });
            var cropped = cache.Get(fixture.Cropped);
            ModAssert.NotNull(cropped, "crop produces a miniature");
            ModAssert.Equal(8, cropped.GetWidth(), "crop width is preserved below the thumbnail limit");
            ModAssert.Equal(10, cropped.GetHeight(), "crop aspect and height");
            using var cropPixels = cropped.GetImage();
            ModAssert.True(cropPixels.GetPixel(0, 0) == Colors.Blue, "crop uses the requested source origin");
            ModAssert.True(cropPixels.GetPixel(1, 1).A == 0, "crop alpha is preserved");
            var actor = cache.Get(fixture.Actor);
            ModAssert.NotNull(actor, "idle frame produces a miniature");
            ModAssert.Equal(20, actor.GetWidth(), "seven columns define the frame width");
            ModAssert.Equal(20, actor.GetHeight(), "four rows define the frame height");
            using var actorPixels = actor.GetImage();
            ModAssert.True(actorPixels.GetPixel(10, 10) == Colors.Lime, "metadata selects idle column three");
            ModAssert.True(actorPixels.GetPixel(0, 0).A == 0, "transparent frame border remains transparent");
            ModAssert.Equal(1920, cache.CachedBytes, "only RGBA8 crop and frame pixels are uploaded");
            ModAssert.Equal(2, cache.Count, "two independent miniature textures");
            ModAssert.Null(cache.Get(null), "a missing object has no image");
            ModAssert.Equal(2, cache.DecodeCount, "an empty request performs no decode");
        }

        [ModTest("palette_images/budget_pins_and_releases_inactive_miniatures")]
        static void Budget(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var cache = new RpgEditorThumbnails(1600);
            cache.SetVisible(new[]
            {
                fixture.Red,
                fixture.Blue
            });
            var red = cache.Get(fixture.Red);
            ModAssert.NotNull(red, "one visible image fits");
            ModAssert.Null(cache.Get(fixture.Blue), "a second image cannot evict a visible borrower");
            ModAssert.Equal(1, cache.RejectedCount, "overflow is available to the palette's text-only hint");
            ModAssert.Equal(cache.BudgetBytes, cache.CachedBytes, "the hard miniature byte budget is respected");
            ModAssert.True(GodotObject.IsInstanceValid(red), "the first visible miniature is still alive");
            cache.SetVisible(new[]
            {
                fixture.Blue
            });
            var blue = cache.Get(fixture.Blue);
            ModAssert.NotNull(blue, "inactive image can now be evicted");
            ModAssert.False(GodotObject.IsInstanceValid(red), "eviction explicitly releases the old texture");
            using var pixels = blue.GetImage();
            ModAssert.True(pixels.GetPixel(0, 0) == Colors.Blue, "replacement has the requested pixels");
            var decodes = cache.DecodeCount;
            ModAssert.True(object.ReferenceEquals(blue, cache.Get(fixture.Blue)), "warm lookup borrows the same miniature");
            ModAssert.Equal(decodes, cache.DecodeCount, "warm lookup does not decode again");
            cache.Clear();
            ModAssert.Equal(0, cache.CachedBytes, "clearing releases the full pixel budget");
            ModAssert.Equal(0, cache.Count, "no owned texture remains after clearing");
            ModAssert.False(GodotObject.IsInstanceValid(blue), "clear explicitly releases the current texture");
        }
    }
}
