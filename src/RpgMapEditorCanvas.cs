namespace Cthangover.Tools.RpgEditor
{
    using System;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.TileRender;
    using Cthangover.Core.Events;
    using Godot;

    /// <summary>
    /// The map as the editor shows it: back, main and front tiles, the events
    /// (their charset frame or tile, else a marker), the spawn point, the
    /// collision layer and a grid. Tiles of layers other than the active one are
    /// dimmed, so the author sees which layer the brush is on; the collision
    /// codes are shown only while the collision layer is active.
    /// <para/>
    /// Mouse, on a tile or collision layer: LMB paints with the current tool and
    /// a drag paints a stroke, RMB erases, Alt+LMB picks the tile under the
    /// pointer. On the events layer: LMB selects the top event of a cell and drags
    /// it, a double click on an empty cell adds an event, RMB clears the selection.
    /// Locked events (other mods' and other files') are drawn faded with a dashed
    /// frame and ignore the mouse.
    /// While <see cref="Placing"/>, a click only chooses a cell for the window.
    /// Ctrl+wheel zooms. The plain wheel (Shift+wheel horizontally) is left to
    /// the surrounding ScrollContainer, and a middle-button drag pans it, so a
    /// map larger than the view is reachable on any layer and with any tool.
    /// <para/>
    /// The canvas owns no data. It edits the window's
    /// <see cref="RpgMapDocument"/> and reports through events; every stroke or
    /// drag starts with <see cref="EditStarting"/>, so the window records exactly
    /// one undo step for it.
    /// </summary>
    public partial class RpgMapEditorCanvas : Control
    {
        private static readonly Color BlockColor = new(0.9f, 0.2f, 0.2f, 0.45f);
        private static readonly Color OpenColor = new(0.2f, 0.85f, 0.35f, 0.4f);
        // Blocked by a solid tile (Auto code): changed by a painted code here or by the tile's Solid in the tileset editor.
        private static readonly Color TileSolidColor = new(1f, 0.55f, 0.15f, 0.3f);
        private static readonly Color GridColor = new(1f, 1f, 1f, 0.12f);
        private static readonly Color MarkerColor = new(0.35f, 0.55f, 1f, 0.55f);
        private static readonly Color LockedModulate = new(1f, 1f, 1f, 0.55f);

        private readonly RpgTextures textures = new();
        private ITileWorldPresentation preview;
        private RpgRenderSceneBuilder builder;
        private TileRenderScene previewScene;
        private ITileProjection projection;
        private RpgMapEditorOverlay overlay;
        public string RendererId { get; private set; } = "topdown";
        public ITileWorldPresentation Preview => preview;
        private Vector2I? hover;
        private bool stroking;
        private bool singleCellStroke;
        private RpgMapEvent dragged;
        private bool dragMoved;
        private bool panning;
        private Vector2 panStartMouse;
        private Vector2 panStartScroll;

        /// <summary>The edited map; null shows an empty canvas.</summary>
        public RpgMapDocument Document { get; set; }

        /// <summary>Tileset of the map (null: tiles are not drawn).</summary>
        public RpgTileset Tileset { get; set; }

        /// <summary>Atlas image of <see cref="Tileset"/>.</summary>
        public Texture2D Atlas { get; set; }

        /// <summary>Layer the brush works on.</summary>
        public RpgEditLayer ActiveLayer { get; set; } = RpgEditLayer.Back;

        /// <summary>Brush.</summary>
        public RpgEditTool Tool { get; set; } = RpgEditTool.Pencil;

        /// <summary>Atlas index the pencil and the fill put down.</summary>
        public int SelectedTile { get; set; }

        /// <summary>Multi-cell brush selected in the object atlas; null uses SelectedTile.</summary>
        public RpgTileStamp SelectedStamp { get; set; }

        /// <summary>Collision code the pencil and the fill put down.</summary>
        public char SelectedCollision { get; set; } = RpgCollision.Block;

        /// <summary>The selected event, or null.</summary>
        public RpgMapEvent SelectedEvent { get; set; }

        /// <summary>Draw the cell grid.</summary>
        public bool ShowGrid { get; set; } = true;

        /// <summary>Screen pixels per tile pixel.</summary>
        public float Zoom { get; private set; } = 1.5f;

        /// <summary>An edit is about to change the document: record an undo step.</summary>
        public event Action EditStarting;

        /// <summary>The document changed.</summary>
        public event Action Changed;

        /// <summary>The selected event changed (null for none).</summary>
        public event Action<RpgMapEvent> EventSelected;

        /// <summary>Alt+click picked a tile.</summary>
        public event Action<int> TilePicked;

        /// <summary>
        /// The next LMB on a cell is not an edit but a choice reported through
        /// <see cref="CellChosen"/> (placing a character); RMB cancels. Either ends it.
        /// </summary>
        public bool Placing { get; set; }

        /// <summary>The cell chosen while <see cref="Placing"/>, or null when cancelled.</summary>
        public event Action<Vector2I?> CellChosen;

        /// <summary>Creates the canvas; input comes through the GuiInput event.</summary>
        public RpgMapEditorCanvas()
        {
            RendererId = Cthangover.Core.Games.ActiveGame.GetSetting("tile.renderer") ?? (TileRendererRegistry.Instance.Ids.Count == 1 ? TileRendererRegistry.Instance.Ids[0] : "topdown");
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--editor-renderer=")) RendererId = arg.Substring(18);
            ClipContents = true;
            FocusMode = FocusModeEnum.Click;
            TextureFilter = TextureFilterEnum.Nearest;
            GuiInput += OnGuiInput;
            MouseExited += OnMouseExited;
            overlay = new RpgMapEditorOverlay(this);
            AddChild(overlay);
        }

        private float CellSize => (Tileset?.TileSize ?? 32) * Zoom;

        /// <summary>Changes the zoom (0.25–4) and resizes the canvas to the map.</summary>
        public void SetZoom(float zoom)
        {
            Zoom = Mathf.Clamp(zoom, 0.25f, 4f);
            Refresh();
        }

        public void SetRenderer(string id)
        {
            TileRendererRegistry.Instance.Resolve(id);
            if (RendererId != id) { ReleasePreview(); projection = null; }
            RendererId = id;
            Refresh();
        }

        public override void _EnterTree()
        {
            TileRendererRegistry.Instance.Removing += OnRendererRemoving;
            ModEventBus.Subscribe<ModSetReloadedEvent>(OnModsReloaded);
        }
        public override void _ExitTree()
        {
            TileRendererRegistry.Instance.Removing -= OnRendererRemoving;
            ModEventBus.Unsubscribe<ModSetReloadedEvent>(OnModsReloaded);
            ReleasePreview(); projection = null; builder?.Dispose(); textures.Dispose();
        }

        private void OnRendererRemoving(string id)
        {
            if (RendererId != id) return;
            ReleasePreview(); projection = null;
        }
        private void OnModsReloaded(ModSetReloadedEvent evt)
        {
            if (!evt.Applied || Document == null) return;
            ReleasePreview(); builder?.Dispose(); textures.Dispose();
            Tileset = RpgTilesetFactory.Instance.ForMap(Document.Map);
            Refresh();
        }

        private void ReleasePreview()
        {
            preview?.Dispose();
            preview = null;
            previewScene?.Release();
            previewScene = null;
        }

        /// <summary>The same renderer as runtime, with a static frame and authoring overlays.</summary>
        public void Refresh()
        {
            if (Document == null || Tileset == null) { ReleasePreview(); CustomMinimumSize = Vector2.Zero; QueueRedraw(); return; }
            var factory = TileRendererRegistry.Instance.Resolve(RendererId);
            projection = factory.Projection(Tileset.TileSize);
            builder ??= new RpgRenderSceneBuilder(textures);
            var previous = previewScene;
            previewScene = builder.Build(Document.Map, Document.Grid, Tileset);
            previewScene.Zoom = Zoom;
            foreach (var visual in previewScene.Visuals)
                visual.Tint = new Color(1, 1, 1, ActiveLayer <= RpgEditLayer.Front && visual.Layer != (int)ActiveLayer ? 0.45f : 1f);
            if (preview == null)
            {
                preview = factory.Create();
                AddChild(preview.Root);
                MoveChild(preview.Root, 0);
                preview.Mount(previewScene);
            }
            else preview.Apply(new TileSceneChanges { BaseRevision = previous.Revision, Scene = previewScene });
            previous?.Release();
            CustomMinimumSize = preview.ContentBounds.Size * Zoom;
            preview.Resize(CustomMinimumSize);
            var frame = new TileFrame { Ambient = Colors.White, Focus = new Vector2(Document.Grid.Width, Document.Grid.Height) / 2 };
            foreach (var mapEvent in Document.LockedEvents) AddPreviewEvent(frame, builder, mapEvent, true);
            foreach (var mapEvent in Document.Events) AddPreviewEvent(frame, builder, mapEvent, false);
            preview.Present(frame, 0);
            overlay.Position = preview.Root.Position;
            overlay.Scale = preview.Root.Scale;
            QueueRedraw();
        }

        private void AddPreviewEvent(TileFrame frame, RpgRenderSceneBuilder builder, RpgMapEvent mapEvent, bool locked)
        {
            frame.Actors.Add(new TileActorPose { Id = (locked ? "locked/" : "own/") + mapEvent.Id,
                Position = new Vector2(mapEvent.X, mapEvent.Y), Visible = true, Look = builder.EventLook(mapEvent, Tileset),
                Facing = (int)mapEvent.Dir, Charset = !string.IsNullOrEmpty(mapEvent.Sprite), Tint = locked ? LockedModulate : Colors.White });
        }

        public new void QueueRedraw() { base.QueueRedraw(); overlay?.QueueRedraw(); }

        public override void _Draw() => DrawRect(new Rect2(Vector2.Zero, Size), UiTokens.Surface0);

        /// <summary>Mouse and automation coordinates use the real presentation transform, including negative iso X.</summary>
        public Vector2 ProjectCell(Vector2 cell) => preview?.ProjectToView(new Vector3(cell.X, cell.Y, 0)) ?? cell * CellSize;

        public void DrawOverlays(CanvasItem target)
        {
            if (Document == null || projection == null) return;
            var grid = Document.Grid;
            var unit = projection.Unit;
            if (ShowGrid)
            {
                for (var x = 0; x <= grid.Width; x++) target.DrawLine(P(x, 0), P(x, grid.Height), GridColor);
                for (var y = 0; y <= grid.Height; y++) target.DrawLine(P(0, y), P(grid.Width, y), GridColor);
            }
            if (ActiveLayer == RpgEditLayer.Collision)
                for (var y = 0; y < grid.Height; y++)
                for (var x = 0; x < grid.Width; x++)
                {
                    var code = grid.GetCollision(x, y);
                    if (code == RpgCollision.Block || code == RpgCollision.Open)
                        target.DrawColoredPolygon(Polygon(x, y, 1, 1), code == RpgCollision.Open ? OpenColor : BlockColor);
                    else if (!RpgPassability.IsWalkable(grid, Tileset, x, y))
                    {
                        target.DrawColoredPolygon(Polygon(x, y, 1, 1), TileSolidColor);
                        Outline(target, Polygon(x, y, 1, 1), new Color(1f, 0.55f, 0.15f, 0.9f), true);
                    }
                }
            foreach (var mapEvent in Document.LockedEvents) DrawEventMarker(target, mapEvent, true);
            foreach (var mapEvent in Document.Events) DrawEventMarker(target, mapEvent, false);
            if (Document.Map.Spawn is { } spawn)
            {
                var center = P(spawn.X + 0.5f, spawn.Y + 0.5f);
                target.DrawCircle(center, unit * 0.18f, UiTokens.Ok);
                target.DrawArc(center, unit * 0.18f, 0, Mathf.Tau, 24, Colors.Black);
            }
            if (!hover.HasValue || !grid.InBounds(hover.Value.X, hover.Value.Y)) return;
            var cell = hover.Value;
            var stamp = SelectedStamp ?? RpgTileStamp.Single(SelectedTile);
            var fragments = RpgGroupFragments.Of(Tileset);
            if (!Placing && Tool == RpgEditTool.Pencil && ActiveLayer <= RpgEditLayer.Front && Atlas != null)
                for (var y = 0; y < stamp.Height; y++)
                for (var x = 0; x < stamp.Width; x++)
                {
                    var id = stamp.TileAt(x, y);
                    if (id < 0) continue;
                    var info = Tileset.TileOf(id);
                    var texture = textures.Tileset(info?.SourceImage ?? Tileset.Image);
                    var source = RpgRenderSceneBuilder.TileRegion(Tileset, id);
                    if (info?.Surface is { } patch)
                    {
                        texture = textures.Tileset(Tileset.SurfaceImage);
                        source = new Rect2(patch * Tileset.SurfaceSize + (cell.X + x) * Tileset.TileSize % Tileset.SurfaceSize,
                            (cell.Y + y) * Tileset.TileSize % Tileset.SurfaceSize, Tileset.TileSize, Tileset.TileSize);
                    }
                    else if (!string.IsNullOrEmpty(info?.Autotile))
                    {
                        texture = textures.Autotile(info.Autotile, Tileset.TileSize, cell.X + x, cell.Y + y);
                        source = RpgAutotiles.PreviewRect(Tileset.TileSize);
                    }
                    var ghost = new TileVisual { Texture = texture, Source = source, Bounds = new Rect2(cell.X + x, cell.Y + y, 1, 1) };
                    // Objects stand up exactly as the scene builder will draw them; only ground lies flat.
                    if (info?.Surface == null && string.IsNullOrEmpty(info?.Autotile))
                    {
                        if (fragments.TryGetValue(id, out var part))
                            (ghost.Upright, ghost.Anchor) = (true, new Vector2(cell.X + x - part.Cell.X + part.Group.AnchorX, cell.Y + y - part.Cell.Y + part.Group.AnchorY));
                        else if (ActiveLayer != RpgEditLayer.Back)
                            (ghost.Upright, ghost.Anchor) = (true, new Vector2(cell.X + x + 0.5f, cell.Y + y + 1));
                    }
                    TileDrawing.Draw(target, ghost, projection, new Color(1, 1, 1, 0.6f));
                    if (ActiveLayer != RpgEditLayer.Front && Tileset.IsSolid(id))
                        Outline(target, Polygon(cell.X + x, cell.Y + y, 1, 1), new Color(1f, 0.55f, 0.15f, 0.9f), true);
                }
            Outline(target, Polygon(cell.X, cell.Y, 1, 1), UiTokens.AccentBright, false);
        }

        private void DrawEventMarker(CanvasItem target, RpgMapEvent mapEvent, bool locked)
        {
            var polygon = Polygon(mapEvent.X, mapEvent.Y, 1, 1);
            if (string.IsNullOrEmpty(mapEvent.Sprite) && mapEvent.Tile < 0 && string.IsNullOrEmpty(mapEvent.Icon))
                target.DrawColoredPolygon(polygon, MarkerColor);
            if (ActiveLayer != RpgEditLayer.Events) return;
            Outline(target, polygon, mapEvent == SelectedEvent ? UiTokens.AccentBright : UiTokens.Alpha(UiTokens.White, 0.6f), locked);
        }
        private static void Outline(CanvasItem target, Vector2[] polygon, Color color, bool dashed)
        {
            for (var i = 0; i < polygon.Length; i++)
                if (dashed) target.DrawDashedLine(polygon[i], polygon[(i + 1) % polygon.Length], color, 1, 4);
                else target.DrawLine(polygon[i], polygon[(i + 1) % polygon.Length], color, 1.5f);
        }
        private Vector2 P(float x, float y) => TileDrawing.Project(projection, new Vector2(x, y));
        private Vector2[] Polygon(int x, int y, int width, int height)
            => TileDrawing.CellPolygon(projection, new Rect2(x, y, width, height));
        private Vector2I CellAt(Vector2 position)
            => preview != null && preview.TryPickGround(position, out var cell) ? cell : new Vector2I(-1, -1);

        private void OnGuiInput(InputEvent @event)
        {
            if (Document == null)
                return;

            if (@event is InputEventMouseMotion motion)
            {
                if (panning)
                {
                    Pan(motion);
                    AcceptEvent();
                    return;
                }
                hover = CellAt(motion.Position);
                if (stroking && (motion.ButtonMask & (MouseButtonMask.Left | MouseButtonMask.Right)) != 0)
                    Apply(hover.Value, (motion.ButtonMask & MouseButtonMask.Right) != 0 || Tool == RpgEditTool.Eraser, motion.Position);
                else if (dragged != null && (motion.ButtonMask & MouseButtonMask.Left) != 0)
                    Drag(hover.Value);
                QueueRedraw();
                return;
            }

            if (@event is not InputEventMouseButton button)
                return;

            if (button.Pressed && button.CtrlPressed && (button.ButtonIndex == MouseButton.WheelUp || button.ButtonIndex == MouseButton.WheelDown))
            {
                SetZoom(Zoom * (button.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f));
                AcceptEvent();
                return;
            }
            // Not accepted: the event bubbles up and the ScrollContainer scrolls.
            if (button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight)
                return;
            if (button.ButtonIndex == MouseButton.Middle)
            {
                StartOrEndPan(button);
                AcceptEvent();
                return;
            }

            var cell = CellAt(button.Position);
            if (!button.Pressed)
            {
                if (stroking || dragMoved)
                    Changed?.Invoke();
                stroking = false;
                dragged = null;
                dragMoved = false;
                return;
            }

            if (Placing && (button.ButtonIndex == MouseButton.Left || button.ButtonIndex == MouseButton.Right))
            {
                Placing = false;
                var chosen = button.ButtonIndex == MouseButton.Left && Document.Grid.InBounds(cell.X, cell.Y);
                CellChosen?.Invoke(chosen ? cell : null);
                AcceptEvent();
                return;
            }

            if (ActiveLayer == RpgEditLayer.Events)
                PressOnEvents(button, cell);
            else if (button.ButtonIndex == MouseButton.Left || button.ButtonIndex == MouseButton.Right)
                PressOnCells(button, cell);
            AcceptEvent();
        }

        private void PressOnCells(InputEventMouseButton button, Vector2I cell)
        {
            if (button.AltPressed && button.ButtonIndex == MouseButton.Left && ActiveLayer != RpgEditLayer.Collision)
            {
                var index = Document.Grid.GetTile((RpgLayer)ActiveLayer, cell.X, cell.Y);
                if (index >= 0)
                    TilePicked?.Invoke(index);
                return;
            }

            var erase = button.ButtonIndex == MouseButton.Right || Tool == RpgEditTool.Eraser;
            singleCellStroke = button.ShiftPressed;
            EditStarting?.Invoke();
            if (Tool == RpgEditTool.Fill && !erase)
            {
                Document.Fill(ActiveLayer, cell.X, cell.Y, SelectedTile, SelectedCollision);
                Changed?.Invoke();
                QueueRedraw();
                return;
            }
            stroking = true;
            Apply(cell, erase, button.Position);
        }

        private void PressOnEvents(InputEventMouseButton button, Vector2I cell)
        {
            if (button.ButtonIndex == MouseButton.Right)
            {
                Select(null);
                return;
            }
            if (button.ButtonIndex != MouseButton.Left || !Document.Grid.InBounds(cell.X, cell.Y))
                return;

            var found = Document.EventsAt(cell.X, cell.Y, true);
            if (found.Count == 0 && button.DoubleClick)
            {
                EditStarting?.Invoke();
                Select(Document.AddEvent(cell.X, cell.Y));
                Changed?.Invoke();
                return;
            }

            // Repeated clicks on a stack of events cycle through it.
            var index = SelectedEvent != null ? found.IndexOf(SelectedEvent) : -1;
            Select(found.Count == 0 ? null : found[(index + 1) % found.Count]);
            dragged = Document.Events.Contains(SelectedEvent) ? SelectedEvent : null;
            dragMoved = false;
        }

        private void Apply(Vector2I cell, bool erase, Vector2 pointer)
        {
            var tile = erase ? -1 : SelectedTile;
            var collision = erase ? RpgCollision.Auto : SelectedCollision;
            if (erase && !singleCellStroke && ActiveLayer <= RpgEditLayer.Front)
                cell = ObjectCellAt(pointer) ?? cell;
            var changed = erase && !singleCellStroke && ActiveLayer <= RpgEditLayer.Front
                ? Document.EraseObject(ActiveLayer, cell.X, cell.Y, Tileset) > 0
                : !erase && !singleCellStroke && ActiveLayer <= RpgEditLayer.Front && SelectedStamp != null
                ? Document.Stamp(ActiveLayer, cell.X, cell.Y, SelectedStamp, singleCellStroke ? null : Tileset) > 0
                : Document.Paint(ActiveLayer, cell.X, cell.Y, tile, collision);
            if (changed)
            {
                Refresh();
            }
        }

        /// <summary>Picks an upright object or its fragment at its drawn position, rather than the ground behind it.</summary>
        private Vector2I? ObjectCellAt(Vector2 position)
        {
            if (preview == null || previewScene == null || Tileset == null)
                return null;
            var point = preview.Root.Transform.AffineInverse() * position;
            for (var v = previewScene.Visuals.Count - 1; v >= 0; v--)
            {
                var visual = previewScene.Visuals[v];
                if (!string.IsNullOrEmpty(visual.Only) && visual.Only != RendererId)
                    continue;
                var group = Tileset.VisualGroups.Find(g => g.Id == visual.Id);
                if (group == null)
                    continue;
                var uv = projection.Place(visual).Transform.AffineInverse() * point;
                if (uv.X < 0 || uv.Y < 0 || uv.X >= 1 || uv.Y >= 1)
                    continue;
                var pixel = visual.Source.Position + uv * visual.Source.Size;
                if (visual.Texture != null)
                {
                    using var image = visual.Texture.GetImage();
                    if (image != null && image.GetPixel((int)pixel.X, (int)pixel.Y).A < 0.05f)
                        continue;
                }
                var origin = visual.WorldQuad != null ? visual.Bounds.Position
                    : visual.Anchor - new Vector2(group.AnchorX, group.AnchorY);
                for (var i = 0; i < group.Pattern.Length; i++)
                    if (RpgRenderSceneBuilder.TileRegion(Tileset, group.Pattern[i]).HasPoint(pixel))
                    {
                        var cell = new Vector2I(Mathf.RoundToInt(origin.X) + i % group.Width,
                            Mathf.RoundToInt(origin.Y) + i / group.Width);
                        if (Document.Grid.GetTile((RpgLayer)ActiveLayer, cell.X, cell.Y) == group.Pattern[i])
                            return cell;
                    }
            }
            return null;
        }

        private void Drag(Vector2I cell)
        {
            if (!Document.Grid.InBounds(cell.X, cell.Y) || (dragged.X == cell.X && dragged.Y == cell.Y))
                return;
            if (!dragMoved)
            {
                EditStarting?.Invoke();
                dragMoved = true;
            }
            dragged.X = cell.X;
            dragged.Y = cell.Y;
            Refresh();
            EventSelected?.Invoke(dragged);
        }

        private void Select(RpgMapEvent mapEvent)
        {
            SelectedEvent = mapEvent;
            EventSelected?.Invoke(mapEvent);
            QueueRedraw();
        }

        /// <summary>
        /// Middle button down starts panning the parent ScrollContainer, up ends
        /// it. The scroll offset is anchored to the press point rather than
        /// accumulated from deltas, so hitting a scroll edge builds no drift.
        /// </summary>
        private void StartOrEndPan(InputEventMouseButton button)
        {
            var scroll = GetParentOrNull<ScrollContainer>();
            panning = button.Pressed && scroll != null;
            MouseDefaultCursorShape = panning ? CursorShape.Drag : CursorShape.Arrow;
            if (!panning)
                return;
            panStartMouse = button.GlobalPosition;
            panStartScroll = new Vector2(scroll.ScrollHorizontal, scroll.ScrollVertical);
        }

        private void Pan(InputEventMouseMotion motion)
        {
            var scroll = GetParentOrNull<ScrollContainer>();
            if (scroll == null || (motion.ButtonMask & MouseButtonMask.Middle) == 0)
            {
                panning = false;
                MouseDefaultCursorShape = CursorShape.Arrow;
                return;
            }
            var delta = motion.GlobalPosition - panStartMouse;
            scroll.ScrollHorizontal = Mathf.RoundToInt(panStartScroll.X - delta.X);
            scroll.ScrollVertical = Mathf.RoundToInt(panStartScroll.Y - delta.Y);
        }

        private void OnMouseExited()
        {
            hover = null;
            QueueRedraw();
        }
    }
}
