/*
 * Script Summary:
 * ----------------
 * Simplifies raw RAG search results into a minimal JSON form for prompt injection.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: RagCompactor (static).
 * - Key Methods:
 *     • CompactRagJson(rawJson, topK, maxChars) – Produces a JSON array of the topK hits with only
 *       the most essential fields (unique_id, type, tags, bbox).
 * - Helper Methods:
 *     • SafeTruncate(str, max) – Ensures final output does not exceed character limit.
 * - Dependencies/Interactions:
 *     • Called before injecting semantic search results into PromptBuilder.
 *     • Uses SimpleJSON for parsing input arrays or "hits" objects.
 * - Special Considerations:
 *     • Discards extra metadata to avoid prompt bloat.
 *     • Ensures safe fallback (truncates raw JSON) on parse errors or invalid structure.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string compact = RagCompactor.CompactRagJson(searchResultJson, 5, 1500);
 * // compact now contains only the top 5 hits with unique_id/type/tags/bbox.
 * ```
 */

using System.Collections.Generic;
using SimpleJSON;

namespace RenovAite.AI.Prompting
{
    public static class RagCompactor
    {
        public static string CompactRagJson(string rawJson, int topK, int maxChars)
        {
            if (string.IsNullOrEmpty(rawJson)) return rawJson;

            try
            {
                var node = JSON.Parse(rawJson);
                if (node == null) return SafeTruncate(rawJson, maxChars);

                JSONArray arr = node.IsArray ? node.AsArray
                                  : (node.IsObject && node.AsObject.HasKey("hits") && node["hits"].IsArray)
                                        ? node["hits"].AsArray
                                        : null;

                if (arr == null) return SafeTruncate(rawJson, maxChars);

                var outArr = new JSONArray();
                int n = 0;
                foreach (var item in arr)
                {
                    if (n++ >= topK) break;

                    var obj = new JSONObject();
                    var it = item.Value;

                    obj["unique_id"] = it["unique_id"];
                    obj["type"] = it["type"];
                    obj["tags"] = it["tags"]; // keep small arrays only
                    obj["bbox"] = it["bbox"]; // include if you store a tiny bbox

                    outArr.Add(obj);
                }

                var compact = outArr.ToString(0);
                return SafeTruncate(compact, maxChars);
            }
            catch
            {
                return SafeTruncate(rawJson, maxChars);
            }
        }


        private static string SafeTruncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

    }
}
