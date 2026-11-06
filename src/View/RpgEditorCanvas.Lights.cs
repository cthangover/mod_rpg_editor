namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>Optional lighting frames and editor-only handles, reused by the existing light editor's map adapter.</summary>
    public partial class RpgEditorCanvas
    {
        /// <summary>Resolved renderer lights, already in cell coordinates.</summary>
        public List<TileLight> PreviewLights { get; } = new();
        /// <summary>White keeps the map editor's ordinary working view; null uses the phase colour alone.</summary>
        public Color? PreviewAmbient { get; set; } = Colors.White;
        /// <summary>
        /// Time phase for this preview, independent of the saved map. Day by default: the shader multiplies the phase
        /// colour by the map's ambient, so white ambient on day gives the plain map colours of the editor.
        /// </summary>
        public Vector4 PreviewTimeWeights { get; set; } = new(0, 1, 0, 0);
        /// <summary>Independent light handles, excluded from resources, scenery and collision.</summary>
        public List<TileLight> LightMarkers { get; } = new();
        /// <summary>Selected handle index, or -1.</summary>
        public int SelectedLight { get; set; } = -1;
        private void PaintLightMarkers(Node2D target)
        {
            for (var index = 0; index < LightMarkers.Count; index++)
            {
                var light = LightMarkers[index];
                var selected = index == SelectedLight;
                var center = view.ProjectToView(new Vector3(light.Position.X, light.Position.Y, 0));
                var color = selected ? Colors.White : new Color(light.Color, 1);
                var size = 6f / world.Scale.X;
                target.DrawCircle(center, size, color);
                target.DrawLine(center - Vector2.Right * size * 1.8f, center + Vector2.Right * size * 1.8f, Colors.Black, 1f / world.Scale.X);
                target.DrawLine(center - Vector2.Down * size * 1.8f, center + Vector2.Down * size * 1.8f, Colors.Black, 1f / world.Scale.X);
                if (!selected) continue;
                if (light.ViewSpace)
                {
                    target.DrawArc(center, light.Radius * scene.TileSize * view.Root.Transform.X.Length(), 0, Mathf.Tau, 64,
                        new Color(color, 0.7f), 1.5f / world.Scale.X, true);
                    continue;
                }
                var ring = new Vector2[65];
                for (var i = 0; i < ring.Length; i++)
                {
                    var angle = i * Mathf.Tau / (ring.Length - 1);
                    ring[i] = view.ProjectToView(new Vector3(light.Position.X + Mathf.Cos(angle) * light.Radius,
                        light.Position.Y + Mathf.Sin(angle) * light.Radius, 0));
                }
                target.DrawPolyline(ring, new Color(color, 0.7f), 1.5f / world.Scale.X, true);
            }
        }
    }
}
