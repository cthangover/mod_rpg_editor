using System.Threading.Tasks;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Interface.Tool;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.Palette
{
    /// <summary>Checks usable editor area and resize propagation through the utility widget and window chrome.</summary>
    public sealed class RpgEditorWorkbenchTests
    {
        [ModTest("palette_images/workbench_fills_window_and_resizes")]
        static async Task Layout(ModTestContext ctx)
        {
            var window = ToolWindow.Open<RpgEditorWindow>();
            try
            {
                window.Size = new Vector2I(1280, 900);
                var tree = window.GetTree();
                await window.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await window.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                var palette = window.Palette;
                var canvas = window.Canvas;
                ModAssert.True(palette.Size.Y > window.Size.Y * 0.6f,
                    "the palette uses the window height instead of remaining at its minimum");
                ModAssert.True(canvas.Size.X > window.Size.X * 0.35f,
                    "the map keeps a useful central editing area between sidebars");
                var previousWindow = window.Size;
                var previousCanvas = canvas.Size;
                window.Size = new Vector2I(1500, 900);
                await window.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                await window.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                ModAssert.True(window.Size.X > previousWindow.X, "the host permits the requested width increase");
                ModAssert.True(canvas.Size.X >= previousCanvas.X + window.Size.X - previousWindow.X - 2,
                    "extra window width reaches the canvas while sidebars retain their widths");
            }
            finally
            {
                window.QueueFree();
            }
        }
    }
}
