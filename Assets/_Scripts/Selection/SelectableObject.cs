/*
 * Script Summary:
 * ----------------
 * Wrapper MonoBehaviour exposing IFC metadata and transform info for selection.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: SelectableObject – MonoBehaviour requiring ifcProperties.
 * - Key Methods:
 *     • GetObjectType() – Returns IFC "Type" or GameObject name.
 *     • GetIFCMetadata() – Builds dictionary of all IFC key/value pairs.
 *     • GetMaterialName() – Gets first material name of MeshRenderer.
 *     • GetPosition(), GetRotation(), GetScale() – Transform data accessors.
 * - Dependencies/Interactions:
 *     • ifcProperties component – Provides source IFC metadata.
 *     • Used by SelectionManager and Preview systems.
 * - Special Considerations:
 *     • Safe fallback to GameObject name if IFC type missing.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var so = go.GetComponent<SelectableObject>();
 * Debug.Log("Type=" + so.GetObjectType());
 * ```
 */

using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(ifcProperties))]
public class SelectableObject : MonoBehaviour
{
    public ifcProperties ifcProps;

    private void Awake()
    {
        ifcProps = GetComponent<ifcProperties>();
    }

    public string GetObjectType()
    {
        return ifcProps != null && ifcProps.properties.Contains("Type")
            ? ifcProps.nominalValues[ifcProps.properties.IndexOf("Type")]
            : gameObject.name;
    }

    public Dictionary<string, string> GetIFCMetadata()
    {
        Dictionary<string, string> metadata = new();

        if (ifcProps != null)
        {
            for (int i = 0; i < ifcProps.properties.Count; i++)
            {
                string key = ifcProps.properties[i];
                string value = ifcProps.nominalValues[i];
                if (!metadata.ContainsKey(key))
                    metadata[key] = value;
            }
        }

        return metadata;
    }

    public string GetMaterialName()
    {
        MeshRenderer mr = GetComponent<MeshRenderer>();
        if (mr != null && mr.materials.Length > 0)
            return mr.materials[0].name;
        return "default";
    }

    public Vector3 GetPosition() => transform.position;
    public Vector3 GetRotation() => transform.eulerAngles;
    public Vector3 GetScale() => transform.localScale;
}
