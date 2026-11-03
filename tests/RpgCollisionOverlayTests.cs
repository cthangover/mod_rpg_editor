namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using Cthangover.Core.Mods.Testing;
    using Godot;

    /// <summary>The preview follows terrain/object collision and a pending stroke before mouse release.</summary>
    public class RpgCollisionOverlayTests
    {
        [ModTest("collision_overlay/terrain_pending_open_block_and_auto")]
        static void Preview(ModTestContext context)
        {
            var doc = RpgEditorFixture.OwnMap();
            var scene = RpgEditorScene.From(doc, RpgEditorFixture.ObjectLookup());
            var marks = new List<CanvasRect>();
            RpgCollisionOverlay.AddMarks(scene, marks, null, '#');
            ModAssert.True(Has(marks, 0, 0) && Has(marks, 2, 1), "both solid fence and explicit block are red");
            ModAssert.False(Has(marks, 3, 1), "forced open is clear");
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, new[] { new Vector2I(0, 0), new Vector2I(2, 1) }, 'o');
            ModAssert.False(Has(marks, 0, 0) || Has(marks, 2, 1), "opening removes red before commit, including terrain");
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, new[] { new Vector2I(3, 1) }, '#');
            ModAssert.True(Has(marks, 3, 1), "blocking adds red before commit");
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, new[] { new Vector2I(0, 0), new Vector2I(2, 1) }, '.');
            ModAssert.True(Has(marks, 0, 0) && !Has(marks, 2, 1), "auto restores the object's collision");
            ModAssert.Equal('#', doc.CodeAt(2, 1), "preview does not mutate document");
            ModAssert.False(doc.CanUndo, "preview creates no undo steps");
        }
        [ModTest("collision_overlay/actor_obeys_map_overrides_and_auto_preview")]
        static void Actor(ModTestContext context)
        {
            var doc = RpgEditorFixture.OwnMap();
            doc.AddPlacement("npc", RpgEditorFixture.Merchant, 0, 2);
            var scene = RpgEditorScene.From(doc, RpgEditorFixture.ObjectLookup());
            var marks = new List<CanvasRect>();
            RpgCollisionOverlay.AddMarks(scene, marks, null, '.');
            ModAssert.True(Has(marks, 0, 2), "actor's normal collision is visible");
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, new[] { new Vector2I(0, 2) }, 'o');
            ModAssert.False(Has(marks, 0, 2), "forced-open preview overrides actor");
            doc.PaintCollision(new[] { (0, 2) }, 'o');
            scene = RpgEditorScene.From(doc, RpgEditorFixture.ObjectLookup());
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, null, '.');
            ModAssert.False(Has(marks, 0, 2), "saved o stays clear under actor");
            marks.Clear();
            RpgCollisionOverlay.AddMarks(scene, marks, new[] { new Vector2I(0, 2) }, '.');
            ModAssert.True(Has(marks, 0, 2), "auto preview restores actor collision");
        }

        private static bool Has(List<CanvasRect> marks, int x, int y)
        {
            foreach (var mark in marks) if (mark.X == x && mark.Y == y && mark.Fill && mark.Color == RpgCollisionOverlay.BlockedFill) return true;
            return false;
        }
    }
}
