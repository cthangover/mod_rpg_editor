namespace Cthangover.Tools.RpgEditor.Tests
{
    using System;
    using Cthangover.Core.Mods.Testing;
    using Godot;

    public sealed class RpgRouteTraceTests
    {
        [ModTest("routes/trace_pauses_and_hidden_reset")]
        static void Trace(ModTestContext ctx)
        {
            var origin = new Vector2I(4,3);
            var trace = new RpgRouteTrace(origin, new[] { "right", "wait:4", "face_up", "hide", "reset", "show", "down" });
            ModAssert.Equal(2, trace.Segments.Count, "reset and facing changes are not walking segments");
            ModAssert.True(trace.Segments[1].From == origin && trace.End == new Vector2I(4,4), "reappearance starts a new path at the original cell");
            ModAssert.True(trace.Stops[0].Cell == new Vector2I(5,3) && trace.Stops[0].Label == "4s", "pause marker retains position and duration");
        }

        [ModTest("routes/drawn_waypoints_avoid_walls_and_refuse_unreachable_cells")]
        static void Sketch(ModTestContext ctx)
        {
            Func<Vector2I,bool> open = c => c.X >= 0 && c.X < 5 && c.Y >= 0 && c.Y < 4 && !(c.X == 2 && c.Y < 3);
            var from = new Vector2I(0,1); var to = new Vector2I(4,1);
            var steps = RpgRouteSketch.Connect(from,to,open);
            ModAssert.True(steps != null && steps.Count == 8, "shortest cardinal connection goes around the wall");
            var trace = new RpgRouteTrace(from,steps);
            foreach (var edge in trace.Segments) ModAssert.True(open(edge.To), "every painted segment is walkable");
            ModAssert.True(trace.End == to, "the draft reaches the clicked cell");
            ModAssert.Null(RpgRouteSketch.Connect(from,new Vector2I(2,1),open), "blocked target is refused");
            ModAssert.Null(RpgRouteSketch.Connect(from,to,c => open(c) && c.Y != 3), "disconnected cells are refused");
        }
    }
}
