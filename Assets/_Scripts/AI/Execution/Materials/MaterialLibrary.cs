/*
 * Script Summary:
 * ----------------
 * ScriptableObject container for mapping canonical material keys and aliases
 * to actual Unity Material assets.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MaterialLibrary (ScriptableObject).
 * - Nested Class:
 *     • Entry – { string key, Material material, string[] aliases }.
 * - Key Fields:
 *     • entries (List<Entry>) – Collection of mappings (keys → Material).
 * - Dependencies/Interactions:
 *     • Consumed by MaterialResolver to find appropriate materials.
 *     • Used by MaterialApplier when executing "material" instructions.
 * - Special Considerations:
 *     • Keys should be short canonical names (e.g., "wood", "concrete").
 *     • Aliases expand coverage for natural language prompts (e.g., "timber", "oak").
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var entry = new MaterialLibrary.Entry {
 *     key = "wood",
 *     material = myWoodMaterial,
 *     aliases = new string[] { "timber", "oak" }
 * };
 * materialLibrary.entries.Add(entry);
 * ```
 */

using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "RenovAite/Materials/Material Library", fileName = "MaterialLibrary")]
public sealed class MaterialLibrary : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        [Tooltip("Canonical key you expect in prompts (e.g., wood, concrete, glass)")]
        public string key;

        [Tooltip("The Material asset to apply when this key (or any alias) is requested.")]
        public Material material;

        [Tooltip("Optional alternate words: timber, oak, stone, granite, frosted glass, etc.")]
        public string[] aliases;
    }

    [Tooltip("Map prompt words → Material assets")]
    public List<Entry> entries = new List<Entry>();
}
