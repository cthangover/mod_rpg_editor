using System;
using System.Collections.Generic;
using Cthangover.Core.Characters;
using Cthangover.Core.Mods.Resolvers;
using Cthangover.Mods.Interface.Base.Lists;
using Cthangover.Mods.Rpg;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.TileRender;
using Cthangover.Tools.Common;
using Godot;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// The object palette by shape (plan 12.4): one tab per form — surfaces, segments, sprites and actors — each a
    /// searchable list with a thumbnail. Choosing a row raises <see cref="ObjectChosen"/>; the window turns the form
    /// into the tool (a surface or a segment paints, a sprite or an actor is placed).
    /// <para>
    /// A fifth tab lists the characters that have a walking sheet (plan 14.G7). Choosing one raises
    /// <see cref="CharacterChosen"/> instead: the window makes the character's actor object (see
    /// <c>RpgCharacterActors</c>) and places it like any actor.
    /// </para>
    /// </summary>
    public partial class RpgEditorPalette : ModWidget
    {
        private const int ThumbSide = 56;

        private static readonly RpgObjectForm[] Forms =
        {
            RpgObjectForm.Surface,
            RpgObjectForm.Segment,
            RpgObjectForm.Sprite,
            RpgObjectForm.Actor
        };

        private readonly Dictionary<RpgObjectForm, FilteredList> lists = new();

        private readonly Dictionary<RpgObjectForm, List<RpgObject>> rows = new();

        private readonly RpgEditorThumbnails thumbnails;

        private IReadOnlyDictionary<string, RpgObject> objects;

        private readonly List<RpgObject> characterRows = new();

        private FilteredList characters;

        private TabContainer tabs;

        private bool rebuilding = true;

        /// <summary>A palette row supplies its stable object id and actual form.</summary>
        public event Action<string, RpgObjectForm> ObjectChosen;

        /// <summary>A character row requests creation/selection of that character's actor object.</summary>
        public event Action<string> CharacterChosen;

        /// <summary>
        /// Stores catalogue/owner references; the host adds this widget then calls EnsureConstructed to build
        /// its tabs.
        /// </summary>
        public RpgEditorPalette(IReadOnlyDictionary<string, RpgObject> objects, RpgEditorThumbnails thumbnails)
        {
            this.thumbnails = thumbnails ?? throw new ArgumentNullException(nameof(thumbnails));
            this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
            Name = "RpgPalette";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            CustomMinimumSize = new Vector2(0, 280);
        }

        /// <summary>Selected native tab; changing it creates only that tab's miniature images.</summary>
        public int CurrentTab
        {
            get
            {
                return tabs?.CurrentTab ?? 0;
            }

            set
            {
                EnsureConstructed();
                tabs.CurrentTab = value;
            }
        }

        /// <summary>Number of form/character tabs, preserving the editor and GUI-driver contract.</summary>
        public int GetTabCount()
        {
            EnsureConstructed();
            return tabs.GetTabCount();
        }

        protected override void Construct()
        {
            tabs = new TabContainer
            {
                Name = "PaletteTabs"
            };
            AddChild(tabs);
            tabs.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            foreach (var form in Forms)
            {
                var list = new FilteredList(220f, "tools/rpg_editor/palette_search")
                {
                    Name = "Palette_" + form,
                    SizeFlagsVertical = SizeFlags.ExpandFill
                };
                list.FitIcons(ThumbSide);
                list.Chosen += OnObjectChosen;
                lists[form] = list;
                tabs.AddChild(list);
                tabs.SetTabTitle(tabs.GetChildCount() - 1, ToolUi.Tr(TabKey(form)));
            }

            characters = new FilteredList(220f, "tools/rpg_editor/palette_search")
            {
                Name = "Palette_Characters",
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            characters.FitIcons(ThumbSide);
            characters.Chosen += OnCharacterChosen;
            tabs.AddChild(characters);
            tabs.SetTabTitle(tabs.GetChildCount() - 1, ToolUi.Tr("tools/rpg_editor/tab_character"));
            tabs.TabChanged += Activate;
            Rebuild(objects);
        }

        /// <summary>
        /// Refreshes metadata/miniatures after a definition changed, preserving tab and stable row selections.
        /// </summary>
        public void Rebuild(IReadOnlyDictionary<string, RpgObject> next)
        {
            rebuilding = true;
            ReleaseIcons();
            thumbnails.Clear();
            objects = next ?? throw new ArgumentNullException(nameof(next));
            rows.Clear();
            foreach (var form in Forms)
            {
                rows[form] = new List<RpgObject>();
            }

            foreach (var obj in objects.Values)
            {
                if (obj != null && !string.IsNullOrEmpty(obj.ID) && obj.Form is { } form && rows.TryGetValue(form, out var group))
                {
                    group.Add(obj);
                }
            }

            foreach (var form in Forms)
            {
                rows[form].Sort(CompareObjects);
                lists[form].SetItems(RowsOf(rows[form], false));
            }

            characterRows.Clear();
            foreach (var pair in Entities.Collect<CharacterInfo>(required: false))
            {
                var sheet = RpgPlayerLook.CharsetOfId(pair.Key);
                if (sheet != null)
                {
                    characterRows.Add(new RpgObject
                    {
                        ID = pair.Key,
                        Name = pair.Value?.Name,
                        Form = RpgObjectForm.Actor,
                        Charset = sheet,
                    });
                }
            }

            characterRows.Sort(CompareObjects);
            characters.SetItems(RowsOf(characterRows, false));
            rebuilding = false;
            Activate(CurrentTab);
        }

        /// <summary>Updates character definitions without losing the other tabs' ids, filters or selections.</summary>
        public void RebuildCharacters()
        {
            Rebuild(objects);
        }

        /// <summary>Detaches every native and managed row reference before cache clearing or window teardown.</summary>
        public void ReleaseIcons()
        {
            foreach (var pair in lists)
            {
                pair.Value.SetItems(RowsOf(rows.TryGetValue(pair.Key, out var source) ? source : Array.Empty<RpgObject>(), false));
            }

            characters?.SetItems(RowsOf(characterRows, false));
        }

        /// <summary>Selects by id silently, activating the required tab before scrolling to the row.</summary>
        public void Select(RpgObjectForm form, string id)
        {
            if (!lists.TryGetValue(form, out var list))
            {
                return;
            }

            CurrentTab = Array.IndexOf(Forms, form);
            list.Select(id);
        }

        /// <summary>Total metadata row count, available even before the tab's images have been requested.</summary>
        public int Count(RpgObjectForm form)
        {
            return rows.TryGetValue(form, out var group) ? group.Count : 0;
        }

        protected override void Destruct()
        {
            ReleaseIcons();
            tabs.TabChanged -= Activate;
            foreach (var list in lists.Values)
            {
                list.Chosen -= OnObjectChosen;
            }

            characters.Chosen -= OnCharacterChosen;
            ObjectChosen = null;
            CharacterChosen = null;
            base.Destruct();
        }

        private void Activate(long index)
        {
            if (rebuilding || index < 0)
            {
                return;
            }

            ReleaseIcons();
            var active = index < Forms.Length ? rows[Forms[(int)index]] : characterRows;
            thumbnails.SetVisible(active);
            var list = index < Forms.Length ? lists[Forms[(int)index]] : characters;
            list.SetItems(RowsOf(active, true));
            var hint = list.GetNodeOrNull<Label>("ThumbnailBudget");
            if (thumbnails.RejectedCount > 0)
            {
                if (hint == null)
                {
                    hint = new Label
                    {
                        Name = "ThumbnailBudget",
                        AutowrapMode = TextServer.AutowrapMode.WordSmart,
                        MouseFilter = MouseFilterEnum.Ignore,
                    };
                    list.AddChild(hint);
                }

                hint.Text = ToolUi.Tr("tools/rpg_editor/thumbnail_budget");
                hint.Visible = true;
            }
            else if (hint != null)
            {
                hint.Visible = false;
            }
        }

        private List<(string Id, string Text, Texture2D Icon)> RowsOf(IEnumerable<RpgObject> source, bool images)
        {
            var result = new List<(string Id, string Text, Texture2D Icon)>();
            foreach (var obj in source)
            {
                result.Add((obj.ID, Caption(obj), images ? thumbnails.Get(obj) : null));
            }

            return result;
        }

        private static int CompareObjects(RpgObject first, RpgObject second)
        {
            return StringComparer.Ordinal.Compare(first.ID, second.ID);
        }

        private static string Caption(RpgObject obj)
        {
            return string.IsNullOrEmpty(obj.Name) ? obj.ID : TranslationServer.Translate(obj.Name) + "  ·  " + obj.ID;
        }

        private void OnObjectChosen(string id)
        {
            if (!string.IsNullOrEmpty(id) && objects.TryGetValue(id, out var obj) && obj.Form is { } form)
            {
                ObjectChosen?.Invoke(id, form);
            }
        }

        private void OnCharacterChosen(string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                CharacterChosen?.Invoke(id);
            }
        }

        /// <summary>
        /// Full-resolution placement preview, not a palette miniature. Source textures are borrowed;
        /// an AtlasTexture result is newly owned by the caller and must be detached then disposed.
        /// RpgEditorTextures tracks that wrapper and its source pins for ghosts and drags.
        /// </summary>
        public static Texture2D ThumbnailOf(RpgObject obj, Func<string, Texture2D> picture, Func<string, Texture2D> charset)
        {
            if (obj == null)
            {
                return null;
            }

            if (obj.Form == RpgObjectForm.Actor)
            {
                var sheet = charset?.Invoke(obj.Charset ?? string.Empty);
                if (sheet == null)
                {
                    return null;
                }

                var frame = TileWalkSheet.Frame(sheet, TileWalkSheet.Animation(sheet).IdleColumn, 0);
                return new AtlasTexture
                {
                    Atlas = sheet,
                    Region = frame
                };
            }

            var path = obj.Form switch
            {
                RpgObjectForm.Surface => obj.Surface?.Image,
                RpgObjectForm.Segment => obj.Segment?.Image,
                _ => FirstImagePart(obj)?.Image,
            };
            var texture = string.IsNullOrEmpty(path) ? null : picture?.Invoke(path);
            if (texture == null)
            {
                return null;
            }

            var part = FirstImagePart(obj);
            if (obj.Form is RpgObjectForm.Sprite or RpgObjectForm.Actor && part?.Crop is { Length: 4 } crop)
            {
                return new AtlasTexture
                {
                    Atlas = texture,
                    Region = new Rect2(crop[0], crop[1], crop[2], crop[3])
                };
            }

            return texture;
        }

        private static RpgObjectPart FirstImagePart(RpgObject obj)
        {
            if (obj?.Parts != null)
            {
                foreach (var part in obj.Parts)
                {
                    if (!string.IsNullOrEmpty(part?.Image))
                    {
                        return part;
                    }
                }
            }

            return null;
        }

        /// <summary>The locale key of a form's tab title.</summary>
        public static string TabKey(RpgObjectForm form)
        {
            return form switch
            {
                RpgObjectForm.Surface => "tools/rpg_editor/tab_surface",
                RpgObjectForm.Segment => "tools/rpg_editor/tab_segment",
                RpgObjectForm.Sprite => "tools/rpg_editor/tab_sprite",
                _ => "tools/rpg_editor/tab_actor",
            };
        }
    }
}
