/*
 * Script Summary:
 * ----------------
 * Serializes a GameObject into a structured JSON with IFC properties and mesh summary.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ObjectStructureSerializer (static).
 * - Key Methods:
 *     • Serialize(GameObject) – Returns indented JSON string with:
 *         - name, unique_id, type.
 *         - ifc_properties dictionary.
 *         - meshSummary (bounding box, counts, holes).
 *         - meshSummaryText (human-readable).
 * - Dependencies/Interactions:
 *     • Uses ifcProperties and MeshSummarizer.
 *     • Consumed by AIClient and ObjectSummaryExporter.
 * - Special Considerations:
 *     • Gracefully handles missing IFC or mesh.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string json = ObjectStructureSerializer.Serialize(go);
 * Debug.Log(json);
 * ```
 */

/*
 * Serializes a GameObject into a structured JSON with IFC properties and mesh summary.
 * Keeps existing shape, but:
 *  - Accepts Unique ID / GlobalId / GUID, via UniqueIdExtractor.
 *  - Falls back to a deterministic stable id if missing.
 *  - Uses sharedMesh to avoid editor-time allocations.
 */

using Newtonsoft.Json;
using UnityEngine;
using System.Collections.Generic;

public static class ObjectStructureSerializer
{
    public static string Serialize(GameObject obj)
    {
        var data = new Dictionary<string, object>();

        // --- ID & IFC props ----------------------------------------------------
        string uniqueId = UniqueIdExtractor.GetOrStable(obj); // <— robust id
        string type = "unknown";

        var ifc = obj.GetComponent<ifcProperties>();
        if (ifc != null)
        {
            var dict = new Dictionary<string, string>();
            if (ifc.properties != null && ifc.nominalValues != null)
            {
                int count = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
                for (int i = 0; i < count; i++)
                {
                    string key = ifc.properties[i];
                    string value = ifc.nominalValues[i];
                    if (key == null) continue;

                    dict[key] = value;

                    // Keep your original "type" behavior
                    if (key.ToLower() == "type" && !string.IsNullOrWhiteSpace(value))
                        type = value;
                }
            }
            data["ifc_properties"] = dict;
        }

        // --- Identity basics ---------------------------------------------------
        data["name"] = obj.name;
        data["unique_id"] = string.IsNullOrWhiteSpace(uniqueId) ? "unknown" : uniqueId; // should never be "unknown" now
        data["type"] = type;
        data["parameters"] = new Dictionary<string, string>();

        // --- Mesh summary (sharedMesh) ----------------------------------------
        Mesh mesh = null;
        var mf = obj.GetComponent<MeshFilter>();
        var smr = obj.GetComponent<SkinnedMeshRenderer>();
        if (mf?.sharedMesh != null) mesh = mf.sharedMesh;
        else if (smr?.sharedMesh != null) mesh = smr.sharedMesh;

        if (mesh != null)
        {
            var summary = MeshSummarizer.Summarize(mesh);
            data["meshSummary"] = summary;
            if (summary.ContainsKey("meshSummaryText"))
                data["meshSummaryText"] = summary["meshSummaryText"];
        }
        else
        {
            data["meshSummary"] = new { boundingBox = "N/A" };
            data["meshSummaryText"] = "No mesh data available.";
        }

        var settings = new JsonSerializerSettings { ReferenceLoopHandling = ReferenceLoopHandling.Ignore };
        return JsonConvert.SerializeObject(data, Formatting.Indented, settings);
    }
}
