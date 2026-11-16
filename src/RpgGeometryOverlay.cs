using System;
using System.Collections.Generic;
using System.Text;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.TileRender;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Read-only map geometry diagnostics. Projected bounds come from the actual renderer; the owned
    /// figure enters its ordinary actor list, so scenery occludes it through the real depth path.
    /// Only the diagnostic clock advances; no gameplay session, document or collision data is changed.
    /// </summary>
    internal sealed partial class RpgGeometryOverlay : ModWidget
    {
        private const string ProbeId = "rpg_editor:geometry_probe";

        private readonly RpgEditorWindow host;

        private readonly RpgGeometryControls controls;

        private readonly List<(TileVisual Visual, TileVisualPlacement Placement)> visuals = new();

        private RpgEditorScene source;

        private RpgGeometryProbe route;

        private ITileProjection projection;

        private string selected;

        private string projectionId;

        private long revision = -1;

        private ImageTexture figure;

        private TileActorPose pose;

        private int step = -1;

        private float progress;

        private double clock;

        private bool enabled;

        private bool running;

        private bool retired;

        private Vector2 cameraOrigin;

        private Vector2 cameraAxis;

        internal RpgGeometryOverlay(RpgEditorWindow host, RpgGeometryControls controls)
        {
            this.host = host;
            this.controls = controls;
            Name = "GeometryOverlay";
            MouseFilter = MouseFilterEnum.Ignore;
            Visible = false;
        }

        protected override void Construct()
        {
            controls.EnabledChanged += OnEnabled;
            controls.StepRequested += OnStep;
            controls.PlayRequested += OnPlay;
            controls.ResetRequested += OnReset;
            Draw += Paint;
            CreateFigure();
            SetProcess(false);
        }

        /// <summary>Enables only the diagnostic process selected by the utility checkbox.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
            SetProcess(enabled);
        }

        /// <summary>Moves the preview figure along recorded walkable neighbours without ticking gameplay or editing data.</summary>
        public override void _Process(double delta)
        {
            if (!enabled || retired)
            {
                return;
            }

            var canvas = host.Canvas;
            var changed = source != host.Scene || selected != host.SelectedId || revision != (canvas.Scene?.Revision ?? -1);
            if (changed || projectionId != canvas.Projection)
            {
                Rebuild();
            }

            if (running && route != null && step >= 0)
            {
                progress += (float)Math.Max(0, delta) * 2;
                clock += Math.Max(0, delta);
                while (progress >= 1 && running)
                {
                    progress -= 1;
                    Advance();
                }

                UpdatePose();
                ShowSummary();
                QueueRedraw();
            }

            var origin = canvas.CanvasPointAt(Vector2.Zero);
            var axis = canvas.CanvasPointAt(Vector2.Right);
            if (origin != cameraOrigin || axis != cameraAxis)
            {
                cameraOrigin = origin;
                cameraAxis = axis;
                QueueRedraw();
            }
        }

        private void OnEnabled(bool value)
        {
            enabled = value;
            Visible = value;
            SetProcess(value);
            running = false;
            if (value)
            {
                Rebuild();
            }
            else
            {
                DetachFigure();
                ShowSummary();
            }

            QueueRedraw();
        }

        private void Rebuild()
        {
            DetachFigure();
            source = host.Scene;
            selected = host.SelectedId;
            projectionId = host.Canvas.Projection;
            revision = host.Canvas.Scene?.Revision ?? -1;
            visuals.Clear();
            projection = host.Canvas.Scene == null ? null
                : TileRendererRegistry.Instance.Resolve(projectionId).Projection(host.Canvas.Scene.TileSize);
            if (projection != null && selected != null)
            {
                foreach (var visual in host.Canvas.Scene.Visuals)
                {
                    if (visual.Id?.StartsWith(selected + "#", StringComparison.Ordinal) == true
                        && (string.IsNullOrEmpty(visual.Only) || visual.Only == projectionId))
                    {
                        visuals.Add((visual, projection.Place(visual)));
                    }
                }
            }

            route = RpgGeometryProbe.Around(source, selected);
            OnReset();
            QueueRedraw();
        }

        private void OnStep()
        {
            running = false;
            progress = 0;
            Advance();
            UpdatePose();
            ShowSummary();
            QueueRedraw();
        }

        private void OnPlay()
        {
            running = !running && step >= 0;
            progress = 0;
            ShowSummary();
        }

        private void OnReset()
        {
            running = false;
            progress = 0;
            clock = 0;
            step = -1;
            if (route != null)
            {
                for (var index = 0; index < route.Cells.Count; index++)
                {
                    if (route.CanEnter(index))
                    {
                        step = index;
                        break;
                    }
                }
            }

            UpdatePose();
            ShowSummary();
            QueueRedraw();
        }

        private void Advance()
        {
            if (route == null || step < 0)
            {
                running = false;
                return;
            }

            var next = (step + 1) % route.Cells.Count;
            if (!route.CanEnter(next))
            {
                running = false;
                progress = 0;
                return;
            }

            step = next;
        }

        private void UpdatePose()
        {
            if (!enabled || route == null || step < 0)
            {
                DetachFigure();
                return;
            }

            var next = (step + 1) % route.Cells.Count;
            var fraction = running && route.CanEnter(next) ? progress : 0;
            pose.Position = ((Vector2)route.Cells[step]).Lerp(route.Cells[next], fraction);
            pose.Column = TileWalkSheet.Animation(figure).Column(clock, running);
            pose.Visible = true;
            if (!host.Canvas.Actors.Contains(pose))
            {
                host.Canvas.Actors.Add(pose);
            }

            host.Canvas.Refresh();
        }

        private void DetachFigure()
        {
            if (pose != null && host.Canvas.Actors.Remove(pose))
            {
                host.Canvas.Refresh();
            }
        }

        private void ShowSummary()
        {
            if (route == null || route.Cells.Count == 0)
            {
                controls.SetSummary(ToolUi.Tr("tools/rpg_editor/geometry_select"), false, false);
                return;
            }

            var text = new StringBuilder();
            text.Append(string.Format(ToolUi.Tr("tools/rpg_editor/geometry_summary"), selected,
                route.Footprint.Size.X, route.Footprint.Size.Y, route.Bounds.Size.X, route.Bounds.Size.Y,
                route.Cells.Count, route.BlockedCells.Count));
            text.Append(' ').Append(ToolUi.Tr(route.Closed ? "tools/rpg_editor/geometry_closed"
                : "tools/rpg_editor/geometry_incomplete"));
            text.Append('\n').Append(ToolUi.Tr("tools/rpg_editor/geometry_legend"));
            var count = Math.Min(2, visuals.Count);
            for (var index = 0; index < count; index++)
            {
                var entry = visuals[index];
                var anchor = entry.Visual.Anchor;
                var sort = entry.Visual.SortAnchor ?? anchor;
                text.Append('\n').Append(string.Format(ToolUi.Tr("tools/rpg_editor/geometry_part"), entry.Visual.Id,
                    anchor.X, anchor.Y, sort.X, sort.Y, Relation(entry)));
            }

            if (visuals.Count > count)
            {
                text.Append(' ').Append(string.Format(ToolUi.Tr("tools/rpg_editor/geometry_more_parts"), visuals.Count - count));
            }

            if ((long)route.Footprint.Size.X * route.Footprint.Size.Y > 256)
            {
                text.Append(' ').Append(ToolUi.Tr("tools/rpg_editor/geometry_large_mask"));
            }

            controls.SetSummary(text.ToString(), enabled && step >= 0, running);
        }

        private string Relation((TileVisual Visual, TileVisualPlacement Placement) entry)
        {
            if (entry.Visual.DrawOrder == TileDrawOrder.BelowCharacters)
            {
                return ToolUi.Tr("tools/rpg_editor/geometry_behind");
            }

            if (entry.Visual.DrawOrder == TileDrawOrder.AboveCharacters)
            {
                return ToolUi.Tr("tools/rpg_editor/geometry_front");
            }

            if (step < 0 || projection == null)
            {
                return ToolUi.Tr("tools/rpg_editor/geometry_by_position");
            }

            var actor = projection.Project(new Vector3(pose.Position.X + projection.ActorAnchor(pose.Look.Anchor).X,
                pose.Position.Y + projection.ActorAnchor(pose.Look.Anchor).Y, 0));
            return ToolUi.Tr(actor.Y >= entry.Placement.Anchor.Y ? "tools/rpg_editor/geometry_behind"
                : "tools/rpg_editor/geometry_front");
        }

        private void Paint()
        {
            if (!enabled || retired || projection == null || route == null || route.Cells.Count == 0)
            {
                return;
            }

            DrawFootprint();
            for (var index = 0; index < route.Cells.Count; index++)
            {
                var color = route.CanEnter(index) ? Colors.LimeGreen : Colors.OrangeRed;
                DrawCircle(host.Canvas.CanvasPointOf(route.Cells[index]), 3, color);
            }

            foreach (var entry in visuals)
            {
                var transform = entry.Placement.Transform;
                var corners = new[]
                {
                    ProjectedPoint(transform.Origin),
                    ProjectedPoint(transform.Origin + transform.X),
                    ProjectedPoint(transform.Origin + transform.X + transform.Y),
                    ProjectedPoint(transform.Origin + transform.Y),
                    ProjectedPoint(transform.Origin),
                };
                DrawPolyline(corners, Colors.DeepSkyBlue, 1.5f, true);
                var anchor = host.Canvas.CanvasPointAt(entry.Visual.Anchor);
                var sort = ProjectedPoint(entry.Placement.Anchor);
                DrawLine(anchor, sort, Colors.Gold, 2, true);
                DrawCircle(anchor, 4, Colors.Gold);
                DrawCircle(sort, 3, Colors.Magenta);
            }

            if (step >= 0)
            {
                var feet = pose.Position + projection.ActorAnchor(pose.Look.Anchor);
                DrawCircle(host.Canvas.CanvasPointAt(feet), 3, Colors.White);
            }
        }

        private void DrawFootprint()
        {
            var footprint = route.Footprint;
            var outline = CellOutline(footprint);
            DrawPolyline(outline, Colors.Orange, 2, true);
            var clipped = footprint.Intersection(new Rect2I(0, 0, source.Map.Width, source.Map.Height));
            if ((long)clipped.Size.X * clipped.Size.Y > 256)
            {
                return;
            }

            var obj = source.ObjectOf(selected);
            if (obj == null)
            {
                return;
            }

            for (var y = clipped.Position.Y; y < clipped.End.Y; y++)
            {
                for (var x = clipped.Position.X; x < clipped.End.X; x++)
                {
                    if (obj.BlocksAt(x - footprint.Position.X, y - footprint.Position.Y))
                    {
                        DrawPolyline(CellOutline(new Rect2I(x, y, 1, 1)), Colors.OrangeRed, 1, true);
                    }
                }
            }
        }

        private Vector2[] CellOutline(Rect2I rect)
        {
            var corners = new[]
            {
                host.Canvas.CanvasPointAt(rect.Position),
                host.Canvas.CanvasPointAt(rect.Position + new Vector2I(rect.Size.X, 0)),
                host.Canvas.CanvasPointAt(rect.End),
                host.Canvas.CanvasPointAt(rect.Position + new Vector2I(0, rect.Size.Y)),
                host.Canvas.CanvasPointAt(rect.Position),
            };
            return corners;
        }

        private Vector2 ProjectedPoint(Vector2 point)
        {
            return host.Canvas.CanvasPointAt(projection.Unproject(point));
        }

        private void CreateFigure()
        {
            using var image = Image.CreateEmpty(48, 128, false, Image.Format.Rgba8);
            image.Fill(Colors.Transparent);
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 3; column++)
                {
                    var origin = new Vector2I(column * 16, row * 32);
                    image.FillRect(new Rect2I(origin + new Vector2I(5, 2), new Vector2I(6, 8)), Colors.SpringGreen);
                    image.FillRect(new Rect2I(origin + new Vector2I(3, 10), new Vector2I(10, 14)), Colors.SpringGreen);
                    image.FillRect(new Rect2I(origin + new Vector2I(4, 24), new Vector2I(3, 8)), Colors.White);
                    image.FillRect(new Rect2I(origin + new Vector2I(9, 24), new Vector2I(3, 8)), Colors.White);
                }
            }

            figure = ImageTexture.CreateFromImage(image);
            TileWalkSheet.Configure(figure, new TileWalkAnimation());
            pose = new TileActorPose
            {
                Id = ProbeId,
                Charset = true,
                Visible = true,
                Look = new TileVisual
                {
                    Texture = figure,
                    Source = TileWalkSheet.Frame(figure, 1, 0),
                    Anchor = new Vector2(0.5f, 1),
                    Bounds = new Rect2(0.2f, -0.8f, 0.6f, 1.8f),
                },
            };
        }

        internal void Retire()
        {
            if (retired)
            {
                return;
            }

            retired = true;
            DetachFigure();
            controls.EnabledChanged -= OnEnabled;
            controls.StepRequested -= OnStep;
            controls.PlayRequested -= OnPlay;
            controls.ResetRequested -= OnReset;
            Draw -= Paint;
            if (pose != null)
            {
                pose.Look = null;
            }

            figure?.Dispose();
            figure = null;
            visuals.Clear();
            projection = null;
            route = null;
            source = null;
        }

        protected override void Destruct()
        {
            Retire();
            base.Destruct();
        }
    }
}
