using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.UI.Theming;
using Cthangover.Mods.Rpg;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Brush picker of the editor's left column. For a tile layer it shows the
    /// tileset atlas at one and a half times the tile size with the selected tile framed and the
    /// solid tiles marked by a red corner; a click picks a tile, the tooltip
    /// names it (index, tag, solid). The tileset's autotiles follow in a row under the
    /// atlas, each shown by its block's preview tile (<see cref="RpgAutotiles"/>). For the
    /// collision layer it shows the three collision codes instead.
    /// </summary>
    public partial class RpgTilePalette : Control
    {
        // Keep wide architectural blocks fully visible in the standard sidebar.
        private float Scale => tileset == null ? 1.5f : Math.Min(1.5f, 384f / Math.Max(1, tileset.Columns * tileset.TileSize));
        private const float CodeHeight = 44f;
        private const float AutotileGap = 8f;

        private static readonly char[] Codes = { RpgCollision.Block, RpgCollision.Open, RpgCollision.Auto };

        private readonly RpgTextures textures = new();
        private readonly List<RpgTileInfo> autotiles = new();
        private RpgTileset tileset;
        private Texture2D atlas;
        private bool collisionMode;
        private Vector2I? selectionStart;
        private Rect2I? selection;
        private bool dragged;

        /// <summary>The current rectangular brush in logical map ids.</summary>
        public RpgTileStamp SelectedStamp { get; private set; } = RpgTileStamp.Single(0);

        /// <summary>A dragged rectangle changed the whole brush.</summary>
        public event Action<RpgTileStamp> StampSelected;

        /// <summary>Selected atlas index.</summary>
        public int SelectedTile { get; private set; }

        /// <summary>Selected collision code.</summary>
        public char SelectedCollision { get; private set; } = RpgCollision.Block;

        /// <summary>A tile was picked.</summary>
        public event Action<int> TileSelected;

        /// <summary>A collision code was picked.</summary>
        public event Action<char> CollisionSelected;

        /// <summary>Creates the palette.</summary>
        public RpgTilePalette()
        {
            TextureFilter = TextureFilterEnum.Nearest;
            MouseFilter = MouseFilterEnum.Stop;
            GuiInput += OnGuiInput;
        }

        /// <summary>Shows a tileset (null: nothing to pick).</summary>
        public void SetTileset(RpgTileset value, Texture2D image)
        {
            tileset = value;
            atlas = image;
            selection = null;
            selectionStart = null;
            autotiles.Clear();
            if (value != null)
                autotiles.AddRange(value.Tiles.Where(t => !string.IsNullOrEmpty(t.Autotile) || t.Surface != null));
            Relayout();
        }

        /// <summary>Switches between the tile atlas and the collision codes.</summary>
        public void SetCollisionMode(bool value)
        {
            selectionStart = null;
            collisionMode = value;
            Relayout();
        }

        /// <summary>Selects a tile from outside (the canvas' pipette).</summary>
        public void Select(int index)
        {
            SelectedTile = index;
            SelectedStamp = RpgTileStamp.Single(index);
            selection = null;
            selectionStart = null;
            QueueRedraw();
        }

        /// <summary>Draws the atlas or the codes.</summary>
        public override void _Draw()
        {
            if (collisionMode)
            {
                for (var i = 0; i < Codes.Length; i++)
                    DrawCode(i);
                return;
            }
            if (atlas == null || tileset == null)
                return;

            var cell = tileset.TileSize * Scale;
            DrawTextureRect(atlas, new Rect2(Vector2.Zero, atlas.GetSize() * Scale), false);
            foreach (var tile in tileset.Tiles.Where(t => t.Solid && string.IsNullOrEmpty(t.Autotile) && t.Surface == null))
                DrawSolidMark((Vector2)RpgTileSetBuilder.AtlasCoords(tileset, tile.Index) * cell, cell);
            for (var i = 0; i < autotiles.Count; i++)
            {
                if (autotiles[i].Surface is { } patch && textures.Tileset(tileset.SurfaceImage) is { } surface)
                    DrawTextureRectRegion(surface, new Rect2(Slot(i), new Vector2(cell, cell)),
                        new Rect2(patch * tileset.SurfaceSize, 0, tileset.TileSize, tileset.TileSize));
                else if (textures.Tileset(autotiles[i].Autotile) is { } block)
                    DrawTextureRectRegion(block, new Rect2(Slot(i), new Vector2(cell, cell)), RpgAutotiles.PreviewRect(tileset.TileSize));
                if (autotiles[i].Solid)
                    DrawSolidMark(Slot(i), cell);
            }
            var slot = autotiles.FindIndex(t => t.Index == SelectedTile);
            var selected = slot >= 0 ? Slot(slot) : (Vector2)RpgTileSetBuilder.AtlasCoords(tileset, SelectedTile) * cell;
            var highlight = selection is { } rectangle
                ? new Rect2((Vector2)rectangle.Position * cell, (Vector2)rectangle.Size * cell)
                : new Rect2(selected, new Vector2(cell, cell));
            DrawRect(highlight, UiTokens.AccentBright, false, 3f);
        }

        private void DrawSolidMark(Vector2 position, float cell)
            => DrawColoredPolygon(new[] { position, position + new Vector2(cell * 0.25f, 0f), position + new Vector2(0f, cell * 0.25f) }, new Color(0.9f, 0.2f, 0.2f, 0.9f));

        private void DrawCode(int i)
        {
            var rect = new Rect2(0f, i * (CodeHeight + 6f), Size.X, CodeHeight);
            var color = Codes[i] == RpgCollision.Block ? new Color(0.9f, 0.2f, 0.2f, 0.6f)
                : Codes[i] == RpgCollision.Open ? new Color(0.2f, 0.85f, 0.35f, 0.5f) : UiTokens.Surface2;
            DrawRect(rect, color);
            if (Codes[i] == SelectedCollision)
                DrawRect(rect, UiTokens.AccentBright, false, 3f);
            var font = GetThemeDefaultFont();
            DrawString(font, rect.Position + new Vector2(10f, CodeHeight / 2f + 6f), CodeLabel(Codes[i]), HorizontalAlignment.Left, -1, UiTokens.FontBody, UiTokens.White);
        }

        private static string CodeLabel(char code) => TranslationServer.Translate(code == RpgCollision.Block
            ? "tools/rpg_editor/collision_block"
            : code == RpgCollision.Open ? "tools/rpg_editor/collision_open" : "tools/rpg_editor/collision_auto");

        private void Relayout()
        {
            if (collisionMode)
                CustomMinimumSize = new Vector2(200f, Codes.Length * (CodeHeight + 6f));
            else
                CustomMinimumSize = atlas != null && tileset != null ? new Vector2(atlas.GetWidth() * Scale, AutotileTop + AutotileRows * tileset.TileSize * Scale) : Vector2.Zero;
            TooltipText = string.Empty;
            QueueRedraw();
        }

        private void OnGuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseMotion motion && !collisionMode && tileset != null)
            {
                if (selectionStart.HasValue && (dragged || AtlasCellAt(motion.Position) != selectionStart.Value))
                {
                    dragged = true;
                    SelectRectangle(motion.Position);
                }
                TooltipText = Describe(IndexAt(motion.Position));
                return;
            }
            if (@event is InputEventMouseButton { Pressed: false, ButtonIndex: MouseButton.Left })
            {
                selectionStart = null;
                return;
            }
            if (@event is not InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } button)
                return;

            if (collisionMode)
            {
                var i = (int)(button.Position.Y / (CodeHeight + 6f));
                if (i >= 0 && i < Codes.Length)
                {
                    SelectedCollision = Codes[i];
                    CollisionSelected?.Invoke(SelectedCollision);
                }
            }
            else if (tileset != null && atlas != null)
            {
                var index = IndexAt(button.Position);
                if (index >= 0 && (index < RpgTileSetBuilder.TileCount(tileset, atlas) || tileset.TileOf(index) != null))
                {
                    SelectedTile = index;
                    SelectedStamp = RpgTileStamp.Single(index);
                    selection = null;
                    selectionStart = button.Position.Y < atlas.GetHeight() * Scale
                        ? AtlasCellAt(button.Position) : null;
                    dragged = false;
                    SelectGroupOf(index);
                    TileSelected?.Invoke(index);
                    StampSelected?.Invoke(SelectedStamp);
                }
            }
            QueueRedraw();
            AcceptEvent();
        }

        /// <summary>
        /// A click on any cell of a visual group picks the whole object, so it is stamped complete: a partial
        /// pattern does not match the group and the isometric view falls back to separate per-cell sprites.
        /// </summary>
        private void SelectGroupOf(int index)
        {
            var group = tileset.VisualGroups?.Find(g => g.Pattern != null && Array.IndexOf(g.Pattern, index) >= 0);
            if (group == null || RpgVisualGroupValidator.Error(group) != null)
                return;
            SelectedStamp = new RpgTileStamp(group.Width, group.Height, group.Pattern);
            SelectedTile = group.Pattern[0];
            var origin = RpgTileSetBuilder.AtlasCoords(tileset, group.Pattern[0]);
            var packed = true;
            for (var y = 0; y < group.Height; y++)
                for (var x = 0; x < group.Width; x++)
                    packed &= RpgTileSetBuilder.AtlasCoords(tileset, group.Pattern[y * group.Width + x]) == origin + new Vector2I(x, y);
            selection = packed ? new Rect2I(origin, group.Width, group.Height) : null;
        }

        private Vector2I AtlasCellAt(Vector2 position)
        {
            var cell = tileset.TileSize * Scale;
            return new Vector2I(Math.Clamp((int)(position.X / cell), 0, tileset.Columns - 1),
                Math.Clamp((int)(position.Y / cell), 0, atlas.GetHeight() / tileset.TileSize - 1));
        }

        private void SelectRectangle(Vector2 position)
        {
            var end = AtlasCellAt(position);
            var start = selectionStart.Value;
            var left = Math.Min(start.X, end.X);
            var top = Math.Min(start.Y, end.Y);
            selection = new Rect2I(left, top, Math.Abs(start.X - end.X) + 1, Math.Abs(start.Y - end.Y) + 1);
            SelectedStamp = RpgTileStamp.FromAtlas(tileset, selection.Value);
            SelectedTile = SelectedStamp.TileAt(0, 0);
            TileSelected?.Invoke(SelectedTile);
            StampSelected?.Invoke(SelectedStamp);
            QueueRedraw();
        }

        private float AutotileTop => atlas == null ? 0f : atlas.GetHeight() * Scale + (autotiles.Count > 0 ? AutotileGap : 0f);

        private int AutotileRows => (autotiles.Count + Math.Max(1, tileset.Columns) - 1) / Math.Max(1, tileset.Columns);

        private Vector2 Slot(int i)
        {
            var cell = tileset.TileSize * Scale;
            var columns = Math.Max(1, tileset.Columns);
            return new Vector2(i % columns * cell, AutotileTop + i / columns * cell);
        }

        private int IndexAt(Vector2 position)
        {
            var cell = tileset.TileSize * Scale;
            if (autotiles.Count > 0 && position.Y >= AutotileTop)
            {
                var slot = (int)((position.Y - AutotileTop) / cell) * Math.Max(1, tileset.Columns) + (int)(position.X / cell);
                return slot >= 0 && slot < autotiles.Count && position.X < tileset.Columns * cell ? autotiles[slot].Index : -1;
            }
            var column = (int)(position.X / cell);
            var row = (int)(position.Y / cell);
            if (column < 0 || column >= tileset.Columns || row < 0 || position.Y >= atlas.GetHeight() * Scale)
                return -1;
            var physical = row * tileset.Columns + column;
            return tileset.Tiles.FirstOrDefault(t => t.AtlasIndex == physical)?.Index
                ?? (tileset.Tiles.Any(t => t.AtlasIndex != null) ? -1 : physical);
        }

        private string Describe(int index)
        {
            var info = tileset.Tiles.FirstOrDefault(t => t.Index == index);
            var solid = info?.Solid == true ? " · " + TranslationServer.Translate("tools/rpg_editor/solid_tile") : string.Empty;
            var auto = string.IsNullOrEmpty(info?.Autotile) ? string.Empty : " · " + TranslationServer.Translate("tools/rpg_editor/autotile");
            return $"#{index} {info?.Tag}{solid}{auto}";
        }
    }
}
