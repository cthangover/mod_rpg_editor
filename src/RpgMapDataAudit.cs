using System;
using System.Collections.Generic;
using System.Text.Json;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;
using Cthangover.Mods.Rpg.Validation;
using Cthangover.Tools.Services;
using Cthangover.Tools.EntityEditor;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>Audits unsaved map/placement data with the normal effective-object resolver and no live gameplay state.</summary>
    public sealed class RpgMapDataAudit
    {
        private readonly RpgMapDocument document;

        private readonly Func<string, RpgObject> objects;

        private readonly string projection;

        private readonly RpgValidationImages images = new();

        private readonly RpgMapDataReport result = new();

        private readonly Dictionary<string, RpgDataIssue> findings = new(StringComparer.Ordinal);

        private RpgMap map;

        private RpgMapDataAudit(RpgMapDocument document, string projection, Func<string, RpgObject> objects)
        {
            this.document = document;
            this.projection = projection;
            this.objects = objects;
        }

        /// <summary>
        /// Checks up to 16,384 cells and all retained placements; an oversize input reports incomplete work without
        /// writes.
        /// </summary>
        public static RpgMapDataReport Check(RpgMapDocument document, string projection, Func<string, RpgObject> objects)
        {
            ArgumentNullException.ThrowIfNull(document);
            ArgumentNullException.ThrowIfNull(objects);
            var audit = new RpgMapDataAudit(document, projection, objects);
            audit.Run();
            return audit.result;
        }

        private void Run()
        {
            foreach (var issue in RpgMapSyntax.Check(document.Data))
            {
                issue.Resource = document.MapId;
                issue.Source = MapSource();
                Add(issue);
            }

            if (document.Width <= 0 || document.Height <= 0 || (long)document.Width * document.Height > 16384)
            {
                result.Complete = false;
                Add(new RpgDataIssue
                {
                    Code = "budget",
                    Field = "Width/Height",
                    Resource = document.MapId,
                    Source = MapSource(),
                });
                return;
            }

            try
            {
                map = RpgEditorScene.MapOf(document.Data);
                CheckPainted();
                foreach (var record in document.Placements)
                {
                    if (document.IsDeleted(record.Id) || record.Data == null)
                    {
                        continue;
                    }

                    var placement = RpgEditorScene.PlacementOf(record.Data);
                    CheckObject(placement.Object, placement, 1, "Object");
                }
            }
            catch (JsonException error)
            {
                result.Complete = false;
                Add(new RpgDataIssue
                {
                    Code = "json",
                    Field = error.Path ?? "$",
                    Detail = error.Message,
                    Resource = document.MapId,
                    Source = MapSource(),
                });
            }

            result.ImagesDecoded = images.Decoded;
        }

        private void CheckPainted()
        {
            var ground = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var structure = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var y = 0; y < document.Height; y++)
            {
                for (var x = 0; x < document.Width; x++)
                {
                    Count(ground, document.ObjectAt(RpgStrokeLayer.Ground, x, y));
                    Count(structure, document.ObjectAt(RpgStrokeLayer.Structure, x, y));
                }
            }

            foreach (var entry in ground)
            {
                CheckObject(entry.Key, null, entry.Value, "Ground");
                used.Add(entry.Key);
            }

            foreach (var entry in structure)
            {
                CheckObject(entry.Key, null, entry.Value, "Structure");
                used.Add(entry.Key);
            }

            foreach (var entry in document.Legend)
            {
                if (!used.Contains(entry.Value))
                {
                    result.UnusedLegendEntries++;
                }
            }
        }

        private static void Count(Dictionary<string, int> counts, string id)
        {
            if (!string.IsNullOrEmpty(id))
            {
                counts.TryGetValue(id, out var count);
                counts[id] = count + 1;
            }
        }

        private void CheckObject(string id, RpgPlacement placement, int uses, string field)
        {
            var value = id == null ? null : objects(id);
            if (value == null)
            {
                Add(new RpgDataIssue
                {
                    Code = "reference",
                    Field = field,
                    Resource = id,
                    Placement = placement?.ID,
                    Source = MapSource(),
                    Uses = uses,
                });
                return;
            }

            if (field == "Ground" && value.Form != RpgObjectForm.Surface
                || field == "Structure" && value.Form != RpgObjectForm.Segment
                || field == "Object" && value.Form is not (RpgObjectForm.Sprite or RpgObjectForm.Actor))
            {
                Add(new RpgDataIssue
                {
                    Code = "layer_form",
                    Field = field,
                    Resource = id,
                    Placement = placement?.ID,
                    Source = MapSource(),
                    Uses = uses,
                });
            }

            var authored = ForeignEntities.Explain("rpg_object", id)?.Value;
            if (authored != null)
            {
                foreach (var issue in RpgDataCheck.Authored(authored, projection, map.PatchSize, images.Size))
                {
                    issue.Resource = id;
                    issue.Placement = placement?.ID;
                    issue.Source = ObjectSource(id);
                    issue.Context = "authored";
                    issue.Uses = uses;
                    Add(issue);
                }
            }

            if (placement != null)
            {
                var record = document.AnyPlacement(placement.ID);
                foreach (var issue in RpgDataCheck.Authored(record.Data, projection, map.PatchSize, images.Size))
                {
                    issue.Resource = id;
                    issue.Placement = placement.ID;
                    issue.Source = (record.OwnerMod ?? document.OwnMod) + "/" + record.File;
                    issue.Context = "authored placement";
                    Add(issue);
                }
            }

            result.ObjectsChecked++;
            foreach (var issue in RpgObjectDataAudit.Check(value, projection, map.PatchSize, images,
                ObjectSource(id), map.Variant, placement))
            {
                issue.Uses = uses;
                Add(issue);
            }
        }

        private void Add(RpgDataIssue issue)
        {
            issue.Projection = projection;
            var key = issue.Code + "|" + issue.Resource + "|" + issue.Field + "|" + issue.Context + "|" + issue.Detail;
            if (findings.TryGetValue(key, out var previous))
            {
                previous.Uses += issue.Uses;
                return;
            }

            findings.Add(key, issue);
            result.Issues.Add(issue);
        }

        private string MapSource()
        {
            return (document.Map.OwnerMod ?? document.OwnMod) + "/" + document.Map.File;
        }

        private static string ObjectSource(string id)
        {
            var source = ForeignEntities.Explain("rpg_object", id);
            return source?.Fields != null && source.Fields.TryGetValue("Id", out var origin)
                ? origin.ModId + "/" + origin.File : id;
        }
    }
}
