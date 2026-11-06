namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Mods.TileRender;
    using Godot;

    /// <summary>
    /// The map canvas: the game's own renderer in one of two projections (<c>topdown</c> or <c>isometric</c>, switched
    /// during editing without losing the view), a mark layer (footprints, selection, the ghost of the object under the
    /// cursor), and the mouse. Wheel zooms about the cursor, middle drag pans, clicks and drags are reported as cells
    /// (the renderer's <c>TryPickGround</c> inverts the projection). The canvas knows nothing of documents: the window
    /// decides what a click does.
    /// </summary>
    public partial class RpgEditorCanvas : Control
    {
        /// <summary>Renderer id of the top-down projection.</summary>
        public const string TopDown = "topdown";

        /// <summary>Renderer id of the isometric projection.</summary>
        public const string Isometric = "isometric";

        private const float MinZoom = 0.1f;
        private const float MaxZoom = 6f;
        private const float WheelStep = 1.15f;
        private const float FitMargin = 16f;

        private readonly Node2D world = new() { Name = "MapWorld" };
        private readonly RpgEditorOverlay overlay = new() { Name = "Marks" };
        private readonly TileFrame frame = new() { Ambient = Colors.White };
        private ITileWorldPresentation view;
        private TileRenderScene scene;
        private string projection = TopDown;
        private bool fitted;
        private bool fitPending;
        private bool panning;
        private bool pressing;
        private bool resizing;
        private Vector2 resizeStart;
        private bool resizeMoved;
        private MouseButton pressButton;
        private Vector2I? hover;
        private Vector2I? lastDrag;

        /// <summary>Button press on a cell: the cell, the button, and whether it is a double click.</summary>
        public event Action<Vector2I, MouseButton, bool> CellPressed;

        /// <summary>The pointer moved over a new cell while a button is held.</summary>
        public event Action<Vector2I, MouseButton> CellDragged;

        /// <summary>Button released (over the cell the pointer is on, if any).</summary>
        public event Action<Vector2I, MouseButton> CellReleased;

        /// <summary>The cell under the pointer changed (null when the pointer left the map).</summary>
        public event Action<Vector2I?> Hovered;

        /// <summary>
        /// A left or right press on a teleport arrow (plan 14.G18): its index in <see cref="Arrows"/>, the part hit, and the cell
        /// pressed ((-1, -1) when the press is off the map). The press is not a cell press; its drag still reports cells.
        /// </summary>
        public event Action<int, RpgArrowHandle, Vector2I, MouseButton> ArrowPressed;

        /// <summary>Selected event bounds; four handles use screen pixels so they remain usable at every zoom.</summary>
        public Rect2I? ResizeArea { get; set; }
        public event Action<int, Vector2> ResizePressed;
        public event Action<Vector2> ResizeDragged;
        public event Action<Vector2> ResizeReleased;

        /// <summary>Inverse of the two affine editor projections, including points outside the map for boundary handles.</summary>
        public Vector2 CellSpaceAt(Vector2 point)
        {
            var origin = CanvasPointAt(Vector2.Zero);
            var x = CanvasPointAt(Vector2.Right) - origin;
            var y = CanvasPointAt(Vector2.Down) - origin;
            var determinant = x.X * y.Y - x.Y * y.X;
            if (Mathf.IsZeroApprox(determinant)) return Vector2.Zero;
            var delta = point - origin;
            return new Vector2((delta.X * y.Y - delta.Y * y.X) / determinant,
                (x.X * delta.Y - x.Y * delta.X) / determinant);
        }

        private static Vector2 Corner(Rect2I area, int index) => index switch
        {
            0 => area.Position,
            1 => new Vector2(area.End.X, area.Position.Y),
            2 => area.End,
            _ => new Vector2(area.Position.X, area.End.Y),
        };

        /// <summary>The visible event corner under the pointer, or -1; handles take precedence over teleport arrows.</summary>
        public int ResizeHandleAt(Vector2 point)
        {
            if (view == null || ResizeArea is not { } area) return -1;
            var picked = -1;
            var distance = 100f;
            for (var i = 0; i < 4; i++)
            {
                var candidate = CanvasPointAt(Corner(area, i)).DistanceSquaredTo(point);
                if (candidate <= distance) { distance = candidate; picked = i; }
            }
            return picked;
        }

        /// <summary>Rectangles of cells drawn over the map, in order (footprints, selection, paint preview).</summary>
        public List<CanvasRect> Marks { get; } = new();

        /// <summary>Picture of the ghost object (null hides the ghost), drawn anchored on the south edge of the footprint.</summary>
        public Texture2D GhostTexture { get; set; }

        /// <summary>The ghost footprint: origin cell and size in cells.</summary>
        public CanvasRect GhostRect { get; set; }

        /// <summary>Tint of the ghost (red when it would block).</summary>
        public Color GhostTint { get; set; } = new(1, 1, 1, 0.6f);

        /// <summary>The actors' poses of the scene, drawn on top of the static map.</summary>
        public List<TileActorPose> Actors { get; } = new();

        /// <summary>Teleport arrows drawn over the marks (plan 14.G3); the window fills them from the map's links.</summary>
        public List<RpgTeleportArrow> Arrows { get; } = new();

        /// <summary>Only selection tools intercept clicks on links; paint tools always receive the cell.</summary>
        public bool ArrowsInteractive { get; set; } = true;

        /// <summary>Renderer id in use (<see cref="TopDown"/> or <see cref="Isometric"/>).</summary>
        public string Projection => projection;

        /// <summary>True when a renderer for <paramref name="id"/> is registered.</summary>
        public static bool HasProjection(string id) => TileRendererRegistry.Instance.Ids.Contains(id);

        /// <summary>The scene the canvas shows, or null.</summary>
        public TileRenderScene Scene => scene;

        /// <summary>The cell under the pointer, or null.</summary>
        public Vector2I? Hover => hover;

        /// <summary>Builds the canvas; the scene and the projection come later.</summary>
        public RpgEditorCanvas()
        {
            Name = "RpgEditorCanvas";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(320, 240);
            ClipContents = true;
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.None;
            AddChild(world);
            world.AddChild(overlay);
            overlay.Painter = PaintMarks;
            Resized += OnResized;
        }

        /// <summary>Shows <paramref name="next"/> (the canvas takes ownership and releases the previous one).</summary>
        public void SetScene(TileRenderScene next)
        {
            var previous = scene;
            scene = next;
            Mount();
            previous?.Release();
            if (!fitted && scene != null)
                FitToView();
        }

        /// <summary>Switches the renderer; the camera stays where it is.</summary>
        public void SetProjection(string id)
        {
            if (id == projection && view != null)
                return;
            projection = id;
            Mount();
        }

        /// <summary>Scales the map to the canvas and centres it. Before the canvas has a size or a map, the fit waits for them.</summary>
        public void FitToView()
        {
            fitted = true;
            if (view == null || Size.X <= 0 || Size.Y <= 0)
            {
                fitPending = true;
                return;
            }
            fitPending = false;
            SetCamera(FitZoom(), Size / 2f);
        }

        /// <summary>The zoom that shows the whole map in the canvas (what <see cref="FitToView"/> sets); 1 without a map.</summary>
        public float FitZoom()
        {
            if (view == null || Size.X <= 0 || Size.Y <= 0)
                return 1f;
            var content = view.ContentBounds.Size * (scene?.Zoom ?? 1f);
            var zoom = Mathf.Min((Size.X - FitMargin) / Mathf.Max(1f, content.X), (Size.Y - FitMargin) / Mathf.Max(1f, content.Y));
            return Mathf.Clamp(zoom, MinZoom, MaxZoom);
        }

        /// <summary>The current zoom of the map.</summary>
        public float MapZoom => world.Scale.X;

        /// <summary>Re-presents the frame (actors, marks) after a change of the scene or the marks.</summary>
        public void Refresh()
        {
            if (view == null)
                return;
            frame.Actors.Clear();
            frame.Actors.AddRange(Actors);
            frame.Focus = scene == null ? Vector2.Zero : new Vector2(scene.Size.X, scene.Size.Y) / 2f;
            frame.Ambient = PreviewAmbient;
            frame.TimeWeights = PreviewTimeWeights;
            frame.Lights.Clear();
            frame.Lights.AddRange(PreviewLights.OrderBy(light => light.Position.DistanceSquaredTo(frame.Focus)).Take(16));
            view.Present(frame, 0);
            overlay.QueueRedraw();
        }

        /// <summary>Converts a point of the canvas into a cell of the map, or null outside the map.</summary>
        public Vector2I? CellAt(Vector2 canvasPoint)
        {
            if (view == null)
                return null;
            var local = (canvasPoint - world.Position) / world.Scale.X;
            return view.TryPickGround(local, out var cell) ? cell : null;
        }

        /// <summary>The canvas position of a cell's centre.</summary>
        public Vector2 CanvasPointOf(Vector2I cell)
        {
            if (view == null)
                return Vector2.Zero;
            return world.Position + world.Scale.X * view.ProjectToView(new Vector3(cell.X + 0.5f, cell.Y + 0.5f, 0));
        }

        /// <summary>The canvas position of a point of cell space (a cell centre is at x + 0.5), so arrows can be hit in pixels.</summary>
        public Vector2 CanvasPointAt(Vector2 cellSpace)
        {
            if (view == null)
                return Vector2.Zero;
            return world.Position + world.Scale.X * view.ProjectToView(new Vector3(cellSpace.X, cellSpace.Y, 0));
        }

        /// <summary>The teleport arrow under a canvas point (plan 14.G18): its index in <see cref="Arrows"/> (-1 for none) and the part hit.</summary>
        public int ArrowAt(Vector2 canvasPoint, out RpgArrowHandle handle)
        {
            handle = RpgArrowHandle.None;
            if (view == null || Arrows.Count == 0)
                return -1;
            var segments = Arrows.Select(a => (From: CanvasPointAt(a.From), To: CanvasPointAt(a.To))).ToList();
            return RpgArrowHit.Pick(segments, canvasPoint, out handle);
        }

        /// <summary>Runs the renderer's presentation teardown; the canvas is then empty.</summary>
        public void Clear()
        {
            view?.Dispose();
            view = null;
            scene?.Release();
            scene = null;
            fitted = false;
            fitPending = false;
            Marks.Clear();
            ResizeArea = null;
            resizing = false;
            Arrows.Clear();
            Routes.Clear();
            GhostRect = null;
            GhostTexture = null;
            overlay.QueueRedraw();
        }

        /// <inheritdoc />
        public override void _ExitTree() => Clear();

        /// <inheritdoc />
        public override void _GuiInput(InputEvent @event)
        {
            switch (@event)
            {
                case InputEventMouseButton button:
                    HandleButton(button);
                    break;
                case InputEventMouseMotion motion:
                    HandleMotion(motion);
                    break;
            }
        }

        private void HandleButton(InputEventMouseButton button)
        {
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
            {
                if (button.Pressed)
                    Zoom(button.Position, button.ButtonIndex == MouseButton.WheelUp ? WheelStep : 1f / WheelStep);
                AcceptEvent();
                return;
            }
            if (button.ButtonIndex == MouseButton.Middle)
            {
                panning = button.Pressed;
                AcceptEvent();
                return;
            }
            if (button.ButtonIndex is not (MouseButton.Left or MouseButton.Right))
                return;
            AcceptEvent();
            if (button.ButtonIndex == MouseButton.Left && resizing && !button.Pressed)
            {
                resizing = false;
                // A click without a drag still cycles stacked placements, including underneath the event.
                var moved = resizeMoved || button.Position.DistanceSquaredTo(resizeStart) > 9;
                ResizeReleased?.Invoke(CellSpaceAt(moved ? button.Position : resizeStart));
                if (!moved && CellAt(resizeStart) is { } clicked)
                {
                    CellPressed?.Invoke(clicked, button.ButtonIndex, button.DoubleClick);
                    CellReleased?.Invoke(clicked, button.ButtonIndex);
                }
                return;
            }
            if (button.Pressed)
            {
                var corner = button.ButtonIndex == MouseButton.Left ? ResizeHandleAt(button.Position) : -1;
                if (corner >= 0)
                {
                    resizing = true;
                    resizeStart = button.Position;
                    resizeMoved = false;
                    pressing = false;
                    ResizePressed?.Invoke(corner, CellSpaceAt(button.Position));
                    return;
                }
                var cell = CellAt(button.Position);
                var handle = RpgArrowHandle.None;
                var arrow = button.ButtonIndex == MouseButton.Left && ArrowsInteractive ? ArrowAt(button.Position, out handle) : -1;
                if (arrow >= 0)
                {
                    // An arrow is a handle, even where it leaves the map: its drag reports the cells the pointer crosses.
                    pressing = true;
                    pressButton = button.ButtonIndex;
                    lastDrag = cell;
                    ArrowPressed?.Invoke(arrow, handle, cell ?? new Vector2I(-1, -1), button.ButtonIndex);
                    return;
                }
                if (cell is not { } at)
                    return;
                pressing = true;
                pressButton = button.ButtonIndex;
                lastDrag = at;
                CellPressed?.Invoke(at, button.ButtonIndex, button.DoubleClick);
                return;
            }
            if (!pressing || button.ButtonIndex != pressButton)
                return;
            pressing = false;
            var released = CellAt(button.Position) ?? lastDrag ?? new Vector2I(-1, -1);
            CellReleased?.Invoke(released, button.ButtonIndex);
            lastDrag = null;
        }

        private void HandleMotion(InputEventMouseMotion motion)
        {
            if (panning)
            {
                world.Position += motion.Relative;
                AcceptEvent();
                return;
            }
            if (resizing)
            {
                resizeMoved |= motion.Position.DistanceSquaredTo(resizeStart) > 9;
                if (resizeMoved) ResizeDragged?.Invoke(CellSpaceAt(motion.Position));
                AcceptEvent();
                return;
            }
            var cell = CellAt(motion.Position);
            if (cell != hover)
            {
                hover = cell;
                Hovered?.Invoke(cell);
                overlay.QueueRedraw();
            }
            if (pressing && cell is { } at && at != lastDrag)
            {
                lastDrag = at;
                CellDragged?.Invoke(at, pressButton);
            }
        }

        private void Zoom(Vector2 anchor, float factor)
        {
            var before = world.Scale.X;
            var next = Mathf.Clamp(before * factor, MinZoom, MaxZoom);
            world.Position = anchor - (anchor - world.Position) * (next / before);
            world.Scale = Vector2.One * next;
        }

        private void SetCamera(float zoom, Vector2 centre)
        {
            world.Scale = Vector2.One * zoom;
            world.Position = centre * (1f - zoom);
        }

        private void OnResized()
        {
            if (view == null)
                return;
            view.Resize(Size);
            Refresh();
            if (fitPending)
                FitToView();
        }

        /// <summary>Mounts the scene in the current projection, replacing the previous presentation.</summary>
        private void Mount()
        {
            view?.Dispose();
            view = null;
            if (scene == null || !HasProjection(projection))
            {
                overlay.QueueRedraw();
                return;
            }
            view = TileRendererRegistry.Instance.Resolve(projection).Create();
            world.AddChild(view.Root);
            world.MoveChild(overlay, world.GetChildCount() - 1);
            view.Mount(scene);
            view.Resize(Size);
            Refresh();
            if (fitPending)
                FitToView();
        }

        private void PaintMarks(Node2D target)
        {
            if (view == null)
                return;
            PaintLightMarkers(target);
            foreach (var mark in Marks)
            {
                var polygon = Outline(mark);
                if (mark.Fill)
                    target.DrawColoredPolygon(polygon, mark.Color);
                else
                    target.DrawPolyline(polygon.Append(polygon[0]).ToArray(), mark.Color, 2f);
            }
            if (ResizeArea is { } area)
            {
                var side = 9f / world.Scale.X;
                for (var i = 0; i < 4; i++)
                {
                    var corner = Corner(area, i);
                    var point = view.ProjectToView(new Vector3(corner.X, corner.Y, 0));
                    target.DrawRect(new Rect2(point - Vector2.One * side / 2, Vector2.One * side), Colors.White);
                }
            }
            foreach (var arrow in Arrows)
                arrow.Draw(target, cell => view.ProjectToView(new Vector3(cell.X, cell.Y, 0)));
            if (GhostTexture != null && GhostRect != null)
            {
                var ghost = GhostRect;
                var anchor = view.ProjectToView(new Vector3(ghost.X + ghost.W / 2f, ghost.Y + ghost.H, 0));
                var size = GhostTexture.GetSize() * (scene?.Zoom ?? 1f);
                target.DrawTextureRect(GhostTexture, new Rect2(anchor.X - size.X / 2f, anchor.Y - size.Y, size.X, size.Y), false, GhostTint);
            }
            PaintRoutes(target);
        }

        private Vector2[] Outline(CanvasRect rect)
            => new[]
            {
                view.ProjectToView(new Vector3(rect.X, rect.Y, 0)),
                view.ProjectToView(new Vector3(rect.X + rect.W, rect.Y, 0)),
                view.ProjectToView(new Vector3(rect.X + rect.W, rect.Y + rect.H, 0)),
                view.ProjectToView(new Vector3(rect.X, rect.Y + rect.H, 0)),
            };
    }

    /// <summary>A rectangle of cells drawn over the map: origin, size, colour, filled or outlined.</summary>
    public sealed class CanvasRect
    {
        /// <summary>A rectangle of <paramref name="w"/>×<paramref name="h"/> cells from (<paramref name="x"/>, <paramref name="y"/>).</summary>
        public CanvasRect(int x, int y, int w, int h, Color color, bool fill)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
            Color = color;
            Fill = fill;
        }

        /// <summary>Origin cell.</summary>
        public int X { get; }

        /// <summary>Origin cell.</summary>
        public int Y { get; }

        /// <summary>Width in cells.</summary>
        public int W { get; }

        /// <summary>Height in cells.</summary>
        public int H { get; }

        /// <summary>Colour.</summary>
        public Color Color { get; }

        /// <summary>True when filled, false when outlined.</summary>
        public bool Fill { get; }
    }
}
