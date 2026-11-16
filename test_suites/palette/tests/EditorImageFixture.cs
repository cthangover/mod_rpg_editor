using System;
using System.IO;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.TileRender;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    /// <summary>
    /// Owns scratch PNGs and an overlay with known pixels, crop coordinates and a seven-column idle frame.
    /// No installed resources are replaced. Disposal unmounts before deleting the owned directory.
    /// </summary>
    public sealed class EditorImageFixture : IDisposable
    {
        private const string Overlay = "editor_image_fixture";

        private readonly string root;

        private readonly FolderModFileProvider provider;

        /// <summary>Full-size 20x20 source with a blue crop-origin pixel and a transparent adjacent pixel.</summary>
        public RpgObject Red { get; }

        /// <summary>Independent 20x20 source, used to observe eviction without sharing an image key.</summary>
        public RpgObject Blue { get; }

        /// <summary>An 8x10 subregion; its first pixel must come from source coordinate (4,6).</summary>
        public RpgObject Cropped { get; }

        /// <summary>Four-facing sheet with seven columns and idle column three, whose centre is green.</summary>
        public RpgObject Actor { get; }

        /// <summary>Creates files only below the supplied sandbox and mounts them after all writes complete.</summary>
        public EditorImageFixture(string sandbox)
        {
            if (!GameEnvironment.IsRuntime)
            {
                throw new InvalidOperationException("Image fixtures require the real Godot runtime.");
            }

            root = Path.Combine(sandbox, "owned_images");
            Directory.CreateDirectory(Path.Combine(root, "rpg_images", "cache_probe"));
            Directory.CreateDirectory(Path.Combine(root, "charsets", "cache_probe"));
            provider = new FolderModFileProvider(root, Overlay);
            Red = Sprite("red", null);
            Blue = Sprite("blue", null);
            Cropped = Sprite("red", new[] { 4, 6, 8, 10 });
            Actor = new RpgObject
            {
                ID = "probe/actor",
                Form = RpgObjectForm.Actor,
                Charset = "cache_probe/sheet",
            };
            try
            {
                WriteImages();
                WriteLayout();
                ModOverlays.Mount(Overlay, provider);
                InvalidateFiles();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Removes the overlay and its files after image owners have released every borrowed texture.
        /// </summary>
        public void Dispose()
        {
            ModOverlays.Unmount(Overlay);
            InvalidateFiles();
            provider.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        private void WriteImages()
        {
            using var red = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
            red.Fill(Colors.Red);
            red.SetPixel(4, 6, Colors.Blue);
            red.SetPixel(5, 7, new Color(1, 0, 0, 0));
            ModAssert.Equal((long)Error.Ok, (long)red.SavePng(Path.Combine(root, "rpg_images", "cache_probe", "red.png")),
                "red fixture writes");
            using var blue = Image.CreateEmpty(20, 20, false, Image.Format.Rgba8);
            blue.Fill(Colors.Blue);
            ModAssert.Equal((long)Error.Ok, (long)blue.SavePng(Path.Combine(root, "rpg_images", "cache_probe", "blue.png")),
                "blue fixture writes");
            using var sheet = Image.CreateEmpty(140, 80, false, Image.Format.Rgba8);
            sheet.Fill(Colors.Transparent);
            sheet.FillRect(new Rect2I(0, 0, 20, 20), Colors.Red);
            sheet.FillRect(new Rect2I(63, 3, 14, 14), Colors.Lime);
            ModAssert.Equal((long)Error.Ok, (long)sheet.SavePng(Path.Combine(root, "charsets", "cache_probe", "sheet.png")),
                "sheet fixture writes");
        }

        private void WriteLayout()
        {
            var original = new TileWalkAnimation
            {
                Columns = 7,
                IdleColumn = 3,
                WalkFrames = new[]
                {
                    0,
                    2,
                    4,
                    6
                },
                SpeedScale = 0.8f,
            };
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<TileWalkAnimation>(json);
            ModAssert.Equal(original.Columns, restored.Columns, "columns survive JSON");
            ModAssert.Equal(original.IdleColumn, restored.IdleColumn, "idle column survives JSON");
            ModAssert.Equal(original.SpeedScale, restored.SpeedScale, 0, "speed survives JSON");
            ModAssert.Equal(original.WalkFrames.Length, restored.WalkFrames.Length, "cycle length survives JSON");
            for (var i = 0; i < original.WalkFrames.Length; i++)
            {
                ModAssert.Equal(original.WalkFrames[i], restored.WalkFrames[i], "cycle frame survives JSON");
            }

            File.WriteAllText(Path.Combine(root, "charsets", "cache_probe", "sheet.walk.json"), json);
        }

        private static RpgObject Sprite(string name, int[] crop)
        {
            return new RpgObject
            {
                ID = "probe/" + name,
                Form = RpgObjectForm.Sprite,
                Parts = new()
                {
                    new RpgObjectPart
                    {
                        Image = "cache_probe/" + name,
                        Crop = crop,
                    },
                },
            };
        }

        private static void InvalidateFiles()
        {
            ModManager.Instance.GetResolver<FileListResolver>().ClearCache();
        }
    }
}
