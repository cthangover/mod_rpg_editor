namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using Godot;

    public partial class RpgEditorCanvas
    {
        /// <summary>Logical-cell paths projected with the map; the chosen variant also shows directions and stops.</summary>
        public List<(RpgRouteTrace Path, Color Color, bool Selected)> Routes { get; } = new();
        private Vector2 RoutePoint(Vector2I cell) => view.ProjectToView(new Vector3(cell.X+.5f, cell.Y+.5f, 0));

        private void PaintRoutes(Node2D target)
        {
            var unit = 1f/world.Scale.X;
            foreach (var route in Routes)
            {
                foreach (var edge in route.Path.Segments)
                {
                    var from = RoutePoint(edge.From);
                    var to = RoutePoint(edge.To);
                    target.DrawLine(from, to, route.Color, (route.Selected ? 3f : 1.5f)*unit, true);
                    if (!route.Selected) continue;
                    var middle = from.Lerp(to, .6f);
                    var direction = (to-from).Normalized();
                    var side = direction.Orthogonal();
                    target.DrawLine(middle, middle-direction*7*unit+side*4*unit, route.Color, 2*unit, true);
                    target.DrawLine(middle, middle-direction*7*unit-side*4*unit, route.Color, 2*unit, true);
                }
                if (!route.Selected) continue;
                foreach (var stop in route.Path.Stops)
                {
                    var point = RoutePoint(stop.Cell);
                    target.DrawCircle(point, 6*unit, new Color(.08f,.09f,.1f,.95f));
                    target.DrawArc(point, 6*unit, 0, Mathf.Tau, 20, route.Color, 2*unit, true);
                    target.DrawString(ThemeDB.FallbackFont, point+new Vector2(9,-7)*unit, stop.Label,
                        HorizontalAlignment.Left, -1, Mathf.Max(8, Mathf.RoundToInt(13*unit)), route.Color);
                }
            }
        }
    }
}
