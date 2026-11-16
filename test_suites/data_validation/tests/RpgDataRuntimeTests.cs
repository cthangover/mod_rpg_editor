using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.View;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.Rpg.Render;
using Cthangover.Mods.TileRender;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.DataValidation
{
    /// <summary>One malformed external quad is diagnosed and quarantined while its valid neighbour still produces native pixels.</summary>
    public sealed class RpgDataRuntimeTests
    {
        private ImageTexture texture;

        private readonly List<string> errors = new();

        [ModTest("data_gui/runtime_bad_part_preserves_valid_pixels", TimeoutSeconds = 60)]
        static async Task Render(ModTestContext ctx)
        {
            ModAssert.True(GameEnvironment.IsRuntime, "runtime quarantine needs the real renderer");
            ModAssert.False(DisplayServer.GetName() == "headless", "GPU pixels require the windowed launch");
            await new RpgDataRuntimeTests().Run(ctx);
        }

        private async Task Run(ModTestContext ctx)
        {
            using (var image = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8))
            {
                image.Fill(Colors.Lime);
                texture = ImageTexture.CreateFromImage(image);
            }

            GameLogger.LogEmitted += OnLog;
            try
            {
                using var builder = new RpgRenderSceneBuilder(ImageOf, ImageOf);
                var map = new RpgMap
                {
                    ID = "validation_runtime",
                    Width = 2,
                    Height = 2,
                    Zoom = 1,
                };
                var source = new RpgObject
                {
                    ID = "validation_runtime/prop",
                    Form = RpgObjectForm.Sprite,
                    Parts = new List<RpgObjectPart>
                    {
                        new RpgObjectPart
                        {
                            Id = "good",
                            Image = "green",
                            Geometry = RpgPartGeometry.Ground,
                            Size = new float[]
                            {
                                2,
                                2,
                            },
                        },
                        new RpgObjectPart
                        {
                            Id = "bad_quad",
                            Image = "green",
                            Geometry = RpgPartGeometry.Quad,
                            Points = new List<float[]>
                            {
                                new float[]
                                {
                                    0,
                                    0,
                                    float.NaN,
                                },
                                new float[]
                                {
                                    1,
                                    0,
                                    0,
                                },
                                new float[]
                                {
                                    1,
                                    1,
                                    0,
                                },
                                new float[]
                                {
                                    0,
                                    1,
                                    0,
                                },
                            },
                        },
                    },
                };
                var placed = new List<(string Id, RpgObject Object, int X, int Y)>
                {
                    (source.ID, source, 0, 0),
                };
                var scene = builder.Build(map, RpgMapCells.Build(map, ObjectOf), placed);
                ModAssert.Equal(1, scene.Visuals.Count, "only the malformed part is removed before rendering");
                ModAssert.Equal(source.ID + "#good", scene.Visuals[0].Id, "the original valid neighbour is retained");
                ModAssert.Equal(1, scene.Warnings.Count, "the render scene exposes the rejected part");
                ModAssert.True(scene.Warnings[0].Contains("quad_number"), "the rejection names its numeric cause");
                ModAssert.Equal(1, errors.Count, "exactly one intentional external-data error is logged");
                ModAssert.True(errors[0].Contains("bad_quad") && errors[0].Contains("part skipped"),
                    "the error identifies the skipped part rather than silently hiding it");
                var projections = new[]
                {
                    "topdown",
                    "isometric",
                };
                foreach (var projection in projections)
                {
                    await CheckPixels(scene, projection, ctx);
                }

                source.Anchor = new float[]
                {
                    float.NaN,
                    1,
                };
                ModAssert.Equal(0, builder.PartLooks(source, 32).Count,
                    "a nonfinite shared origin never reaches a renderer transform");
                ModAssert.Equal(2, errors.Count, "the invalid shared origin has its own explicit diagnostic");
                ModAssert.True(errors[1].Contains("Anchor") && errors[1].Contains("parts skipped"),
                    "the shared-origin diagnostic names the field and affected object");
            }
            finally
            {
                GameLogger.LogEmitted -= OnLog;
                texture.Dispose();
            }
        }

        private async Task CheckPixels(TileRenderScene scene, string projection, ModTestContext ctx)
        {
            var viewport = new SubViewport
            {
                Size = new Vector2I(128, 128),
                Disable3D = true,
                TransparentBg = true,
                UseHdr2D = ((SceneTree)Godot.Engine.GetMainLoop()).Root.UseHdr2D,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            ITileWorldPresentation view = null;
            try
            {
                ((SceneTree)Godot.Engine.GetMainLoop()).Root.AddChild(viewport);
                view = TileRendererRegistry.Instance.Resolve(projection).Create();
                viewport.AddChild(view.Root);
                view.Resize(new Vector2(128, 128));
                view.Mount(scene);
                view.Present(new TileFrame
                {
                    Focus = Vector2.One,
                    Ambient = Colors.White,
                }, 0);
                await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                await viewport.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = ViewportCapture.Take(viewport);
                var point = (Vector2I)view.ProjectToView(new Vector3(1, 1, 0));
                var color = image.GetPixel(point.X, point.Y);
                ModAssert.True(color.G > 0.9f && color.R < 0.1f,
                    "valid neighbour still draws green after rejecting the external quad in " + projection);
                ctx.Log(projection + " retained neighbour pixel=" + color);
            }
            finally
            {
                view?.Dispose();
                viewport.Free();
            }
        }

        private Texture2D ImageOf(string id)
        {
            return texture;
        }

        private static RpgObject ObjectOf(string id)
        {
            return null;
        }

        private void OnLog(string category, string message, LogLevel level)
        {
            if (category == "RPG" && level == LogLevel.Error)
            {
                errors.Add(message);
            }
        }
    }
}
