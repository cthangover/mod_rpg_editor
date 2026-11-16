using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Rpg.Model;

namespace Cthangover.Tools.RpgEditor
{
    /// <summary>
    /// Document-local presentation inputs. Values never enter story flags, undo history or saved JSON;
    /// effective variant/placement/state merging uses the runtime resolver on detached object copies.
    /// Conditions use the ordinary grammar with this owner's scoped flags and no live quest service.
    /// </summary>
    public sealed class RpgEditorPreview
    {
        private static readonly ConditionalWeakTable<RpgMapDocument, RpgEditorPreview> documents = new();

        private readonly Dictionary<string, string> flags = new(StringComparer.Ordinal);

        private RpgEditorPreview()
        {
        }

        /// <summary>Shares preview values between the canvas and inspector for the lifetime of one document.</summary>
        public static RpgEditorPreview ForDocument(RpgMapDocument document)
        {
            ArgumentNullException.ThrowIfNull(document);
            return documents.GetValue(document, Create);
        }

        /// <summary>Reads a detached placement-local checkbox; absent inputs start false, independently of live flags.</summary>
        public bool IsSet(string placement, string name)
        {
            return flags.TryGetValue(ScopedKey(placement, name), out var value) && value == "1";
        }

        /// <summary>Changes only this document's preview input; callers rebuild presentation without recording an edit.</summary>
        public void Set(string placement, string name, bool enabled)
        {
            flags[ScopedKey(placement, name)] = enabled ? "1" : "0";
        }

        /// <summary>Resets this placement while preserving every other placement's detached inputs.</summary>
        public void Reset(string placement)
        {
            ArgumentException.ThrowIfNullOrEmpty(placement);
            var prefix = placement + ":";
            var remove = new List<string>();
            foreach (var key in flags.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    remove.Add(key);
                }
            }

            foreach (var key in remove)
            {
                flags.Remove(key);
            }
        }

        /// <summary>Resolves map/placement variants and overrides before choosing the last eligible detached state.</summary>
        public RpgObject Resolve(RpgObject resolved, RpgMap map, RpgPlacement placement)
        {
            var effective = RpgObjectResolve.Effective(resolved, map?.Variant, placement);
            if (effective == null)
            {
                return null;
            }

            var detached = JsonSerializer.Deserialize<RpgObject>(JsonSerializer.Serialize(effective, JsonOptionsProvider.ReadOptions),
                JsonOptionsProvider.ReadOptions);
            return placement == null ? detached : RpgObjectResolve.WithState(detached, StateIndex(detached, placement.ID));
        }

        /// <summary>Chooses the same last matching state as runtime, using only this document's flags.</summary>
        public int StateIndex(RpgObject effective, string placement)
        {
            return RpgObjectResolve.ActiveState(effective, Holds, placement);
        }

        private bool Holds(string condition, string scope)
        {
            try
            {
                var syntax = ConditionParser.Parse(condition);
                return syntax == null || syntax.Evaluate(new EvaluationContext
                {
                    Flags = flags,
                    Scope = scope,
                });
            }
            catch (FormatException error)
            {
                GameLogger.Log("COND", "Editor preview condition parse error: " + error.Message, LogLevel.Error);
                return false;
            }
        }

        private static string ScopedKey(string placement, string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(placement);
            ArgumentException.ThrowIfNullOrEmpty(name);
            return placement + ":" + name;
        }

        private static RpgEditorPreview Create(RpgMapDocument document)
        {
            return new RpgEditorPreview();
        }
    }
}
