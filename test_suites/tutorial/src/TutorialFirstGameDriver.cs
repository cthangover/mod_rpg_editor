namespace Cthangover.Tools.RpgEditor.Tutorial
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods;
    using Cthangover.Core.UI.Menu;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Menu;
    using Cthangover.Mods.Interface.View;
    using global::Mods.ModManager;
    using Godot;

    /// <summary>
    /// Step 1 of the tutorial "Your first game in 10 minutes" (rpg_editor docs), clicked as a player: the Mods &amp; Collections
    /// editor of the game RPG Plus, its toolbar's "New mod", the title typed, the base kept at rpg_plus_game, "Create mod".
    /// The wizard writes the mod folder and adds the mod to the game (the user copy of the collection). The game's collection
    /// is the real one, not a fixture, because the next suite (<c>tutorial_first_game_play</c>) starts the game with it.
    /// <para/>
    /// Before the wizard the user collections folder is saved (every file, its bytes). A marker file in the new mod's folder
    /// holds that copy, so the next suite can put the collections back exactly as they were. A failure here restores them now.
    /// <para/>
    /// Result line: <c>[TEST_RESULT] name=tutorial_first_game passed=N failed=M</c>.
    /// </summary>
    public partial class TutorialFirstGameDriver : Node
    {
        private const string Game = "rpg_plus";
        private const string ModId = "tutorial_first_game";
        private const string ModTitle = "Tutorial first game";
        private const string MarkerFile = "tutorial_marker.json";
        private const float TimeoutSeconds = 120f;

        private readonly string shotsDir;
        private readonly List<string> failures = new();
        private Dictionary<string, string> userBefore;
        private ModHubScreen hub;
        private ModCollectionsScreen editor;
        private string originalLocale;
        private string modPath;
        private int step;
        private int passed;
        private double elapsed;
        private double next;
        private bool done;
        private string pendingShot;

        /// <summary>Creates the driver; <paramref name="shotsDir"/> (may be null) receives the wizard and the result.</summary>
        public TutorialFirstGameDriver(string shotsDir)
        {
            this.shotsDir = shotsDir;
            Name = "TutorialFirstGameDriver";
        }

        /// <summary>One step per 0.35 s; every step checks what the screen shows and what the files hold.</summary>
        public override void _Process(double delta)
        {
            if (done)
                return;
            if (pendingShot != null)
            {
                SaveShot(pendingShot);
                pendingShot = null;
            }
            elapsed += delta;
            if (elapsed > TimeoutSeconds)
            {
                Fail("timeout at step " + step);
                Finish();
                return;
            }

            if (elapsed < next)
                return;
            next = elapsed + 0.35;
            try
            {
                RunStep();
            }
            catch (Exception ex)
            {
                Fail("step " + step + ": " + ex.GetType().Name + ": " + ex.Message);
                Finish();
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcher)
                    {
                        launcher.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    originalLocale = TranslationServer.GetLocale();
                    userBefore = SnapshotUserCollections();
                    RemoveLeftovers();
                    var entry = MenuRegistry.GetButtonById("mods_hub");
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry.ScenePath, "mods_hub") as ModHubScreen;
                    Check(hub != null, "the mods hub opens");
                    hub.OpenCollectionEditor(Game);
                    editor = hub.FindChildren("*", "", true, false).OfType<ModCollectionsScreen>().Single();
                    break;
                case 1:
                    Check(ModCollectionOpen(), "the collection editor shows the game RPG Plus");
                    PressButton(Tr("mod_manager/collection_new_mod"));
                    break;
                case 2:
                    var form = FindDialog(Tr("mod_manager/newmod_title"));
                    var edits = form.FindChildren("*", "", true, false).OfType<LineEdit>().ToList();
                    Check(edits.Count >= 2, "the wizard asks for a title and an id");
                    Type(edits[0], ModTitle);
                    Check(edits[1].Text == ModId, $"the id follows the title ({edits[1].Text})");
                    var pickers = form.FindChildren("*", "", true, false).OfType<OptionButton>().ToList();
                    Check(pickers.Count == 2, "the wizard has a base and a game to pick");
                    if (pickers.Count == 2)
                    {
                        var baseId = pickers[0].GetItemMetadata(pickers[0].Selected).AsString();
                        var gameId = pickers[1].GetItemMetadata(pickers[1].Selected).AsString();
                        Check(baseId == "rpg_plus_game>=1", $"the base is the game's own content first ({baseId})");
                        Check(gameId == Game, $"the game is RPG Plus ({gameId})");
                    }
                    Shot("wizard");
                    form.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 3:
                    modPath = Path.Combine(ModRoots.ModsPath, ModId);
                    Check(Directory.Exists(modPath), "the mod folder is written next to the other mods");
                    using (var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(modPath, "manifest.json"))))
                    {
                        Check(doc.RootElement.GetProperty("id").GetString() == ModId, "the manifest has the id");
                        Check(doc.RootElement.GetProperty("depends")[0].GetString() == "rpg_plus_game>=1", "the manifest depends on rpg_plus_game");
                    }
                    Check(File.ReadAllText(Path.Combine(modPath, "locale", "en.properties")).Contains($"mod_{ModId}_name = {ModTitle}"), "the English locale stub has the title");
                    Check(!Directory.Exists(Path.Combine(modPath, ".git")), "no repository is made");
                    var joined = ModCollectionStore.LoadUser().Single(c => c.Id == Game);
                    Check(joined.Mechanics.Any(m => m.Mods != null && m.Mods.Contains(ModId)), "the game RPG Plus lists the mod as a block");
                    WriteMarker();
                    break;
                case 4:
                    var notice = FindNotice(Tr("mod_manager/newmod_title"));
                    Check(notice != null && notice.DialogText.Contains(ModId), "the wizard says the mod joined the game");
                    Shot("done");
                    notice?.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 5:
                    Check(FindNotice(Tr("mod_manager/newmod_title")) == null, "the notice closes");
                    Finish();
                    return;
            }

            step++;
        }

        /// <summary>The user collections folder as it is now: every file, its bytes in base64 (the relative path is the key).</summary>
        private static Dictionary<string, string> SnapshotUserCollections()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            var root = ModCollectionStore.UserPath;
            if (!Directory.Exists(root))
                return files;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                files[Path.GetRelativePath(root, file).Replace('\\', '/')] = Convert.ToBase64String(File.ReadAllBytes(file));
            return files;
        }

        /// <summary>Writes the mod folder's marker: the collections as they were before the wizard, for the next suite.</summary>
        private void WriteMarker()
        {
            var collections = new JsonObject();
            foreach (var pair in userBefore)
                collections[pair.Key] = pair.Value;
            var marker = new JsonObject
            {
                ["mod"] = ModId,
                ["game"] = Game,
                ["user_collections"] = collections,
            };
            File.WriteAllText(Path.Combine(modPath, MarkerFile), marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
            Check(File.Exists(Path.Combine(modPath, MarkerFile)), "the marker of the collections is written for the next suite");
        }

        /// <summary>Puts the user collections folder back as it was before this test; the mod folder goes too.</summary>
        private void RestoreAll()
        {
            var root = ModCollectionStore.UserPath;
            if (userBefore != null && Directory.Exists(root))
            {
                foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (!userBefore.ContainsKey(Path.GetRelativePath(root, file).Replace('\\', '/')))
                        File.Delete(file);
                }
                foreach (var pair in userBefore)
                {
                    var full = Path.Combine(root, pair.Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(full));
                    File.WriteAllBytes(full, Convert.FromBase64String(pair.Value));
                }
                foreach (var dir in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length))
                {
                    if (Directory.GetFileSystemEntries(dir).Length == 0)
                        Directory.Delete(dir);
                }
            }
            RemoveLeftovers();
        }

        /// <summary>Removes the mod folder of this suite (a leftover of an earlier run, or the one this test made).</summary>
        private static void RemoveLeftovers()
        {
            var mod = Path.Combine(ModRoots.ModsPath, ModId);
            if (Directory.Exists(mod))
                Directory.Delete(mod, true);
        }

        /// <summary>True when the collection editor is showing the game RPG Plus.</summary>
        private bool ModCollectionOpen() => editor.FindChildren("*", "", true, false).OfType<Label>().Any(l => l.Text != null && l.Text.Contains("RPG Plus"));

        private static string Tr(string key) => TranslationServer.Translate(key);

        private void PressButton(string text)
        {
            var button = editor.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
            Check(button != null, $"the button '{text}' is on the editor");
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private AcceptDialog FindDialog(string title)
        {
            var dialog = editor.FindChildren("*", "", true, false).OfType<AcceptDialog>()
                .FirstOrDefault(d => d.Visible && d.Title == title && !d.IsQueuedForDeletion());
            Check(dialog != null, $"the dialog '{title}' is open");
            return dialog;
        }

        private AcceptDialog FindNotice(string title)
            => editor.FindChildren("*", "", true, false).OfType<AcceptDialog>()
                .FirstOrDefault(d => d.Visible && d.Title == title && !d.IsQueuedForDeletion());

        private static void Type(LineEdit edit, string text)
        {
            edit.Text = text;
            edit.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        /// <summary>Asks for a picture of the state the next step shows: the capture reads the last drawn frame, so it waits a frame.</summary>
        private void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotsDir) || DisplayServer.GetName() == "headless")
                return;
            pendingShot = name;
        }

        private void SaveShot(string name)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotsDir);
            ViewportCapture.SavePng(GetViewport(), shotsDir.TrimEnd('/') + "/tutorial_01_" + name + ".png");
        }

        private void Check(bool condition, string label)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", "[PASS] " + label);
            }
            else
            {
                Fail(label);
            }
        }

        private void Fail(string label)
        {
            failures.Add(label);
            GameLogger.Log("TEST", "[FAIL] " + label, LogLevel.Error);
        }

        private void Finish()
        {
            done = true;
            hub?.QueueFree();
            if (originalLocale != null)
                LocaleLoader.LoadLocale(originalLocale);
            // A red run leaves nothing behind: the collections and the mod folder go back. A green run keeps them for the next suite.
            if (failures.Count > 0)
            {
                try
                {
                    RestoreAll();
                }
                catch (Exception ex)
                {
                    GameLogger.Log("TEST", "restore after a failure: " + ex.Message, LogLevel.Error);
                }
            }
            GameLogger.Log("TEST", $"[TEST_RESULT] name=tutorial_first_game passed={passed} failed={failures.Count}");
            ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
