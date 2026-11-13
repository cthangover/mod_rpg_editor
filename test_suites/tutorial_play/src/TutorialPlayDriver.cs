namespace Cthangover.Tools.RpgEditor.Tutorial
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Characters;
    using Cthangover.Core.Items;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Settings;
    using Cthangover.Core.UI;
    using Cthangover.Core.UI.Tool.ScenarioEditor;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Equipment;
    using Cthangover.Mods.Interface.Tool;
    using Cthangover.Mods.Interface.View;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Rpg.Play;
    using Cthangover.Mods.Stats;
    using Cthangover.Tools.CharactersEditor;
    using Cthangover.Tools.EntityEditor;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Schemas;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>
    /// Steps 2 to 10 of the tutorial "Your first game in 10 minutes" (rpg_editor docs), clicked. The map editor makes the map
    /// with its "+" button, the ground and the houses are placed (steps 2 to 4), then the chest, the door, the scenario and the
    /// villager, the companion Lina with her scenario (steps 5 to 8) are made in the editors. "Play from here" starts the game
    /// on the map: the chest, the villager, Lina (her line, then the party) and the door, walked with real key events (step 9),
    /// and the character sheet's Equip puts the mantle on (step 10).
    /// <para/>
    /// The end of every run removes the mod folder, and the user collections come back byte for byte from the marker of the
    /// previous suite. The rpg_plus_game files are compared with their text and their list from the start.
    /// <para/>
    /// Result line: <c>[TEST_RESULT] name=tutorial_first_game_play passed=N failed=M</c>.
    /// </summary>
    public partial class TutorialPlayDriver : Node
    {
        private int readinessToken = int.MinValue;
        private ulong readinessStarted;
        private ulong readinessFrame;
        private const string Game = "rpg_plus";
        private const string Mod = "tutorial_first_game";
        private const string MapName = "tutorial_map";
        private const string MapPath = "maps/tutorial_map.json";
        private const string PlacementPath = "placements/tutorial_map/tutorial_map.json";
        private const string ManifestFile = "manifest.json";
        private const string MarkerFile = "tutorial_marker.json";
        private const string ScenarioPath = "scenarios/elder_welcome.scenario";
        private const string LinaScenarioPath = "scenarios/lina_hello.scenario";
        private const string CharactersPath = "characters/" + Mod + ".json";
        private const string Grass = "village_pack/ground/grass";
        private const string ForestPath = "village_pack/ground/forest_path";
        private const string Cottage = "village_pack/building/cottage";
        private const string Oak = "village_pack/vegetation/oak";
        private const string Chest = "village_pack/chest";
        private const string Trigger = "village_pack/marker/trigger";
        private const string Villager = "village_pack/npc/villager";
        private const string Mantle = "rpg_plus/fur_mantle";
        private const string Village = "rpg_village";
        private const string Hero = "rpg_hero";
        private const string ElderScenario = "elder_welcome";
        private const string LinaScenario = "lina_hello";
        private const string ElderLine = "Добро пожаловать, путник.";
        private const string LinaLine = "Рада встрече. Я пойду с тобой.";
        private const string LinaName = "Lina";
        private const string LinaCharset = "village/villager";
        private const string Placeholder = "text \"…\"";
        private const string Body = "body";
        private const string SheetEntry = "sheet";
        private const float TimeoutSeconds = 300f;
        private const int KindItem = 1;
        private const int KindMap = 2;
        private const int KindDialog = 3;

        private static readonly RpgObjectForm[] Tabs = { RpgObjectForm.Surface, RpgObjectForm.Segment, RpgObjectForm.Sprite, RpgObjectForm.Actor };
        private static readonly Vector2I PlayCell = new(6, 8);
        private static readonly Vector2I ChestCell = new(9, 4);
        private static readonly Vector2I ChestStand = new(9, 5);
        private static readonly Vector2I VillagerCell = new(8, 3);
        private static readonly Vector2I VillagerStand = new(9, 3);
        private static readonly Vector2I LinaCell = new(14, 3);
        private static readonly Vector2I LinaStand = new(13, 3);
        private static readonly Vector2I DoorCell = new(15, 4);

        private readonly string shotsDir;
        private readonly List<string> failures = new();
        private readonly Queue<Func<bool>> script = new();
        private Dictionary<string, string> collectionsBefore;
        private Dictionary<string, string> gameBefore;
        private RpgEditorWindow window;
        private ScenarioEditorWindow scenarios;
        private CommandPaletteWindow palette;
        private CharacterEditorWindow characters;
        private string modPath;
        private string manifestBefore;
        private string chestId;
        private string doorId;
        private string villagerId;
        private string linaId;
        private string linaPlacement;
        private string sheetHero = Hero;
        private Vector2I joinCell;
        private int mantleBefore;
        private int pickSpawnX;
        private int pickSpawnY;
        private bool lineSeen;
        private bool lineJoined;
        private int step;
        private int passed;
        private double elapsed;
        private bool done;
        private bool cleanable;
        private string pendingShot;
        private Timer driverTimer;
        private int queuedReadiness;

        /// <summary>Creates the driver; <paramref name="shotsDir"/> (may be null) receives a picture of the steps.</summary>
        public TutorialPlayDriver(string shotsDir)
        {
            this.shotsDir = shotsDir;
            Name = "TutorialPlayDriver";
            driverTimer = new Timer { WaitTime = 0.05, Autostart = true };
            AddChild(driverTimer);
            driverTimer.Timeout += Tick;
        }

        private static RpgWorldView View => RpgWorldHost.Current?.View;

        private static CharacterData Roster => GameData.Instance.Runtime.CharacterData;

        private static IInventory Inventory => GameData.Instance.Runtime.Inventory;

        private static RpgFollower Follower0 => View?.Session?.Followers.Members.FirstOrDefault();

        private static bool BoxRunning => RpgInput.FindDialogBox()?.IsRunning == true;

        private void Tick()
        {
            if (done)
                return;
            if (pendingShot != null)
            {
                SaveShot(pendingShot);
                pendingShot = null;
            }
            elapsed += driverTimer.WaitTime;
            // The walking queue retains its original polling period and bounded key holds.
            driverTimer.WaitTime = step >= 47 ? 0.15 : 0.05;
            if (elapsed > TimeoutSeconds)
            {
                Fail("timeout at step " + step);
                Finish();
                return;
            }
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

        // Deferred UI/scene work must finish before the original assertions run.
        private bool WaitForReadyFrames(int token, bool ready, double timeoutSeconds = 30)
        {
            var now = global::Godot.Time.GetTicksMsec();
            if (readinessToken != token)
            {
                readinessToken = token;
                readinessStarted = now;
                readinessFrame = 0;
            }
            if (now - readinessStarted > timeoutSeconds * 1000)
                throw new global::System.TimeoutException($"readiness at step {token}: scene/UI did not become ready");
            if (!ready) { readinessFrame = 0; return false; }
            if (readinessFrame == 0) readinessFrame = global::Godot.Engine.GetProcessFrames();
            return global::Godot.Engine.GetProcessFrames() - readinessFrame >= 2;
        }

        private void RunStep()
        {
            if (step != 47 && !WaitForReadyFrames(step, Boot.IsReady && GetTree().CurrentScene != null && !GodotSceneService.IsTransitioning, 60)) return;
            switch (step)
            {
                case 0:
                    // The prologue is clicked through; the game's own map is on screen; the previous suite's marker is read.
                    if (!Boot.IsReady || AdvanceDialog() || RpgWorldHost.Current?.View == null)
                        return;
                    modPath = Path.Combine(ModRoots.ModsPath, Mod);
                    var markerPath = Path.Combine(modPath, MarkerFile);
                    if (!File.Exists(markerPath))
                    {
                        Fail("the mod " + Mod + " and its marker are missing: run tutorial_first_game first");
                        Finish();
                        return;
                    }
                    collectionsBefore = ReadMarker(markerPath);
                    manifestBefore = ModFileService.ReadText(Mod, ManifestFile);
                    cleanable = true;
                    gameBefore = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var path in GameFiles())
                        gameBefore[path] = ModFileService.ReadText(Game, path);
                    window = ToolWindow.Open<RpgEditorWindow>();
                    step++;
                    return;
                case 1:
                    if (window.Document == null)
                        return;
                    // Step 2: the mod is picked in the file selector's mod list, the "+" button makes the map.
                    var selector = window.FindChildren("*", "", true, false).OfType<ModFileSelector>().Single();
                    var modPicker = selector.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().First();
                    var at = Enumerable.Range(0, modPicker.ItemCount).FirstOrDefault(i => modPicker.GetItemText(i).StartsWith(Mod, StringComparison.Ordinal), -1);
                    Check(at >= 0, "the mod list of the map editor offers the tutorial mod");
                    if (at >= 0)
                        Choose(modPicker, at);
                    step++;
                    return;
                case 2:
                    var selectorNow = window.FindChildren("*", "", true, false).OfType<ModFileSelector>().Single();
                    var plus = selectorNow.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "+");
                    Check(plus != null, "the file selector has its + button");
                    plus?.EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    return;
                case 3:
                    var dialog = window.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().FirstOrDefault(d => d.Visible);
                    Check(dialog != null, "+ asks for the name of the map");
                    if (dialog == null)
                    {
                        step = 99;
                        return;
                    }
                    Type(dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(), MapName);
                    dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    step++;
                    return;
                case 4:
                    Check(window.Document != null && window.Document.MapId == MapName, "OK makes the map " + MapName + " in the editor");
                    Check(ModFileService.ReadText(Mod, MapPath) == null, "nothing is written before Save");
                    Press(window, "tools/rpg_editor/save");
                    step++;
                    return;
                case 5:
                    Check((ModFileService.ReadText(Mod, MapPath) ?? string.Empty).Contains("\"Kind\": \"rpg_map\""), "Save writes the map file");
                    step++;
                    return;
                case 6:
                    // Step 3: the ground. Surfaces tab, grass, Paint, then a stroke across each row of the map.
                    window.Palette.CurrentTab = Array.IndexOf(Tabs, RpgObjectForm.Surface);
                    ChooseRow(RpgObjectForm.Surface, "grass", Grass);
                    Press(window, "tools/rpg_editor/tool_paint");
                    Check(window.Tool == RpgEditorTool.Paint, "Paint is the tool");
                    for (var y = 1; y <= 7; y++)
                        Drag(new Vector2I(0, y), new Vector2I(15, y));
                    Check(Ground(0, 1) == Grass && Ground(15, 7) == Grass, "a drag over the map paints the grass");
                    step++;
                    return;
                case 7:
                    ChooseRow(RpgObjectForm.Surface, "forest_path", ForestPath);
                    Drag(new Vector2I(6, 9), new Vector2I(6, 5));
                    Check(Ground(6, 7) == ForestPath, "a path drawn from the bottom edge reaches the middle");
                    Press(window, "tools/rpg_editor/save");
                    step++;
                    return;
                case 8:
                    Check(window.Document.Data != null && (ModFileService.ReadText(Mod, MapPath) ?? string.Empty).Contains(Grass), "the saved map holds the ground");
                    // Step 4: the houses and trees from the Sprites tab, one click each.
                    window.Palette.CurrentTab = Array.IndexOf(Tabs, RpgObjectForm.Sprite);
                    ChooseRow(RpgObjectForm.Sprite, "cottage", Cottage);
                    Click(new Vector2I(3, 2));
                    Check(PlacedAt(new Vector2I(3, 2), Cottage), "Place puts the cottage on the cell clicked");
                    step++;
                    return;
                case 9:
                    ChooseRow(RpgObjectForm.Sprite, "oak", Oak);
                    Click(new Vector2I(10, 2));
                    Click(new Vector2I(11, 6));
                    Click(new Vector2I(2, 7));
                    Check(PlacedAt(new Vector2I(10, 2), Oak) && PlacedAt(new Vector2I(11, 6), Oak) && PlacedAt(new Vector2I(2, 7), Oak),
                        "three trees are placed");
                    Press(window, "tools/rpg_editor/save");
                    step++;
                    return;
                case 10:
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains(Cottage), "the placement file holds the cottage");
                    step++;
                    return;
                case 11:
                    // Step 5: a chest from village_pack, placed from the Sprites tab, selected with Select, gives the mantle.
                    window.Palette.CurrentTab = Array.IndexOf(Tabs, RpgObjectForm.Sprite);
                    ChooseRow(RpgObjectForm.Sprite, "chest", Chest);
                    Click(ChestCell);
                    chestId = window.SelectedId;
                    Check(chestId != null && PlacedAt(ChestCell, Chest), "Place puts the chest on the cell clicked");
                    window.SelectTool(RpgEditorTool.Select);
                    Click(ChestCell);
                    Check(chestId != null && window.SelectedId == chestId, "Select with a click chooses the chest");
                    Choose(PickerNamed("InteractKind"), KindItem);
                    Choose(PickerNamed("InteractItem"), IndexOfValue(PickerNamed("InteractItem"), Mantle));
                    var chestCommands = window.Scene.ObjectOf(chestId)?.Interact?.Commands;
                    Check(chestCommands is { Count: 1 } && chestCommands[0] == "action item.add id=" + Mantle + " count=1",
                        "Gives an item writes the mantle with count 1");
                    Shot("05_chest");
                    step++;
                    return;
                case 12:
                    Press(window, "tools/rpg_editor/save");
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains(Mantle), "Save writes the chest's interaction");
                    // The mod reaches village_pack through rpg_plus_game, so no dependency is added (the notice is not shown).
                    Check(!StatusHas(string.Format(ToolUi.Tr("tools/common/dependency_added"), "village_pack")),
                        "no dependency notice: the mod already reaches village_pack through rpg_plus_game");
                    Check(ModFileService.ReadText(Mod, ManifestFile) == manifestBefore, "the manifest keeps its text after the save");
                    step++;
                    return;
                case 13:
                    // Step 6: a trigger on the right edge, leads to rpg_village; When: stepping onto it.
                    window.Palette.CurrentTab = Array.IndexOf(Tabs, RpgObjectForm.Sprite);
                    ChooseRow(RpgObjectForm.Sprite, "trigger", Trigger);
                    Click(DoorCell);
                    doorId = window.SelectedId;
                    Check(doorId != null && PlacedAt(DoorCell, Trigger), "Place puts the trigger on the edge cell");
                    Choose(PickerNamed("InteractKind"), KindMap);
                    Choose(PickerNamed("InteractTrigger"), 1);
                    Choose(PickerNamed("InteractMap"), IndexOfValue(PickerNamed("InteractMap"), Village));
                    var door = window.Scene.ObjectOf(doorId)?.Interact;
                    Check(door?.Teleport?.Map == Village && door.Trigger == RpgInteractTrigger.Step, "the door leads to rpg_village when stepped on");
                    Shot("06_door");
                    step++;
                    return;
                case 14:
                    Press(window, "tools/rpg_editor/save");
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains("\"Map\": \"" + Village + "\""), "Save writes the door before the pick");
                    step++;
                    return;
                case 15:
                    // The target cell is picked on the village: the village opens, the click picks the cell, the map comes back.
                    var pickButton = NamedAs<Button>("InteractPick");
                    Check(pickButton != null, "the door has the Pick the cell on the map button");
                    pickButton?.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(window.Document != null && window.Document.MapId == Village, "the pick opens rpg_village");
                    pickSpawnX = window.Document?.Data["Spawn"]?["X"]?.GetValue<int>() ?? 0;
                    pickSpawnY = window.Document?.Data["Spawn"]?["Y"]?.GetValue<int>() ?? 0;
                    step++;
                    return;
                case 16:
                    Click(new Vector2I(pickSpawnX, pickSpawnY));
                    Check(window.Document != null && window.Document.MapId == MapName, "a click on the village returns to the tutorial map");
                    var target = window.Scene.ObjectOf(doorId)?.Interact?.Teleport;
                    Check(target != null && target.Map == Village && target.X == pickSpawnX && target.Y == pickSpawnY,
                        $"the door leads to the cell picked on {Village}, ({pickSpawnX}, {pickSpawnY})");
                    step++;
                    return;
                case 17:
                    Press(window, "tools/rpg_editor/save");
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains("\"X\": " + pickSpawnX), "Save writes the picked cell");
                    Shot("06_door_pick");
                    step++;
                    return;
                case 18:
                    // Step 7: the scenario elder_welcome in the scenario editor (New, a name, the palette's text line, Save).
                    scenarios = ToolWindow.Open<ScenarioEditorWindow>();
                    step++;
                    return;
                case 19:
                    if (!scenarios.IsInsideTree())
                        return;
                    Press(scenarios, "tools/scenario_editor/new");
                    step++;
                    return;
                case 20:
                    FillNewScenario(ElderScenario);
                    step++;
                    return;
                case 21:
                    Check(ModFileService.ReadText(Mod, ScenarioPath) != null, "New makes scenarios/" + ElderScenario + ".scenario in the mod");
                    Check(scenarios.OpenKey != null, "the new scenario is open in the editor");
                    FocusScenarioText();
                    palette = ToolWindow.Open<CommandPaletteWindow>();
                    step++;
                    return;
                case 22:
                    if (!palette.IsInsideTree())
                        return;
                    palette.Choose("command:text");
                    palette.SetValue("text", ElderLine);
                    Check(palette.Line.Contains(ElderLine), "the palette writes the greeting line (" + palette.Line + ")");
                    Press(palette, "tools/schema/palette/insert");
                    step++;
                    return;
                case 23:
                    Check(ScenarioText().Contains(ElderLine), "the palette's Insert puts the line into the scenario's text");
                    RemoveLines(Placeholder);
                    Check(!ScenarioText().Contains(Placeholder), "the placeholder line is replaced by the greeting");
                    Shot("07_scenario");
                    Press(scenarios, "tools/common/save");
                    step++;
                    return;
                case 24:
                    Check((ModFileService.ReadText(Mod, ScenarioPath) ?? string.Empty).Contains(ElderLine), "Save writes the greeting into the scenario");
                    CloseWindows();
                    step++;
                    return;
                case 25:
                    // The villager: an actor from the Actors tab, Interact opens the scenario.
                    window.Palette.CurrentTab = Array.IndexOf(Tabs, RpgObjectForm.Actor);
                    ChooseRow(RpgObjectForm.Actor, "villager", Villager);
                    Click(VillagerCell);
                    villagerId = window.SelectedId;
                    Check(villagerId != null && PlacedAt(VillagerCell, Villager), "Place puts the villager on the cell clicked");
                    window.SelectTool(RpgEditorTool.Select);
                    Click(VillagerCell);
                    Choose(PickerNamed("InteractKind"), KindDialog);
                    Choose(PickerNamed("InteractScenario"), IndexOfValue(PickerNamed("InteractScenario"), ElderScenario));
                    Check(window.Scene.ObjectOf(villagerId)?.Interact?.Scenario == ElderScenario, "the dialog picker sets the villager's scenario");
                    Shot("07_villager");
                    step++;
                    return;
                case 26:
                    Press(window, "tools/rpg_editor/save");
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains(ElderScenario), "Save writes the villager's dialog");
                    step++;
                    return;
                case 27:
                    // Step 8: the map editor stays open; its Characters tab takes Lina live when the characters editor saves her (plan 14.G17).
                    step++;
                    return;
                case 28:
                    characters = ToolWindow.Open<CharacterEditorWindow>();
                    step++;
                    return;
                case 29:
                    if (!characters.IsInsideTree() || characters.Document == null)
                        return;
                    var modChoice = characters.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().First();
                    Choose(modChoice, IndexOfText(modChoice, Mod));
                    Press(characters, "tools/entity_editor/new");
                    step++;
                    return;
                case 30:
                    linaId = characters.SelectedId;
                    Check(linaId != null && linaId.StartsWith(Mod, StringComparison.Ordinal), "New adds a character of the mod (" + linaId + ")");
                    Type(NameEdit(), LinaName);
                    ClickButton(RowButton(characters, "tools/characters_editor/charset", "…"));
                    step++;
                    return;
                case 31:
                    var picker = characters.FindChildren("*", "", true, false).OfType<PicturePickerDialog>().FirstOrDefault(d => d.Visible);
                    Check(picker != null, "the charset's … button opens the picture picker");
                    if (picker == null)
                    {
                        step = 99;
                        return;
                    }
                    var pictures = picker.FindChildren("*", "ItemList", true, false).OfType<ItemList>().First();
                    var pictureRow = Enumerable.Range(0, pictures.ItemCount).FirstOrDefault(i => pictures.GetItemTooltip(i) == LinaCharset, -1);
                    Check(pictureRow >= 0, "the picture picker offers " + LinaCharset);
                    if (pictureRow >= 0)
                        pictures.EmitSignal(ItemList.SignalName.ItemSelected, (long)pictureRow);
                    picker.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    step++;
                    return;
                case 32:
                    var charsetEdit = RowOf(characters, "tools/characters_editor/charset").OfType<LineEdit>().First();
                    Check(charsetEdit.Text == LinaCharset, "the picked charset is in the form");
                    var join = characters.FindChildren("JoinsParty", "", true, false).OfType<CheckBox>().FirstOrDefault();
                    Check(join != null && !join.ButtonPressed, "Joins the party is off for a new character");
                    if (join != null)
                        join.ButtonPressed = true;
                    Check(linaId != null && CompanionScenario.Exists(Mod, linaId), "Joins the party writes Lina's join scenario");
                    Shot("08_lina_form");
                    step++;
                    return;
                case 33:
                    Press(characters, "tools/common/save");
                    Check((ModFileService.ReadText(Mod, CharactersPath) ?? string.Empty).Contains(linaId ?? "-"), "Save writes Lina into the mod's characters file");
                    characters.QueueFree();
                    characters = null;
                    step++;
                    return;
                case 34:
                    scenarios = ToolWindow.Open<ScenarioEditorWindow>();
                    step++;
                    return;
                case 35:
                    if (!scenarios.IsInsideTree())
                        return;
                    Press(scenarios, "tools/scenario_editor/new");
                    step++;
                    return;
                case 36:
                    FillNewScenario(LinaScenario);
                    step++;
                    return;
                case 37:
                    Check(ModFileService.ReadText(Mod, LinaScenarioPath) != null, "New makes scenarios/" + LinaScenario + ".scenario in the mod");
                    palette = ToolWindow.Open<CommandPaletteWindow>();
                    step++;
                    return;
                case 38:
                    if (!palette.IsInsideTree())
                        return;
                    FocusScenarioText();
                    palette.Choose("command:text");
                    palette.SetValue("text", LinaLine);
                    Press(palette, "tools/schema/palette/insert");
                    step++;
                    return;
                case 39:
                    Check(ScenarioText().Contains(LinaLine), "the palette's Insert puts Lina's line into her scenario's text");
                    // The template stays as it is: its end line no longer stops the join lines, they run after it (plan 14.G17).
                    Check(ScenarioText().Split('\n').Any(l => l.Trim() == "end"), "the template's end line stays in Lina's scenario");
                    Press(scenarios, "tools/common/save");
                    step++;
                    return;
                case 40:
                    Check((ModFileService.ReadText(Mod, LinaScenarioPath) ?? string.Empty).Contains(LinaLine), "Save writes Lina's line");
                    CloseWindows();
                    step++;
                    return;
                case 41:
                    // The map editor was never closed: it still shows the tutorial map, and its Characters tab lists Lina (plan 14.G17).
                    Check(window != null && window.IsInsideTree() && window.Document != null && window.Document.MapId == MapName,
                        "the map editor is still open on the tutorial map");
                    step++;
                    return;
                case 42:
                    if (window.Document == null || window.Document.MapId != MapName)
                        return;
                    window.Palette.CurrentTab = Tabs.Length;
                    ChooseListRow("Palette_Characters", "lina", linaId);
                    Check(window.Tool == RpgEditorTool.Place, "choosing Lina in the Characters tab makes her the object to place");
                    step++;
                    return;
                case 43:
                    Click(LinaCell);
                    linaPlacement = window.SelectedId;
                    Check(linaPlacement != null && window.Document.Placements.Any(r => r.Id == linaPlacement), "Place puts Lina next to the door");
                    window.SelectTool(RpgEditorTool.Select);
                    Click(LinaCell);
                    Choose(PickerNamed("InteractKind"), KindDialog);
                    Choose(PickerNamed("InteractScenario"), IndexOfValue(PickerNamed("InteractScenario"), LinaScenario));
                    Check(window.Scene.ObjectOf(linaPlacement)?.Interact?.Scenario == LinaScenario, "the dialog picker sets Lina's scenario");
                    var joinCommands = window.Scene.ObjectOf(linaPlacement)?.Interact?.Commands;
                    Check(joinCommands != null && joinCommands.Contains("action party.add id=" + linaId),
                        "the join lines follow the dialog (" + string.Join(" | ", joinCommands ?? new List<string>()) + ")");
                    Shot("08_lina_inspector");
                    step++;
                    return;
                case 44:
                    Press(window, "tools/rpg_editor/save");
                    Check(linaId != null && RpgCharacterActors.JoinsParty(Mod, linaId), "Lina still joins the party after her dialog");
                    Check((ModFileService.ReadText(Mod, PlacementPath) ?? string.Empty).Contains(LinaScenario), "Save writes Lina's dialog");
                    step++;
                    return;
                case 45:
                    // Play from here: the running game enters the map at the cell clicked last.
                    window.SelectTool(RpgEditorTool.Select);
                    Click(PlayCell);
                    Press(window, "tools/rpg_editor/play");
                    window = null;
                    step = 46;
                    return;
                case 46:
                    if (View?.Map?.ID != MapName || GodotSceneService.IsTransitioning || AdvanceDialog())
                        return;
                    BuildGame();
                    step = 47;
                    return;
                case 47:
                    if (script.Count == 0)
                    {
                        step = 99;
                        return;
                    }
                    var next = script.Peek();
                    if (next() && script.Count > 0)
                        script.Dequeue();
                    return;
                case 99:
                    Finish();
                    return;
            }
        }

        /// <summary>
        /// The game part of step 9 as a queue: real key events for the walking (a key is released on the cell the step
        /// reaches), E for the chest, the villager and Lina, then the door last.
        /// </summary>
        private void BuildGame()
        {
            Do(CloseEditors);
            WaitForWorldFrames();
            Do(() => Check(View.Player.Cell == PlayCell, "Play from here enters the map on the cell clicked (" + PlayCell + "), stands at " + View.Player.Cell));
            Do(() => mantleBefore = Inventory.CheckCount(Mantle));

            // The chest: round the cottage to the cell below the chest, face it, use it.
            Step(Key.W, 6, 7);
            Step(Key.D, 7, 7);
            Step(Key.D, 8, 7);
            Step(Key.D, 9, 7);
            Step(Key.W, 9, 6);
            Step(Key.W, ChestStand.X, ChestStand.Y);
            Bump(Key.W, RpgDirection.Up);
            Do(() => Tap(Key.E));
            Until(() => Inventory.CheckCount(Mantle) > mantleBefore, "E on the chest puts the mantle in the bag", 40);
            Do(() => Check(Inventory.CheckCount(Mantle) == mantleBefore + 1, "E on the chest gives the mantle, count " + Inventory.CheckCount(Mantle)));
            WaitForWorldFrames();
            Do(() => Shot("09_chest"));

            // The villager: round the chest to his right side, face him, talk: his dialog plays its line.
            Step(Key.D, 10, 5);
            Step(Key.W, 10, 4);
            Step(Key.W, 10, 3);
            Step(Key.A, VillagerStand.X, VillagerStand.Y);
            Talk(ElderLine, "the villager");
            Do(() => Check(lineSeen, "the villager's dialog plays its line"));
            WaitForWorldFrames();

            // Lina: east along the row, face her from the cell beside her, talk: her line plays before she joins.
            Step(Key.D, 10, 3);
            Step(Key.D, 11, 3);
            Step(Key.D, 12, 3);
            Step(Key.D, LinaStand.X, LinaStand.Y);
            Do(() => Check(!Roster.BattleSet.Contains(linaId ?? "-"), "Lina is not in the party before she is talked to"));
            Talk(LinaLine, "Lina");
            Do(() => Shot("09_lina_line"));
            Do(() => Check(lineSeen && !lineJoined, "Lina says her line before she joins"));
            Until(() => Roster.BattleSet.Contains(linaId ?? "-"), "Lina joins the party after her line", 40);
            Until(() => View.Find(linaPlacement)?.Visible == false, "Lina leaves the map once she joined", 40);
            Do(() => joinCell = Follower0?.Cell ?? default);
            Do(() => Check(View.Session.Followers.Members.Count == 1, "one follower walks behind the hero, got " + View.Session.Followers.Members.Count));

            // She follows: the hero steps down and left, she stays close behind on a new cell.
            Step(Key.S, 13, 4);
            Step(Key.A, 12, 4);
            Do(() => CheckFollowing("after two steps"));
            Step(Key.D, 13, 4);
            Step(Key.D, 14, 4);

            // The door last: stepping onto it takes the player to rpg_village.
            Step(Key.D, DoorCell.X, DoorCell.Y);
            Until(() => View?.Map?.ID == Village && !GodotSceneService.IsTransitioning, "stepping on the door takes the player to " + Village, 80);
            WaitForWorldFrames();
            Do(() => Check(View.Map.ID == Village, "the door changes the map to " + Village));
            Do(() => Shot("09_village"));

            // Step 10: the character sheet, the hero's empty body slot, the mantle from the bag.
            Do(() => UiRegistry.Toggle(SheetEntry));
            Until(() => SheetPanel()?.Visible == true, "the toolbar entry sheet opens the character sheet", 20);
            WaitForWorldFrames();
            Do(() => ClickButton(MemberButton(sheetHero)));
            WaitForWorldFrames();
            Do(() => Check(SlotButton(Body, "Equip") != null, "the empty body slot offers Equip"));
            Do(() => ClickButton(SlotButton(Body, "Equip")));
            WaitForWorldFrames();
            Do(() => Check(Picker(Body) != null && PickButton(Mantle) != null, "Equip opens the bag's picker, the mantle is offered"));
            Do(() => Shot("10_picker"));
            Do(() => ClickButton(PickButton(Mantle)));
            WaitForWorldFrames();
            Do(() => Check(EquipmentService.ItemIn(sheetHero, Body) == Mantle, "the mantle is worn in the body slot"));
            Do(() => Check(Inventory.CheckCount(Mantle) == 0, "the worn mantle left the bag"));
            Do(() => Check(StatsService.Sheet(sheetHero).Get("def") - StatsService.Sheet(sheetHero).GetBase("def") == 3, "the mantle's +3 defence reaches the sheet"));
            Do(() => Shot("10_equipped"));
        }

        /// <summary>A walking step: the key is held until the hero's reserved cell is the target, then released.</summary>
        private void Step(Key key, int x, int y)
        {
            var to = new Vector2I(x, y);
            var ticks = 0;
            Do(() => Press(key, true));
            // The cell is reserved when the step starts: the key is released in the same tick that sees it, before the next step.
            script.Enqueue(() =>
            {
                if (View.Player.Cell == to)
                {
                    Press(key, false);
                    return true;
                }
                if (++ticks < 80)
                    return false;
                var focus = GetViewport().GuiGetFocusOwner();
                Press(key, false);
                Fail("the hero does not reach " + to + ", stands at " + View.Player.Cell + " facing " + View.Player.Facing + ", moving " + View.Player.IsMoving
                    + ", held " + RpgInput.HeldDirection() + ", blocked " + RpgInput.IsWalkBlocked(RpgWorldHost.Current) + ", focus " + (focus == null ? "-" : focus.GetType().Name)
                    + ", can enter " + View.CanEnter(to));
                script.Clear();
                return true;
            });
        }

        /// <summary>A key pressed against a blocked cell: the player turns to face it and stays.</summary>
        private void Bump(Key key, RpgDirection facing)
        {
            Do(() => Press(key, true));
            Settle(2);
            Do(() => Press(key, false));
            Do(() => Check(View.Player.Facing == facing && View.Player.Cell == ChestStand, "a blocked key turns the player to " + facing));
        }

        /// <summary>Talks to the target in front of the player: E starts it, E clicks through it; the line seen in the box is recorded.</summary>
        private void Talk(string line, string who)
        {
            Do(() =>
            {
                lineSeen = false;
                lineJoined = false;
                Tap(Key.E);
            });
            Until(() => BoxRunning, "a dialog starts with " + who, 20);
            var ticks = 0;
            script.Enqueue(() =>
            {
                var box = RpgInput.FindDialogBox();
                if (box == null || !box.IsRunning)
                    return true;
                if (!lineSeen && DialogSays(line))
                {
                    lineSeen = true;
                    lineJoined = linaId != null && Roster.BattleSet.Contains(linaId);
                }
                if (++ticks > 400)
                {
                    Fail("the dialog with " + who + " does not end");
                    script.Clear();
                    return true;
                }
                if (box.IsWaitingAnswer)
                    box.TrySelectVariant(0);
                else
                    Tap(Key.E);
                return false;
            });
        }

        /// <summary>The follower stands on a new cell next to the hero or one step behind it.</summary>
        private void CheckFollowing(string when)
        {
            var follower = Follower0;
            var hero = View.Player.Cell;
            var near = follower != null && Math.Abs(follower.Cell.X - hero.X) + Math.Abs(follower.Cell.Y - hero.Y) <= 2;
            Check(follower != null && near && follower.Cell != joinCell, "Lina follows the hero " + when + ", at " + (follower?.Cell.ToString() ?? "-") + ", hero " + hero);
        }

        private void Do(Action action) => script.Enqueue(() => { action(); return true; });

        private void WaitForWorldFrames() => script.Enqueue(WorldFramesReady);

        private bool WorldFramesReady()
        {
            if (!WaitForReadyFrames(100000 + queuedReadiness,
                View?.Map != null && !GodotSceneService.IsTransitioning && !View.Player.IsMoving)) return false;
            queuedReadiness++;
            return true;
        }

        private void Settle(int ticks)
        {
            var waited = 0;
            script.Enqueue(() => ++waited >= ticks);
        }

        private void Until(Func<bool> condition, string what, int maxTicks)
        {
            var ticks = 0;
            script.Enqueue(() =>
            {
                if (condition())
                    return true;
                if (++ticks < maxTicks)
                    return false;
                Fail("timed out: " + what);
                script.Clear();
                return true;
            });
        }

        /// <summary>Frees every editor window the game still shows, so the walking is not blocked by them.</summary>
        private void CloseEditors()
        {
            var open = GetTree().Root.FindChildren("*", "", true, false)
                .Where(n => n is RpgEditorWindow || n is ScenarioEditorWindow || n is CharacterEditorWindow || n is CommandPaletteWindow)
                .ToList();
            foreach (var node in open)
                node.QueueFree();
        }

        /// <summary>Frees the editor windows this run opened (the map editor is freed by the game's Play).</summary>
        private void CloseWindows()
        {
            if (scenarios != null)
                scenarios.QueueFree();
            if (palette != null)
                palette.QueueFree();
            scenarios = null;
            palette = null;
        }

        /// <summary>The New scenario dialog: the mod chosen in its list, the name typed, Create.</summary>
        private void FillNewScenario(string name)
        {
            var dialog = scenarios.FindChildren("*", "ConfirmationDialog", true, false).OfType<ConfirmationDialog>().FirstOrDefault(d => d.Visible);
            Check(dialog != null, "New asks for the mod and the name");
            if (dialog == null)
                return;
            var modList = dialog.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().First();
            Choose(modList, IndexOfText(modList, Mod));
            Type(dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(), name);
            dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
        }

        /// <summary>
        /// The scenario's text box takes the focus as a click does: the palette inserts into the text box that had the focus last.
        /// </summary>
        private void FocusScenarioText()
        {
            var edit = scenarios?.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().FirstOrDefault(t => t.IsVisibleInTree());
            Check(edit != null, "the scenario's text box is on the editor");
            edit?.GrabFocus();
        }

        /// <summary>Removes the lines of the scenario's text box that equal one of <paramref name="lines"/>, as an author deletes them.</summary>
        private void RemoveLines(params string[] lines)
        {
            var edit = scenarios?.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().FirstOrDefault(t => t.IsVisibleInTree());
            if (edit == null)
                return;
            var kept = edit.Text.Split('\n').Where(l => !lines.Contains(l.Trim())).ToArray();
            edit.Text = string.Join("\n", kept);
            edit.EmitSignal(TextEdit.SignalName.TextChanged);
        }

        /// <summary>The text of the scenario's text box (empty when none is shown).</summary>
        private string ScenarioText()
            => scenarios?.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>().FirstOrDefault(t => t.IsVisibleInTree())?.Text ?? string.Empty;

        /// <summary>Chooses a row of a palette tab the way a click does: the search text is typed, then the row is selected.</summary>
        private void ChooseRow(RpgObjectForm form, string search, string id)
            => ChooseListRow("Palette_" + form, search, id);

        /// <summary>Chooses the row whose id is <paramref name="id"/> in the palette list <paramref name="listName"/> (search typed first).</summary>
        private void ChooseListRow(string listName, string search, string id)
        {
            var list = window.Palette.FindChild(listName, true, false) as FilteredList;
            if (list == null)
            {
                Fail("the palette has a list " + listName);
                return;
            }
            Type(list.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().First(), search);
            var items = list.FindChildren("*", "ItemList", true, false).OfType<ItemList>().First();
            for (var i = 0; i < items.ItemCount; i++)
            {
                if (items.GetItemTooltip(i) != id)
                    continue;
                items.EmitSignal(ItemList.SignalName.ItemSelected, (long)i);
                return;
            }
            Fail("the palette offers " + id);
        }

        private static void Choose(OptionButton picker, int index)
        {
            picker.Select(index);
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }

        /// <summary>Presses the button of that text (a translation key) on the window; the check fails when there is none.</summary>
        private void Press(Node root, string key)
        {
            var text = ToolUi.Tr(key);
            var button = root.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
            Check(button != null, "the button '" + text + "' is on the editor");
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private static void ClickButton(Button button)
        {
            if (button == null)
            {
                GameLogger.Log("TEST", "[FAIL] click on a button that is not on the screen", LogLevel.Error);
                return;
            }
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private T NamedAs<T>(string name) where T : class => window?.FindChild(name, true, false) as T;

        /// <summary>The inspector's picker of that name (the window rebuilds it on every change, so it is looked up each time).</summary>
        private OptionButton PickerNamed(string name) => NamedAs<OptionButton>(name);

        /// <summary>Index of the picker entry whose id (metadata) is <paramref name="value"/>, or -1.</summary>
        private static int IndexOfValue(OptionButton picker, string value)
        {
            for (var i = 0; i < picker.ItemCount; i++)
            {
                if (picker.GetItemMetadata(i).AsString() == value)
                    return i;
            }
            return -1;
        }

        /// <summary>Index of the picker entry whose text is <paramref name="text"/>, or -1.</summary>
        private static int IndexOfText(OptionButton picker, string text)
            => Enumerable.Range(0, picker.ItemCount).FirstOrDefault(i => picker.GetItemText(i) == text, -1);

        /// <summary>The controls of the form row labelled <paramref name="labelKey"/>.</summary>
        private static IEnumerable<Node> RowOf(Node form, string labelKey)
        {
            var text = ToolUi.Tr(labelKey);
            return form.FindChildren("*", "Label", true, false).OfType<Label>().First(l => l.Text == text).GetParent().FindChildren("*", "", true, false);
        }

        /// <summary>The button of a form row with that text.</summary>
        private static Button RowButton(Node form, string labelKey, string text)
            => RowOf(form, labelKey).OfType<Button>().FirstOrDefault(b => b.Text == text);

        /// <summary>The name text row of the characters form: the first locale field of the current language.</summary>
        private LineEdit NameEdit()
        {
            var language = LanguageCode.Normalize(TranslationServer.GetLocale()) ?? "ru";
            var label = string.Format(ToolUi.Tr("tools/entity_editor/text_in"), language);
            return characters.FindChildren("*", "", true, false).OfType<Label>().First(l => l.Text == label && l.IsVisibleInTree())
                .GetParent().GetChildren().OfType<LineEdit>().First();
        }

        /// <summary>Whether the window's status line (or any label on it) shows this text.</summary>
        private bool StatusHas(string text)
            => window != null && window.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text != null && l.Text.Contains(text));

        /// <summary>Whether a dialog box shows this line now.</summary>
        private static bool DialogSays(string text)
        {
            var box = RpgInput.FindDialogBox();
            return box != null && box.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == text);
        }

        private static StatSheetPanel SheetPanel()
            => UiHost.Instance != null && UiHost.Instance.TryGetInstance(SheetEntry, out var control) ? control as StatSheetPanel : null;

        private static Button MemberButton(string id)
            => SheetPanel()?.FindChild("Member_" + id.Replace('/', '_'), true, false) as Button;

        private static Button SlotButton(string slot, string name)
            => SheetPanel()?.FindChild("Slot_" + slot, true, false)?.FindChild(name, true, false) as Button;

        private static Control Picker(string slot)
            => SheetPanel()?.FindChild("Picker_" + slot, true, false) as Control;

        private static Button PickButton(string itemId)
            => SheetPanel()?.FindChild("Pick_" + itemId.Replace('/', '_'), true, false) as Button;

        private bool PlacedAt(Vector2I cell, string id)
            => window.Document.Placements.Any(r => r.Data["X"]?.GetValue<int>() == cell.X && r.Data["Y"]?.GetValue<int>() == cell.Y
                && r.Data["Object"]?.ToString() == id);

        private string Ground(int x, int y) => window.Document.ObjectAt(RpgStrokeLayer.Ground, x, y);

        private static void Type(LineEdit edit, string text)
        {
            edit.Text = text;
            edit.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        /// <summary>A left click on a canvas cell: press and release through the canvas' own input.</summary>
        private void Click(Vector2I cell)
        {
            var at = window.Canvas.CanvasPointOf(cell);
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = at });
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = at });
        }

        /// <summary>
        /// A left drag from one cell to another: press, a motion event on every cell the pointer passes (a stroke paints the
        /// cells between its events), release.
        /// </summary>
        private void Drag(Vector2I from, Vector2I to)
        {
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = window.Canvas.CanvasPointOf(from) });
            var count = Math.Max(Math.Abs(to.X - from.X), Math.Abs(to.Y - from.Y));
            for (var i = 1; i <= count; i++)
            {
                var cell = new Vector2I(from.X + (to.X - from.X) * i / count, from.Y + (to.Y - from.Y) * i / count);
                window.Canvas._GuiInput(new InputEventMouseMotion { Position = window.Canvas.CanvasPointOf(cell), ButtonMask = MouseButtonMask.Left });
            }
            window.Canvas._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = window.Canvas.CanvasPointOf(to) });
        }

        private static void Press(Key key, bool pressed)
            => Input.ParseInputEvent(new InputEventKey { PhysicalKeycode = key, Keycode = key, Pressed = pressed });

        private static void Tap(Key key)
        {
            Press(key, true);
            Press(key, false);
        }

        /// <summary>Plays the dialog box one press per tick; true while it still runs.</summary>
        private static bool AdvanceDialog()
        {
            var box = RpgInput.FindDialogBox();
            if (box == null || !box.IsRunning)
                return false;
            if (box.IsWaitingAnswer)
                box.TrySelectVariant(0);
            else
                box.NextAction();
            return true;
        }

        /// <summary>The rpg_plus_game files compared at the end: the JSON, scenario and locale files of the game's mods.</summary>
        private static List<string> GameFiles()
            => ModFileService.ListFiles(Game, string.Empty, ".json")
                .Concat(ModFileService.ListFiles(Game, string.Empty, ".scenario"))
                .Concat(ModFileService.ListFiles(Game, string.Empty, ".properties"))
                .ToList();

        /// <summary>The user collections as the marker of the previous suite holds them: relative path to base64 bytes.</summary>
        private static Dictionary<string, string> ReadMarker(string path)
        {
            var root = JsonNode.Parse(File.ReadAllText(path))?["user_collections"] as JsonObject ?? new JsonObject();
            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in root)
                files[pair.Key] = pair.Value?.ToString() ?? string.Empty;
            return files;
        }

        /// <summary>Puts the user collections back as the marker holds them; a file the marker does not know goes.</summary>
        private static void RestoreCollections(Dictionary<string, string> files)
        {
            var root = ModCollectionStore.UserPath;
            if (files == null || !Directory.Exists(root))
                return;
            foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!files.ContainsKey(Path.GetRelativePath(root, file).Replace('\\', '/')))
                    File.Delete(file);
            }
            foreach (var pair in files)
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

        /// <summary>Asks for a picture of the state the next tick shows: the capture reads the last drawn frame, so it waits a tick.</summary>
        private void Shot(string name)
        {
            if (string.IsNullOrEmpty(shotsDir) || DisplayServer.GetName() == "headless")
                return;
            pendingShot = name;
        }

        private void SaveShot(string name)
        {
            DirAccess.MakeDirRecursiveAbsolute(shotsDir);
            ViewportCapture.SavePng(GetViewport(), shotsDir.TrimEnd('/') + "/tutorial_" + name + ".png");
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

        /// <summary>Removes the mod and puts the collections back; the rpg_plus_game files are compared with their text from the start.</summary>
        private void Finish()
        {
            done = true;
            if (gameBefore != null)
            {
                var changed = gameBefore.Where(f => ModFileService.ReadText(Game, f.Key) != f.Value).Select(f => f.Key).ToList();
                Check(changed.Count == 0, "every file of rpg_plus_game is as it was" + (changed.Count == 0 ? string.Empty : ": " + string.Join(", ", changed)));
                var added = GameFiles().Where(f => !gameBefore.ContainsKey(f)).ToList();
                Check(added.Count == 0, "rpg_plus_game gained no file" + (added.Count == 0 ? string.Empty : ": " + string.Join(", ", added)));
            }
            if (cleanable)
            {
                try
                {
                    CloseEditors();
                    if (Directory.Exists(modPath))
                        Directory.Delete(modPath, true);
                    RestoreCollections(collectionsBefore);
                    ModManager.Instance.GetResolver<PatchResolver>()?.ClearCache();
                    ModManager.Instance.GetResolver<EntityResolver>()?.ClearCache();
                }
                catch (Exception ex)
                {
                    Fail("cleanup: " + ex.Message);
                }
            }
            GameLogger.Log("TEST", $"[TEST_RESULT] name=tutorial_first_game_play passed={passed} failed={failures.Count}");
            ApplicationExit.Quit(GetTree(), failures.Count == 0 ? 0 : 1);
        }
    }
}
