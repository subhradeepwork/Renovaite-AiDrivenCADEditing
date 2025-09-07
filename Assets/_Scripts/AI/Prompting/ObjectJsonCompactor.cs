/*
 * Script Summary:
 * ----------------
 * Reduces verbose object JSON into a compact form suitable for LLM prompts.
 * Keeps only properties relevant to user intent (material, dimensions, transforms, etc.).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ObjectJsonCompactor (static).
 * - Key Methods:
 *     • CompactObjectJson(rawJson, maxChars, userPrompt) – Returns truncated/minified JSON containing
 *       only the most relevant IFC/mesh properties, based on prompt keywords and size constraints.
 * - Helper Methods:
 *     • InferExtraKeysForPrompt(prompt) – Adds exact keys to retain depending on intent (e.g., door, material).
 *     • BuildContainsTokens(prompt) – Creates tokens for fuzzy contains matches on property names.
 *     • KeyContainsAny(key, tokens) – Fuzzy property filter.
 *     • SafeTruncate(str, max) – Ensures output fits within maxChars.
 * - Default Keep List:
 *     • Unique ID, Element Classification, Material, IFC summary fields, bounding box dims, triangle count.
 * - Dependencies/Interactions:
 *     • Consumed in PromptBuilder before sending context to OpenAI.
 *     • Uses SimpleJSON for parsing and manipulation.
 * - Special Considerations:
 *     • Expands keep list dynamically based on prompt (regex for material, scale, move, rotate, doors/windows).
 *     • Applies two-pass trimming if still too large (drops fuzzy-kept keys first).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string compact = ObjectJsonCompactor.CompactObjectJson(fullJson, 2000, "resize all concrete walls");
 * // compact contains minimal JSON needed for model reasoning.
 * ```
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using SimpleJSON;

namespace RenovAite.AI.Prompting
{
    public static class ObjectJsonCompactor
    {
        // Keep only fields that actually help the model decide an action
        private static readonly HashSet<string> Keep = new()
        {
            "Unique ID", "ID", "Element Classification", "Type", "IsExternal",
            "Material", "Building Material / Composite / Profile / Fill",
            "meshSummaryText", "ifcSummaryTextShort",
            "bbox_width_m", "bbox_height_m", "bbox_depth_m", "triangle_count"
        };

        public static string CompactObjectJson(string rawJson, int maxChars, string userPrompt)
        {
            if (string.IsNullOrEmpty(rawJson)) return rawJson;

            try
            {
                var node = JSON.Parse(rawJson);
                if (node == null || !node.IsObject) return SafeTruncate(rawJson, maxChars);

                var root = node.AsObject;

                // Baseline keep list (case-insensitive)
                var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Unique ID", "ID", "Element Classification", "Type", "IsExternal",
            "Material", "Building Material / Composite / Profile / Fill",
            "meshSummaryText", "ifcSummaryTextShort",
            "bbox_width_m", "bbox_height_m", "bbox_depth_m", "triangle_count"
        };

                // Expand keep set based on the user's prompt (intent packs + fuzzy contains)
                var extraExactKeys = InferExtraKeysForPrompt(userPrompt);          // exact names to keep
                foreach (var k in extraExactKeys) keep.Add(k);

                var fuzzyTokens = BuildContainsTokens(userPrompt);                 // tokens for fuzzy "contains" match

                if (root.HasKey("ifc_properties") && root["ifc_properties"].IsObject)
                {
                    var props = root["ifc_properties"].AsObject;

                    // If intent is unknown and you prefer not to drop information:
                    // (read from the serialized field above)
                    // NOTE: since this is a static method, move the flag to a const or make this method non-static if you want to read the instance field.
                    // For simplicity, we emulate OFF here; to use the flag, make this method non-static and access the field directly.

                    var toRemove = new List<string>();
                    foreach (var kv in props)
                    {
                        var key = kv.Key;
                        bool keepBecauseExact = keep.Contains(key);
                        bool keepBecauseFuzzy = KeyContainsAny(key, fuzzyTokens);

                        if (!keepBecauseExact && !keepBecauseFuzzy)
                            toRemove.Add(key);
                    }
                    foreach (var k in toRemove) props.Remove(k);

                    // If still too big, do a second pass to drop fuzzy-kept keys first
                    var compactOnce = root.ToString(0);
                    if (compactOnce.Length > maxChars && fuzzyTokens.Count > 0)
                    {
                        var fuzzyRemovals = new List<string>();
                        foreach (var kv in props)
                        {
                            var key = kv.Key;
                            if (!keep.Contains(key) && KeyContainsAny(key, fuzzyTokens))
                                fuzzyRemovals.Add(key);
                        }
                        foreach (var k in fuzzyRemovals) props.Remove(k);
                    }
                }

                var compact = root.ToString(0); // minified
                return SafeTruncate(compact, maxChars);
            }
            catch
            {
                return SafeTruncate(rawJson, maxChars);
            }
        }


        // Exact keys to include for specific intents (door/window/material/transform)
        private static HashSet<string> InferExtraKeysForPrompt(string prompt)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(prompt)) return set;

            string p = prompt.ToLowerInvariant();

            // Material / colour / finish
            if (Regex.IsMatch(p, @"\b(material|texture|colour|color|paint|finish|surface|kleur|afwerking)\b"))
            {
                set.UnionWith(new[]
                {
            "Material",
            "Building Material / Composite / Profile / Fill",
            "kleur",
            "afwerking",
            "Surface",           // if present
            "Finish"             // if present
        });
            }

            // Scale / dimensions / thickness
            if (Regex.IsMatch(p, @"\b(scale|resize|width|height|depth|thickness|size|dimension|expand|shrink)\b"))
            {
                set.UnionWith(new[]
                {
            "Nominal W x H x T Size",
            "Nominal W x H Size",
            "Reveal Dimensions",
            "Wallhole Dimensions",
            "Unit Dimensions",
            "Leaf Dimensions",
            "Egress Dimensions"
        });
            }

            // Move / position
            if (Regex.IsMatch(p, @"\b(move|position|translate|offset|relocate|reposition)\b"))
            {
                set.UnionWith(new[]
                {
            "Position" // if you store one; otherwise bbox_* already helps
        });
            }

            // Rotate / orientation / flip / swing
            if (Regex.IsMatch(p, @"\b(rotate|rotation|angle|tilt|yaw|pitch|roll|orientation|draairichting|flip)\b"))
            {
                set.UnionWith(new[]
                {
            "Orientation",
            "Draairichting",
            "Flip",
            "Openingshoek"
        });
            }

            // Door / window specific tweaks (openings, leaves, sidelights) — keep minimal
            if (Regex.IsMatch(p, @"\b(door|window|opening|sidelight|bovenlicht|leaf|handle|klink)\b"))
            {
                set.UnionWith(new[]
                {
            "Leaf Dimensions",
            "Egress Dimensions",
            "Openingshoek",
            "Type deurpaneel (gs_door_typ)",
            "Type opening (gs_optype_m_05)", // keep just a couple of i18n-heavy flags
            "Type opening (gs_optype_m_06)"
        });
            }

            return set;
        }



        // Words that, if found in a property name, mean "keep it"
        private static List<string> BuildContainsTokens(string prompt)
        {
            var tokens = new List<string>();
            if (string.IsNullOrWhiteSpace(prompt)) return tokens;

            string p = prompt.ToLowerInvariant();

            // Material/colour
            if (Regex.IsMatch(p, @"\b(material|texture|colour|color|paint|finish|surface|kleur|afwerking)\b"))
                tokens.AddRange(new[] { "material", "surface", "finish", "kleur", "afwerking" });

            // Dimensions
            if (Regex.IsMatch(p, @"\b(scale|resize|width|height|depth|thickness|size|dimension)\b"))
                tokens.AddRange(new[] { "width", "height", "depth", "thickness", "dimension", "maat", "size", "w x h", "w x h x t" });

            // Move
            if (Regex.IsMatch(p, @"\b(move|position|translate|offset|relocate|reposition)\b"))
                tokens.AddRange(new[] { "position", "pos", "offset" });

            // Rotate/orientation
            if (Regex.IsMatch(p, @"\b(rotate|rotation|angle|tilt|yaw|pitch|roll|orientation|draairichting|flip)\b"))
                tokens.AddRange(new[] { "orientation", "draairichting", "angle", "flip" });

            // Openings/door/window
            if (Regex.IsMatch(p, @"\b(door|window|opening|sidelight|bovenlicht|leaf|handle|klink)\b"))
                tokens.AddRange(new[] { "leaf", "opening", "sidelight", "bovenlicht", "handle", "klink" });

            return tokens;
        }



        private static bool KeyContainsAny(string key, List<string> tokens)
        {
            if (tokens == null || tokens.Count == 0) return false;
            var lk = key.ToLowerInvariant();
            foreach (var t in tokens)
                if (!string.IsNullOrEmpty(t) && lk.Contains(t)) return true;
            return false;
        }

        private static string SafeTruncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

    }
}
