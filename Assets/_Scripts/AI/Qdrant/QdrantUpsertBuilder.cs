/*
 * Script Summary:
 * ----------------
 * Builds a Qdrant UpsertRequest from a Unity GameObject by extracting IFC metadata and mesh stats.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: QdrantUpsertBuilder (static).
 * - Key Methods:
 *     • TryBuild(GameObject go, string sceneId, out UpsertRequest req)
 *       - Extracts IFC properties (Unique ID, Element Classification, etc.).
 *       - Derives ifc_type_final and a short summary.
 *       - Summarizes mesh (bounds, vertex/triangle count).
 *       - Generates tags (e.g., external/internal, material, ifc_type_final).
 *       - Populates UpsertRequest DTO.
 * - Helper Methods:
 *     • TryReadIfc(go, out uid, out dict) – Reads ifcProperties component.
 *     • DeriveIfcTypeFinal(rawType, dict, nameFallback) – Fallback logic for IFC type classification.
 *     • BuildIfcSummaryShort(dict) – Short summary from key IFC props.
 *     • DeriveTags(ifcTypeFinal, dict) – Tags: type, external/internal, material.
 *     • TrySummarizeMesh(go, out meshSummary, out numeric, out hasGeometry) – Collects mesh stats + bounds.
 * - Dependencies/Interactions:
 *     • Requires ifcProperties component on GameObjects.
 *     • Outputs UpsertRequest consumed by QdrantApiClient.UpsertAsync.
 * - Special Considerations:
 *     • Unique ID required; fails if missing.
 *     • Mesh must exist (MeshFilter or SkinnedMeshRenderer).
 *     • Tags auto-derived for searchability in RAG pipeline.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * if (QdrantUpsertBuilder.TryBuild(go, "SampleScene", out var req)) {
 *     var res = await client.UpsertAsync(req);
 * }
 * ```
 */
/*
 * Builds a Qdrant UpsertRequest from a Unity GameObject by extracting IFC metadata and mesh stats.
 * Hardenings:
 *  - Accepts Unique ID / GlobalId / Global ID / GUID (case-insensitive, substring ok).
 *  - ifcProperties can be on self OR parents.
 *  - If no explicit ID is present, uses UniqueIdExtractor.GetOrStable(go) (never null/unknown).
 *  - Mesh: sharedMesh preferred; falls back to MeshCollider.sharedMesh.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Qdrant
{
    public static class QdrantUpsertBuilder
    {
        private static readonly string[] IdKeys = new[]
        {
            "unique id","uniqueid","unique_id","globalid","global id","guid"
        };

        // Build the /upsert body from the selected object (ifcProperties + mesh)
        public static bool TryBuild(GameObject go, string sceneId, out UpsertRequest req)
        {
            req = null;
            if (go == null) return false;

            // ---- A) IFC metadata (self or parents) ----
            string explicitId; Dictionary<string, string> ifcDict;
            TryReadIfcSelfOrParents(go, out explicitId, out ifcDict); // never hard-fail here

            // Derived IFC fields
            var ifcTypeRaw = ifcDict.TryGetValue("Type", out var t) ? t : null;
            var ifcTypeFinal = DeriveIfcTypeFinal(ifcTypeRaw, ifcDict, go.name);
            var ifcShort = BuildIfcSummaryShort(ifcDict);
            var tags = DeriveTags(ifcTypeFinal, ifcDict);

            // ---- B) Mesh summary + numeric ----
            if (!TrySummarizeMesh(go, out string meshText, out Dictionary<string, float> numeric, out bool hasGeo))
                return false; // still require a usable mesh to upsert

            // ---- C) Robust unique_id (never null/unknown) ----
            string uniqueId = !string.IsNullOrWhiteSpace(explicitId)
                ? explicitId.Trim()
                : UniqueIdExtractor.GetOrStable(go);

            if (string.IsNullOrWhiteSpace(uniqueId))
            {
                // Ultra-safe guard: if something still went wrong, refuse instead of posting a bad body
                Debug.LogWarning("[QdrantUpsertBuilder] No unique_id could be derived; aborting upsert.");
                return false;
            }

            // ---- D) Assemble request ----
            req = new UpsertRequest
            {
                unique_id = uniqueId,
                name = go.name,
                ifc_type_final = ifcTypeFinal,
                meshSummaryText = meshText,
                ifcSummaryTextShort = ifcShort,
                tags = tags,
                numeric = numeric,
                has_geometry = hasGeo,
                scene_id = sceneId
            };
            return true;
        }

        // --- IFC helpers ---

        /// <summary>
        /// Reads IFC properties from the given GameObject or any of its parents.
        /// Fills a flat dictionary of properties and returns an explicit id if found
        /// under any of: Unique ID / GlobalId / Global ID / GUID (case-insensitive).
        /// Never hard-fails; returns empty dict if nothing found.
        /// </summary>
        private static void TryReadIfcSelfOrParents(GameObject leaf, out string uniqueId, out Dictionary<string, string> dict)
        {
            uniqueId = null;
            dict = new Dictionary<string, string>();

            GameObject n = leaf;
            ifcProperties ifc = null;
            while (n != null && ifc == null)
            {
                ifc = n.GetComponent<ifcProperties>();
                n = n.transform?.parent ? n.transform.parent.gameObject : null;
            }

            if (ifc == null || ifc.properties == null || ifc.nominalValues == null)
                return; // leave dict empty; we'll still build using fallback ID

            int count = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
            for (int i = 0; i < count; i++)
            {
                var key = ifc.properties[i] ?? "";
                var val = ifc.nominalValues[i] ?? "";
                dict[key] = val;

                var lk = key.ToLowerInvariant();
                if (IdKeys.Any(k => lk.Contains(k)) && !string.IsNullOrWhiteSpace(val))
                    uniqueId = val.Trim();
            }
        }

        private static string DeriveIfcTypeFinal(string ifcTypeRaw, Dictionary<string, string> ifcProps, string nameFallback)
        {
            string TrimOrNull(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            string t = TrimOrNull(ifcTypeRaw);
            if (!string.IsNullOrEmpty(t)) return t;

            string get(string k)
            {
                foreach (var kv in ifcProps)
                    if (kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return null;
            }

            var elemClass = get("Element Classification");
            if (!string.IsNullOrEmpty(elemClass)) return elemClass;

            var family = get("Family");
            if (!string.IsNullOrEmpty(family)) return family;

            var layer = get("Layer");
            if (!string.IsNullOrEmpty(layer))
            {
                var prefix = layer.Split(new[] { '_', '-', '/' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrEmpty(prefix)) return prefix;
                return layer;
            }

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

        private static string BuildIfcSummaryShort(Dictionary<string, string> ifcProps)
        {
            if (ifcProps == null || ifcProps.Count == 0) return "";
            var keysPreferred = new[]
            {
                "Element Classification","Type","Family","Layer",
                "IsExternal","FireRating","Home Story","Material",
                "Width","Height","Thickness"
            };

            var parts = new List<string>();
            foreach (var k in keysPreferred)
            {
                var v = ifcProps.FirstOrDefault(p => p.Key.Equals(k, StringComparison.OrdinalIgnoreCase)).Value;
                if (!string.IsNullOrWhiteSpace(v)) parts.Add($"{k}={v}");
            }

            if (parts.Count == 0)
            {
                // fall back to at most 6 arbitrary props
                foreach (var kv in ifcProps.Take(6))
                {
                    if (!string.IsNullOrWhiteSpace(kv.Value))
                        parts.Add($"{kv.Key}={kv.Value}");
                }
            }
            return string.Join("; ", parts);
        }

        private static List<string> DeriveTags(string ifcTypeFinal, Dictionary<string, string> ifcProps)
        {
            var tags = new List<string>();
            if (!string.IsNullOrWhiteSpace(ifcTypeFinal)) tags.Add(ifcTypeFinal);

            string get(string k)
            {
                foreach (var kv in ifcProps)
                    if (kv.Key.Equals(k, StringComparison.OrdinalIgnoreCase)) return kv.Value;
                return null;
            }

            var isExt = (get("IsExternal") ?? "").Trim().ToLowerInvariant();
            if (isExt == "true" || isExt == "yes" || isExt == "1") tags.Add("external");
            if (isExt == "false" || isExt == "no" || isExt == "0") tags.Add("internal");

            var material = get("Material");
            if (!string.IsNullOrWhiteSpace(material)) tags.Add(material.ToLowerInvariant());

            return tags;
        }

        // --- Mesh helpers ---
        private static bool TrySummarizeMesh(GameObject go,
            out string meshSummaryText,
            out Dictionary<string, float> numeric,
            out bool hasGeometry)
        {
            meshSummaryText = ""; numeric = new Dictionary<string, float>(); hasGeometry = false;

            Mesh mesh = null;
            var mf = go.GetComponent<MeshFilter>();
            var smr = go.GetComponent<SkinnedMeshRenderer>();
            if (mf?.sharedMesh != null) mesh = mf.sharedMesh;
            else if (smr?.sharedMesh != null) mesh = smr.sharedMesh;

            // Fallback to collider mesh (some imports only add colliders)
            if (mesh == null)
            {
                var col = go.GetComponent<MeshCollider>();
                if (col?.sharedMesh != null) mesh = col.sharedMesh;
            }

            if (mesh == null) return false;

            hasGeometry = true;

            int verts = mesh.vertexCount;
            int tris = 0;
            if (mesh.isReadable)           // <-- guard
                tris = (mesh.triangles != null) ? mesh.triangles.Length / 3 : 0;

            var b = mesh.bounds;
            float w = Mathf.Abs(b.size.x);
            float h = Mathf.Abs(b.size.y);
            float d = Mathf.Abs(b.size.z);

            numeric["bbox_width_m"] = w;
            numeric["bbox_height_m"] = h;
            numeric["bbox_depth_m"] = d;
            numeric["triangle_count"] = tris;     // will be 0 if not readable
            numeric["vertex_count"] = verts;
            numeric["hole_count"] = numeric.ContainsKey("hole_count") ? numeric["hole_count"] : 0;


            meshSummaryText = $"This mesh has {verts} vertices and {tris} triangles. " +
                              $"Bounding box spans {w:F2}m (W) × {h:F2}m (H) × {d:F2}m (D).";
            return true;
        }
    }
}
