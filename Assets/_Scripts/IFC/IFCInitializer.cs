/*
 * Script Summary:
 * ----------------
 * Ensures GameObjects with IFC metadata (ifcProperties) are registered into the ObjectRegistry
 * under their Unique ID at runtime, and unregistered on destruction.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: IFCInitializer – MonoBehaviour attached to imported IFC objects.
 * - Key Methods:
 *     • Start() – On scene load, extracts Unique ID from ifcProperties and registers it in ObjectRegistry.
 *     • OnDestroy() – When object is destroyed, unregisters the Unique ID.
 *     • ExtractUniqueId(ifcProperties) – Scans properties/nominalValues lists for "Unique ID".
 * - Dependencies/Interactions:
 *     • ifcProperties – Custom component storing IFC property/value lists.
 *     • ObjectRegistry – Global registry mapping Unique IDs to GameObjects.
 * - Special Considerations:
 *     • Logs if ifcProperties or lists are null.
 *     • Registration is skipped if no Unique ID is found.
 *     • Keeps registry consistent with scene lifecycle.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // Attached automatically during IFC import pipeline:
 * // On Start(), registers object into ObjectRegistry for LLM/AI actions.
 * // On Destroy(), cleans it up.
 * ```
 */

using UnityEngine;

public class IFCInitializer : MonoBehaviour
{
    void Start()
    {
        var ifc = GetComponent<ifcProperties>();
        string uniqueId = ExtractUniqueId(ifc);

        if (!string.IsNullOrEmpty(uniqueId))
        {
            ObjectRegistry._instance.Register(uniqueId, gameObject);
        }
        else
        {
            //Debug.Log("Not satisfied");
        }
    }

    void OnDestroy()
    {
        var ifc = GetComponent<ifcProperties>();
        string uniqueId = ExtractUniqueId(ifc);

        if (!string.IsNullOrEmpty(uniqueId))
        {
            ObjectRegistry._instance.Unregister(uniqueId);
        }
    }

    private string ExtractUniqueId(ifcProperties ifc)
    {
        if (ifc == null || ifc.properties == null || ifc.nominalValues == null)
        {
            Debug.Log("ifcProperties or its lists are null");
            return null;
        }

        //Debug.Log("🔎 Inspecting ifc.properties:");
        for (int i = 0; i < ifc.properties.Count; i++)
        {
            string key = ifc.properties[i];
            string value = i < ifc.nominalValues.Count ? ifc.nominalValues[i] : "(no value)";
            //Debug.Log($"  {i}: {key} = {value}");
        }

        // Try "Unique ID" first (matches your context JSON)
        for (int i = 0; i < ifc.properties.Count; i++)
        {
            if (ifc.properties[i] == "Unique ID" && i < ifc.nominalValues.Count)
            {
                return ifc.nominalValues[i];
            }
        }

        //Debug.Log("Unique ID not found in ifcProperties");
        return null;
    }

}
