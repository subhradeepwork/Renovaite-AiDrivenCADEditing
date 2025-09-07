/*
 * Script Summary:
 * ----------------
 * Applies materials from a MaterialLibrary to a target GameObject and its children.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MaterialApplier – Executes "material" actions.
 * - Key Methods:
 *     • Apply(AIInstruction) – Resolves target, resolves material by key, assigns to renderers.
 * - Key Properties/Fields:
 *     • materialLibrary – Library asset containing available materials.
 *     • applyToAllSubmeshes (bool) – Whether to apply to all submeshes or only first.
 *     • clearPropertyBlocks (bool) – Reset renderer property blocks before applying.
 *     • verbose (bool) – Detailed logging for debugging.
 * - Dependencies/Interactions:
 *     • MaterialLibrary, MaterialResolver.
 *     • ObjectRegistry for UID resolution.
 *     • Unity Renderer.sharedMaterials assignment.
 * - Special Considerations:
 *     • Can auto-load library from Resources/Config/MaterialLibrary.
 *     • Marks scene dirty in Editor when changing materials.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var instr = new AIInstruction {
 *     action = "material",
 *     target = new AITarget { unique_id = "WALL_001" },
 *     modification = new AIModification { material = "Concrete" }
 * };
 * materialApplier.Apply(instr);
 * ```
 */

using UnityEngine;
using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Execution
{
    [CreateAssetMenu(menuName = "RenovAite/Appliers/MaterialApplier (Library-Only Minimal)")]
    public sealed class MaterialApplier : ActionApplierBase
    {
        [Header("Library (required)")]
        public MaterialLibrary materialLibrary;
        public bool autoLoadLibraryFromResources = true;

        [Header("Apply options")]
        public bool applyToAllSubmeshes = true;
        public bool clearPropertyBlocks = true;
        public bool verbose = true;

        public override void Apply(AIInstruction i)
        {
            if (i?.target == null) { Debug.LogWarning("[MaterialApplier] Missing target."); return; }

            // Resolve target GameObject (uid -> registry; else name)
            GameObject go = null;
            var uid = i.target.unique_id;
            var name = i.target.name;

            try { if (!string.IsNullOrEmpty(uid)) go = ObjectRegistry._instance.GetObjectById(uid); } catch { }
            if (!go && !string.IsNullOrEmpty(name)) go = GameObject.Find(name);
            if (!go) { Debug.LogWarning($"[MaterialApplier] Target not found (name='{name}', uid='{uid}')."); return; }

            // Make sure we have a library
            if (materialLibrary == null && autoLoadLibraryFromResources)
                materialLibrary = Resources.Load<MaterialLibrary>("Config/MaterialLibrary");
            if (materialLibrary == null) { Debug.LogWarning("[MaterialApplier] MaterialLibrary not assigned."); return; }

            // Resolve the requested material name from the instruction
            var mod = i.modification;
            string key = mod?.material;
            if ((key == null || key.Length == 0) && mod?.mat != null)
                key = mod.mat.materialName;

            if (string.IsNullOrEmpty(key)) { Debug.LogWarning("[MaterialApplier] No material name in instruction."); return; }

            var res = MaterialResolver.Resolve(materialLibrary, key);
            var mat = res.material;
            if (mat == null) { Debug.LogWarning($"[MaterialApplier] Library could not resolve '{key}'."); return; }

            // Apply to all child renderers
            var rends = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in rends)
            {
                if (!r) continue;

                if (verbose)
                    Debug.Log($"[MaterialApplier] BEFORE '{r.gameObject.name}': {Names(r.sharedMaterials)}");

                if (clearPropertyBlocks) r.SetPropertyBlock(null);

                var shared = r.sharedMaterials;
                int n = Mathf.Max(1, shared?.Length ?? 0);
                var arr = new Material[n];
                if (applyToAllSubmeshes)
                {
                    for (int s = 0; s < n; s++) arr[s] = mat;
                }
                else
                {
                    for (int s = 0; s < n; s++)
                        arr[s] = (s == 0 ? mat : (shared != null && s < shared.Length ? shared[s] : mat));
                }
                r.sharedMaterials = arr;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.EditorUtility.SetDirty(r);
                    UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
                }
#endif
                if (verbose)
                    Debug.Log($"[MaterialApplier] AFTER  '{r.gameObject.name}': {Names(r.sharedMaterials)}");
            }

            if (verbose)
                Debug.Log($"[MaterialApplier] Applied '{mat.name}' to '{go.name}' (allSubmeshes={applyToAllSubmeshes}).");
        }

        // ---- small helpers ----
        static string Names(Material[] arr)
        {
            if (arr == null || arr.Length == 0) return "(empty)";
            var s = new System.Text.StringBuilder();
            for (int i = 0; i < arr.Length; i++)
            {
                if (i > 0) s.Append(", ");
                s.Append(arr[i] ? arr[i].name : "null");
            }
            return s.ToString();
        }
    }
}
