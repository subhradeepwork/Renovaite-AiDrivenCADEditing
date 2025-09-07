/*
 * Script Summary:
 * ----------------
 * Extracts IFC properties from a GameObject’s ifcProperties component into key-value pairs.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: IfcPropertyExtractor (static).
 * - Key Methods:
 *     • Extract(GameObject go) – Returns list of (property, value) pairs.
 *         - Reads from ifcProperties component (properties + nominalValues).
 *         - Includes Unique ID (via UniqueIdExtractor) if available.
 *         - Always prepends Object Name as first property.
 * - Dependencies/Interactions:
 *     • ifcProperties component – Source of IFC data.
 *     • UniqueIdExtractor – Ensures Unique ID is available.
 *     • Used when building context JSON for RAG and prompt injection.
 * - Special Considerations:
 *     • Safe against null GameObjects or missing ifcProperties.
 *     • Handles mismatched property/value counts.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var pairs = IfcPropertyExtractor.Extract(go);
 * foreach (var kv in pairs)
 *     Debug.Log($"{kv.Key} = {kv.Value}");
 * ```
 */

using System.Collections.Generic;
using UnityEngine;

public static class IfcPropertyExtractor
{
    // Returns key/value pairs from an ifcProperties component if present.
    public static List<KeyValuePair<string, string>> Extract(GameObject go)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (go == null) return result;

        var comp = go.GetComponent<ifcProperties>();
        if (comp == null) return result;

        // Safely pair properties & values by the shortest count
        var props = comp.properties;
        var vals = comp.nominalValues;
        if (props == null || vals == null) return result;

        int n = Mathf.Min(props.Count, vals.Count);
        for (int i = 0; i < n; i++)
        {
            var k = (props[i] ?? "").Trim();
            var v = (vals[i] ?? "").Trim();
            if (string.IsNullOrEmpty(k) && string.IsNullOrEmpty(v)) continue;
            result.Add(new KeyValuePair<string, string>(k, v));
        }

        // Optionally add a UID if present via your extractor
        var uid = UniqueIdExtractor.TryGetUniqueId(go);
        if (!string.IsNullOrEmpty(uid))
            result.Insert(0, new KeyValuePair<string, string>("Unique ID", uid));

        // Optional: add GameObject name/type
        result.Insert(0, new KeyValuePair<string, string>("Object Name", go.name));

        return result;
    }
}
