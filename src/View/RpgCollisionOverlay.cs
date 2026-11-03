namespace Cthangover.Tools.RpgEditor
{
    using System.Collections.Generic;
    using Cthangover.Mods.Rpg;
    using Godot;

    /// <summary>Effective blocked cells, including object footprints; a pending stroke previews without writing or creating undo steps.</summary>
    public static class RpgCollisionOverlay
    {
        public static readonly Color BlockedFill = new(1f, 0.1f, 0.08f, 0.45f);

        /// <summary>Rebuilds red cell marks using the same walk grid that validates the editor's placements.</summary>
        public static void AddMarks(RpgEditorScene scene, List<CanvasRect> marks, IReadOnlyCollection<Vector2I> stroke, char code)
        {
            var pending = stroke == null ? null : new HashSet<Vector2I>(stroke);
            for (var y = 0; y < scene.Map.Height; y++)
                for (var x = 0; x < scene.Map.Width; x++)
                {
                    var blocked = !scene.Walk.IsWalkable(x, y);
                    if (pending?.Contains(new Vector2I(x, y)) == true)
                    {
                        blocked = code == RpgCollision.Block;
                        if (code == RpgCollision.Auto)
                        {
                            var ground = scene.Cells.Ground(x, y);
                            var structure = scene.Cells.Structure(x, y);
                            blocked = ground is { Hidden: false, IsSolid: true } || structure is { Hidden: false, IsSolid: true };
                            foreach (var placement in scene.Statics)
                                if (placement.Object.BlocksAt(x - placement.X, y - placement.Y)) { blocked = true; break; }
                            foreach (var placement in scene.Actors)
                                if (placement.Object.BlocksAt(x - placement.X, y - placement.Y)) { blocked = true; break; }
                        }
                    }
                    if (blocked) marks.Add(new CanvasRect(x, y, 1, 1, BlockedFill, true));
                }
        }
    }
}
