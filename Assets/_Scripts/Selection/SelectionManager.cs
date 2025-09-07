/*
 * Script Summary:
 * ----------------
 * Handles object selection, visual feedback (wireframe + bounding box), and event broadcasting.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: SelectionManager – MonoBehaviour managing scene selection.
 * - Key Methods:
 *     • SetSelected(go) – Sets new selection, triggers SelectionChanged event.
 *     • Update() – Handles mouse input for selection and Ctrl+D for restore.
 *     • TrySelectObject() – Raycasts under mouse to find SelectableObject.
 *     • SelectNewObject(obj, so) – Applies wireframe to others, bounding box to selected.
 *     • RestoreAllOriginalMaterials() – Resets all materials to baseline.
 *     • GetSelectedContextJSON() – Builds JSON with type, transform, material, Unique ID, IFC metadata.
 * - Key Fields:
 *     • mainCamera – Raycasting source.
 *     • highlightMaterial, wireframeMaterial – Used for selection feedback.
 *     • boundingBoxLineMaterial – Material for line renderer bounding box.
 *     • rootParent – Root container for renderables to manage.
 * - Dependencies/Interactions:
 *     • Works with SelectableObject (must be present).
 *     • Raises SelectionChanged event consumed by QdrantSelectionListener.
 * - Special Considerations:
 *     • Stores/restores original sharedMaterials to avoid leaks.
 *     • Draws dynamic bounding box with LineRenderer.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * selectionManager.SetSelected(go);
 * var ctx = selectionManager.GetSelectedContextJSON();
 * Debug.Log(ctx.ToString(2));
 * ```
 */

using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;
using System;
using UnityEngine.EventSystems;

public class SelectionManager : MonoBehaviour
{
    public Camera mainCamera;
    public Material highlightMaterial;
    public Material wireframeMaterial; // ✅ NEW: Wireframe Material for non-selected

    private Material originalMaterial;
    private GameObject selectedObject;
    private SelectableObject selectedComponent;


    public GameObject rootParent;  // 🔓 Drag your parent container in the Inspector
    private List<Renderer> allRenderableParts = new();  // 💾 All renderers under root


    // ✅ NEW: Dictionary to store all original materials
    private Dictionary<GameObject, Material[]> originalMaterials = new();


    public Material boundingBoxLineMaterial;
    private GameObject boundingBoxLines;
    [Range(0.001f, 0.1f)]
    public float boundingBoxLineThickness = 0.01f;



    public static event Action<GameObject> SelectionChanged;
    private GameObject _selected;


    // call this from wherever you currently change selection
    public void SetSelected(GameObject newSelection)
    {
        if (_selected == newSelection) return;
        _selected = newSelection;
        SelectionChanged?.Invoke(_selected);
    }



    void Start()
    {
        CacheAllRenderersUnderParent();
    }

    void CacheAllRenderersUnderParent()
    {
        allRenderableParts.Clear();

        if (rootParent == null)
        {
            Debug.LogError("❌ No root parent assigned in SelectionManager.");
            return;
        }

        // Get all Renderer components in parent and children
        Renderer[] renderers = rootParent.GetComponentsInChildren<Renderer>(includeInactive: true);

        foreach (Renderer rend in renderers)
        {
            // Optional: you can skip non-mesh renderers if needed
            if (rend is MeshRenderer || rend is SkinnedMeshRenderer)
            {
                allRenderableParts.Add(rend);
            }
        }

        Debug.Log($"✅ Cached {allRenderableParts.Count} renderers under {rootParent.name}");
    }


    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (!EventSystem.current.IsPointerOverGameObject())
                TrySelectObject();
        }


        // ✅ Check for Ctrl + D
        if ((Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)) &&
            Input.GetKeyDown(KeyCode.D))
        {
            RestoreAllOriginalMaterials();
            Debug.Log("🔄 Ctrl+D pressed → Restored original materials");
        }
    }

    void TrySelectObject()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (hit.collider != null && hit.collider.gameObject.TryGetComponent(out SelectableObject so))
            {
                SelectNewObject(hit.collider.gameObject, so);
            }
        }
    }

    void SelectNewObject(GameObject obj, SelectableObject so)
    {
        // ✅ Restore all previously overridden materials
        RestoreAllOriginalMaterials();

        selectedObject = obj;
        selectedComponent = so;

        // ✅ Set wireframe on all selectable objects except the selected one
        ApplyWireframeToAllExceptSelected(selectedObject);

        ShowBoundingBoxLines(selectedObject);

        SetSelected(selectedObject);
        Debug.Log("🟦 Selected: " + obj.name);
        Debug.Log("📦 Context:\n" + GetSelectedContextJSON().ToString(2));
    }



    void ShowBoundingBoxLines(GameObject target)
    {
        if (boundingBoxLines != null)
            Destroy(boundingBoxLines);

        Bounds bounds = target.GetComponent<Renderer>().bounds;
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;

        // Define 8 corners of the bounding box
        Vector3[] corners = new Vector3[8];
        corners[0] = center + new Vector3(-extents.x, -extents.y, -extents.z);
        corners[1] = center + new Vector3(extents.x, -extents.y, -extents.z);
        corners[2] = center + new Vector3(extents.x, -extents.y, extents.z);
        corners[3] = center + new Vector3(-extents.x, -extents.y, extents.z);
        corners[4] = center + new Vector3(-extents.x, extents.y, -extents.z);
        corners[5] = center + new Vector3(extents.x, extents.y, -extents.z);
        corners[6] = center + new Vector3(extents.x, extents.y, extents.z);
        corners[7] = center + new Vector3(-extents.x, extents.y, extents.z);

        // 12 edges of a cube (pairs of corner indices)
        int[] edges = new int[]
        {
        0, 1, 1, 2, 2, 3, 3, 0, // bottom
        4, 5, 5, 6, 6, 7, 7, 4, // top
        0, 4, 1, 5, 2, 6, 3, 7  // verticals
        };

        // Create GameObject with LineRenderer
        boundingBoxLines = new GameObject("BoundingBoxLines");
        boundingBoxLines.transform.SetParent(target.transform);
        boundingBoxLines.transform.localPosition = Vector3.zero;
        boundingBoxLines.transform.localRotation = Quaternion.identity;

        LineRenderer lr = boundingBoxLines.AddComponent<LineRenderer>();
        lr.positionCount = edges.Length;
        lr.material = boundingBoxLineMaterial;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.widthMultiplier = boundingBoxLineThickness;

        // Assign line points from corners
        for (int i = 0; i < edges.Length; i++)
        {
            lr.SetPosition(i, corners[edges[i]]);
        }
    }




    // ✅ NEW: Apply wireframe material to all selectable objects except the selected one
    void ApplyWireframeToAllExceptSelected(GameObject selected)
    {
        foreach (Renderer rend in allRenderableParts)
        {
            if (rend == null) continue;

            GameObject go = rend.gameObject;
            if (go == selected) continue;

            // ✅ Always refresh baseline from SHARED materials (clone to avoid aliasing)
            var baseline = rend.sharedMaterials;
            if (baseline == null) baseline = Array.Empty<Material>();
            originalMaterials[go] = (Material[])baseline.Clone();


            // ✅ Apply wireframe to all submesh slots without touching the selected object
            if (wireframeMaterial != null)
            {
                int count = Mathf.Max(1, baseline.Length);
                var wire = new Material[count];
                for (int i = 0; i < count; i++) wire[i] = wireframeMaterial;
                rend.sharedMaterials = wire;
                rend.SetPropertyBlock(null); // clear any MPB overrides
            }
        }
    }




    // ✅ NEW: Restore original materials to all objects
    void RestoreAllOriginalMaterials()
    {
        foreach (var kvp in originalMaterials)
        {
            var go = kvp.Key;
            if (go == null) continue;

            var rend = go.GetComponent<Renderer>();
            if (rend == null) continue;

            // ✅ Restore baseline at SHARED level (authoritative) and clear MPB
            rend.sharedMaterials = kvp.Value;
            rend.SetPropertyBlock(null);
        }

        if (boundingBoxLines != null)
        {
            Destroy(boundingBoxLines);
            boundingBoxLines = null;
        }

        originalMaterials.Clear();
    }


    public void NoteBaselineChanged(GameObject go)
    {
        if (!go) return;
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;

        var baseline = rend.sharedMaterials;
        if (baseline == null) baseline = Array.Empty<Material>();
        originalMaterials[go] = (Material[])baseline.Clone();
    }

    // -------- Existing JSON Context Function --------
    public JSONNode GetSelectedContextJSON()
    {
        if (selectedComponent == null) return null;

        JSONObject json = new();
        json["object_type"] = selectedComponent.GetObjectType();

        Vector3 pos = selectedComponent.GetPosition();
        Vector3 rot = selectedComponent.GetRotation();
        Vector3 scale = selectedComponent.GetScale();

        json["position"] = new JSONObject
        {
            ["x"] = pos.x,
            ["y"] = pos.y,
            ["z"] = pos.z
        };
        json["rotation"] = new JSONObject
        {
            ["x"] = rot.x,
            ["y"] = rot.y,
            ["z"] = rot.z
        };
        json["scale"] = new JSONObject
        {
            ["x"] = scale.x,
            ["y"] = scale.y,
            ["z"] = scale.z
        };

        json["material"] = selectedComponent.GetMaterialName();

        string uniqueId = null;
        var ifcProps = selectedObject.GetComponent<ifcProperties>();
        if (ifcProps != null)
        {
            for (int i = 0; i < ifcProps.properties.Count; i++)
            {
                if (ifcProps.properties[i] == "GlobalId" && i < ifcProps.nominalValues.Count)
                {
                    uniqueId = ifcProps.nominalValues[i];
                    break;
                }
            }
        }

        if (!string.IsNullOrEmpty(uniqueId))
        {
            json["unique_id"] = uniqueId;
        }

        JSONObject metadata = new();
        var meta = selectedComponent.GetIFCMetadata();
        foreach (var kv in meta)
            metadata[kv.Key] = kv.Value;
        json["ifc_properties"] = metadata;

        return json;
    }

    public GameObject GetSelectedGameObject()
    {
        return selectedObject;
    }
}
