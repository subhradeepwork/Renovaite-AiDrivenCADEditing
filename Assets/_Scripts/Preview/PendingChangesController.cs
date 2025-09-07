/*
 * Script Summary:
 * ----------------
 * Manages the preview/confirmation UI for pending AI-driven modifications.
 * Highlights affected objects, shows changes in a panel, and applies or rejects them.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: PendingChangesController – MonoBehaviour singleton.
 * - Key Fields:
 *     • panelPrefab (PendingChangesPanel) – Prefab for the confirmation panel UI.
 *     • targetCanvas (Canvas) – Target canvas for panel placement (auto-created if null).
 *     • highlighter (OutlineHighlighter) – Highlights targets visually.
 *     • executor (InstructionExecutor) – Applies confirmed AIInstructions.
 * - Key Methods:
 *     • Present(string normalizedArgsJson, GameObject selectedObject, List<GameObject> targets)
 *       - Creates a ChangeSet, highlights targets, spawns preview panel, and lists changes.
 *     • ApplyCurrent() – Applies instruction to all targets via executor.
 *     • RejectCurrent() – Cancels and clears highlights.
 *     • DestroyPanelIfAny() – Removes UI panel and temporary canvas.
 *     • DescribeEffectFromJson(JSONNode, GameObject, int) – Generates human-readable action description.
 * - Dependencies/Interactions:
 *     • Requires OutlineHighlighter, InstructionExecutor, and UniqueIdExtractor.
 *     • Uses SimpleJSON for schema-agnostic JSON parsing.
 * - Special Considerations:
 *     • Safe multi-target handling (move absolute suppressed for multiple objects).
 *     • Auto-creates Canvas + EventSystem if none present in scene.
 *     • Logs accepted/rejected changes for debugging.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * PendingChangesController.Instance.Present(
 *     normalizedArgsJson: args,
 *     selectedObject: selectedGO,
 *     targets: similarObjects
 * );
 * ```
 */

using System.Collections.Generic;
using SimpleJSON;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using RenovAite.AI.Execution;

public sealed class PendingChangesController : MonoBehaviour
{
    public static PendingChangesController Instance { get; private set; }

    [Header("Prefab & Placement")]
    public PendingChangesPanel panelPrefab; // <- assign the prefab asset here
    public Canvas targetCanvas;             // optional; auto-find or create if null

    [Header("Runtime deps")]
    public OutlineHighlighter highlighter;
    public InstructionExecutor executor;

    private ChangeSet _current;
    private PendingChangesPanel _panelInstance;
    private Canvas _ownedCanvas; // if we create one on the fly

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (!executor) executor = FindObjectOfType<InstructionExecutor>();
        if (!highlighter) highlighter = FindObjectOfType<OutlineHighlighter>(true);
    }

    public void Present(string normalizedArgsJson, GameObject selectedObject, List<GameObject> targets)
    {
        // Destroy any previous panel first
        DestroyPanelIfAny();

        // Build changeset
        _current = new ChangeSet
        {
            normalizedArgsJson = normalizedArgsJson,
            instruction = JsonUtility.FromJson<AIInstruction>(normalizedArgsJson),
            selectedObject = selectedObject,
            targets = targets ?? new List<GameObject>()
        };

        // Highlight
        highlighter?.ClearAll();
        highlighter?.Highlight(_current.targets);

        // Instantiate panel
        var parent = GetCanvasTransform();
        _panelInstance = Instantiate(panelPrefab, parent);
        _panelInstance.gameObject.name = "PendingChangesPanel (Instance)";

        // Build row texts (schema-agnostic; reads from JSON)
        var instrNode = JSON.Parse(_current.normalizedArgsJson);

        /*
        var rows = new List<RowData>();
        foreach (var go in _current.targets)
        {
            var uid = UniqueIdExtractor.TryGetUniqueId(go) ?? "(no-uid)";
            var action = DescribeEffectFromJson(instrNode, go, _current.targets.Count);

            rows.Add(new RowData
            {
                Name = go.name,
                ID = uid,
                Action = action
            });
        }
        */

        // --- Build RowData for each target (fills the new fields too)
        var rows = new List<RowData>();

        // From the instruction JSON
        var selApplyTo = instrNode["selection"]?["apply_to"]?.Value ?? (selectedObject != null ? "selected" : "none");
        var modNode = instrNode["modification"];

        foreach (var go in _current.targets)
        {
            // ID / Action (unchanged)
            var uid = UniqueIdExtractor.TryGetUniqueId(go) ?? "(no-uid)";
            var action = DescribeEffectFromJson(instrNode, go, _current.targets.Count);

            // IFC props (self or parents), plus quick dictionary for lookups
            var ifcComp = go.GetComponentInParent<ifcProperties>(true);
            var ifcDict = ReadIfcAsDict(ifcComp); // helper below

            // Semantic type + level
            var semType = DeriveIfcTypeFinalFromDict(ifcDict, go.name); // helper below
            var level = GetIfc(ifcDict, "Home Story");

            // Parent path (2 steps up is enough for context)
            var parentPath = BuildParentPath(go.transform, 2);

            // Dimensions + quick geom flags
            float w, h, d; int tris;
            TryGetBoundsAndTris(go, out w, out h, out d, out tris); // helper below
            var dims = (w > 0 && h > 0 && d > 0) ? $"{w:F2}×{h:F2}×{d:F2} m" : "";

            // Tags: type + external/internal + material (lowercased)
            var tagsList = BuildTags(semType, ifcDict); // helper below
            var tagsText = (tagsList != null && tagsList.Count > 0) ? string.Join(", ", tagsList) : "";

            // Delta summary from instruction (position_delta / rotation_delta / scale)
            var delta = DescribeDelta(modNode); // helper below

            // Material changes (before -> after)
            var materialNow = GuessCurrentMaterial(go, ifcDict);     // helper below
            var materialNew = GuessNewMaterialFromMod(modNode);      // helper below
            var materialChange = string.IsNullOrEmpty(materialNew) ? "" : $"{materialNow} → {materialNew}";

            // Replacement summary, if any
            var replaceTo = modNode?["replace"]?["to"]?.Value ?? modNode?["replace"]?.Value;
            var replace = string.IsNullOrEmpty(replaceTo) ? "" : $"→ {replaceTo}";

            // Risk/diagnostic flags
            var flags = new List<string>();
            if (uid == "(no-uid)") flags.Add("⚠ missing UID");
            if (tris == 0) flags.Add("⚠ zero-geometry");
            var flagsText = string.Join("; ", flags);

            rows.Add(new RowData
            {
                // Existing
                Name = go.name,
                ID = uid,
                Action = action,

                // New (fill what you added to RowData)
                Type = semType,
                Level = level,
                ParentPath = parentPath,
                SelectionSource = selApplyTo,             // "filters" | "similar" | "ids" | "selected" | "none"
                //Similarity = null,                   // no score here (TargetResolver doesn’t pass it)
                //Delta = delta,
                //MaterialChange = materialChange,
                //Replace = replace,
                //Dimensions = dims,
                Tags = tagsText
                //Flags = flagsText
            });
        }




        var header = $"Will apply to {_current.targets.Count} object(s).";


        _panelInstance.Show(
            header,
            rows,   // now passing structured rows
            onAccept: () => { ApplyCurrent(); DestroyPanelIfAny(); },
            onReject: () => { RejectCurrent(); DestroyPanelIfAny(); }
        );

    }

    // ---- Helpers (schema/scene safe) ----
    private static Dictionary<string, string> ReadIfcAsDict(ifcProperties ifc)
    {
        var dict = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        if (ifc == null || ifc.properties == null || ifc.nominalValues == null) return dict;
        int n = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
        for (int i = 0; i < n; i++)
        {
            var k = ifc.properties[i] ?? "";
            var v = ifc.nominalValues[i] ?? "";
            if (!string.IsNullOrWhiteSpace(k) && !dict.ContainsKey(k)) dict[k] = v;
        }
        return dict;
    }

    private static string GetIfc(Dictionary<string, string> d, string key)
    {
        if (d == null || d.Count == 0 || string.IsNullOrEmpty(key)) return null;
        foreach (var kv in d)
            if (kv.Key.Equals(key, System.StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrWhiteSpace(kv.Value) ? null : kv.Value.Trim();
        return null;
    }

    // Matches your export/upsert derivation order
    private static string DeriveIfcTypeFinalFromDict(Dictionary<string, string> ifc, string nameFallback)
    {
        var t = GetIfc(ifc, "Type");
        if (!string.IsNullOrWhiteSpace(t)) return t;

        var elemClass = GetIfc(ifc, "Element Classification");
        if (!string.IsNullOrWhiteSpace(elemClass)) return elemClass;

        var family = GetIfc(ifc, "Family");
        if (!string.IsNullOrWhiteSpace(family)) return family;

        var layer = GetIfc(ifc, "Layer");
        if (!string.IsNullOrWhiteSpace(layer))
        {
            var prefix = layer.Split(new[] { '_', '-', '/' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (prefix.Length > 0 && !string.IsNullOrWhiteSpace(prefix[0])) return prefix[0];
            return layer;
        }

        if (!string.IsNullOrWhiteSpace(nameFallback))
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

    // Dimensions + triangle count (mirrors QdrantUpsertBuilder mesh summary)
    private static bool TryGetBoundsAndTris(GameObject go, out float w, out float h, out float d, out int tris)
    {
        w = h = d = 0f; tris = 0;
        if (go == null) return false;

        Mesh mesh = null;
        var mf = go.GetComponent<MeshFilter>();
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        if (mf?.sharedMesh != null) mesh = mf.sharedMesh;
        else if (smr?.sharedMesh != null) mesh = smr.sharedMesh;

        if (mesh == null)
        {
            var col = go.GetComponent<MeshCollider>();
            if (col?.sharedMesh != null) mesh = col.sharedMesh;
        }
        if (mesh == null) return false;

        var b = mesh.bounds;
        w = Mathf.Abs(b.size.x); h = Mathf.Abs(b.size.y); d = Mathf.Abs(b.size.z);

        if (mesh.isReadable && mesh.triangles != null)
            tris = mesh.triangles.Length / 3;
        else
            tris = 0;

        return true;
    }

    // Consistent tagging (type + external/internal + material)
    private static List<string> BuildTags(string semType, Dictionary<string, string> ifc)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(semType)) tags.Add(semType);

        var isExt = (GetIfc(ifc, "IsExternal") ?? "").Trim().ToLowerInvariant();
        if (isExt == "true" || isExt == "yes" || isExt == "1") tags.Add("external");
        if (isExt == "false" || isExt == "no" || isExt == "0") tags.Add("internal");

        var material = GetIfc(ifc, "Material");
        if (!string.IsNullOrWhiteSpace(material)) tags.Add(material.ToLowerInvariant());

        return tags;
    }

    private static string BuildParentPath(Transform t, int maxDepth)
    {
        if (t == null) return "";
        var parts = new List<string>();
        var cur = t.parent;
        while (cur != null && maxDepth-- > 0)
        {
            parts.Insert(0, cur.name);
            cur = cur.parent;
        }
        return (parts.Count > 0) ? string.Join("/", parts) : "";
    }

    private static string DescribeDelta(SimpleJSON.JSONNode mod)
    {
        if (mod == null || !mod.IsObject) return "";
        var bits = new List<string>();

        var dp = mod["position_delta"];
        if (dp != null && dp.IsObject)
            bits.Add($"Δpos({dp["x"]?.AsFloat:F2},{dp["y"]?.AsFloat:F2},{dp["z"]?.AsFloat:F2} m)");

        var dr = mod["rotation_delta"];
        if (dr != null && dr.IsObject)
            bits.Add($"Δrot({dr["x"]?.AsFloat:F1},{dr["y"]?.AsFloat:F1},{dr["z"]?.AsFloat:F1}°)");

        var sc = mod["scale"];
        if (sc != null && sc.IsObject)
        {
            // accept width/height/depth or x/y/z – we just show what exists
            var w = sc["width"] ?? sc["x"];
            var h = sc["height"] ?? sc["y"];
            var d = sc["depth"] ?? sc["z"];
            var parts = new List<string>();
            if (w != null) parts.Add($"x{w.AsFloat:F2}");
            if (h != null) parts.Add($"y{h.AsFloat:F2}");
            if (d != null) parts.Add($"z{d.AsFloat:F2}");
            if (parts.Count > 0) bits.Add("scale×(" + string.Join(",", parts) + ")");
        }
        return string.Join(", ", bits);
    }

    private static string GuessCurrentMaterial(GameObject go, Dictionary<string, string> ifc)
    {
        // Prefer IFC "Material", else renderer.sharedMaterial
        var m = GetIfc(ifc, "Material");
        if (!string.IsNullOrWhiteSpace(m)) return m;
        var r = go.GetComponentInChildren<Renderer>(true);
        return r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "";
    }

    private static string GuessNewMaterialFromMod(SimpleJSON.JSONNode mod)
    {
        if (mod == null) return "";
        var matNode = mod["material"];
        if (matNode == null) return "";
        // accept either { "material": {"name":"Concrete"} } or { "material":"Concrete" }
        var name = matNode["name"]?.Value ?? matNode.Value;
        return string.IsNullOrWhiteSpace(name) ? "" : name.Trim();
    }



    private void ApplyCurrent()
    {
        if (_current == null || executor == null)
        {
            Debug.LogError("[Preview] Missing ChangeSet or executor.");
            return;
        }

        // Try the centralized fan-out in AIClient (preferred)
        var ai = FindObjectOfType<AIClient>(true);   // same scene; includes inactive
        int applied = 0;

        if (ai != null)
        {
            applied = ai.FanOutAndExecute(
                executor,
                _current.normalizedArgsJson,
                _current.targets,
                _current.instruction
            );
        }
        else
        {
            // Fallback: keep your existing per-target execution
            foreach (var go in _current.targets)
            {
                var uid = UniqueIdExtractor.TryGetUniqueId(go);
                var node = SimpleJSON.JSON.Parse(_current.normalizedArgsJson);
                if (node["target"] == null) node["target"] = new SimpleJSON.JSONObject();
                node["target"]["unique_id"] = uid;
                node["target"]["name"] = go.name;

                var perTarget = JsonUtility.FromJson<AIInstruction>(node.ToString());
                executor.Execute(perTarget);
                applied++;
            }
        }

        Debug.Log($"[Preview] ✅ Applied to {applied}/{_current.targets.Count} targets.");
        highlighter?.ClearAll();
        _current = null;
    }




    private void RejectCurrent()
    {
        Debug.Log("[Preview] ❌ Rejected changes.");
        highlighter?.ClearAll();
        _current = null;
    }

    private void DestroyPanelIfAny()
    {
        if (_panelInstance != null)
        {
            _panelInstance.CloseAndDestroy();
            _panelInstance = null;
        }
        if (_ownedCanvas != null)
        {
            Destroy(_ownedCanvas.gameObject);
            _ownedCanvas = null;
        }
    }

    private Transform GetCanvasTransform()
    {
        // Use assigned canvas if provided
        if (targetCanvas != null)
        {
            if (!targetCanvas.gameObject.activeSelf) targetCanvas.gameObject.SetActive(true);
            EnsureEventSystem();
            return targetCanvas.transform;
        }

        // Try find an existing canvas (include inactive)
        var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var c in canvases)
        {
            if (c.gameObject.scene.IsValid()) // ignore prefabs in project
            {
                if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
                EnsureEventSystem();
                return c.transform;
            }
        }

        // Create a temporary canvas we own (destroyed when panel closes)
        var go = new GameObject("PreviewCanvas (Temp)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;

        _ownedCanvas = canvas;
        EnsureEventSystem();
        return canvas.transform;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            DontDestroyOnLoad(es); // harmless; avoids losing it on scene reloads
        }
    }

    // --------- Effect description helpers (schema-agnostic) ---------

    private static string DescribeEffectFromJson(JSONNode instrNode, GameObject go, int totalTargets)
    {
        if (instrNode == null) return "(no changes)";
        var action = instrNode["action"]?.Value ?? "(unknown)";
        var mod = instrNode["modification"];

        switch (action)
        {
            case "scale":
                {
                    var parts = new List<string>();
                    float dw = mod?["width"]?.AsFloat ?? 0f;
                    float dh = mod?["height"]?.AsFloat ?? 0f;
                    float dd = mod?["depth"]?.AsFloat ?? 0f;
                    if (Mathf.Abs(dw) > 1e-6f || Mathf.Abs(dh) > 1e-6f || Mathf.Abs(dd) > 1e-6f)
                    {
                        var b = GetWorldBounds(go);
                        float sx = (Mathf.Abs(dw) < 1e-6f || b.size.x <= 1e-6f) ? 1f : 1f + (dw / b.size.x);
                        float sy = (Mathf.Abs(dh) < 1e-6f || b.size.y <= 1e-6f) ? 1f : 1f + (dh / b.size.y);
                        float sz = (Mathf.Abs(dd) < 1e-6f || b.size.z <= 1e-6f) ? 1f : 1f + (dd / b.size.z);
                        parts.Add($"Δ→scale ≈ {{x:{sx:0.###}, y:{sy:0.###}, z:{sz:0.###}}}");
                    }
                    var s = mod?["scale"];
                    if (s != null)
                    {
                        var (sx, sy, sz) = ReadVec3(s);
                        parts.Add($"× scale {{x:{sx:0.###}, y:{sy:0.###}, z:{sz:0.###}}}");
                    }
                    return parts.Count > 0 ? string.Join("  |  ", parts) : "(no-op)";
                }

            case "move":
                {
                    var p = mod?["position"];
                    if (!IsNonZero(p)) return "(no move)";
                    if (totalTargets > 1) return "position IGNORED (multi-target safety); rotate/scale will apply if present";
                    var (x, y, z) = ReadVec3(p);
                    return $"move → ({x:0.###}, {y:0.###}, {z:0.###}) world";
                }

            case "rotate":
                {
                    var r = mod?["rotation"];
                    if (!IsNonZero(r)) return "(no rotate)";
                    var (x, y, z) = ReadVec3(r);
                    return $"rotate → euler({x:0.#}, {y:0.#}, {z:0.#})";
                }

            case "material":
                {
                    var mat = mod?["material"];
                    if (mat == null) return "(no material)";
                    if (mat.IsString) return $"material → {mat.Value}";
                    var name = mat["materialName"]?.Value ?? "(unnamed)";
                    var metallic = (mat["metallic"] != null) ? mat["metallic"].AsFloat : 0f;
                    var smoothness = (mat["smoothness"] != null) ? mat["smoothness"].AsFloat : 0f;
                    return $"material → {name}  (metallic {metallic:0.##}, smooth {smoothness:0.##})";
                }

            case "replace":
                {
                    var rep = mod?["replace"];
                    if (rep == null) return "(no replace)";
                    if (rep.IsString) return $"replace → {rep.Value}";
                    var prefab = rep["prefabPath"]?.Value ?? rep["libraryId"]?.Value ?? "(prefab?)";
                    return $"replace → {prefab}";
                }

            case "delete":
                return "delete → object will be removed";

            default:
                return $"(unsupported action: {action})";
        }
    }

    private static (float x, float y, float z) ReadVec3(JSONNode node)
    {
        if (node == null) return (0, 0, 0);
        if (node.IsArray && node.Count >= 3) return (node[0].AsFloat, node[1].AsFloat, node[2].AsFloat);
        float x = node["x"]?.AsFloat ?? 0f;
        float y = node["y"]?.AsFloat ?? 0f;
        float z = node["z"]?.AsFloat ?? 0f;
        return (x, y, z);
    }

    private static bool IsNonZero(JSONNode node)
    {
        var (x, y, z) = ReadVec3(node);
        return Mathf.Abs(x) > 1e-6f || Mathf.Abs(y) > 1e-6f || Mathf.Abs(z) > 1e-6f;
    }

    private static Bounds GetWorldBounds(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        if (r) return r.bounds;
        var mf = go.GetComponent<MeshFilter>();
        if (mf && mf.sharedMesh != null)
        {
            var b = mf.sharedMesh.bounds;
            var ls = go.transform.lossyScale;
            var size = Vector3.Scale(b.size, ls);
            return new Bounds(go.transform.position, size);
        }
        return new Bounds(go.transform.position, Vector3.zero);
    }
}
