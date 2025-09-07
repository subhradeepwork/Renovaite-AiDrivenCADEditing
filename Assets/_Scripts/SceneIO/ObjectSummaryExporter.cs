/*
 * Script Summary:
 * ----------------
 * Exports all scene objects with mesh + IFC metadata into a JSON file for RAG embedding/indexing.
 *
 * Developer Notes:
 * ----------------
 * - Data Classes:
 *     • ObjectRecord – per-object metadata (id, name, type, tags, summaries, numeric).
 *     • Numeric – bounding box + mesh stats.
 *     • SceneExport – top-level export { scene_id, exported_at, objects[] }.
 * - Key Methods:
 *     • ExportAll(path) – Writes JSON file (default ../exports/object_summaries.json).
 *     • BuildAndCleanRecord(go) – Collects IFC + mesh data, cleans type, tags, summaries.
 * - Dependencies/Interactions:
 *     • ObjectStructureSerializer, MeshSummarizer.
 *     • IfcProperties and StableGuid for unique_id fallback.
 * - Special Considerations:
 *     • Skips or tags zero-geometry meshes.
 *     • Editor menu: RAG → Export Object Summaries.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * ObjectSummaryExporter.ExportAll();
 * ```
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;                 // for MenuItem
using Newtonsoft.Json.Linq;        // parse JSON from ObjectStructureSerializer

[Serializable]
public class ObjectRecord
{
    public string unique_id;
    public string name;
    public string ifc_type;              // raw (may be empty)
    public string ifc_type_final;        // cleaned + stable
    public string meshSummaryText;
    public string ifcSummaryText;        // full flattened (verbose ok)
    public string ifcSummaryTextShort;   // compact for embeddings
    public bool has_geometry;            // vertex/triangle > 0
    public Numeric numeric = new Numeric();
    public List<string> tags = new List<string>();
}

[Serializable]
public class Numeric
{
    public float bbox_width_m;
    public float bbox_height_m;
    public float bbox_depth_m;
    public int triangle_count;
    public int vertex_count;
    public int hole_count;
}

[Serializable]
public class SceneExport
{
    public string scene_id;
    public string exported_at;
    public List<ObjectRecord> objects = new List<ObjectRecord>();
}

public static class ObjectSummaryExporter
{
    // Toggle behaviors
    private const bool EXCLUDE_ZERO_GEOMETRY = false;      // set true to skip empty meshes entirely
    private const bool TAG_ZERO_GEOMETRY_AS_MISSING = true; // add "geometry_missing" tag when empty

    private static readonly string DefaultPath =
        Path.Combine(Application.dataPath, "../exports/object_summaries.json");

#if UNITY_EDITOR
    [MenuItem("RAG/Export Object Summaries")]
    public static void ExportMenu() => ExportAll();

#endif
    public static void ExportAll(string path = null)
    {
        path ??= DefaultPath;

        var scene = new SceneExport
        {
            scene_id = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name,
            exported_at = DateTime.UtcNow.ToString("o")
        };

        // Gather objects with meshes
        var meshGOs = GameObject.FindObjectsOfType<MeshFilter>()
            .Select(mf => mf.gameObject)
            .Concat(GameObject.FindObjectsOfType<SkinnedMeshRenderer>().Select(smr => smr.gameObject))
            .Distinct();

        int kept = 0, skipped = 0;
        foreach (var go in meshGOs)
        {
            try
            {
                var rec = BuildAndCleanRecord(go);
                if (rec == null)
                {
                    skipped++;
                    continue;
                }

                if (EXCLUDE_ZERO_GEOMETRY && !rec.has_geometry)
                {
                    skipped++;
                    continue;
                }

                if (TAG_ZERO_GEOMETRY_AS_MISSING && !rec.has_geometry)
                {
                    if (!rec.tags.Contains("geometry_missing"))
                        rec.tags.Add("geometry_missing");
                }

                scene.objects.Add(rec);
                kept++;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RAG Export] Failed {go.name}: {ex.Message}");
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, JsonUtility.ToJson(scene, prettyPrint: true));
        Debug.Log($"[RAG Export] Wrote {kept} objects (skipped {skipped}) → {path}");
    }

    private static ObjectRecord BuildAndCleanRecord(GameObject go)
    {
        // ----- 1) Pull IFC + existing summary via your serializer (JSON string) -----
        var serialized = ObjectStructureSerializer.Serialize(go);
        var j = JObject.Parse(serialized);

        string uniqueId = j.Value<string>("unique_id") ?? "";

        if (string.IsNullOrWhiteSpace(uniqueId) || uniqueId.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            uniqueId = UniqueIdExtractor.GetOrStable(go);
        }


        string ifcTypeRaw = j.Value<string>("type") ?? "";
        string meshTextFromSerializer = j.Value<string>("meshSummaryText") ?? "";

        // Flatten full IFC properties (verbose ok)
        string ifcSummaryText = "";
        var ifcProps = j["ifc_properties"] as JObject;
        if (ifcProps != null)
        {
            var kvs = ifcProps.Properties()
                               .Select(p => $"{p.Name}={p.Value?.ToString()}")
                               .ToArray();
            ifcSummaryText = string.Join("; ", kvs);
        }

        // ----- 2) Summarize mesh with MeshSummarizer.Summarize(Mesh) -----
        Mesh mesh = null;
        var mf = go.GetComponent<MeshFilter>();
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (mf?.sharedMesh != null) mesh = mf.sharedMesh;
        else if (smr?.sharedMesh != null) mesh = smr.sharedMesh;

        string meshSummaryText = meshTextFromSerializer; // default if summarizer missing mesh
        int vertexCount = 0, triangleCount = 0, holeCount = 0;
        float width = 0, height = 0, depth = 0;

        if (mesh != null)
        {
            var summary = MeshSummarizer.Summarize(mesh);

            if (summary.TryGetValue("vertexCount", out var vtxObj))
                vertexCount = Convert.ToInt32(vtxObj);
            if (summary.TryGetValue("triangleCount", out var triObj))
                triangleCount = Convert.ToInt32(triObj);

            if (summary.TryGetValue("holes", out var holesObj) && holesObj is Dictionary<string, object> holesDict)
            {
                if (holesDict.TryGetValue("count", out var c))
                    holeCount = Convert.ToInt32(c);
            }

            if (summary.TryGetValue("meshSummaryText", out var textObj) && textObj is string s)
                meshSummaryText = s;

            if (summary.TryGetValue("boundingBox", out var bbObj) && bbObj is Dictionary<string, float[]> bb)
            {
                var min = bb.ContainsKey("min") ? bb["min"] : null;
                var max = bb.ContainsKey("max") ? bb["max"] : null;
                if (min != null && max != null && min.Length == 3 && max.Length == 3)
                {
                    width = Mathf.Abs(max[0] - min[0]);
                    height = Mathf.Abs(max[1] - min[1]);
                    depth = Mathf.Abs(max[2] - min[2]);
                }
            }
        }

        // ----- 3) Stable unique id fallback if serializer didn't find one -----
        if (string.IsNullOrEmpty(uniqueId))
            uniqueId = GetOrCreateStableId(go);

        // ----- 4) Derive cleaned fields BEFORE writing -----
        bool hasGeometry = (vertexCount > 0 && triangleCount > 0);
        string ifcTypeFinal = DeriveIfcTypeFinal(ifcTypeRaw, ifcProps, go.name);

        string ifcShort = BuildIfcSummaryShort(ifcProps);

        // ----- 5) Tags -----
        var tags = new List<string>();
        if (!string.IsNullOrEmpty(ifcTypeFinal)) tags.Add(ifcTypeFinal);
        if (holeCount > 0) tags.Add("has_cutout");

        // ----- 6) Assemble record -----
        var rec = new ObjectRecord
        {
            unique_id = uniqueId,
            name = go.name,
            ifc_type = ifcTypeRaw ?? "",
            ifc_type_final = ifcTypeFinal ?? "unknown",
            meshSummaryText = meshSummaryText ?? "",
            ifcSummaryText = ifcSummaryText ?? "",
            ifcSummaryTextShort = ifcShort ?? "",
            has_geometry = hasGeometry,
            numeric = new Numeric
            {
                bbox_width_m = width,
                bbox_height_m = height,
                bbox_depth_m = depth,
                triangle_count = triangleCount,
                vertex_count = vertexCount,
                hole_count = holeCount
            },
            tags = tags
        };

        return rec;
    }

    private static string DeriveIfcTypeFinal(string ifcTypeRaw, JObject ifcProps, string nameFallback)
    {
        // Priority order: explicit IFC type → Element Classification → Type/Family → Layer prefix → guess from name → "unknown"
        string t = TrimOrNull(ifcTypeRaw);
        if (!string.IsNullOrEmpty(t)) return t;

        string elemClass = GetPropString(ifcProps, "Element Classification");
        if (!string.IsNullOrEmpty(elemClass)) return elemClass;

        string typ = GetPropString(ifcProps, "Type");
        if (!string.IsNullOrEmpty(typ)) return typ;

        // Try common alternates
        string family = GetPropString(ifcProps, "Family");
        if (!string.IsNullOrEmpty(family)) return family;

        // Try Layer; often contains something like "3130_Walls_Exterior"
        string layer = GetPropString(ifcProps, "Layer");
        if (!string.IsNullOrEmpty(layer))
        {
            var prefix = layer.Split(new[] { '_', '-', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(prefix)) return prefix;
            return layer;
        }

        // Last resort: naive guess from object name
        if (!string.IsNullOrEmpty(nameFallback))
        {
            var n = nameFallback.ToLowerInvariant();
            if (n.Contains("wall")) return "Wall";
            if (n.Contains("door")) return "Door";
            if (n.Contains("window")) return "Window";
            if (n.Contains("slab") || n.Contains("floor")) return "Slab";
            if (n.Contains("roof")) return "Roof";
        }

        return "unknown";
    }

    private static string BuildIfcSummaryShort(JObject ifcProps)
    {
        if (ifcProps == null) return "";

        // Keep a concise subset that helps retrieval and filtering
        var keysPreferred = new[]
        {
            "Element Classification", "Type", "Family", "Layer",
            "IsExternal", "FireRating", "Home Story", "Material",
            "Width", "Height", "Thickness"
        };

        var parts = new List<string>();
        foreach (var k in keysPreferred)
        {
            var v = GetPropString(ifcProps, k);
            if (!string.IsNullOrEmpty(v)) parts.Add($"{k}={v}");
        }

        // If nothing found, fall back to at most 6 arbitrary props (avoid overlong embeddings)
        if (parts.Count == 0)
        {
            int cap = 6;
            foreach (var prop in ifcProps.Properties())
            {
                if (cap-- <= 0) break;
                var pv = prop.Value?.ToString();
                if (!string.IsNullOrEmpty(pv))
                    parts.Add($"{prop.Name}={pv}");
            }
        }

        return string.Join("; ", parts);
    }

    private static string GetPropString(JObject obj, string key)
    {
        if (obj == null) return null;
        var token = obj[key];
        if (token == null) return null;
        var s = token.ToString();
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static string TrimOrNull(string s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        s = s.Trim();
        return s.Length == 0 ? null : s;
    }

    private static string GetOrCreateStableId(GameObject go)
    {
        var guid = go.GetComponent<StableGuid>();
        if (guid != null && !string.IsNullOrEmpty(guid.Value))
            return guid.Value;

        if (guid == null)
            guid = go.AddComponent<StableGuid>();

        if (string.IsNullOrEmpty(guid.Value))
            guid.Value = System.Guid.NewGuid().ToString();

        return guid.Value;
    }
}

// Simple component to persist a stable GUID on objects that lack one
public class StableGuid : MonoBehaviour
{
    public string Value;
}
