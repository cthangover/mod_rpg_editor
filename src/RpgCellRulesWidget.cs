using System;
using System.Text;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Opt-in utility inspector; it reads hovered cells and runs bounded audits only on an explicit button press.</summary>
    internal sealed partial class RpgCellRulesWidget : ModWidget
    {
        private readonly RpgEditorWindow host;

        private VBoxContainer content;

        private CheckButton enabled;

        private Button check;

        private TextEdit details;

        private TextEdit results;

        private RpgEditorScene source;

        private RpgMapDiagnostics audit;

        private Vector2I? cell;

        private bool dirty = true;

        private bool retired;

        internal RpgCellRulesWidget(RpgEditorWindow host)
        {
            this.host = host;
            Name = "CellRules";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
        }

        protected override void Construct()
        {
            content = new VBoxContainer();
            AddChild(content);
            content.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            var row = new HBoxContainer();
            content.AddChild(row);
            enabled = new CheckButton
            {
                Name = "CellRulesEnabled",
                Text = Tr("toggle"),
                TooltipText = Tr("tip"),
            };
            enabled.Toggled += OnEnabled;
            row.AddChild(enabled);
            check = new Button
            {
                Name = "CellRulesCheck",
                Text = Tr("check"),
                Disabled = true,
                TooltipText = Tr("check_tip"),
            };
            check.Pressed += OnCheck;
            row.AddChild(check);
            details = TextBox("CellRulesDetails", 150);
            results = TextBox("CellRulesResults", 150);
            content.AddChild(details);
            content.AddChild(results);
            host.Canvas.Hovered += OnHovered;
            content.MinimumSizeChanged += UpdateMinimum;
            SetProcess(false);
            UpdateMinimum();
        }

        private static TextEdit TextBox(string name, float height)
        {
            return new TextEdit
            {
                Name = name,
                Editable = false,
                WrapMode = TextEdit.LineWrappingMode.Boundary,
                CustomMinimumSize = new Vector2(0, height),
                Visible = false,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
        }

        private static string Tr(string name)
        {
            return ToolUi.Tr("tools/rpg_editor/rules_" + name);
        }

        /// <summary>The checkbox alone determines whether per-frame scene invalidation is observed.</summary>
        public override void _Ready()
        {
            EnsureConstructed();
            SetProcess(enabled.ButtonPressed);
        }

        /// <summary>Refreshes descriptive text only when the scene or hovered coordinate changes.</summary>
        public override void _Process(double delta)
        {
            if (retired || !enabled.ButtonPressed)
            {
                return;
            }

            if (source != host.Scene)
            {
                source = host.Scene;
                audit = null;
                results.Visible = false;
                dirty = true;
            }

            if (dirty)
            {
                RefreshCell();
                dirty = false;
            }
        }

        private void OnEnabled(bool value)
        {
            details.Visible = value;
            results.Visible = value && audit != null;
            check.Disabled = !value;
            dirty = true;
            SetProcess(value);
            UpdateMinimum();
        }

        private void OnHovered(Vector2I? next)
        {
            if (next != null && next != cell)
            {
                cell = next;
                dirty = true;
            }
        }

        private void RefreshCell()
        {
            var scene = host.Scene;
            var at = cell ?? new Vector2I(scene?.Map.Spawn?.X ?? 0, scene?.Map.Spawn?.Y ?? 0);
            var report = RpgCellReport.At(scene, at);
            var text = new StringBuilder();
            text.AppendLine(string.Format(Tr("cell"), at.X, at.Y, Tr(report.Walkable ? "walkable" : "blocked")));
            text.AppendLine(Tr("reason_" + report.Rule));
            var document = host.Document;
            if (document?.Map != null)
            {
                text.AppendLine(string.Format(Tr("source"), document.Map.OwnerMod ?? document.OwnMod, document.Map.File));
                if (document.IsForeign)
                {
                    text.AppendLine(string.Format(Tr("patch"), document.OwnMod));
                }
            }

            if (report.InBounds)
            {
                text.AppendLine("Collision " + report.Collision + " ← " + report.CollisionSource);
                text.AppendLine("Ground " + (report.Ground ?? "∅") + " ← " + report.GroundSource);
                text.AppendLine("Structure " + (report.Structure ?? "∅") + " ← " + report.StructureSource);
                text.AppendLine(string.Format(Tr("masks"), string.Join(", ", report.Blockers)));
                if (audit != null)
                {
                    var path = audit.PathTo(at);
                    text.AppendLine(path == null ? Tr("no_path") : string.Format(Tr("path"), path.Count - 1));
                }
            }

            details.Text = text.ToString();
        }

        private void OnCheck()
        {
            source = host.Scene;
            audit = host.DiagnoseMap();
            var text = new StringBuilder();
            text.AppendLine(string.Format(Tr("summary"), audit.Reached, audit.Issues.Count));
            text.AppendLine(Tr("snapshot"));
            foreach (var issue in audit.Issues)
            {
                text.AppendLine(string.Format(Tr("issue"), Tr("issue_" + issue.Code), issue.Id, issue.Cell.X, issue.Cell.Y));
            }

            results.Text = text.ToString();
            results.Visible = true;
            dirty = true;
            UpdateMinimum();
        }

        private void UpdateMinimum()
        {
            var minimum = content.GetCombinedMinimumSize();
            if (CustomMinimumSize != minimum)
            {
                CustomMinimumSize = minimum;
            }
        }

        internal void Retire()
        {
            if (retired)
            {
                return;
            }

            retired = true;
            SetProcess(false);
            host.Canvas.Hovered -= OnHovered;
            enabled.Toggled -= OnEnabled;
            check.Pressed -= OnCheck;
            content.MinimumSizeChanged -= UpdateMinimum;
            audit = null;
            source = null;
        }

        protected override void Destruct()
        {
            Retire();
            base.Destruct();
        }
    }
}
