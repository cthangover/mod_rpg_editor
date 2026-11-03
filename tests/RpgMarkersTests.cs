namespace Cthangover.Tools.RpgEditor.Tests
{
    using System.Collections.Generic;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;

    /// <summary>
    /// The picture-less sprites (plan 14.G18): a sprite with no picture part is a marker; a marker with a teleport is a door.
    /// </summary>
    public class RpgMarkersTests
    {
        [ModTest("view/picture_less_sprites_are_markers")]
        static void Markers(ModTestContext ctx)
        {
            var empty = new RpgObject { ID = "trigger", Form = RpgObjectForm.Sprite, Parts = new List<RpgObjectPart>() };
            var none = new RpgObject { ID = "trigger2", Form = RpgObjectForm.Sprite };
            var picture = new RpgObject
            {
                ID = "chest",
                Form = RpgObjectForm.Sprite,
                Parts = new List<RpgObjectPart> { new RpgObjectPart { Image = "village_pack/chest.png" } },
            };
            var actor = new RpgObject { ID = "npc", Form = RpgObjectForm.Actor };
            ModAssert.True(RpgMarkers.IsPictureless(empty), "a sprite with an empty Parts list is a marker");
            ModAssert.True(RpgMarkers.IsPictureless(none), "and so is one without a Parts key");
            ModAssert.False(RpgMarkers.IsPictureless(picture), "a sprite with a picture part is not");
            ModAssert.False(RpgMarkers.IsPictureless(actor), "an actor is not a marker");
            ModAssert.False(RpgMarkers.IsPictureless(null), "nothing is not a marker");
        }

        [ModTest("view/marker_with_teleport_is_a_door")]
        static void Doors(ModTestContext ctx)
        {
            var door = new RpgObject
            {
                ID = "trigger",
                Form = RpgObjectForm.Sprite,
                Parts = new List<RpgObjectPart>(),
                Interact = new RpgInteract { Teleport = new RpgTeleport { Map = "here", X = 1, Y = 1 } },
            };
            var plain = new RpgObject { ID = "marker", Form = RpgObjectForm.Sprite, Parts = new List<RpgObjectPart>() };
            ModAssert.True(RpgMarkers.IsDoor(door), "a marker that leads to a cell is a door");
            ModAssert.False(RpgMarkers.IsDoor(plain), "a marker without a teleport is a generic marker");
        }
    }
}
