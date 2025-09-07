/*
 * Script Summary:
 * ----------------
 * Editor/runtime utility to batch-setup IFC model objects with required components.  
 * Adds MeshCollider, ifcProperties, SelectableObject, and IFCInitializer.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: AutoSetupIfcModel – MonoBehaviour.
 * - Key Methods:
 *     • SetupNow() – Run from Inspector, sets up current GameObject hierarchy.
 *     • SetupOnSelected() – Editor menu command for selected roots.
 *     • SetupOnRoot(...) – Core loop adding components per mesh-bearing GameObject.
 * - Components Added:
 *     • MeshCollider (with options for convex/trigger).
 *     • ifcProperties (holds metadata).
 *     • SelectableObject (for selection handling).
 *     • IFCInitializer (for ObjectRegistry registration).
 * - Special Considerations:
 *     • Skips empty GameObjects (no mesh).
 *     • Uses Undo system in editor for safety.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // In Unity Editor: Tools → RenovAite → Setup Model Components (Selected Roots)
 * // Or: Add AutoSetupIfcModel to root prefab and click "Setup Now".
 * ```
 */

using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Adds MeshCollider + ifcProperties + SelectableObject + IFCInitializer
/// to every GameObject in a hierarchy that has a MeshFilter (or SkinnedMeshRenderer if enabled).
/// Use the Tools menu (Editor) or the component's context menu (both Edit/Play).
/// </summary>
[DisallowMultipleComponent]
public class AutoSetupIfcModel : MonoBehaviour
{
    [Header("Scope")]
    public bool includeInactive = true;
    public bool includeSkinnedMeshes = false;

    [Header("Collider Settings")]
    public bool forceResetCollider = false;    // reassign sharedMesh even if a MeshCollider exists
    public bool convex = false;
    public bool isTrigger = false;

    [Header("Logging")]
    public bool verbose = true;

    // --- Run from the component (right-click the gear icon > "Setup Now") ---
    [ContextMenu("Setup Now")]
    public void SetupNow()
    {
        var count = SetupOnRoot(gameObject, includeInactive, includeSkinnedMeshes, forceResetCollider, convex, isTrigger, verbose);
        Debug.Log($"[AutoSetup] ✅ Done. Affected {count.total} objects (colliders:{count.collidersAdded} ifcProps:{count.ifcPropsAdded} selectable:{count.selectablesAdded} ifcInit:{count.ifcInitAdded}).");
    }

#if UNITY_EDITOR
    [MenuItem("Tools/RenovAite/Setup Model Components (Selected Roots)")]
    private static void SetupOnSelected()
    {
        var roots = Selection.gameObjects;
        if (roots == null || roots.Length == 0)
        {
            Debug.LogWarning("[AutoSetup] Nothing selected.");
            return;
        }

        int total = 0, cA = 0, iA = 0, sA = 0, fA = 0;
        foreach (var root in roots)
        {
            Undo.RegisterFullObjectHierarchyUndo(root, "AutoSetupIfcModel");
            // 🔧 FIX: use includeSkinned (method param name), not includeSkinnedMeshes
            var res = SetupOnRoot(
                root,
                includeInactive: true,
                includeSkinned: false,
                forceResetCol: false,
                convex: false,
                isTrigger: false,
                verbose: true
            );
            total += res.total; cA += res.collidersAdded; iA += res.ifcPropsAdded; sA += res.selectablesAdded; fA += res.ifcInitAdded;
        }
        Debug.Log($"[AutoSetup] ✅ Completed for {roots.Length} root(s). Affected {total} objects (colliders:{cA} ifcProps:{iA} selectable:{sA} ifcInit:{fA}).");
    }
#endif


    // ---------------- core logic ----------------

    private static (int total, int collidersAdded, int ifcPropsAdded, int selectablesAdded, int ifcInitAdded)
    SetupOnRoot(GameObject root, bool includeInactive, bool includeSkinned, bool forceResetCol, bool convex, bool isTrigger, bool verbose)
    {
        int total = 0, cA = 0, iA = 0, sA = 0, fA = 0;

        foreach (var go in EnumerateHierarchy(root, includeInactive))
        {
            // --- SAFELY check for a mesh on this node (empty IFC containers are common) ---
            var mf = go.GetComponent<MeshFilter>();
            var smr = includeSkinned ? go.GetComponent<SkinnedMeshRenderer>() : null;

            Mesh baseMesh = null;
            if (mf != null)
            {           // only read sharedMesh if the component exists
                baseMesh = mf.sharedMesh;
            }
            if (baseMesh == null && smr != null)
            {
                baseMesh = smr.sharedMesh;
            }

            if (baseMesh == null)
                continue; // skip empty/organizational GameObjects

            total++;

            // 1) MeshCollider (assign the *found* baseMesh)
            var mc = go.GetComponent<MeshCollider>();
            if (mc == null)
            {
#if UNITY_EDITOR
                mc = Undo.AddComponent<MeshCollider>(go);
#else
            mc = go.AddComponent<MeshCollider>();
#endif
                cA++;
                if (verbose) Debug.Log($"[AutoSetup] +MeshCollider  -> {GetPath(go)}");
            }
            mc.convex = convex;
            mc.isTrigger = isTrigger;

            if (forceResetCol || mc.sharedMesh == null)
                mc.sharedMesh = baseMesh;

            // 2) ifcProperties
            var ifc = go.GetComponent<ifcProperties>();
            if (ifc == null)
            {
#if UNITY_EDITOR
                ifc = Undo.AddComponent<ifcProperties>(go);
#else
            ifc = go.AddComponent<ifcProperties>();
#endif
                iA++;
                if (verbose) Debug.Log($"[AutoSetup] +ifcProperties -> {GetPath(go)}");
            }

            // 3) SelectableObject (+ wire to ifcProperties if field exists)
            var sel = go.GetComponent<SelectableObject>();
            if (sel == null)
            {
#if UNITY_EDITOR
                sel = Undo.AddComponent<SelectableObject>(go);
#else
            sel = go.AddComponent<SelectableObject>();
#endif
                sA++;
                if (verbose) Debug.Log($"[AutoSetup] +Selectable    -> {GetPath(go)}");
            }
            TryWireSelectableToIfc(sel, ifc);

            // 4) IFCInitializer
            var init = go.GetComponent<IFCInitializer>();
            if (init == null)
            {
#if UNITY_EDITOR
                init = Undo.AddComponent<IFCInitializer>(go);
#else
            init = go.AddComponent<IFCInitializer>();
#endif
                fA++;
                if (verbose) Debug.Log($"[AutoSetup] +IFCInitializer-> {GetPath(go)}");
            }
        }

        return (total, cA, iA, sA, fA);
    }


    private static IEnumerable<GameObject> EnumerateHierarchy(GameObject root, bool includeInactive)
    {
        if (!includeInactive && !root.activeInHierarchy) yield break;

        var stack = new Stack<Transform>();
        stack.Push(root.transform);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (includeInactive || t.gameObject.activeInHierarchy)
                yield return t.gameObject;

            for (int i = 0; i < t.childCount; i++)
                stack.Push(t.GetChild(i));
        }
    }

    private static void TryWireSelectableToIfc(SelectableObject sel, ifcProperties ifc)
    {
        if (sel == null || ifc == null) return;

        // Attempt common field names via reflection: "ifcProps", "ifcProperties"
        var type = sel.GetType();
        var field = type.GetField("ifcProps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetField("ifcProperties", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (field != null && field.FieldType == typeof(ifcProperties))
        {
            field.SetValue(sel, ifc);
        }
        else
        {
            // Optional: look for a setter method like SetIfcProps(ifcProperties p)
            var method = type.GetMethod("SetIfcProps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                method.Invoke(sel, new object[] { ifc });
            }
        }
    }

    private static string GetPath(GameObject go)
    {
        var path = go.name;
        var t = go.transform.parent;
        while (t != null) { path = t.name + "/" + path; t = t.parent; }
        return path;
    }
}
