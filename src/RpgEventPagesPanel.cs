namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Mods.Rpg;
    using Cthangover.Tools.Common;
    using Cthangover.Tools.Services;
    using Godot;

    /// <summary>Edits event states without replacing fields the form does not expose, such as routes and lighting.</summary>
    public partial class RpgEventPagesPanel : VBoxContainer
    {
        private readonly OptionButton picker;
        private readonly Button add;
        private readonly Button remove;
        private readonly LineEdit condition;
        private readonly LineEdit sprite;
        private readonly CodeEdit commands;
        private RpgMapEvent mapEvent;
        private RpgEventPage page;
        private bool binding;
        private bool readOnly;

        public event Action EditStarting;
        public event Action Changed;

        /// <summary>Builds the state picker and the fields used by chests, barrels and conditional NPCs.</summary>
        public RpgEventPagesPanel()
        {
            Name = "EventStates";
            AddChild(ToolUi.Section("tools/rpg_editor/section_pages"));
            AddChild(ToolUi.Hint(ToolUi.Tr("tools/rpg_editor/pages_hint")));
            picker = new OptionButton { Name = "StatePicker" };
            picker.ItemSelected += OnSelected;
            AddChild(picker);
            var row = new HBoxContainer();
            add = ToolUi.Button("tools/rpg_editor/page_add", AddPage);
            add.Name = "AddState";
            remove = ToolUi.Button("tools/rpg_editor/page_remove", RemovePage);
            remove.Name = "RemoveState";
            row.AddChild(add);
            row.AddChild(remove);
            AddChild(row);
            condition = ToolUi.Field(this, "tools/rpg_editor/page_condition", ToolUi.Text("", OnCondition));
            condition.Name = "StateCondition";
            sprite = ToolUi.Field(this, "tools/rpg_editor/page_sprite", ToolUi.Text("desert/barrel_open", OnSprite));
            sprite.Name = "StateSprite";
            AddChild(ToolUi.Section("tools/rpg_editor/page_commands"));
            commands = ToolUi.Code();
            commands.Name = "StateCommands";
            commands.CustomMinimumSize = new Vector2(0, 130);
            commands.SyntaxHighlighter = ScenarioSyntaxService.CreateHighlighter();
            commands.TextChanged += OnCommands;
            AddChild(commands);
        }

        /// <summary>Rebinds after selection or undo. Null Commands continue to inherit until the author edits them.</summary>
        public void Bind(RpgMapEvent value, bool locked)
        {
            mapEvent = value;
            readOnly = locked;
            Rebuild(0);
        }

        private void Rebuild(int selected)
        {
            binding = true;
            picker.Clear();
            foreach (var state in mapEvent?.Pages ?? new List<RpgEventPage>())
                picker.AddItem(string.Format(ToolUi.Tr("tools/rpg_editor/page_title"), picker.ItemCount + 1)
                    + " · " + (state.Condition ?? ToolUi.Tr("tools/rpg_editor/page_always")));
            if (picker.ItemCount > 0)
                picker.Select(Math.Clamp(selected, 0, picker.ItemCount - 1));
            add.Disabled = readOnly || mapEvent == null;
            binding = false;
            LoadPage();
        }

        private void OnSelected(long index) => LoadPage();

        private void LoadPage()
        {
            binding = true;
            page = picker.Selected >= 0 && picker.Selected < (mapEvent?.Pages?.Count ?? 0) ? mapEvent.Pages[picker.Selected] : null;
            condition.Text = page?.Condition ?? "";
            sprite.Text = page?.Sprite ?? "";
            commands.Text = string.Join("\n", page?.Commands ?? new List<string>());
            condition.Editable = sprite.Editable = commands.Editable = page != null && !readOnly;
            remove.Disabled = page == null || readOnly;
            binding = false;
        }

        private bool BeginEdit()
        {
            if (binding || readOnly || mapEvent == null)
                return false;
            EditStarting?.Invoke();
            return true;
        }

        private void AddPage()
        {
            if (!BeginEdit()) return;
            mapEvent.Pages ??= new List<RpgEventPage>();
            mapEvent.Pages.Add(new RpgEventPage { Commands = new List<string>() });
            Rebuild(mapEvent.Pages.Count - 1);
            Changed?.Invoke();
        }

        private void RemovePage()
        {
            if (page == null || !BeginEdit()) return;
            mapEvent.Pages.Remove(page);
            Rebuild(0);
            Changed?.Invoke();
        }

        private void OnCondition(string value)
        {
            if (page == null || !BeginEdit()) return;
            page.Condition = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Changed?.Invoke();
        }

        private void OnSprite(string value)
        {
            if (page == null || !BeginEdit()) return;
            page.Sprite = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            Changed?.Invoke();
        }

        private void OnCommands()
        {
            if (page == null || !BeginEdit()) return;
            page.Commands = commands.Text.Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            Changed?.Invoke();
        }
    }
}
