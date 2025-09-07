using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Qdrant; // Uses QdrantSettings, QdrantApiClient, QdrantUpsertBuilder

/// <summary>
/// Runtime index "pre-warm" for Qdrant.
/// Scans the active scene for mesh-bearing GameObjects, builds UpsertRequests using
/// QdrantUpsertBuilder.TryBuild(...), and posts them to Qdrant via QdrantApiClient.UpsertAsync(...).
/// 
/// Drop this on a empty GameObject and press Play.
/// Now includes: safe cancellation on Stop/Exit Play, throttling, optional IFC-type filtering,
/// and a UI progress bar + status label.
/// </summary>
[DefaultExecutionOrder(-5000)]
public sealed class QdrantWarmIndex : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("If left empty, will try Resources/Config/QdrantSettings.asset")]
    public QdrantSettings settings;

    [Tooltip("Run automatically in Start(). You can also call TriggerWarmIndex() manually.")]
    public bool runOnStart = true;

    [Tooltip("Include inactive objects when scanning the scene.")]
    public bool includeInactive = false;

    [Tooltip("Only upsert objects whose IFC type matches this (e.g., 'Window'). Leave empty to index all valid objects.")]
    public string includeOnlyIfcType = string.Empty;

    [Tooltip("Limit how many objects to upsert in one run (0 = no limit).")]
    [Min(0)] public int maxObjects = 0;

    [Header("Performance")]
    [Tooltip("Yield to the player loop after this many upserts to keep frames responsive.")]
    [Range(1, 200)] public int yieldEvery = 25;

    [Tooltip("Optional small delay (ms) between upserts to avoid overloading your API.")]
    [Range(0, 250)] public int delayMsBetweenPosts = 0;

    [Tooltip("Log each successful upsert.")]
    public bool logEach = false;

    [Tooltip("Log warnings for objects that fail TryBuild or Upsert.")]
    public bool logProblems = true;

    private QdrantApiClient _client;
    private bool _started;
    private CancellationTokenSource _cts;

    // --- UI (optional) ---
    [Header("UI (optional)")]
    public UnityEngine.UI.Slider progressBar;   // assign a Slider in your Canvas
    public UnityEngine.GameObject progressRoot; // parent panel to show/hide (optional)
    public UnityEngine.Component progressLabel; // assign either Text or TextMeshProUGUI

    [Tooltip("How often to refresh the UI (number of scanned objects between UI updates).")]
    [Range(1, 200)]
    public int uiUpdateEvery = 10;

#if UNITY_EDITOR
    private void OnEnable() { UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged; }
    private void OnDisable() { UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged; CancelWarmIndex("OnDisable"); }
    private void OnPlayModeChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
            CancelWarmIndex("ExitingPlayMode");
    }
#endif
    private void OnDestroy()
    {
        CancelWarmIndex("OnDestroy");
        SetProgressText("Indexing stopped.");
        // Optional: hide the UI automatically on destroy
        SetProgressUIVisible(false);
    }

    private void CancelWarmIndex(string from)
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            Debug.Log($"[WarmIndex] Cancel requested ({from}).");
        }
    }

    private void Awake()
    {
        // Auto-load settings from Resources if not wired in the Inspector
        if (settings == null)
            settings = Resources.Load<QdrantSettings>("Config/QdrantSettings");
    }

    private async void Start()
    {
        if (!runOnStart || _started) return;
        _started = true;

        if (settings == null)
        {
            Debug.LogError("[WarmIndex] Missing QdrantSettings (assign in Inspector or place at Resources/Config/QdrantSettings.asset)");
            return;
        }

        _client = new QdrantApiClient(settings);
        _cts = new CancellationTokenSource();

        // UI init
        SetProgressUIVisible(true);
        SetProgressText("Indexing…");
        if (progressBar != null) { progressBar.value = 0; }

        await WarmIndexAsync(_cts.Token);
    }

    /// <summary>
    /// Public entry-point if you want to trigger from code/UI instead of Start().
    /// </summary>
    public async void TriggerWarmIndex()
    {
        if (_started) { Debug.Log("[WarmIndex] Already started for this scene."); return; }
        _started = true;
        if (settings == null) settings = Resources.Load<QdrantSettings>("Config/QdrantSettings");
        if (settings == null) { Debug.LogError("[WarmIndex] Missing QdrantSettings"); return; }
        _client = new QdrantApiClient(settings);
        _cts = new CancellationTokenSource();

        // UI init (same as Start)
        SetProgressUIVisible(true);
        SetProgressText("Indexing…");
        if (progressBar != null) { progressBar.value = 0; }

        await WarmIndexAsync(_cts.Token);
    }

    private static bool sRunning;

    private async Task WarmIndexAsync(CancellationToken token)
    {
        if (sRunning) { Debug.LogWarning("[WarmIndex] Already running in this process."); return; }
        sRunning = true;

        try
        {
            var sceneId = string.IsNullOrWhiteSpace(settings.defaultSceneId)
                ? UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
                : settings.defaultSceneId;

            // 1) Gather mesh-bearing objects (MeshFilter + SkinnedMeshRenderer)
            var objects = CollectMeshGameObjects(includeInactive);
            Debug.Log($"[WarmIndex] Cached {objects.Count} renderers under {sceneId}");

            int totalToScan = objects.Count;
            UpdateProgressUI(0, 0, 0, 0, 0, totalToScan);

            int matched = 0;
            int posted = 0, skipped = 0, failed = 0;
            int i = 0; // scanned counter

            foreach (var go in objects)
            {
                if (token.IsCancellationRequested || !Application.isPlaying) break;

                // count this object as scanned
                i++;

                if (!QdrantUpsertBuilder.TryBuild(go, sceneId, out var req))
                {
                    skipped++;
                    if (logProblems) Debug.LogWarning($"[WarmIndex] Skip (TryBuild=false): {go.name}");
                    if (uiUpdateEvery <= 1 || (i % uiUpdateEvery) == 0)
                        UpdateProgressUI(i, matched, posted, skipped, failed, totalToScan);
                    if ((i % yieldEvery) == 0) await Task.Yield();
                    continue;
                }

                // IFC-type filter
                if (!string.IsNullOrWhiteSpace(includeOnlyIfcType) &&
                    !string.Equals(req.ifc_type_final, includeOnlyIfcType, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (logEach) Debug.Log($"[WarmIndex] Skip by type filter: {go.name} ({req.ifc_type_final})");
                    if (uiUpdateEvery <= 1 || (i % uiUpdateEvery) == 0)
                        UpdateProgressUI(i, matched, posted, skipped, failed, totalToScan);
                    if ((i % yieldEvery) == 0) await Task.Yield();
                    continue;
                }

                // Maintain a registry of types seen (used elsewhere for fuzzy inference, optional)
                IfcTypeRegistry.Register(req.ifc_type_final);

                // Apply maxObjects AFTER we know it matches
                if (maxObjects > 0 && matched >= maxObjects) break;
                matched++;

                try
                {
                    // POST
                    var res = await _client.UpsertAsync(req);

                    if (token.IsCancellationRequested || !Application.isPlaying) break;

                    if (IsUpsertSuccess(res))
                    {
                        posted++;
                        if (logEach)
                            Debug.Log($"[WarmIndex] ✅ Upserted: {go.name}  id={req.unique_id}  type={req.ifc_type_final}");
                    }
                    else
                    {
                        failed++;
                        if (logProblems)
                        {
                            string err = TryGetError(res);
                            Debug.LogWarning($"[WarmIndex] ❌ Upsert failed: {go.name}  id={req.unique_id}  type={req.ifc_type_final}  error={err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    if (logProblems)
                        Debug.LogWarning($"[WarmIndex] ❌ Exception upserting {go.name}: {ex.Message}");
                }

                if (delayMsBetweenPosts > 0)
                {
                    try { await Task.Delay(delayMsBetweenPosts, token); } catch { }
                }

                // periodic progress update + cooperative yield
                if (uiUpdateEvery <= 1 || (i % uiUpdateEvery) == 0)
                    UpdateProgressUI(i, matched, posted, skipped, failed, totalToScan);

                if ((i % yieldEvery) == 0)
                {
                    await Task.Yield();
                    if (token.IsCancellationRequested || !Application.isPlaying) break;
                }
            }

            // Final UI update
            UpdateProgressUI(i, matched, posted, skipped, failed, totalToScan);

            if (token.IsCancellationRequested || !Application.isPlaying)
            {
                Debug.Log("[WarmIndex] Cancelled.");
                SetProgressText("Indexing cancelled.");
            }
            else
            {
                Debug.Log($"[WarmIndex] Done. Upserted={posted}, Skipped={skipped}, Failed={failed}. Scanned={objects.Count}.");
                SetProgressText($"Done. Upserted={posted}, Skipped={skipped}, Failed={failed}. Scanned={objects.Count}.");
                SetProgressUIVisible(false);

            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WarmIndex] Unhandled error: {e}");
            SetProgressText("Indexing error (see console).");
        }
        finally
        {
            sRunning = false;
        }
    }

    // Accept either res.ok == true OR res.status == "ok"
    private static bool IsUpsertSuccess(object res)
    {
        if (res == null) return false;
        try
        {
            var t = res.GetType();
            var okProp = t.GetProperty("ok");
            if (okProp != null && okProp.PropertyType == typeof(bool) && (bool)okProp.GetValue(res))
                return true;

            var statusProp = t.GetProperty("status");
            var status = statusProp?.GetValue(res) as string;
            if (!string.IsNullOrEmpty(status) && string.Equals(status, "ok", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch { }
        return false;
    }

    private static string TryGetError(object res)
    {
        if (res == null) return "null";
        try
        {
            var t = res.GetType();
            var p = t.GetProperty("error");
            if (p != null)
            {
                var v = p.GetValue(res) as string;
                if (!string.IsNullOrEmpty(v)) return v;
            }
        }
        catch { }
        return "(no error field)";
    }

    private static List<GameObject> CollectMeshGameObjects(bool includeInactive)
    {
        var result = new List<GameObject>();
        try
        {
#if UNITY_2020_1_OR_NEWER
            var mfs = GameObject.FindObjectsOfType<MeshFilter>(includeInactive);
            var sks = GameObject.FindObjectsOfType<SkinnedMeshRenderer>(includeInactive);
#else
            var mfs = Resources.FindObjectsOfTypeAll<MeshFilter>();
            var sks = Resources.FindObjectsOfTypeAll<SkinnedMeshRenderer>();
#endif
            var set = new HashSet<int>();
            foreach (var mf in mfs)
            {
                if (mf == null) continue;
                var go = mf.gameObject; if (go == null) continue;
                if (set.Add(go.GetInstanceID())) result.Add(go);
            }
            foreach (var sk in sks)
            {
                if (sk == null) continue;
                var go = sk.gameObject; if (go == null) continue;
                if (set.Add(go.GetInstanceID())) result.Add(go);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WarmIndex] Collect failed, falling back: {ex.Message}");
        }
        return result;
    }

    // ---------- UI helpers ----------
    private void SetProgressUIVisible(bool visible)
    {
        if (progressRoot != null) progressRoot.SetActive(visible);
        if (progressBar != null) progressBar.gameObject.SetActive(visible);
        if (progressLabel != null) progressLabel.gameObject.SetActive(visible);
    }

    private void SetProgressText(string text)
    {
        if (progressLabel == null) return;

        // Try UnityEngine.UI.Text first
        var uText = progressLabel as UnityEngine.UI.Text;
        if (uText != null) { uText.text = text; return; }

        // Try TextMeshProUGUI via reflection (no hard reference to TMPro)
        var tmpType = System.Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
        if (tmpType != null && tmpType.IsInstanceOfType(progressLabel))
        {
            var prop = tmpType.GetProperty("text");
            if (prop != null) prop.SetValue(progressLabel, text, null);
        }
    }

    private void UpdateProgressUI(int scanned, int matched, int posted, int skipped, int failed, int totalToScan)
    {
        if (progressBar != null)
        {
            progressBar.minValue = 0;
            progressBar.maxValue = Mathf.Max(1, totalToScan);
            progressBar.value = Mathf.Clamp(scanned, 0, totalToScan);
        }
        SetProgressText($"Indexing… scanned {scanned}/{totalToScan}  |  matched {matched}  •  upserted {posted}  •  skipped {skipped}  •  failed {failed}");
    }
}
