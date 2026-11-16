using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using Cthangover.Core.Utils;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests.CellRules
{
    /// <summary>Checks entry versus action-side access, arrival masks and fixed route occupancy without executing gameplay.</summary>
    public sealed class RpgMapDiagnosticsTests
    {
        private RpgEditorScene destination;

        [ModTest("map_diagnostics/reachable_interactions_arrivals_and_paths")]
        static void Reachability(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            for (var y = 0; y < map.Height; y++)
            {
                map.Collision[y] = "....#...";
            }

            map.Events = new List<RpgMapEvent>
            {
                new()
                {
                    Id = "near_use",
                    X = 3,
                    Y = 1,
                    Solid = true,
                    Trigger = RpgInteractTrigger.Use,
                },
                new()
                {
                    Id = "far_step",
                    X = 6,
                    Y = 1,
                    Trigger = RpgInteractTrigger.Step,
                },
                new()
                {
                    Id = "local_arrival",
                    X = 2,
                    Y = 4,
                    Teleport = new()
                    {
                        X = 4,
                        Y = 4
                    },
                },
                new()
                {
                    Id = "remote_arrival",
                    X = 2,
                    Y = 5,
                    Teleport = new()
                    {
                        Map = "destination",
                        X = 2,
                        Y = 2
                    },
                },
                new()
                {
                    Id = "missing_arrival",
                    X = 2,
                    Y = 6,
                    Teleport = new()
                    {
                        Map = "missing",
                        X = 0,
                        Y = 0
                    },
                },
            };
            var fixture = new RpgRulesFixture(map);
            var before = fixture.Document.Data.ToJsonString();
            var target = RpgRulesFixture.Map();
            target.ID = "destination";
            target.Events = new List<RpgMapEvent>
            {
                new()
                {
                    Id = "blocker",
                    X = 2,
                    Y = 2,
                    Solid = true
                },
            };
            var helper = new RpgMapDiagnosticsTests
            {
                destination = new RpgRulesFixture(target).Scene(),
            };
            var audit = new RpgMapDiagnostics(fixture.Scene(), helper.Target);
            ModAssert.True(audit.Complete, "working-size map is fully searched");
            ModAssert.Equal(31, audit.Reached, "reachable region excludes the solid action target");
            ModAssert.True(audit.CanReach(new Vector2I(3, 3)), "the player can approach the solid target");
            ModAssert.False(audit.CanReach(new Vector2I(6, 1)), "the wall separates the far step event");
            ModAssert.False(audit.CanReach(new Vector2I(-1, 0)), "outside is unreachable");
            ModAssert.True(Has(audit, "event", "rules_fixture/far_step", new Vector2I(6, 1)),
                "a Step target requires actual entry");
            ModAssert.False(HasCode(audit, "event", "rules_fixture/near_use"),
                "a solid Use target remains accessible from its neighbour");
            ModAssert.True(Has(audit, "arrival", "rules_fixture/local_arrival", new Vector2I(4, 4)),
                "local arrival sees effective collision");
            ModAssert.True(Has(audit, "arrival", "rules_fixture/remote_arrival", new Vector2I(2, 2)),
                "remote arrival also sees the destination event mask");
            ModAssert.True(Has(audit, "target", "rules_fixture/missing_arrival", Vector2I.Zero),
                "unavailable maps are explicit, never accepted unchecked");
            ModAssert.Equal(4, audit.Issues.Count, "the audit reports only the four real problems");
            var incoming = new[]
            {
                ("another_map/door", new Vector2I(2, 2)),
                ("another_map/open_door", new Vector2I(3, 3)),
            };
            var targetAudit = new RpgMapDiagnostics(helper.destination, null, incoming);
            ModAssert.True(Has(targetAudit, "arrival", "another_map/door", new Vector2I(2, 2)),
                "incoming links are rechecked against the edited destination mask");
            ModAssert.False(HasCode(targetAudit, "arrival", "another_map/open_door"),
                "a valid incoming arrival is not warned about");
            var goal = new Vector2I(3, 3);
            var path = audit.PathTo(goal);
            ModAssert.NotNull(path, "reachable cells have a recorded path");
            ModAssert.True(path[0] == new Vector2I(1, 1) && path[^1] == goal, "path includes exact endpoints");
            ModAssert.Equal(5, path.Count, "the recorded route has minimal cardinal length");
            for (var i = 1; i < path.Count; i++)
            {
                var delta = path[i] - path[i - 1];
                ModAssert.Equal(1, Math.Abs(delta.X) + Math.Abs(delta.Y), "route has no diagonal or skipped steps");
                ModAssert.True(audit.CanReach(path[i]), "every path step belongs to the reached component");
            }

            ModAssert.Equal(1, audit.PathTo(new Vector2I(1, 1)).Count, "spawn path contains its single endpoint");
            ModAssert.Null(audit.PathTo(new Vector2I(6, 1)), "failure produces no direct movement fallback");
            ModAssert.Equal(before, fixture.Document.Data.ToJsonString(), "audit preserves map JSON");
            ModAssert.False(fixture.Document.CanUndo, "audit adds no history");
            ModAssert.Equal(0, fixture.Document.Changes().Count, "audit produces no authoring writes");
        }

        [ModTest("map_diagnostics/routes_lift_own_mask_and_stop_before_dynamic_steps")]
        static void Routes(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            map.Collision[4] = "....#...";
            var actor = new RpgObject
            {
                ID = "rules_fixture/actor",
                Form = RpgObjectForm.Actor,
                Solid = true,
                Footprint = new()
                {
                    Size = new[] { 2, 1 }
                },
                Route = new RpgMoveRoute
                {
                    Steps = new() { "right", "left" },
                },
            };
            var objects = new Dictionary<string, RpgObject>
            {
                [actor.ID] = actor
            };
            var placement = new RpgPlacement
            {
                ID = map.ID + "/walker",
                Object = actor.ID,
                X = 1,
                Y = 4,
            };
            var fixture = new RpgRulesFixture(map, objects, placement);
            ModAssert.Equal(0, new RpgMapDiagnostics(fixture.Scene()).Issues.Count,
                "one-cell actor can return through its own lifted starting mask");
            ModAssert.Equal(1, actor.Width, "actor footprint contract remains one cell despite authored size");
            actor.Form = RpgObjectForm.Sprite;
            fixture = new RpgRulesFixture(map, objects, placement);
            ModAssert.Equal(2, actor.Width, "sprite uses the authored two-cell footprint");
            ModAssert.Equal(0, new RpgMapDiagnostics(fixture.Scene()).Issues.Count,
                "wide sprite can also return through its own lifted starting mask");
            actor.Route.Variants = new()
            {
                new() { "right", "right" },
                new() { "random", "right", "right", "right" },
            };
            fixture = new RpgRulesFixture(map, objects, placement);
            var audit = new RpgMapDiagnostics(fixture.Scene());
            ModAssert.True(Has(audit, "route", placement.ID + ":0", new Vector2I(3, 4)),
                "the entire moving footprint, not only its origin, hits the wall");
            ModAssert.True(Has(audit, "route_dynamic", placement.ID + ":1", new Vector2I(1, 4)),
                "randomness explicitly ends deterministic prediction");
            ModAssert.False(HasCode(audit, "route", placement.ID + ":1"),
                "steps after an unknown direction do not become false blocked-path claims");
            ModAssert.Equal(2, audit.Issues.Count, "one fixed obstacle and one dynamic limit are distinct");
        }

        [ModTest("map_diagnostics/invalid_spawn_budget_and_force_open")]
        static void Limits(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            map.Collision[1] = ".#......";
            var audit = new RpgMapDiagnostics(new RpgRulesFixture(map).Scene());
            ModAssert.True(audit.Complete && audit.Reached == 0, "blocked spawn does not seed a flood through obstacles");
            ModAssert.True(Has(audit, "spawn", map.ID, new Vector2I(1, 1)), "invalid spawn is explicit");
            ModAssert.Null(audit.PathTo(new Vector2I(2, 1)), "invalid spawn yields no invented path");
            map.Collision[1] = ".o......";
            map.Events = new()
            {
                new()
                {
                    Id = "intentional_open",
                    X = 1,
                    Y = 1,
                    Solid = true
                },
            };
            audit = new RpgMapDiagnostics(new RpgRulesFixture(map).Scene());
            ModAssert.True(audit.CanReach(new Vector2I(1, 1)), "intentional force-open survives a solid event mask");
            ModAssert.Equal(64, audit.Reached, "force-open region is searched completely");
            map.Width = 129;
            map.Height = 128;
            map.Events.Clear();
            map.Collision.Clear();
            for (var y = 0; y < map.Height; y++)
            {
                map.Collision.Add(new string('.', map.Width));
            }

            audit = new RpgMapDiagnostics(new RpgRulesFixture(map).Scene());
            ModAssert.False(audit.Complete, "oversize audit is explicitly incomplete");
            ModAssert.Equal(0, audit.Reached, "budget exhaustion does not claim a partial flood is complete");
            ModAssert.Equal("budget", audit.Issues[0].Code, "budget has a separate reason");
            ModAssert.Null(audit.PathTo(Vector2I.Zero), "incomplete audit supplies no direct fallback");
            ModAssert.False(new RpgMapDiagnostics(null).Complete, "unopened editor is also incomplete");
        }

        [ModTest("map_diagnostics/working_load_128_square_twenty_npc")]
        static void Workload(ModTestContext ctx)
        {
            var map = RpgRulesFixture.Map();
            map.Width = 128;
            map.Height = 128;
            map.Collision.Clear();
            for (var y = 0; y < map.Height; y++)
            {
                map.Collision.Add(new string('.', map.Width));
            }

            var actor = new RpgObject
            {
                ID = "rules_fixture/worker",
                Form = RpgObjectForm.Actor,
                Solid = true,
                Route = new RpgMoveRoute
                {
                    Steps = new() { "right", "left" },
                },
            };
            var objects = new Dictionary<string, RpgObject>
            {
                [actor.ID] = actor
            };
            var placements = new RpgPlacement[20];
            for (var index = 0; index < placements.Length; index++)
            {
                placements[index] = new RpgPlacement
                {
                    ID = map.ID + "/worker_" + index,
                    Object = actor.ID,
                    X = 10,
                    Y = 10 + index,
                };
            }

            var fixture = new RpgRulesFixture(map, objects, placements);
            var scene = fixture.Scene();
            var before = fixture.Document.Data.ToJsonString();
            var samples = new double[20];
            for (var index = 0; index < samples.Length; index++)
            {
                var watch = Stopwatch.StartNew();
                var audit = new RpgMapDiagnostics(scene);
                samples[index] = watch.Elapsed.TotalMilliseconds;
                ModAssert.True(audit.Complete, "the owner's target workload fits the declared budget");
                ModAssert.Equal(16364, audit.Reached, "all cells except twenty occupied NPC cells are reached");
                ModAssert.Equal(0, audit.Issues.Count, "the twenty fixed routes remain valid");
                ModAssert.Equal(253, audit.PathTo(new Vector2I(127, 127)).Count, "far corner has a shortest cardinal path");
            }

            Array.Sort(samples);
            ctx.Log("128x128, 20 NPC, audit p95=" + samples[18].ToString("0.###",
                System.Globalization.CultureInfo.InvariantCulture) + " ms; min="
                + samples[0].ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " ms");
            ModAssert.Equal(before, fixture.Document.Data.ToJsonString(), "repeated audits preserve source JSON");
        }

        private RpgEditorScene Target(string id)
        {
            return id == "destination" ? destination : null;
        }

        private static bool Has(RpgMapDiagnostics audit, string code, string id, Vector2I cell)
        {
            foreach (var issue in audit.Issues)
            {
                if (issue.Code == code && issue.Id == id && issue.Cell == cell)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasCode(RpgMapDiagnostics audit, string code, string id)
        {
            foreach (var issue in audit.Issues)
            {
                if (issue.Code == code && issue.Id == id)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
