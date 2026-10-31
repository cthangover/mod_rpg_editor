namespace Cthangover.Tools.RpgEditor
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Characters;
    using Cthangover.Core.Mods.Resolvers;
    using Cthangover.Mods.Rpg;
    using Cthangover.Mods.Rpg.Model;
    using Cthangover.Mods.TileRender;
    using Cthangover.Tools.Common;
    using Godot;

    /// <summary>
    /// The object palette by shape (plan 12.4): one tab per form — surfaces, segments, sprites and actors — each a
    /// searchable list with a thumbnail. Choosing a row raises <see cref="ObjectChosen"/>; the window turns the form
    /// into the tool (a surface or a segment paints, a sprite or an actor is placed).
    /// <para>
    /// A sixth tab lists the characters that have a walking sheet (plan 14.G7). Choosing one raises
    /// <see cref="CharacterChosen"/> instead: the window makes the character's actor object (see
    /// <c>RpgCharacterActors</c>) and places it like any actor.
    /// </para>
    /// </summary>
    public partial class RpgEditorPalette : TabContainer
    {
        private const int ThumbSide = 56;

        private static readonly RpgObjectForm[] Forms = { RpgObjectForm.Surface, RpgObjectForm.Segment, RpgObjectForm.Sprite, RpgObjectForm.Actor };

        private readonly Dictionary<RpgObjectForm, FilteredList> lists = new();

        /// <summary>A row of the palette was chosen: the object id and its form.</summary>
        public event Action<string, RpgObjectForm> ObjectChosen;

        /// <summary>A character of the characters tab was chosen: its id.</summary>
        public event Action<string> CharacterChosen;

        private FilteredList characters;

        /// <summary>
        /// Fills the tabs from <paramref name="objects"/>. <paramref name="picture"/> loads a picture of <c>rpg_images</c>
        /// (borrowed) and <paramref name="charset"/> a walking sheet of <c>charsets</c>.
        /// </summary>
        public RpgEditorPalette(IReadOnlyDictionary<string, RpgObject> objects, Func<string, Texture2D> picture, Func<string, Texture2D> charset)
        {
            Name = "RpgPalette";
            SizeFlagsHorizontal = SizeFlags.ExpandFill;
            SizeFlagsVertical = SizeFlags.ExpandFill;
            foreach (var form in Forms)
            {
                var list = new FilteredList(220f, "tools/rpg_editor/palette_search") { Name = "Palette_" + form, SizeFlagsVertical = SizeFlags.ExpandFill };
                list.FitIcons(ThumbSide);
                var current = form;
                list.Chosen += id =>
                {
                    if (!string.IsNullOrEmpty(id))
                        ObjectChosen?.Invoke(id, current);
                };
                AddChild(list);
                SetTabTitle(GetChildCount() - 1, ToolUi.Tr(TabKey(form)));
                lists[form] = list;
                list.SetItems(Rows(objects, form, picture, charset));
            }

            characters = new FilteredList(220f, "tools/rpg_editor/palette_search") { Name = "Palette_Characters", SizeFlagsVertical = SizeFlags.ExpandFill };
            characters.FitIcons(ThumbSide);
            characters.Chosen += id =>
            {
                if (!string.IsNullOrEmpty(id))
                    CharacterChosen?.Invoke(id);
            };
            AddChild(characters);
            SetTabTitle(GetChildCount() - 1, ToolUi.Tr("tools/rpg_editor/tab_character"));
            characters.SetItems(CharacterRows(picture, charset));
        }

        /// <summary>Fills every tab again from the current objects and characters (after the window wrote an actor object).</summary>
        public void Rebuild(IReadOnlyDictionary<string, RpgObject> objects, Func<string, Texture2D> picture, Func<string, Texture2D> charset)
        {
            foreach (var form in Forms)
                lists[form].SetItems(Rows(objects, form, picture, charset));
            characters?.SetItems(CharacterRows(picture, charset));
        }

        /// <summary>The characters that have a walking sheet, by id; each row shows the sheet's idle frame.</summary>
        private static List<(string Id, string Text, Texture2D Icon)> CharacterRows(Func<string, Texture2D> picture, Func<string, Texture2D> charset)
        {
            var rows = new List<(string Id, string Text, Texture2D Icon)>();
            foreach (var pair in Entities.Collect<CharacterInfo>().OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                var sheet = RpgPlayerLook.CharsetOfId(pair.Key);
                if (sheet == null)
                    continue;
                var actor = new RpgObject { ID = pair.Key, Form = RpgObjectForm.Actor, Charset = sheet };
                var name = pair.Value?.Name;
                var caption = string.IsNullOrEmpty(name) ? pair.Key : TranslationServer.Translate(name) + "  ·  " + pair.Key;
                rows.Add((pair.Key, caption, ThumbnailOf(actor, picture, charset)));
            }
            return rows;
        }

        /// <summary>Selects an object in its form's tab without raising <see cref="ObjectChosen"/>.</summary>
        public void Select(RpgObjectForm form, string id)
        {
            if (!lists.TryGetValue(form, out var list))
                return;
            CurrentTab = Array.IndexOf(Forms, form);
            list.Select(id);
        }

        /// <summary>The number of rows in a form's tab.</summary>
        public int Count(RpgObjectForm form) => lists.TryGetValue(form, out var list) ? list.Count : 0;

        /// <summary>The thumbnail of an object: its first picture (cropped as the game crops it), or its charset's first frame.</summary>
        public static Texture2D ThumbnailOf(RpgObject obj, Func<string, Texture2D> picture, Func<string, Texture2D> charset)
        {
            if (obj == null)
                return null;
            if (obj.Form == RpgObjectForm.Actor)
            {
                var sheet = charset?.Invoke(obj.Charset ?? string.Empty);
                if (sheet == null)
                    return null;
                var frame = TileWalkSheet.Frame(sheet.GetSize(), TileWalkSheet.IdleColumn, 0);
                return new AtlasTexture { Atlas = sheet, Region = frame };
            }
            var path = obj.Form switch
            {
                RpgObjectForm.Surface => obj.Surface?.Image,
                RpgObjectForm.Segment => obj.Segment?.Image,
                _ => obj.Parts?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Image))?.Image,
            };
            var texture = string.IsNullOrEmpty(path) ? null : picture?.Invoke(path);
            if (texture == null)
                return null;
            var part = obj.Parts?.FirstOrDefault(p => !string.IsNullOrEmpty(p.Image));
            if (obj.Form is RpgObjectForm.Sprite or RpgObjectForm.Actor && part?.Crop is { Length: 4 } crop)
                return new AtlasTexture { Atlas = texture, Region = new Rect2(crop[0], crop[1], crop[2], crop[3]) };
            return texture;
        }

        /// <summary>The locale key of a form's tab title.</summary>
        public static string TabKey(RpgObjectForm form) => form switch
        {
            RpgObjectForm.Surface => "tools/rpg_editor/tab_surface",
            RpgObjectForm.Segment => "tools/rpg_editor/tab_segment",
            RpgObjectForm.Sprite => "tools/rpg_editor/tab_sprite",
            _ => "tools/rpg_editor/tab_actor",
        };

        private static List<(string Id, string Text, Texture2D Icon)> Rows(IReadOnlyDictionary<string, RpgObject> objects, RpgObjectForm form,
            Func<string, Texture2D> picture, Func<string, Texture2D> charset)
            => objects.Values.Where(o => o != null && o.Form == form && !string.IsNullOrEmpty(o.ID))
                .OrderBy(o => o.ID, StringComparer.Ordinal)
                .Select(o => (o.ID, Caption(o), ThumbnailOf(o, picture, charset)))
                .ToList();

        private static string Caption(RpgObject obj)
            => string.IsNullOrEmpty(obj.Name) ? obj.ID : TranslationServer.Translate(obj.Name) + "  ·  " + obj.ID;
    }
}
