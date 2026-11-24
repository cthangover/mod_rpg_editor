using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    using Cthangover.Core.Mods.Memory;

    /// <summary>
    /// Full-size map/ghost borrowers survive pressure; only detached inactive sources may be released.
    /// </summary>
    public sealed class RpgEditorTexturesTests
    {
        /// <summary>
        /// An uncommitted replacement cannot evict the previous canvas source. A later successful generation
        /// retires only sources absent from that committed generation.
        /// </summary>
        [ModTest("palette_images/uncommitted_rebuild_preserves_previous_source")]
        static void UncommittedScene(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var cache = new RpgEditorTextures(1600);
            cache.BeginScene();
            var red = cache.Image("cache_probe/red");
            cache.EndScene();
            cache.BeginScene();
            var blue = cache.Image("cache_probe/blue");
            ModAssert.Equal(3200, cache.PinnedBytes, "old canvas remains pinned during replacement construction");
            ModAssert.True(GodotObject.IsInstanceValid(red), "uncommitted replacement leaves the old canvas source alive");
            ModAssert.True(GodotObject.IsInstanceValid(blue), "new draft source remains alive until a commit");
            cache.BeginScene();
            ModAssert.True(ReferenceEquals(red, cache.Image("cache_probe/red")), "the surviving source is reused");
            cache.EndScene();
            ModAssert.Equal(1600, cache.PinnedBytes, "the successful generation replaces the old pin set");
            ModAssert.Equal(1600, cache.CachedBytes, "only unpinned replacement input is evicted");
            var metrics = cache.GetMetrics();
            ModAssert.Null(metrics.EstimatedPayloadBytes);
            ModAssert.Equal(1600L, metrics.EstimatedResourceBytes.Value);
            ModAssert.Equal(1L, metrics.Cache.Value.Evictions.Value);
            ModAssert.Equal(1L, metrics.Cache.Value.Hits.Value);
            ModAssert.Equal(2L, metrics.Cache.Value.Misses.Value);
            ((ICache)cache).Clear();
            ModAssert.True(GodotObject.IsInstanceValid(red), "committed source remains borrowed safely");
            ModAssert.False(GodotObject.IsInstanceValid(blue), "unused draft source is released after the successful swap");
        }

        [ModTest("palette_images/full_sources_keep_scene_and_preview_pins")]
        static void SourcePins(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var cache = new RpgEditorTextures(1600);
            cache.BeginScene();
            var red = cache.Image("cache_probe/red");
            cache.EndScene();
            ModAssert.Equal(1600, cache.PinnedBytes, "one active source is pinned");
            ModAssert.Equal(1600, cache.CachedBytes, "source estimate is independent of thumbnails");
            cache.BeginScene();
            var blue = cache.Image("cache_probe/blue");
            cache.EndScene();
            ModAssert.False(GodotObject.IsInstanceValid(red), "replaced scene source is released after unpinning");
            ModAssert.True(GodotObject.IsInstanceValid(blue), "active replacement stays alive");
            ModAssert.Equal(1, cache.Count, "inactive source does not accumulate");
            var previewObject = new RpgObject
            {
                Form = RpgObjectForm.Sprite,
                Parts = new()
                {
                    new RpgObjectPart
                    {
                        Image = "cache_probe/red",
                        Crop = new[]
                        {
                            4,
                            6,
                            8,
                            10
                        }
                    },
                },
            };
            var preview = cache.Preview("ghost", previewObject);
            cache.EndScene();
            ModAssert.Equal(8, preview.GetWidth(), "ghost uses the original full-resolution crop");
            ModAssert.Equal(10, preview.GetHeight(), "ghost size does not depend on miniature scaling");
            ModAssert.Equal(3200, cache.PinnedBytes, "both map and ghost are active borrowers");
            ModAssert.True(cache.CachedBytes > cache.BudgetBytes, "active working set is preserved above retention target");
            ModAssert.True(GodotObject.IsInstanceValid(blue), "pressure does not release the map source");
            cache.ReleasePreview("ghost");
            cache.EndScene();
            ModAssert.False(GodotObject.IsInstanceValid(preview), "detached crop wrapper has an explicit owner");
            ModAssert.Equal(1600, cache.CachedBytes, "unpinning permits trimming back to the target");
            cache.Dispose();
            ModAssert.Equal(0, cache.Count, "window cleanup leaves no source owner");
            ModAssert.Equal(0, cache.PinnedBytes, "cleanup drops every pin");
            ModAssert.False(GodotObject.IsInstanceValid(blue), "cleanup releases the map texture");
        }
    }
}
