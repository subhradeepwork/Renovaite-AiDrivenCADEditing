/*
 * Script Summary:
 * ----------------
 * Builds compact JSON context from Qdrant search/similar queries to feed into LLM prompts.
 * Encapsulates whether to fetch "similar objects" or "semantic search hits" based on user input.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: RagContextBuilder (static).
 * - Key Methods:
 *     • BuildAsync(GameObject selected, string userPrompt, QdrantApiClient qdrant, bool useSimilarOverSearch, int topK)
 *       - If useSimilarOverSearch and selected has UID → call qdrant.SimilarAsync.
 *       - Else → call qdrant.SearchAsync with userPrompt.
 *       - Returns a compact JSON array of hits (unique_id, name, ifc_type, bbox).
 * - Helper Methods:
 *     • FormatHitsJson(List<SearchHit>) – Converts results into SimpleJSON array with limited fields.
 *     • Trunc(string, int) – Truncates long names with ellipsis.
 * - Dependencies/Interactions:
 *     • QdrantApiClient – to fetch similar/search results.
 *     • UniqueIdExtractor – to get UID from selected object.
 *     • Consumed by PromptBuilder/RagCompactor for prompt injection.
 * - Special Considerations:
 *     • Fallbacks gracefully to "[]" on errors or null client.
 *     • Truncates object names to 60 characters for prompt readability.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string ragJson = await RagContextBuilder.BuildAsync(
 *     selected: myGameObject,
 *     userPrompt: "all similar walls",
 *     qdrant: qdrantClient,
 *     useSimilarOverSearch: true,
 *     topK: 5
 * );
 * Debug.Log(ragJson); // → [{"unique_id":"WALL_001", "name":"Wall1", "ifc_type":"Wall", "bbox":{...}}, ...]
 * ```
 */

using System.Collections.Generic;
using System.Threading.Tasks;
using Qdrant;
using SimpleJSON;
using UnityEngine;

public static class RagContextBuilder
{
    public static async Task<string> BuildAsync(
        GameObject selected,
        string userPrompt,
        QdrantApiClient qdrant,
        bool useSimilarOverSearch,
        int topK)
    {
        try
        {
            if (qdrant == null) return "[]";
            var uid = UniqueIdExtractor.TryGetUniqueId(selected);

            if (useSimilarOverSearch && !string.IsNullOrEmpty(uid))
            {
                var simReq = new SimilarRequest { unique_id = uid, top_k = topK, require_geometry = true };
                var simRes = await qdrant.SimilarAsync(simReq);
                return FormatHitsJson(simRes?.results);
            }
            else
            {
                var sReq = new SearchRequest { query = userPrompt, top_k = topK, require_geometry = true };
                var sRes = await qdrant.SearchAsync(sReq);
                return FormatHitsJson(sRes?.results);
            }
        }
        catch { return "[]"; }
    }

    private static string FormatHitsJson(List<SearchHit> hits)
    {
        var arr = new JSONArray();
        if (hits != null)
        {
            foreach (var h in hits)
            {
                var o = new JSONObject();
                o["unique_id"] = h.unique_id ?? "";
                o["name"] = Trunc(h.name ?? "", 60);
                o["ifc_type"] = h.ifc_type_final ?? "";
                var bbox = new JSONObject();
                bbox["w"] = h.bbox?.w ?? 0f; bbox["h"] = h.bbox?.h ?? 0f; bbox["d"] = h.bbox?.d ?? 0f;
                o["bbox"] = bbox;
                arr.Add(o);
            }
        }
        return arr.ToString();
    }

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s.Substring(0, max) + "…");
}
