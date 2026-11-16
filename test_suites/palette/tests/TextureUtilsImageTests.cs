using System.IO;
using System.IO.Compression;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    /// <summary>CPU image loads keep ordinary overlay/ZIP precedence while transferring independent image ownership.</summary>
    public sealed class TextureUtilsImageTests
    {
        [ModTest("palette_images/owned_decode_zip_override_and_restore")]
        static void ZipOverride(ModTestContext ctx)
        {
            using var fixture = new EditorImageFixture(ctx.SandboxDir);
            using var original = TextureUtils.LoadImageFromModGroup("rpg_images", "cache_probe/red");
            ModAssert.NotNull(original, "folder image decodes through the group index");
            ModAssert.True(original.GetPixel(0, 0) == Colors.Red, "original folder pixels");
            var path = Path.Combine(ctx.SandboxDir, "images.zip");
            const string overlay = "editor_zip_image_fixture";
            try
            {
                using (var image = Image.CreateEmpty(128, 64, false, Image.Format.Rgba8))
                {
                    image.Fill(Colors.Blue);
                    image.FillRect(new Rect2I(0, 0, 64, 64), Colors.Transparent);
                    using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
                    using var stream = archive.CreateEntry("rpg_images/cache_probe/red.png").Open();
                    stream.Write(image.SavePngToBuffer());
                }

                using var provider = new ZipModFileProvider(path, overlay);
                ModOverlays.Mount(overlay, provider);
                try
                {
                    InvalidateFiles();
                    using var replacement = TextureUtils.LoadImageFromModGroup("rpg_images", "rpg_images/cache_probe/red.png");
                    ModAssert.Equal(128, replacement.GetWidth(), "ZIP overlay replaces the folder image");
                    ModAssert.True(replacement.GetPixel(127, 63) == Colors.Blue, "ZIP pixels decode correctly");
                    ModAssert.True(original.GetPixel(0, 0) == Colors.Red, "an earlier owned image stays independent");
                    using var thumbnails = new RpgEditorThumbnails();
                    thumbnails.SetVisible(new[] { fixture.Red });
                    var thumbnail = thumbnails.Get(fixture.Red);
                    ModAssert.Equal(56, thumbnail.GetWidth(), "a large source is reduced before GPU upload");
                    ModAssert.Equal(28, thumbnail.GetHeight(), "derived resize preserves source aspect");
                    ModAssert.Equal(56 * 28 * 4, thumbnails.CachedBytes, "the budget counts derived pixels only");
                    using var pixels = thumbnail.GetImage();
                    ModAssert.True(pixels.GetPixel(0, 0).A == 0, "resizing preserves the transparent source region");
                    ModAssert.True(pixels.GetPixel(55, 27) == Colors.Blue, "resizing preserves the opaque source region");
                }
                finally
                {
                    ModOverlays.Unmount(overlay);
                    InvalidateFiles();
                }

                using var restored = TextureUtils.LoadImageFromModGroup("rpg_images", "cache_probe/red");
                ModAssert.Equal(20, restored.GetWidth(), "unmount invalidation restores the folder source");
                ModAssert.True(restored.GetPixel(0, 0) == Colors.Red, "restored source pixels match");
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static void InvalidateFiles()
        {
            ModManager.Instance.GetResolver<FileListResolver>().ClearCache();
        }
    }
}
