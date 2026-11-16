using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Tool;
using Cthangover.Tools.Common;
using Cthangover.Tools.Services;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Own-mod stamp files are independent of map saves; loaded dependencies contribute read-only reusable groups.</summary>
    public partial class RpgEditorWindow
    {
        private const string StampDirectory = "editor_stamps";

        private RpgStampControls stampControls;

        private readonly List<(string Mod, string Path, RpgMapStamp Stamp)> stamps = new();

        private string selectedStamp;

        private void InstallStamps(Control parent)
        {
            stampControls = new RpgStampControls();
            parent.AddChild(stampControls);
            stampControls.EnsureConstructed();
            stampControls.CaptureButton.Pressed += CaptureStamp;
            stampControls.PasteButton.Pressed += PasteStamp;
            stampControls.Picker.ItemSelected += OnStampSelected;
            RefreshStamps();
        }

        private void ReleaseStamps()
        {
            if (GodotObject.IsInstanceValid(stampControls))
            {
                stampControls.CaptureButton.Pressed -= CaptureStamp;
                stampControls.PasteButton.Pressed -= PasteStamp;
                stampControls.Picker.ItemSelected -= OnStampSelected;
            }

            stamps.Clear();
        }

        private void CaptureStamp()
        {
            var name = stampControls.NameInput.Text.Trim();
            if (!Regex.IsMatch(name, "\\A[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}\\z"))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/stamp_bad_name"), ToolStatusKind.Error);
                return;
            }

            var stamp = RpgMapStamp.Capture(document, ActedIds(), name);
            if (stamp == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/stamp_selection"), ToolStatusKind.Error);
                return;
            }

            var path = StampDirectory + "/" + name + ".json";
            if (ModFileService.ReadText(modId, path) != null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/stamp_exists"), ToolStatusKind.Error);
                return;
            }

            if (!ModFileService.WriteText(modId, path, JsonSerializer.Serialize(stamp, JsonOptionsProvider.ReadOptions) + "\n"))
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/stamp_readonly"), ToolStatusKind.Error);
                return;
            }

            selectedStamp = modId + "/" + path;
            RefreshStamps();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/stamp_saved"), selectedStamp), ToolStatusKind.Ok);
        }

        private void RefreshStamps()
        {
            if (!GodotObject.IsInstanceValid(stampControls))
            {
                return;
            }

            stamps.Clear();
            stampControls.Picker.Clear();
            var sources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var pending = new Queue<string>();
            if (!string.IsNullOrEmpty(modId))
            {
                pending.Enqueue(modId);
            }

            while (pending.Count > 0)
            {
                var id = pending.Dequeue();
                if (!sources.Add(id))
                {
                    continue;
                }

                var mod = ModManager.Instance.GetMod(id) as ModInfo;
                foreach (var dependency in mod?.Manifest?.DependencyIds ?? new List<string>())
                {
                    pending.Enqueue(dependency);
                }
            }

            var ordered = new List<string>(sources);
            ordered.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var source in ordered)
            {
                foreach (var path in ModFileService.ListFiles(source, StampDirectory, ".json"))
                {
                    try
                    {
                        var stamp = JsonSerializer.Deserialize<RpgMapStamp>(ModFileService.ReadText(source, path),
                            JsonOptionsProvider.ReadOptions);
                        if (stamp == null || !stamp.Usable())
                        {
                            continue;
                        }

                        stamps.Add((source, path, stamp));
                        stampControls.Picker.AddItem(source + "/" + stamp.Name);
                        if (source + "/" + path == selectedStamp)
                        {
                            stampControls.Picker.Select(stamps.Count - 1);
                        }
                    }
                    catch (JsonException exception)
                    {
                        GameLogger.Log(Category, "stamp skipped: " + source + "/" + path + ": " + exception.Message,
                            LogLevel.Warning);
                    }
                }
            }

            stampControls.CaptureButton.Disabled = document == null || !ModFileService.IsWritable(modId);
            RefreshStampDetails();
        }

        private void OnStampSelected(long index)
        {
            if (index >= 0 && index < stamps.Count)
            {
                selectedStamp = stamps[(int)index].Mod + "/" + stamps[(int)index].Path;
            }

            RefreshStampDetails();
        }

        private void RefreshStampDetails()
        {
            var index = stampControls.Picker.Selected;
            stampControls.PasteButton.Disabled = true;
            if (index < 0 || index >= stamps.Count)
            {
                stampControls.Details.Text = ToolUi.Tr("tools/rpg_editor/stamp_empty");
                return;
            }

            var entry = stamps[index];
            var objects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            foreach (var clip in entry.Stamp.Clips)
            {
                if (clip.EventData == null && objects.Add(clip.Object) && !objectTable.ContainsKey(clip.Object))
                {
                    missing.Add(clip.Object);
                }
            }

            stampControls.Details.Text = string.Format(ToolUi.Tr("tools/rpg_editor/stamp_details"),
                entry.Mod + "/" + entry.Path, entry.Stamp.SourceMap, entry.Stamp.Clips.Count, modId,
                string.Join(", ", objects));
            if (missing.Count > 0)
            {
                stampControls.Details.Text += "\n" + string.Format(ToolUi.Tr("tools/rpg_editor/stamp_missing"),
                    string.Join(", ", missing));
                return;
            }

            if (document == null || document.IsForeign)
            {
                stampControls.Details.Text += "\n" + ToolUi.Tr("tools/rpg_editor/stamp_foreign");
                return;
            }

            stampControls.PasteButton.Disabled = false;
        }

        private void PasteStamp()
        {
            var index = stampControls.Picker.Selected;
            if (index < 0 || index >= stamps.Count || stampControls.PasteButton.Disabled)
            {
                return;
            }

            var at = hover ?? lastCell ?? Vector2I.Zero;
            var ids = stamps[index].Stamp.Paste(document, at.X, at.Y);
            if (ids == null)
            {
                SetStatus(ToolUi.Tr("tools/rpg_editor/paste_refused"), ToolStatusKind.Error);
                return;
            }

            marked.Clear();
            foreach (var id in ids)
            {
                marked.Add(id);
            }

            selectedId = null;
            AfterChange();
            SetStatus(string.Format(ToolUi.Tr("tools/rpg_editor/pasted"), ids.Count), ToolStatusKind.Ok);
        }
    }
}
