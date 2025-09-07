/*
 * Script Summary:
 * ----------------
 * Client wrapper for communicating with Qdrant (via FastAPI proxy) from Unity.
 * Provides typed request/response models and async endpoints for upsert/search/similar/filter operations.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: QdrantApiClient – Handles HTTP POSTs to Qdrant proxy.
 * - Wire Models:
 *     • UpsertRequest / UpsertResponse – Insert or update object vectors + metadata.
 *     • SimilarRequest – kNN query by reference object (unique_id).
 *     • SearchRequest – Semantic text query.
 *     • FilterRequest – Attribute/numeric filtering without text.
 *     • SearchHit / SearchResponse – Standardized search results (with score, tags, bbox, numeric, etc.).
 * - Key Methods:
 *     • UpsertAsync(UpsertRequest) – Push object metadata/vector.
 *     • SimilarAsync(SimilarRequest) – Get top-K similar objects.
 *     • SearchAsync(SearchRequest) – Full-text vector search.
 *     • FilterAsync(FilterRequest) – Structured filters only (fallback to /search if /search/filter unsupported).
 * - Dependencies/Interactions:
 *     • UnityWebRequest for async HTTP transport.
 *     • Uses Newtonsoft.Json for serialization/deserialization.
 *     • Consumed by QdrantSelectionListener and higher-level RAG components.
 * - Special Considerations:
 *     • Supports baseUrl override (local vs remote).
 *     • Retries configurable via QdrantSettings.retryCount.
 *     • Logs request body + URL for debugging.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var client = new QdrantApiClient(settings);
 * var req = new Qdrant.UpsertRequest { unique_id = "WALL_001", name = "Wall", scene_id = "SampleScene" };
 * var res = await client.UpsertAsync(req);
 * Debug.Log("Upsert success: " + res.ok);
 * ```
 */

using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Qdrant
{
    // ------------------------------
    // Wire models (kept compatible)
    // ------------------------------

    [Serializable]
    public class UpsertRequest
    {
        public string unique_id;
        public string name;
        public string ifc_type_final;
        public string meshSummaryText;
        public string ifcSummaryTextShort;
        public string scene_id;
        public List<string> tags;
        // IMPORTANT: float (not double) to match QdrantUpsertBuilder usage
        public Dictionary<string, float> numeric;
        public bool has_geometry = true;
    }

    [Serializable]
    public class UpsertResponse
    {
        public bool ok;
        public string point_id;

        public string status; // server may return "ok" here

        // Tolerate string or number from the server (proxy or direct)
        [Newtonsoft.Json.JsonProperty("updated")]
        public Newtonsoft.Json.Linq.JToken updated;

        public string error;

        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> extra;

        // Helpers (optional)
        [Newtonsoft.Json.JsonIgnore]
        public string UpdatedAsString => updated?.ToString();

        [Newtonsoft.Json.JsonIgnore]
        public int UpdatedAsInt
        {
            get
            {
                if (updated == null) return 0;
                if (updated.Type == Newtonsoft.Json.Linq.JTokenType.Integer) return (int)updated;
                return int.TryParse(updated.ToString(), out var i) ? i : 0;
            }
        }
    }


    [Serializable]
    public class SimilarRequest
    {
        public string unique_id;
        public int top_k = 5;

        // optional filters
        public string ifc_type_final;
        public List<string> ifc_types_any;
        public List<string> tags_all;
        public List<string> tags_any;
        public bool require_geometry = true;

        // optional numeric filters (kept loose)
        public int? min_holes;
        public float? min_width_m, max_width_m;
        public float? min_height_m, max_height_m;
        public float? min_depth_m, max_depth_m;
        public int? min_triangles, max_triangles;
        public int? min_vertices, max_vertices;
    }

    [Serializable]
    public class SearchRequest
    {
        public string query;
        public int top_k = 5;

        public string ifc_type_final;
        public List<string> ifc_types_any;
        public List<string> tags_all;
        public List<string> tags_any;
        public bool require_geometry = true;
    }

    // NEW: Filter request (no text query, no seed id)
    [Serializable]
    public class FilterRequest
    {
        public string ifc_type_final;         // e.g., "Door"
        public List<string> ifc_types_any;    // optional
        public List<string> tags_all;         // optional
        public List<string> tags_any;         // optional
        public bool require_geometry = true;
        public int limit = 1000;

        // optional numeric filters (mirror what's in SimilarRequest)
        public int? min_holes;
        public float? min_width_m, max_width_m;
        public float? min_height_m, max_height_m;
        public float? min_depth_m, max_depth_m;
        public int? min_triangles, max_triangles;
        public int? min_vertices, max_vertices;
    }

    // What the rest of the project expects
    [Serializable]
    public class BBox
    {
        public float w;
        public float h;
        public float d;
    }

    // KEEP this type name; other scripts reference it
    [Serializable]
    public class SearchHit
    {
        public string unique_id;
        public string name;
        public string ifc_type_final;
        public string scene_id;
        public float score;

        // Many call sites expect this
        public BBox bbox;

        // Optional extras (safe)
        public List<string> tags;
        public Dictionary<string, float> numeric;

        [JsonExtensionData] public IDictionary<string, JToken> extra;
    }

    [Serializable]
    public class SearchResponse
    {
        // Canonical list
        public List<SearchHit> results = new List<SearchHit>();

        // Back-compat: if old code uses "hits", keep it mapped
        [JsonProperty("hits")]
        public List<SearchHit> hits
        {
            get => results;
            set { if (value != null) results = value; }
        }

        [JsonExtensionData] public IDictionary<string, JToken> extra;
    }

    // ------------------------------
    // API Client
    // ------------------------------
    public class QdrantApiClient
    {
        private QdrantSettings _settings;
        private string _overrideBaseUrl; // wins if set
        private readonly JsonSerializerSettings _json;

        public QdrantApiClient(QdrantSettings settings = null)
        {
            AttachSettings(settings);
            _json = new JsonSerializerSettings
            {
                MissingMemberHandling = MissingMemberHandling.Ignore,
                NullValueHandling = NullValueHandling.Ignore,
                FloatParseHandling = FloatParseHandling.Double
            };
            Debug.Log($"[Qdrant][Init] base = {GetBaseUrl()}");
        }

        public void AttachSettings(QdrantSettings settings)
        {
            _settings = settings;
        }

        /// <summary>Programmatic override (e.g., http://127.0.0.1:8000)</summary>
        public void OverrideBaseUrl(string url)
        {
            if (!string.IsNullOrWhiteSpace(url))
            {
                _overrideBaseUrl = url.TrimEnd('/');
                Debug.Log($"[Qdrant] Base URL overridden -> {_overrideBaseUrl}");
            }
        }

        private string GetBaseUrl()
        {
            if (!string.IsNullOrEmpty(_overrideBaseUrl))
                return _overrideBaseUrl;

            if (_settings != null && !string.IsNullOrWhiteSpace(_settings.baseUrl))
                return _settings.baseUrl.TrimEnd('/');

            // Final fallback: your FastAPI proxy (has /upsert, /similar, /search)
            return "http://127.0.0.1:8000";
        }

        private static string Combine(string a, string b)
        {
            if (string.IsNullOrEmpty(a)) return b ?? "";
            if (string.IsNullOrEmpty(b)) return a ?? "";
            return a.TrimEnd('/') + "/" + b.TrimStart('/');
        }

        private async Task<UnityWebRequest> SendAsync(UnityWebRequest req)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            req.SendWebRequest().completed += _ => tcs.TrySetResult(req);
            return await tcs.Task;
        }

        private async Task<string> PostRawAsync(string path, object body, string method = "POST")
        {
            var url = Combine(GetBaseUrl(), path);
            var json = JsonConvert.SerializeObject(body);
            var bytes = Encoding.UTF8.GetBytes(json);

            var req = new UnityWebRequest(url, method);
            req.uploadHandler = new UploadHandlerRaw(bytes);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            if (_settings != null && _settings.timeoutSeconds > 0)
                req.timeout = _settings.timeoutSeconds;

            Debug.Log($"[Qdrant][HTTP] {method} {url}");
            Debug.Log($"[Qdrant][Body] {json}");

            var res = await SendAsync(req);

            bool httpOk = res.result == UnityWebRequest.Result.Success &&
                          (res.responseCode >= 200 && res.responseCode < 300);

            if (!httpOk)
            {
                var bodyText = res.downloadHandler != null ? res.downloadHandler.text : "";
                throw new Exception($"Qdrant HTTP error: {res.responseCode} {res.error}. Body: {bodyText}");
            }

            var text = res.downloadHandler.text;
            Debug.Log($"[Qdrant][Resp] {res.responseCode} {text}");
            return text;
        }



        public async Task<T> PostJsonWithRetry<T>(string path, object body)
        {
            int tries = 1 + Math.Max(0, _settings != null ? _settings.retryCount : 0);
            Exception last = null;

            for (int i = 0; i < tries; i++)
            {
                try
                {
                    var text = await PostRawAsync(path, body, "POST");

                    // NEW: normalize Upsert responses that use { "status": "ok" } instead of { "ok": true }
                    if (string.Equals(path, "/upsert", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            var j = JObject.Parse(text);
                            if (j["ok"] == null && j["status"] != null)
                            {
                                j["ok"] = string.Equals((string)j["status"], "ok", StringComparison.OrdinalIgnoreCase);
                                text = j.ToString(Newtonsoft.Json.Formatting.None);
                            }
                        }
                        catch { /* leave text as-is if parsing fails */ }
                    }

                    return JsonConvert.DeserializeObject<T>(text, _json);
                }
                catch (Exception ex)
                {
                    last = ex;
                    if (i + 1 < tries) await Task.Delay(150);
                }
            }
            throw last ?? new Exception("Qdrant PostJsonWithRetry failed.");
        }






        // ------------------ Endpoints ------------------

        public Task<UpsertResponse> UpsertAsync(UpsertRequest req)
                => PostJsonWithRetry<UpsertResponse>("/upsert", req);

        public Task<SearchResponse> SimilarAsync(SimilarRequest req)
            => PostJsonWithRetry<SearchResponse>("/similar", req);

        public Task<SearchResponse> SearchAsync(SearchRequest req)
            => PostJsonWithRetry<SearchResponse>("/search", req);

        public async Task<SearchResponse> FilterAsync(FilterRequest req)
        {
            try
            {
                // Try dedicated filter route first (if your proxy implements it)
                return await PostJsonWithRetry<SearchResponse>("/search/filter", req);
            }
            catch (Exception ex)
            {
                // If the route doesn't exist, gracefully fallback to /search with an empty query
                var is404 = ex.Message != null && ex.Message.Contains(" 404 ");
                if (!is404) throw;

                var searchReq = new SearchRequest
                {
                    query = "", // empty text query, use filters only
                    ifc_type_final = req.ifc_type_final,
                    ifc_types_any = req.ifc_types_any,
                    tags_all = req.tags_all,
                    tags_any = req.tags_any,
                    require_geometry = req.require_geometry,
                    top_k = Math.Max(1, req.limit)
                };
                return await PostJsonWithRetry<SearchResponse>("/search", searchReq);
            }
        }

    }
}
