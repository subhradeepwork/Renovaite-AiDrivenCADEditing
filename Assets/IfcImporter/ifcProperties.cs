// ----------------------------------------------------------------------------
// STUB: ifcProperties / IfcProperties
// Purpose: Compile the project WITHOUT the IFC plugin installed.
// Replace this whole file with the real plugin's component when available.
// ----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Minimal placeholder for the plugin's IFC component.
/// Keep names exactly as used in your codebase.
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("RenovAite/IFC/ifcProperties (Stub)")]
public class ifcProperties : MonoBehaviour
{
    // Common identifiers your code references
    [Header("Identifiers (stub)")]
    public string GlobalId;      // replace with real plugin field/property
    public string GUID;          // sometimes plugins expose GUID separately
    public string IfcType;       // e.g., "IfcWall", "IfcWindow", etc.

    // Your code (e.g., SelectableObject) uses these two lists.
    // Indexes must align: properties[i] -> nominalValues[i]
    [Header("Property sets (stub)")]
    public List<string> properties = new List<string>();
    public List<string> nominalValues = new List<string>();

    // ---- Convenience helpers (safe no-ops if unfilled) ---------------------

    /// <summary>True if the property key exists.</summary>
    public bool Has(string key)
    {
        if (string.IsNullOrEmpty(key) || properties == null) return false;
        return properties.Contains(key);
    }

    /// <summary>Gets the value for a property key, or null if missing.</summary>
    public string GetValue(string key)
    {
        if (string.IsNullOrEmpty(key) || properties == null || nominalValues == null) return null;
        int i = properties.IndexOf(key);
        return (i >= 0 && i < nominalValues.Count) ? nominalValues[i] : null;
    }

    /// <summary>Try-get pattern for convenience.</summary>
    public bool TryGetValue(string key, out string value)
    {
        value = GetValue(key);
        return !string.IsNullOrEmpty(value);
    }

    // You can add more placeholders here to mirror the real component's API.
    // e.g. public Dictionary<string, object> PropertySets; etc.
}

/// <summary>
/// Some parts of your code may refer to 'IfcProperties' (PascalCase).
/// This subclass satisfies those references without duplicating logic.
/// </summary>
public class IfcProperties : ifcProperties { }
