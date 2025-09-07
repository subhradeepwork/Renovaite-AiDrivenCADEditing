/*
 * Script Summary:
 * ----------------
 * Global registry mapping IFC Unique IDs to GameObjects.  
 * Ensures stable lookup for AI instructions by unique_id.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ObjectRegistry – MonoBehaviour singleton.
 * - Key Methods:
 *     • Register(uniqueId, go) / Unregister(uniqueId).
 *     • GetObjectById(uniqueId).
 *     • ClearAll().
 *     • ExportObjectSummary() – Calls ObjectSummaryExporter.ExportAll().
 * - Dependencies/Interactions:
 *     • IFCInitializer auto-registers at runtime.
 *     • Queried by ActionAppliers, DeleteApplier, ReplaceApplier, etc.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * ObjectRegistry._instance.Register("WALL_001", wallGO);
 * var obj = ObjectRegistry._instance.GetObjectById("WALL_001");
 * ```
 */

using System.Collections.Generic;
using UnityEngine;

public class ObjectRegistry : MonoBehaviour
{
    public static ObjectRegistry _instance;

    private void Awake()
    {
        _instance = this;
    }


    public void ExportObjectSummary()
    {
        ObjectSummaryExporter.ExportAll();
    }

    // Mapping: Unique ID → GameObject
    private Dictionary<string, GameObject> registry = new Dictionary<string, GameObject>();

    public void Register(string uniqueId, GameObject obj)
    {
        if (!string.IsNullOrEmpty(uniqueId) && obj != null)
        {
            registry[uniqueId] = obj;
            //Debug.Log("Unique id registered: "+uniqueId + " Object:"+obj);
        }
    }

    public void Unregister(string uniqueId)
    {
        if (!string.IsNullOrEmpty(uniqueId))
        {
            registry.Remove(uniqueId);
        }
    }

    public GameObject GetObjectById(string uniqueId)
    {
        if (registry.TryGetValue(uniqueId, out var obj))
            return obj;

        return null;
    }

    public void ClearAll()
    {
        registry.Clear();
    }
}
