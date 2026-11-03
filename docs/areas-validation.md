# Event area resizing and collision overlay validation

Windows host, 2 November 2026. Accepted job `rpg_areas_accept_20261102T145126_7fbc1d`, exit 0. Full log: `jobs/done/rpg_areas_accept_20261102T145126_7fbc1d.log`. Per-check logs: `gen/rpg_editor_areas/acceptance/`.

| Check | Result |
|---|---|
| RPG editor unit contracts | 71 passed |
| New area and collision GUI | 25 passed |
| Existing editor GUI in isometric projection | 117 passed |
| Existing editor GUI in top-down projection | 117 passed |
| Resource atlas | 0 errors, 0 warnings |
| Locale atlas | 0 errors; 3049 existing project translation warnings |

The GUI drives the tool button and projected pointer input: initial red mask, a live LMB block stroke, RMB opening solid terrain, preview without data mutation, undo/redo, corner resize in both projections, moving the origin while keeping the opposite corner, and click without drag. Existing regression retains the original stacked-placement selection assertions. Unit contracts include bounds clamping, save/reload, atomic undo and foreign patches.

Initial verification failed because temporary C# staging files under gen were included by the core project; staging was moved outside repositories. The test assertion API lacked Rect2I Equal; equivalent equality assertions now use True. The isolated fixture initially lacked a logical scene and then had a map filename that differed from its ID; both setup errors were corrected. The original GUI regression caught corner clicks intercepting selection cycling; runtime input now distinguishes a click from a resize drag, with the original assertions unchanged.

Raw captures, intermediate results, staging files and review notes are archived outside all repositories in `/workspace/art_sources/cthangover/rpg_editor_areas/documentation/20261102/`. Production maps were not saved by the new GUI driver. No commits were created.


## Authoritative map collision (follow-up)

2 November 2026: explicit map `o` now overrides dynamic events, stateful doors and NPCs as well as terrain and static objects. `#` remains blocked; `.` delegates to objects. On-cell Use interactions also respect the opened cell. The editor overlay includes actors and restores their natural collision when previewing `.`.

Checks: RPG unit contracts 80/80; editor unit contracts 72/72; isolated editor GUI plus the actual saved `jrp_bureau_hall` map 29/29; isolated world walking 10/10; existing isometric editor GUI 117/117. Logs: `gen/rpg_collision_priority/verify/` and `gen/rpg_collision_priority/regression/`. The hall probe checked every explicit map override, walked onto the solid door's opened cell, and refreshed states to verify the cell stayed open. The old grid assertion about dynamic objects overriding `o` was intentionally updated to the user's new collision contract; normal dynamic collision and overlap assertions remain.

Job `rpg_collision_priority_20261102T152218_63f25d` stopped at the existing `rpg_walk` test: four fixed-coordinate assertions differ from the current authored RPG Plus maps. The test expects village spawn (15,14), while the map contains (15,9); its house destination expectation is (6,8), while the actual teleport points to (6,7), also affecting battle-return and load-position assertions. Those authored coordinates and the old test were not changed. The remaining editor and isolated walking regressions passed in `rpg_collision_regression_20261102T152408_09a2cc` (exit 0).

Raw evidence and patch staging: `/workspace/art_sources/cthangover/rpg_collision_priority/20261102/`.
