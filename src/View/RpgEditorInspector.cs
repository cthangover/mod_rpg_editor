namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.Json.Nodes;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Interface.Theming;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.Schemas;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// The right-hand column: the selected placement (its object, its cell, whether it is the map's own or another mod's
    /// and patched or deleted there, its interaction, and the actions on it), the map's properties (variant, atmosphere,
    /// spawn point), the teleport links of the map, and — only when the <c>rpg__battles</c> integration is mounted — the
    /// encounters. It holds no document: every change goes out through an event, and the window applies it and calls
    /// <see cref="Show"/> again. The column is rebuilt on each call, so no control outlives its state.
    /// <para/>
    /// Every reference is chosen from a list (plan 14.G5): the interaction's item, map, scenario and shop, the map's variant and
    /// atmosphere, the enemies of an encounter group. A change of an interaction is written at once, so the canvas shows it
    /// (the arrow of a teleport, the broken target in red) before the author saves.
    /// </summary>
    public partial class RpgEditorInspector : VBoxContainer
    {
        private const string ScenarioSuffix = ".scenario";
        private const string ItemKind = "item";
        private const string MapKind = "rpg_map";
        private const string ShopKind = "shop";
        private const string AtmosphereKind = "atmosphere";
        private const string CharacterKind = "character";
        private const string ScenarioGroup = "scenarios";
        private static readonly RpgInteractKind[] PickableKinds =
        {
            RpgInteractKind.None, RpgInteractKind.Item, RpgInteractKind.Map, RpgInteractKind.Dialog, RpgInteractKind.Shop,
        };
        private static readonly RpgDirection[] Directions = { RpgDirection.Down, RpgDirection.Left, RpgDirection.Right, RpgDirection.Up };

        // Whole keys, not "dir_" + name: the locale atlas finds only literal keys.
        private static string DirKey(RpgDirection dir) => dir switch
        {
            RpgDirection.Left => "tools/rpg_editor/dir_left",
            RpgDirection.Right => "tools/rpg_editor/dir_right",
            RpgDirection.Up => "tools/rpg_editor/dir_up",
            _ => "tools/rpg_editor/dir_down",
        };

        private readonly List<RpgInteractKind> kindChoices = new();
        private OptionButton kindPicker;
        private OptionButton triggerPicker;
        private OptionButton itemPicker;
        private SpinBox countSpin;
        private OptionButton mapPicker;
        private SpinBox xSpin;
        private SpinBox ySpin;
        private OptionButton dirPicker;
        private OptionButton scenarioPicker;
        private OptionButton shopPicker;

        /// <summary>Snapshot the column shows: nothing selected and no document is fine.</summary>
        public sealed class State
        {
            /// <summary>The document (null when no map is open).</summary>
            public RpgMapDocument Document { get; init; }

            /// <summary>The selected placement's id, or null.</summary>
            public string Placement { get; init; }

            /// <summary>The selected placement's effective object id, or null.</summary>
            public string ObjectId { get; init; }

            /// <summary>The selected placement's interaction as the game reads it (its object's, with the placement's override), or null.</summary>
            public RpgInteract Interact { get; init; }

            /// <summary>Teleports that leave this map.</summary>
            public IReadOnlyList<RpgTeleportLink> Outgoing { get; init; } = Array.Empty<RpgTeleportLink>();

            /// <summary>Teleports that lead into this map.</summary>
            public IReadOnlyList<RpgTeleportLink> Incoming { get; init; } = Array.Empty<RpgTeleportLink>();

            /// <summary>Map size shown in the map section.</summary>
            public Vector2I MapSize { get; init; }

            /// <summary>Variant names the loaded objects define, for the map's variant picker.</summary>
            public IReadOnlyList<string> Variants { get; init; } = Array.Empty<string>();

            /// <summary>True when the rpg__battles integration is mounted: the encounters section is shown.</summary>
            public bool Encounters { get; init; }
        }

        /// <summary>Move of the selected placement to a cell (id, x, y).</summary>
        public event Action<string, int, int> PlacementMoved;

        /// <summary>Delete of the selected placement (id).</summary>
        public event Action<string> PlacementDeleted;

        /// <summary>Reset of the selected foreign placement to the owner's (id).</summary>
        public event Action<string> PlacementReset;

        /// <summary>A map property changed: key and the new value (null removes it).</summary>
        public event Action<string, JsonNode> PropertyChanged;

        /// <summary>The spawn point is to be chosen on the canvas.</summary>
        public event Action SpawnPickRequested;

        /// <summary>The encounters table changed (its JSON, null when empty).</summary>
        public event Action<JsonObject> EncountersChanged;

        /// <summary>Teleport link chosen in the list: the target map and cell.</summary>
        public event Action<string, int, int> LinkOpened;

        /// <summary>The interaction of a placement changed: the placement id and the wanted interaction (null for none).</summary>
        public event Action<string, RpgInteract> InteractChanged;

        /// <summary>The target cell of a teleport is to be picked on the canvas: the placement id and the target map id.</summary>
        public event Action<string, string> TargetPickRequested;

        /// <summary>Rebuilds the column for <paramref name="state"/>.</summary>
        public void Show(State state)
        {
            foreach (var child in GetChildren())
            {
                RemoveChild(child);
                child.QueueFree();
            }
            if (state.Document == null)
            {
                AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/no_map")));
                return;
            }
            AddChild(ToolUi.Section("tools/rpg_editor/inspector_title"));
            if (state.Placement != null)
                AddPlacement(state);
            else
                AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/placement_none")));

            AddChild(ToolUi.Section("tools/rpg_editor/map_title"));
            AddMap(state);
            AddLinks(state);
            if (state.Encounters)
                AddEncounters(state.Document);
        }

        private void AddPlacement(State state)
        {
            var doc = state.Document;
            var record = doc.Placements.FirstOrDefault(r => r.Id == state.Placement);
            var box = new VBoxContainer();
            AddChild(box);
            box.AddChild(ToolUi.Hint(string.Format(ToolUi.Tr("tools/rpg_editor/placement_object"), state.ObjectId ?? "-")));
            box.AddChild(ToolUi.Hint(string.Format(ToolUi.Tr("tools/rpg_editor/placement_id"), state.Placement)));
            var origin = record == null ? "tools/rpg_editor/placement_own"
                : !record.IsForeign ? "tools/rpg_editor/placement_own"
                : doc.IsDeleted(record.Id) ? "tools/rpg_editor/placement_deleted"
                : doc.IsPatched(record.Id) ? "tools/rpg_editor/placement_patched"
                : "tools/rpg_editor/placement_foreign";
            var owner = record?.OwnerMod;
            box.AddChild(ToolUi.Hint(record != null && record.IsForeign
                ? string.Format(ToolUi.Tr(origin), owner)
                : ToolUi.Tr(origin)));

            var x = record?.Data?["X"]?.ToString() is { } xs && int.TryParse(xs, out var xv) ? xv : 0;
            var y = record?.Data?["Y"]?.ToString() is { } ys && int.TryParse(ys, out var yv) ? yv : 0;
            var spinX = ToolUi.Number(0, Math.Max(0, state.MapSize.X - 1), 1, null);
            var spinY = ToolUi.Number(0, Math.Max(0, state.MapSize.Y - 1), 1, null);
            spinX.Value = x;
            spinY.Value = y;
            var id = state.Placement;
            spinX.ValueChanged += v => PlacementMoved?.Invoke(id, (int)v, (int)spinY.Value);
            spinY.ValueChanged += v => PlacementMoved?.Invoke(id, (int)spinX.Value, (int)v);
            ToolUi.Field(box, "tools/rpg_editor/placement_x", spinX);
            ToolUi.Field(box, "tools/rpg_editor/placement_y", spinY);

            var actions = new HBoxContainer();
            actions.AddThemeConstantOverride("separation", UiTokens.Space2);
            box.AddChild(actions);
            var deleted = record != null && record.IsForeign && doc.IsDeleted(record.Id);
            if (!deleted)
                actions.AddChild(ToolUi.Button("tools/rpg_editor/placement_delete", () => PlacementDeleted?.Invoke(id), ButtonKind.Danger, "tools/rpg_editor/placement_delete_tip"));
            if (doc.IsPatched(id) || deleted)
                actions.AddChild(ToolUi.Button("tools/rpg_editor/placement_reset", () => PlacementReset?.Invoke(id), ButtonKind.Secondary, "tools/rpg_editor/placement_reset_tip"));

            if (!deleted)
                AddInteract(box, state);
            // Extension point of the state preview (plan 14.G11, file View/RpgStatePreview.cs): self. flags, not saved.
            if (!deleted && state.ObjectId != null)
                box.AddChild(new RpgStatePreview(doc, id, state.ObjectId));
        }

        // ── interaction ────────────────────────────────────────────

        /// <summary>
        /// The interaction section of the selected placement: what it does (a picker of kinds), its trigger, and the pickers of
        /// the kind. Only the rows of the current kind are built; a change is written at once through <see cref="InteractChanged"/>.
        /// </summary>
        private void AddInteract(VBoxContainer box, State state)
        {
            var id = state.Placement;
            var kind = RpgInteractEdit.KindOf(state.Interact);
            box.AddChild(ToolUi.Section("tools/rpg_editor/interact_title"));
            box.AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/interact_hint")));
            box.AddChild(ToolUi.Hint(DescribeInteract(state.Interact, kind)));

            kindChoices.Clear();
            kindChoices.AddRange(PickableKinds);
            if (kind == RpgInteractKind.Custom)
                kindChoices.Add(RpgInteractKind.Custom);
            kindPicker = new OptionButton { Name = "InteractKind" };
            foreach (var choice in kindChoices)
                kindPicker.AddItem(ToolUi.Tr(KindKey(choice)));
            kindPicker.Select(Math.Max(0, kindChoices.IndexOf(kind)));
            ToolUi.Field(box, "tools/rpg_editor/interact_kind", kindPicker, "tools/rpg_editor/interact_kind_tip");
            kindPicker.ItemSelected += _ => EmitInteract(id);

            triggerPicker = new OptionButton { Name = "InteractTrigger" };
            triggerPicker.AddItem(ToolUi.Tr("tools/rpg_editor/interact_trigger_use"));
            triggerPicker.AddItem(ToolUi.Tr("tools/rpg_editor/interact_trigger_step"));
            triggerPicker.Select(state.Interact?.Trigger == RpgInteractTrigger.Step ? 1 : 0);
            ToolUi.Field(box, "tools/rpg_editor/interact_trigger", triggerPicker, "tools/rpg_editor/interact_trigger_tip");
            triggerPicker.ItemSelected += _ => EmitInteract(id);

            itemPicker = null;
            countSpin = null;
            mapPicker = null;
            xSpin = null;
            ySpin = null;
            dirPicker = null;
            scenarioPicker = null;
            shopPicker = null;
            switch (kind)
            {
                case RpgInteractKind.Item:
                    itemPicker = Picker("InteractItem", "tools/rpg_editor/interact_item_tip");
                    FillEntities(itemPicker, ItemKind, RpgInteractEdit.ItemOf(state.Interact));
                    ToolUi.Field(box, "tools/rpg_editor/interact_item", itemPicker, "tools/rpg_editor/interact_item_tip");
                    countSpin = ToolUi.Number(1, 999, 1, null);
                    countSpin.Name = "InteractCount";
                    countSpin.Value = RpgInteractEdit.CountOf(state.Interact);
                    countSpin.ValueChanged += _ => EmitInteract(id);
                    ToolUi.Field(box, "tools/rpg_editor/interact_count", countSpin);
                    itemPicker.ItemSelected += _ => EmitInteract(id);
                    break;
                case RpgInteractKind.Map:
                    var target = state.Interact?.Teleport;
                    mapPicker = Picker("InteractMap", "tools/rpg_editor/interact_map_tip");
                    FillEntities(mapPicker, MapKind, target?.Map);
                    ToolUi.Field(box, "tools/rpg_editor/interact_map", mapPicker, "tools/rpg_editor/interact_map_tip");
                    xSpin = ToolUi.Number(0, 999, 1, null);
                    xSpin.Name = "InteractX";
                    xSpin.Value = target?.X ?? 0;
                    ySpin = ToolUi.Number(0, 999, 1, null);
                    ySpin.Name = "InteractY";
                    ySpin.Value = target?.Y ?? 0;
                    xSpin.ValueChanged += _ => EmitInteract(id);
                    ySpin.ValueChanged += _ => EmitInteract(id);
                    ToolUi.Field(box, "tools/rpg_editor/interact_x", xSpin);
                    ToolUi.Field(box, "tools/rpg_editor/interact_y", ySpin);
                    dirPicker = Picker("InteractDir", "tools/rpg_editor/interact_dir_tip");
                    foreach (var dir in Directions)
                        dirPicker.AddItem(ToolUi.Tr(DirKey(dir)));
                    dirPicker.Select(Math.Max(0, Array.IndexOf(Directions, target?.Dir ?? RpgDirection.Down)));
                    ToolUi.Field(box, "tools/rpg_editor/interact_dir", dirPicker, "tools/rpg_editor/interact_dir_tip");
                    dirPicker.ItemSelected += _ => EmitInteract(id);
                    mapPicker.ItemSelected += _ => EmitInteract(id);
                    var pick = ToolUi.Button("tools/rpg_editor/interact_pick", () => TargetPickRequested?.Invoke(id, PickedValue(mapPicker)),
                        ButtonKind.Secondary, "tools/rpg_editor/interact_pick_tip");
                    pick.Name = "InteractPick";
                    box.AddChild(pick);
                    break;
                case RpgInteractKind.Dialog:
                    scenarioPicker = Picker("InteractScenario", "tools/rpg_editor/interact_scenario_tip");
                    FillScenarios(scenarioPicker, state.Interact?.Scenario);
                    ToolUi.Field(box, "tools/rpg_editor/interact_scenario", scenarioPicker, "tools/rpg_editor/interact_scenario_tip");
                    scenarioPicker.ItemSelected += _ => EmitInteract(id);
                    break;
                case RpgInteractKind.Shop:
                    shopPicker = Picker("InteractShop", "tools/rpg_editor/interact_shop_tip");
                    FillEntities(shopPicker, ShopKind, RpgInteractEdit.ShopOf(state.Interact));
                    ToolUi.Field(box, "tools/rpg_editor/interact_shop", shopPicker, "tools/rpg_editor/interact_shop_tip");
                    shopPicker.ItemSelected += _ => EmitInteract(id);
                    break;
            }
        }

        /// <summary>Writes the interaction the controls describe, after a change of any of them.</summary>
        private void EmitInteract(string placement)
        {
            var kind = kindPicker != null && kindPicker.Selected >= 0 && kindPicker.Selected < kindChoices.Count
                ? kindChoices[kindPicker.Selected]
                : RpgInteractKind.None;
            if (kind == RpgInteractKind.Custom)
                return;
            var trigger = triggerPicker != null && triggerPicker.Selected == 1 ? RpgInteractTrigger.Step : RpgInteractTrigger.Use;
            RpgInteract wanted;
            switch (kind)
            {
                case RpgInteractKind.Item:
                    var item = ValueOr(itemPicker, ItemKind);
                    if (item == null)
                        return;
                    wanted = RpgInteractEdit.Make(kind, trigger, item, countSpin == null ? 1 : (int)countSpin.Value, null);
                    break;
                case RpgInteractKind.Map:
                    var map = ValueOr(mapPicker, MapKind);
                    if (map == null)
                        return;
                    var dir = dirPicker != null && dirPicker.Selected >= 0 ? Directions[dirPicker.Selected] : RpgDirection.Down;
                    var cell = new RpgTeleport { Map = map, X = xSpin == null ? 0 : (int)xSpin.Value, Y = ySpin == null ? 0 : (int)ySpin.Value, Dir = dir };
                    wanted = RpgInteractEdit.Make(kind, trigger, null, 1, cell);
                    break;
                case RpgInteractKind.Dialog:
                    var scenario = ValueOr(scenarioPicker, ScenarioGroup);
                    if (scenario == null)
                        return;
                    wanted = RpgInteractEdit.Make(kind, trigger, scenario, 1, null);
                    break;
                case RpgInteractKind.Shop:
                    var shop = ValueOr(shopPicker, ShopKind);
                    if (shop == null)
                        return;
                    wanted = RpgInteractEdit.Make(kind, trigger, shop, 1, null);
                    break;
                default:
                    wanted = null;
                    break;
            }
            InteractChanged?.Invoke(placement, wanted);
        }

        private string DescribeInteract(RpgInteract interact, RpgInteractKind kind)
        {
            switch (kind)
            {
                case RpgInteractKind.Item:
                    return string.Format(ToolUi.Tr("tools/rpg_editor/interact_now_item"), GameEntityIndex.Label(ItemKind, RpgInteractEdit.ItemOf(interact)), RpgInteractEdit.CountOf(interact));
                case RpgInteractKind.Map:
                    var target = interact.Teleport;
                    return string.Format(ToolUi.Tr("tools/rpg_editor/interact_now_map"), target.Map ?? "-", target.X, target.Y);
                case RpgInteractKind.Dialog:
                    return string.Format(ToolUi.Tr("tools/rpg_editor/interact_now_dialog"), interact.Scenario);
                case RpgInteractKind.Shop:
                    return string.Format(ToolUi.Tr("tools/rpg_editor/interact_now_shop"), GameEntityIndex.Label(ShopKind, RpgInteractEdit.ShopOf(interact)));
                case RpgInteractKind.Custom:
                    return ToolUi.Tr("tools/rpg_editor/interact_now_custom");
                default:
                    return ToolUi.Tr("tools/rpg_editor/interact_now_none");
            }
        }

        private static string KindKey(RpgInteractKind kind) => "tools/rpg_editor/interact_kind_" + kind.ToString().ToLowerInvariant();

        private static OptionButton Picker(string name, string tipKey)
            => new() { Name = name, TooltipText = ToolUi.Tr(tipKey), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        /// <summary>Fills a picker with the entities of a kind (label, id as metadata) and selects <paramref name="current"/>.</summary>
        private static void FillEntities(OptionButton picker, string kind, string current)
        {
            foreach (var id in GameEntityIndex.Of(kind).Keys)
            {
                picker.AddItem(GameEntityIndex.Label(kind, id));
                picker.SetItemMetadata(picker.ItemCount - 1, id);
            }
            SelectValue(picker, current);
        }

        /// <summary>Fills a picker with the scenario files of the game (their paths without the extension).</summary>
        private static void FillScenarios(OptionButton picker, string current)
        {
            var paths = Files.Collect(ScenarioGroup).Keys
                .Where(k => k.EndsWith(ScenarioSuffix, StringComparison.OrdinalIgnoreCase))
                .Select(k => k.Substring(0, k.Length - ScenarioSuffix.Length))
                .OrderBy(k => k, StringComparer.Ordinal);
            foreach (var path in paths)
            {
                picker.AddItem(path);
                picker.SetItemMetadata(picker.ItemCount - 1, path);
            }
            SelectValue(picker, current);
        }

        /// <summary>Selects the item whose metadata is <paramref name="value"/>; a value the list does not know is added and shown.</summary>
        private static void SelectValue(OptionButton picker, string value)
        {
            value ??= string.Empty;
            for (var i = 0; i < picker.ItemCount; i++)
            {
                if (string.Equals(picker.GetItemMetadata(i).AsString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    picker.Select(i);
                    return;
                }
            }
            picker.AddItem(value.Length == 0 ? ToolUi.Tr("tools/rpg_editor/interact_none_chosen") : value + "  ?");
            picker.SetItemMetadata(picker.ItemCount - 1, value);
            picker.Select(picker.ItemCount - 1);
        }

        /// <summary>The metadata (the id) of the picker's selected item, or null when nothing is selected.</summary>
        private static string PickedValue(OptionButton picker)
        {
            if (picker == null || picker.Selected < 0 || picker.ItemCount == 0)
                return null;
            var value = picker.GetItemMetadata(picker.Selected).AsString();
            return value.Length == 0 ? null : value;
        }

        /// <summary>The picked value, or the first entry of the kind when the picker is not shown (the kind was just chosen).</summary>
        private static string ValueOr(OptionButton picker, string kind)
        {
            if (picker != null)
                return PickedValue(picker);
            if (kind == ScenarioGroup)
                return Files.Collect(ScenarioGroup).Keys
                    .Where(k => k.EndsWith(ScenarioSuffix, StringComparison.OrdinalIgnoreCase))
                    .Select(k => k.Substring(0, k.Length - ScenarioSuffix.Length))
                    .OrderBy(k => k, StringComparer.Ordinal)
                    .FirstOrDefault();
            return GameEntityIndex.Of(kind).Keys.FirstOrDefault();
        }

        // ── map ────────────────────────────────────────────────────

        private void AddMap(State state)
        {
            var doc = state.Document;
            var box = new VBoxContainer();
            AddChild(box);
            box.AddChild(ToolUi.Hint(string.Format(ToolUi.Tr("tools/rpg_editor/map_size"), state.MapSize.X, state.MapSize.Y)));
            if (doc.IsForeign)
                box.AddChild(ToolUi.Hint(string.Format(ToolUi.Tr("tools/rpg_editor/map_foreign"), doc.Map?.OwnerMod)));

            var spawn = doc.Map?.Data?["Spawn"] as JsonObject;
            var spawnText = spawn == null ? "-" : $"{spawn["X"]},{spawn["Y"]}";
            var spawnRow = new HBoxContainer();
            spawnRow.AddThemeConstantOverride("separation", UiTokens.Space2);
            spawnRow.AddChild(new Label { Text = string.Format(ToolUi.Tr("tools/rpg_editor/map_spawn_value"), spawnText), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            spawnRow.AddChild(ToolUi.Button("tools/rpg_editor/map_spawn_pick", () => SpawnPickRequested?.Invoke(), ButtonKind.Secondary, "tools/rpg_editor/map_spawn_tip"));
            box.AddChild(spawnRow);

            var variant = Picker("MapVariant", "tools/rpg_editor/map_variant_tip");
            variant.AddItem(ToolUi.Tr("tools/rpg_editor/map_variant_none"));
            variant.SetItemMetadata(0, string.Empty);
            foreach (var name in state.Variants)
            {
                variant.AddItem(name);
                variant.SetItemMetadata(variant.ItemCount - 1, name);
            }
            SelectValue(variant, doc.Map?.Data?["Variant"]?.ToString());
            ToolUi.Field(box, "tools/rpg_editor/map_variant", variant, "tools/rpg_editor/map_variant_tip");
            variant.ItemSelected += _ => PropertyChanged?.Invoke("Variant", Text(PickedValue(variant)));

            var atmosphere = Picker("MapAtmosphere", "tools/rpg_editor/map_atmosphere_tip");
            atmosphere.AddItem(ToolUi.Tr("tools/rpg_editor/map_atmosphere_none"));
            atmosphere.SetItemMetadata(0, string.Empty);
            foreach (var id in GameEntityIndex.Of(AtmosphereKind).Keys)
            {
                atmosphere.AddItem(GameEntityIndex.Label(AtmosphereKind, id));
                atmosphere.SetItemMetadata(atmosphere.ItemCount - 1, id);
            }
            SelectValue(atmosphere, doc.Map?.Data?["Atmosphere"]?.ToString());
            ToolUi.Field(box, "tools/rpg_editor/map_atmosphere", atmosphere, "tools/rpg_editor/map_atmosphere_tip");
            atmosphere.ItemSelected += _ => PropertyChanged?.Invoke("Atmosphere", Text(PickedValue(atmosphere)));
        }

        private static JsonNode Text(string text) => string.IsNullOrEmpty(text) ? null : JsonValue.Create(text);

        private void AddLinks(State state)
        {
            AddChild(ToolUi.Section("tools/rpg_editor/links_title"));
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/links_hint")));
            if (state.Outgoing.Count == 0 && state.Incoming.Count == 0)
            {
                AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/links_none")));
                return;
            }
            foreach (var link in state.Outgoing)
                AddLink(link, string.Format(ToolUi.Tr("tools/rpg_editor/link_out"), link.Map, link.X, link.Y, link.Placement));
            foreach (var link in state.Incoming)
                AddLink(link, string.Format(ToolUi.Tr("tools/rpg_editor/link_in"), link.Map, link.Placement));
        }

        /// <summary>The locale key of a broken-target problem.</summary>
        public static string BrokenKey(string problem) => problem switch
        {
            RpgTeleportLink.BrokenMap => "tools/rpg_editor/link_broken_map",
            RpgTeleportLink.BrokenCell => "tools/rpg_editor/link_broken_cell",
            _ => "tools/rpg_editor/link_broken_solid",
        };

        private void AddLink(RpgTeleportLink link, string text)
        {
            var button = new Button
            {
                Text = link.Broken == null ? text : text + "  ! " + ToolUi.Tr(BrokenKey(link.Broken)),
                Alignment = HorizontalAlignment.Left,
                ClipText = true,
                TooltipText = link.Broken == null ? string.Empty : ToolUi.Tr("tools/rpg_editor/link_broken_tip"),
                FocusMode = Control.FocusModeEnum.None,
            };
            if (link.Broken != null)
                button.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.4f));
            var target = link;
            button.Pressed += () => LinkOpened?.Invoke(target.Map, target.X, target.Y);
            AddChild(button);
        }

        private void AddEncounters(RpgMapDocument doc)
        {
            AddChild(ToolUi.Section("tools/rpg_editor/encounters_title"));
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/encounters_hint")));
            var table = doc.Map?.Data?["Encounters"] as JsonObject;
            var steps = ToolUi.Number(0, 100000, 1, null);
            steps.Value = table?["Steps"] is { } stepNode && int.TryParse(stepNode.ToString(), out var stepValue) ? stepValue : 0;
            var groups = new TextEdit
            {
                CustomMinimumSize = new Vector2(0, 90),
                Text = string.Join("\n", (table?["Groups"] as JsonArray)?.Select(g => g?.ToString() ?? string.Empty) ?? Array.Empty<string>()),
                PlaceholderText = ToolUi.Tr("tools/rpg_editor/encounters_groups_hint"),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                WrapMode = TextEdit.LineWrappingMode.Boundary,
            };
            ToolUi.Field(this, "tools/rpg_editor/encounters_steps", steps, "tools/rpg_editor/encounters_steps_tip");
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/encounters_groups")));
            AddChild(groups);

            var enemy = Picker("EncounterEnemy", "tools/rpg_editor/encounters_enemy_tip");
            enemy.AddItem(ToolUi.Tr("tools/rpg_editor/encounters_add_enemy"));
            enemy.SetItemMetadata(0, string.Empty);
            foreach (var id in GameEntityIndex.Of(CharacterKind).Keys)
            {
                enemy.AddItem(GameEntityIndex.Label(CharacterKind, id));
                enemy.SetItemMetadata(enemy.ItemCount - 1, id);
            }
            enemy.ItemSelected += _ =>
            {
                var picked = PickedValue(enemy);
                if (picked != null)
                    groups.Text = (groups.Text.TrimEnd() + "\n" + picked).Trim();
                enemy.Select(0);
            };
            AddChild(enemy);
            AddChild(ToolUi.Button("tools/rpg_editor/encounters_apply", () => ApplyEncounters(steps, groups), ButtonKind.Primary));
        }

        private void ApplyEncounters(SpinBox steps, TextEdit groups)
        {
            var lines = groups.Text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var value = (int)steps.Value;
            if (value <= 0 && lines.Count == 0)
            {
                EncountersChanged?.Invoke(null);
                return;
            }
            var table = new JsonObject { ["Steps"] = value };
            var array = new JsonArray();
            foreach (var line in lines)
                array.Add(JsonValue.Create(line));
            table["Groups"] = array;
            EncountersChanged?.Invoke(table);
        }
    }
}
