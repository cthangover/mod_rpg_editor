using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Characters;
using Cthangover.Core.Games;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Settings;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.Rpg.Play;
using Cthangover.Tools.Common;
using Cthangover.Tools.EntityEditor;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor.Tests
{
    /// <summary>
    /// Plays the characters on an RPG map (plan 14.G7, 14.G11) like an author, then a player. The map editor makes a new map. The join
    /// scenario of a character is written into the mod first (the file the characters editor writes), then the Characters tab of the
    /// palette is chosen: the character is placed as its actor object, saved with the map. The author gives the placed companion a dialog in the inspector (Does → Opens a dialog): the dialog plays first, then the join lines run. In the same window the state preview of a
    /// barrel ticks its <c>self.open</c> flag and the state that applies is shown; the preview writes nothing. Then the game plays the
    /// saved map: the actor stands on its cell, talking to it plays its dialog and then joins the party, the actor leaves its cell and the companion walks behind
    /// the hero. Afterwards the new map's files, the actor entry and the join scenario are removed; every other file of the mod is
    /// compared with its text from the start.
    /// <para/>
    /// Result line: <c>[TEST_RESULT] name=rpg_companion_edit passed=N failed=M</c>, exit code 0/1, 240 s watchdog.
    /// </summary>
    public partial class RpgCompanionTestDriver : Node
    {
        private const float TickSeconds = 0.1f;
        private const float TimeoutSeconds = 240f;
        private const string Mod = "rpg_plus_game";
        private const string Village = "rpg_village";
        private const string Companion = "rpg_companion";
        private const string JoinedFlag = "rpg_companion_joined";
        private const string JoinKey = Mod + "/party/" + Companion;
        private const string JoinPath = "scenarios/" + JoinKey + ".scenario";
        private const string JoinText = "flag rpg_companion_joined 1\naction party.add id=rpg_companion\nend\n";
        private static readonly string[] JoinCommands = { "flag rpg_companion_joined 1", "action party.add id=rpg_companion" };
        private const string DialogKey = Mod + "/party/" + Companion + "_talk";
        private const string DialogPath = "scenarios/" + DialogKey + ".scenario";
        private const string DialogText = "ptext \"Лина смотрит на отряд и думает, идти ли с героем.\"\nflag rpg_companion_talked 1\n";
        private const string TalkedFlag = "rpg_companion_talked";
        private const string ActorObject = Mod + "/character/" + Companion;
        private const string ActorsFile = "objects/characters.json";
        private const string Grass = "village_pack/ground/grass";
        private const string Barrel = "rpg_plus/container/supply_barrel";
        private const string NewMap = "zz_companion_test_map";
        private const string NewMapPath = "maps/zz_companion_test_map.json";
        private const string NewScenePath = "scenes/zz_companion_test_map.json";
        private const string NewPlacementPath = "placements/zz_companion_test_map/zz_companion_test_map.json";

        private static readonly Vector2I PaintCell = new(1, 1);
        private static readonly Vector2I ActorCell = new(6, 4);
        private static readonly Vector2I BarrelCell = new(3, 3);
        private static readonly Vector2I TalkFrom = new(5, 4);

        private readonly List<string> failures = new();
        private readonly Queue<Func<bool>> steps = new();
        private readonly List<Vector2I> arrivals = new();
        private readonly Dictionary<string, string> before = new();
        private RpgEditorWindow window;
        private RpgStatePreview preview;
        private string actorId;
        private string barrelId;
        private Vector2I gatherCell;
        private RpgWorldSession attached;
        private int passed;
        private double elapsed;
        private bool done;

        /// <summary>Creates the driver and queues its steps.</summary>
        public RpgCompanionTestDriver()
        {
            Name = "RpgCompanionTestDriver";
            BuildSteps();
            var timer = new Timer { WaitTime = TickSeconds, Autostart = true };
            AddChild(timer);
            timer.Timeout += Tick;
        }

        private static RpgWorldView View => RpgWorldHost.Current?.View;

        private static CharacterData Roster => GameData.Instance.Runtime.CharacterData;

        private static RpgFollower Follower0 => View?.Session?.Followers.Members.FirstOrDefault();

        private static string Flag(string name) => GameData.Instance.Runtime.Flags.TryGetValue(name, out var value) ? value : null;

        private void BuildSteps()
        {
            // Boot: the prologue is clicked through, the village intro too (as in rpg_party_walk).
            Until(() => Boot.IsReady && !SkipPrologue() && View?.Map?.ID == Village, "the village is up", 900);
            ClickDialogs(600, "the intro dialog ends");
            Do(() => Snapshot());

            // The editor makes a new map, paints a surface and saves it: the map and its scene exist.
            Do(() => window = ToolWindow.Open<RpgEditorWindow>());
            Until(() => window.Document != null, "the map editor opens", 300);
            Do(() =>
            {
                window.OpenMapFile(Mod, NewMapPath);
                Check(window.Document != null && window.Document.MapId == NewMap, "+ makes the new map");
                window.ChooseObject(Grass, RpgObjectForm.Surface);
                Click(PaintCell);
                Check(window.Document.ObjectAt(RpgStrokeLayer.Ground, PaintCell.X, PaintCell.Y) == Grass, "the new map takes a surface");
                Check(window.Save(), "the first save of the new map succeeds");
            });

            // The join scenario of the character, written the way the characters editor writes it.
            Do(() => Check(ModFileService.WriteText(Mod, JoinPath, JoinText), "the join scenario is written into the mod"));
            Do(() => ModManager.Instance.GetResolver<ScenarioResolver>()?.ClearCache());

            // The dialog of the companion is a scenario of its own; the author gives it to the placed companion in the inspector.
            Do(() => Check(ModFileService.WriteText(Mod, DialogPath, DialogText), "the companion's dialog scenario is written into the mod"));
            Do(() => ModManager.Instance.GetResolver<ScenarioResolver>()?.ClearCache());

            // The Characters tab: the character is chosen; its actor object is written with the join only, and placed like any actor.
            Do(() =>
            {
                Check(window.Palette.FindChild("Palette_Characters", true, false) is FilteredList, "the palette has a Characters tab");
                ChooseCharacter(Companion);
                var objects = ModFileService.ReadText(Mod, ActorsFile) ?? string.Empty;
                Check(objects.Contains("\"Id\": \"" + ActorObject + "\"") && objects.Contains("\"Scenario\": \"" + JoinKey + "\""),
                    "choosing the character writes its actor object with the join scenario");
                Check(window.Tool == RpgEditorTool.Place && window.PlaceObject == ActorObject, "the actor is ready to be placed");
            });
            Do(() =>
            {
                Click(ActorCell);
                actorId = window.SelectedId;
                Check(actorId != null && window.Document.Placements.Any(r => r.Id == actorId), "a click places the actor on the map");
                Check(window.Scene.ObjectOf(actorId)?.Interact?.Scenario == JoinKey, "the placed actor only joins: it has no dialog of its own yet");
            });

            // The inspector gives the placed companion a dialog: Does → Opens a dialog, then the dialog is picked (plan 14.G16).
            Do(() =>
            {
                var kind = window.FindChild("InteractKind", true, false) as OptionButton;
                Check(kind != null && kind.GetItemText(kind.Selected) == Translate("tools/rpg_editor/interact_kind_none"),
                    "the inspector reads the actor as Nothing: only the join is left");
                Check(HasLabel(Translate("tools/rpg_editor/interact_join_hint")), "the inspector says the character joins the party after the dialog");
                if (kind != null)
                    PickItem(kind, Translate("tools/rpg_editor/interact_kind_dialog"), null);
            });
            Do(() =>
            {
                var scenario = window.FindChild("InteractScenario", true, false) as OptionButton;
                Check(scenario != null, "choosing Opens a dialog shows the dialog picker");
                if (scenario != null)
                    PickItem(scenario, null, DialogKey);
            });
            Do(() =>
            {
                var kind = window.FindChild("InteractKind", true, false) as OptionButton;
                Check(kind != null && kind.GetItemText(kind.Selected) == Translate("tools/rpg_editor/interact_kind_dialog"),
                    "the placement reads back as a dialog, not as a custom interaction");
                var interact = window.Scene.ObjectOf(actorId)?.Interact;
                Check(interact?.Scenario == DialogKey, "the placed actor talks to the dialog picked in the inspector");
                Check(interact?.Commands?.SequenceEqual(JoinCommands) == true, "and the join lines follow the dialog");
            });

            // The state preview of a barrel: self.open is ticked and the state that applies is shown; nothing is written.
            Do(() =>
            {
                window.ChooseObject(Barrel, RpgObjectForm.Sprite);
                Click(BarrelCell);
                barrelId = window.SelectedId;
                preview = window.FindChild("StatePreview", true, false) as RpgStatePreview;
                Check(preview != null, "the placed barrel has a state preview");
            });
            Do(() =>
            {
                var box = window.FindChild("Preview_open", true, false) as CheckBox;
                Check(box != null && !box.ButtonPressed, "the preview lists self.open of the barrel, off");
                Check(preview?.StateText == Translate("tools/rpg_editor/preview_base"), "without the flag, the base state applies");
                if (box == null)
                    return;
                box.ButtonPressed = true;
                Check(preview?.StateText == string.Format(Translate("tools/rpg_editor/preview_state"), "open"), "ticking self.open shows the open state");
                box.ButtonPressed = false;
                Check(preview?.StateText == Translate("tools/rpg_editor/preview_base"), "unticking shows the base state again");
            });

            // Save: the placements and the actor are written; the game knows the actor object at once.
            Do(() =>
            {
                Check(window.Save(), "the save with the actor and the barrel succeeds");
                var placements = ModFileService.ReadText(Mod, NewPlacementPath) ?? string.Empty;
                Check(placements.Contains(ActorObject), "the placement file holds the actor's object");
                var actors = ModFileService.ReadText(Mod, ActorsFile) ?? string.Empty;
                Check(actors.Contains("\"When\": \"" + JoinedFlag + " == 1\"") && !actors.Contains("\r"),
                    "the actor object hides once the join flag reads 1 (LF lines)");
                Check(Entities.Collect<RpgObject>().ContainsKey(ActorObject), "the game knows the actor object right after the save");
                window.QueueFree();
                window = null;
            });

            // The game plays the saved map: the actor stands on its cell.
            Do(() => RpgWorldHost.Teleport(new RpgTeleport { Map = NewMap, X = 1, Y = 1, Dir = RpgDirection.Down }));
            Until(() => View?.Map?.ID == NewMap && !GodotSceneService.IsTransitioning, "the new map is mounted", 300);
            ClickDialogs(300, "the map's entry text ends");
            Do(() => Check(View.Find(actorId)?.Visible == true, "in game, the placed companion stands on its cell"));
            Do(() => Check(!Roster.BattleSet.Contains(Companion), "before talking, the companion is not in the party"));

            // Talking to the actor joins the party; the actor leaves its cell.
            Do(() => View.PlacePlayer(TalkFrom.X, TalkFrom.Y, RpgDirection.Right));
            Until(() => View.Player.Cell == TalkFrom && !View.Player.IsMoving, "the hero stands next to the companion", 40);
            Do(() => Check(View.Interact(), "in game, talking to the companion starts its dialog"));
            // The dialog waits on its first line: the join lines come after the dialog, so nothing of them has run yet.
            Do(() => Check(Flag(JoinedFlag) == null && Flag(TalkedFlag) == null, "the dialog plays first: the join has not run while its first line waits"));
            ClickDialogs(300, "the join ends");
            Do(() => Check(Flag(TalkedFlag) == "1", "the dialog played first: its flag is set"));
            Do(() => Check(Flag(JoinedFlag) == "1", "then the join lines set the joined flag"));
            Do(() => Check(Roster.BattleSet.Contains(Companion), "party.add put the companion into the battle set"));
            Until(() => View.Find(actorId)?.Visible == false, "the companion leaves its cell once joined", 60);
            Do(() => Check(RpgPartyLook.Followers().Any(f => f.Id == Companion), "the companion follows the hero"));
            Do(() => Check(Follower0 != null && Follower0.Cell == View.Player.Cell, "the companion gathers on the hero when it joins"));

            // The companion walks behind the hero: two steps right, the follower stands on the cell the hero left one step before.
            Do(() =>
            {
                arrivals.Clear();
                gatherCell = View.Player.Cell;
                AttachSession();
            });
            Hold(Key.D, () => arrivals.Count >= 2, "two steps right");
            Settle(25);
            Do(() => CheckTrail());

            // The new map's files go; every other file of the mod is as it was.
            Do(() => Cleanup());
        }

        /// <summary>One queued step per tick; a step returns true once it is done.</summary>
        private void Tick()
        {
            if (done)
                return;
            elapsed += TickSeconds;
            if (elapsed > TimeoutSeconds)
            {
                Fail($"timeout, {steps.Count} step(s) left");
                Finish();
                return;
            }
            try
            {
                AttachSession();
                if (steps.Count == 0)
                {
                    Finish();
                    return;
                }
                if (steps.Peek()())
                    steps.Dequeue();
            }
            catch (Exception ex)
            {
                Fail($"{ex.GetType().Name}: {ex.Message}");
                Finish();
            }
        }

        private void Snapshot()
        {
            foreach (var path in ModFileService.ListFiles(Mod, string.Empty, ".json").Concat(ModFileService.ListFiles(Mod, string.Empty, ".scenario")))
                before[path] = ModFileService.ReadText(Mod, path);
        }

        /// <summary>Chooses a character in the palette's Characters tab the way a click does: the list row is selected and its item activated.</summary>
        private void ChooseCharacter(string id)
        {
            var list = window.Palette.FindChild("Palette_Characters", true, false) as FilteredList;
            var items = list?.FindChildren("*", "ItemList", true, false).OfType<ItemList>().FirstOrDefault();
            if (items == null)
            {
                Fail("the Characters tab lists its rows");
                return;
            }
            for (var i = 0; i < items.ItemCount; i++)
            {
                if (items.GetItemTooltip(i) != id)
                    continue;
                items.EmitSignal(ItemList.SignalName.ItemSelected, (long)i);
                return;
            }
            Fail($"the Characters tab offers {id}");
        }

        /// <summary>
        /// Picks an item of an inspector picker the way a click does: the item is selected and its signal emitted. The item is found by
        /// its text, or by its metadata (the scenario path) when <paramref name="meta"/> is given.
        /// </summary>
        private void PickItem(OptionButton picker, string text, string meta)
        {
            for (var i = 0; i < picker.ItemCount; i++)
            {
                var match = meta == null ? picker.GetItemText(i) == text : picker.GetItemMetadata(i).AsString() == meta;
                if (!match)
                    continue;
                picker.Select(i);
                picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i);
                return;
            }
            Fail($"the picker offers {meta ?? text}");
        }

        /// <summary>True when a label of the window shows this text (the inspector's hints are labels).</summary>
        private bool HasLabel(string text)
            => window.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == text);

        /// <summary>The map's cleanup and the byte comparison of the mod's files.</summary>
        private void Cleanup()
        {
            if (window != null)
                window.QueueFree();
            foreach (var path in new[] { NewMapPath, NewScenePath, NewPlacementPath, JoinPath, DialogPath })
                GameLogger.Log("TEST", $"cleanup {path}: deleted={ModFileService.DeleteFile(Mod, path)}");
            // The actor entry is part of the mod's own characters file: it goes back to what the file held before the test.
            if (before.TryGetValue(ActorsFile, out var original) && original != null)
                ModFileService.WriteText(Mod, ActorsFile, original);
            else
                ModFileService.DeleteFile(Mod, ActorsFile);
            ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
            ModManager.Instance.GetResolver<ScenarioResolver>()?.ClearCache();
            SceneManager.ReloadContent();

            var changed = before.Where(f => ModFileService.ReadText(Mod, f.Key) != f.Value).Select(f => f.Key).ToList();
            var added = ModFileService.ListFiles(Mod, string.Empty, ".json").Concat(ModFileService.ListFiles(Mod, string.Empty, ".scenario"))
                .Where(p => !before.ContainsKey(p)).ToList();
            Check(changed.Count == 0 && added.Count == 0,
                "every file of the mod is as it was (the new map, the join scenario, the dialog and the actor entry are gone)"
                + (changed.Count + added.Count == 0 ? string.Empty : $": changed [{string.Join(", ", changed)}], added [{string.Join(", ", added)}]"));
        }

        /// <summary>The follower stands on the cell the hero reached one arrival before its last one (or the gathering cell), and is still.</summary>
        private void CheckTrail()
        {
            var follower = Follower0;
            var expected = arrivals.Count >= 2 ? arrivals[^2] : gatherCell;
            Check(follower != null, "a follower exists after the steps");
            if (follower == null)
                return;
            Check(!follower.Actor.IsMoving, "the follower has arrived");
            Check(follower.Cell == expected, $"the follower walks the hero's trail: at {follower.Cell}, expected {expected}");
        }

        // Records every arrival of the mounted session: the trail the follower walks.
        private void AttachSession()
        {
            var session = RpgWorldHost.Current?.Session;
            if (session == attached)
                return;
            if (attached != null)
                attached.PlayerStepFinished -= OnArrival;
            attached = session;
            arrivals.Clear();
            if (attached != null)
                attached.PlayerStepFinished += OnArrival;
        }

        private void OnArrival(RpgActorState actor) => arrivals.Add(actor.Cell);

        private void Do(Action action) => steps.Enqueue(() => { action(); return true; });

        private void Until(Func<bool> condition, string what, int maxTicks)
        {
            var ticks = 0;
            steps.Enqueue(() =>
            {
                if (condition())
                    return true;
                if (++ticks < maxTicks)
                    return false;
                Fail("timed out: " + what);
                return true;
            });
        }

        private void Settle(int ticks)
        {
            var waited = 0;
            steps.Enqueue(() => ++waited >= ticks);
        }

        // Holds a key until the condition holds, then releases it.
        private void Hold(Key key, Func<bool> until, string what)
        {
            Do(() => Press(key, true));
            Until(until, what, 200);
            Do(() => Press(key, false));
        }

        // Clicks through a running dialog: the first answer of a choice, else the action key.
        private void ClickDialogs(int maxTicks, string what)
        {
            var ticks = 0;
            steps.Enqueue(() =>
            {
                var box = RpgInput.FindDialogBox();
                if (box == null || !box.IsRunning)
                    return true;
                if (++ticks >= maxTicks)
                {
                    Fail("timed out: " + what);
                    return true;
                }
                if (box.IsWaitingAnswer)
                    box.TrySelectVariant(0);
                else
                    Tap(Key.E);
                return false;
            });
        }

        // Advances the shell's dialog-free path: the prologue's choices take their first answer.
        private static bool SkipPrologue()
        {
            var scene = ((SceneTree)Engine.GetMainLoop()).Root.GetNodeOrNull<SceneManager>("SceneManager")?.CurrentSceneName;
            if (string.IsNullOrEmpty(scene) || RpgWorldHost.Current?.View?.Map != null)
                return false;
            var box = RpgInput.FindDialogBox();
            if (box == null || !box.IsRunning)
                return false;
            if (box.IsWaitingAnswer)
                box.TrySelectVariant(0);
            else
                box.NextAction();
            return true;
        }

        /// <summary>A left click on a cell of the canvas: press and release through the canvas' own input.</summary>
        private void Click(Vector2I cell)
        {
            var at = window.Canvas.CanvasPointOf(cell);
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        }

        private static string Translate(string key) => TranslationServer.Translate(key).ToString();

        private static void Press(Key key, bool pressed)
        {
            Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });
        }

        private static void Tap(Key key)
        {
            Press(key, true);
            Press(key, false);
        }

        private void Check(bool condition, string what)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", $"[PASS] {what}");
            }
            else
            {
                Fail(what);
            }
        }

        private void Fail(string what)
        {
            failures.Add(what);
            GameLogger.Log("TEST", $"[FAIL] {what}", LogLevel.Error);
        }

        private void Finish()
        {
            done = true;
            GameLogger.Log("TEST", $"[TEST_RESULT] name=rpg_companion_edit passed={passed} failed={failures.Count}");
            ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
