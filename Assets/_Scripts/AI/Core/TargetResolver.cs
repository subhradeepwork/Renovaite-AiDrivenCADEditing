/*
 * Script Summary:
 * ----------------
 * Resolves "selection" blocks from AIInstruction into actual Unity GameObjects.  
 * Handles explicit IDs, filters, or similarity-based expansion via Qdrant.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: TargetResolver (static).
 * - DTO:
 *     • Selection { apply_to, ids, top_k, similarity_threshold, filters, exclude_selected }.
 *     • Filters { ifc_type, tags }.
 * - Key Methods:
 *     • TryParseSelection(json) – Extracts selection block from normalized JSON.
 *     • ResolveAsync(selection, selected, qdrant) – Resolves into list of GameObjects.
 * - Resolution Modes:
 *     • selected (default/fallback).
 *     • ids – Exact matches from ObjectRegistry/ifcProperties.
 *     • filters – Queries Qdrant or local scan by IFC type/tags.
 *     • similar – Qdrant kNN search from selected; optionally union with selected.
 * - Special Considerations:
 *     • Deduplicates and ensures stable instance set.
 *     • Gracefully falls back to local scan on Qdrant errors.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var sel = TargetResolver.TryParseSelection(normalizedJson);
 * var targets = await TargetResolver.ResolveAsync(sel, selectedGO, qdrantClient);
 * ```
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

public static class TargetResolver
{
    // Minimal POCO mirroring optional selection block if present in tool args
    [System.Serializable]
    public class Selection
    {
        public string apply_to;                 // "selected" | "similar" | "ids" | "filters"
        public List<string> ids;
        public int top_k = 5;
        public float similarity_threshold = 0.0f;

        // legacy top-level (kept for back-compat)
        public string ifc_type;
        public List<string> tags;

        // NEW: structured filters
        [System.Serializable]
        public class Filters
        {
            public string ifc_type;      // e.g., "Door"
            public List<string> tags;    // optional tag list
        }
        public Filters filters;          // NEW

        // When true and apply_to == "similar", the selected object will NOT be included.
        public bool exclude_selected = false;
    }

    // Extract optional 'selection' JSON from normalized string (if present)
    public static Selection TryParseSelection(string normalizedJson)
    {
        var n = SimpleJSON.JSON.Parse(normalizedJson);
        var sel = n?["selection"];
        if (sel == null || !sel.IsObject) return null;
        return JsonUtility.FromJson<Selection>(sel.ToString());
    }

    public static async Task<List<GameObject>> ResolveAsync(
        Selection sel, GameObject selected, Qdrant.QdrantApiClient qdrant)
    {
        // No selection block or explicit "selected" ⇒ just return the currently selected object (if any)
        if (sel == null || string.IsNullOrEmpty(sel.apply_to) || sel.apply_to == "selected")
        {
            return selected != null ? new List<GameObject> { selected } : new List<GameObject>();
        }

        // Explicit list of IDs
        if (sel.apply_to == "ids" && sel.ids != null && sel.ids.Count > 0)
        {
            var byIds = FindByIds(sel.ids);
            return DedupAndMaybeIncludeSelected(byIds, selected, includeSelected: false); // do NOT auto-include here
        }

        // NEW: Filter path (category/tag queries) – works with NO selection
        if (sel.apply_to == "filters" && qdrant != null)
        {
            var f = sel.filters ?? new Selection.Filters { ifc_type = sel.ifc_type, tags = sel.tags };

            try
            {
                var req = new Qdrant.FilterRequest
                {
                    ifc_type_final = f?.ifc_type,
                    tags_any = f?.tags,
                    require_geometry = true,
                    limit = 5000
                };
                var res = await qdrant.FilterAsync(req);
                var ids = (res?.results ?? new List<Qdrant.SearchHit>())
                          .Select(h => h.unique_id).ToList();
                var matches = FindByIds(ids);
                return DedupAndMaybeIncludeSelected(matches, selected, includeSelected: false);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[TargetResolver] Qdrant filter failed, using local scan. {e.Message}");
                var local = LocalFilterScan(f);
                return DedupAndMaybeIncludeSelected(local, selected, includeSelected: false);
            }
        }

        // Similar to the selected – union with the selected by default
        if (sel.apply_to == "similar" && qdrant != null)
        {
            // If no selected object but filters were provided alongside "similar", gracefully fall back to filters
            if (selected == null && (sel.filters != null || !string.IsNullOrEmpty(sel.ifc_type) || (sel.tags != null && sel.tags.Count > 0)))
            {
                var f = sel.filters ?? new Selection.Filters { ifc_type = sel.ifc_type, tags = sel.tags };
                var reqF = new Qdrant.FilterRequest { ifc_type_final = f?.ifc_type, tags_any = f?.tags, require_geometry = true };
                var resF = await qdrant.FilterAsync(reqF);
                var idsF = (resF?.results ?? new List<Qdrant.SearchHit>()).Select(h => h.unique_id).ToList();
                var matchesF = FindByIds(idsF);
                return DedupAndMaybeIncludeSelected(matchesF, selected, includeSelected: false);
            }

            if (selected != null)
            {
                //var uid = TryGetUniqueId(selected);
                var uid = UniqueIdExtractor.GetOrStable(selected);

                if (string.IsNullOrWhiteSpace(uid) || uid.Equals("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[TargetResolver] No unique_id available for 'similar'; returning selected only.");
                    return selected != null ? new List<GameObject> { selected } : new List<GameObject>();
                }

                var req = new Qdrant.SimilarRequest
                {
                    unique_id = uid,
                    top_k = sel.top_k > 0 ? sel.top_k : 5
                };

                var res = await qdrant.SimilarAsync(req);
                var ids = (res?.results ?? new List<Qdrant.SearchHit>())
                          .Where(h => h.score >= sel.similarity_threshold)
                          .Select(h => h.unique_id)
                          .ToList();

                var similars = FindByIds(ids);

                // By default, include the selected as well to honor "this item and all similar items" semantics.
                bool includeSelected = !sel.exclude_selected;
                return DedupAndMaybeIncludeSelected(similars, selected, includeSelected);
            }
        }

        // Fallback: if we reach here, return the selected if available
        return selected != null ? new List<GameObject> { selected } : new List<GameObject>();
    }

    // Helper: combine, de-null, and de-dup (by instance) and optionally include the selected object
    private static List<GameObject> DedupAndMaybeIncludeSelected(List<GameObject> items, GameObject selected, bool includeSelected)
    {
        var list = (items ?? new List<GameObject>()).Where(go => go != null).ToList();

        if (includeSelected && selected != null && !ReferenceEquals(selected, null))
        {
            // Only add if not already present (instance equality)
            if (!list.Contains(selected))
                list.Insert(0, selected);
        }

        // Distinct by instance ID for safety
        var seen = new HashSet<int>();
        var dedup = new List<GameObject>(list.Count);
        foreach (var go in list)
        {
            if (go == null) continue;
            int id = go.GetInstanceID();
            if (seen.Add(id)) dedup.Add(go);
        }
        return dedup;
    }

    private static List<GameObject> FindByIds(List<string> ids)
    {
        var set = new HashSet<string>(ids ?? new());
        return GameObject.FindObjectsOfType<ifcProperties>()
            .Where(p => p.properties != null && p.nominalValues != null)
            .Where(p => Enumerable.Range(0, Mathf.Min(p.properties.Count, p.nominalValues.Count))
                .Any(i => !string.IsNullOrEmpty(p.properties[i]) &&
                          p.properties[i].ToLower().Contains("unique id") &&
                          set.Contains(p.nominalValues[i])))
            .Select(p => p.gameObject).ToList();
    }

    private static string TryGetUniqueId(GameObject go)
    {
        if (go == null) return null;
        var ifc = go.GetComponent<ifcProperties>();
        if (ifc == null || ifc.properties == null || ifc.nominalValues == null) return null;
        int count = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
        for (int i = 0; i < count; i++)
        {
            var key = ifc.properties[i];
            var val = ifc.nominalValues[i];
            if (!string.IsNullOrEmpty(key) && key.ToLower().Contains("unique id")) return val;
        }
        return null;
    }


    // Very lightweight local scan by IFC type / tags
    private static List<GameObject> LocalFilterScan(Selection.Filters f)
    {
        var results = new List<GameObject>();
        var all = GameObject.FindObjectsOfType<ifcProperties>();
        string wantedType = f?.ifc_type;

        foreach (var p in all)
        {
            bool typeOk = true;
            if (!string.IsNullOrEmpty(wantedType))
            {
                typeOk = MatchesIfcType(p, wantedType);
            }

            bool tagsOk = true; // extend if you store tags on a component
                                // e.g., var tagComp = p.GetComponent<TagComponent>(); compare f.tags against tagComp.Tags

            if (typeOk && tagsOk) results.Add(p.gameObject);
        }
        return results;
    }

    private static bool MatchesIfcType(ifcProperties props, string wantedType)
    {
        if (props == null || props.properties == null || props.nominalValues == null) return false;
        int n = Mathf.Min(props.properties.Count, props.nominalValues.Count);
        for (int i = 0; i < n; i++)
        {
            var k = props.properties[i];
            var v = props.nominalValues[i];
            if (string.IsNullOrEmpty(k) || string.IsNullOrEmpty(v)) continue;

            // Try common IFC keys you already log (e.g., "Element Classification")
            if (k.IndexOf("element classification", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                v.IndexOf(wantedType, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (k.IndexOf("type", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                v.IndexOf(wantedType, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (k.IndexOf("ifc_type_final", System.StringComparison.OrdinalIgnoreCase) >= 0 &&
                v.IndexOf(wantedType, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

}
