/*
 * Script Summary:
 * ----------------
 * Handles replacing a target GameObject with a prefab, maintaining transform, parent, layer/tag, and unique ID.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ReplaceApplier – Executes "replace" actions.
 * - Key Methods:
 *     • Apply(AIInstruction) – Replaces target with prefab and preserves metadata if requested.
 *     • ResolveTarget(AIInstruction) – Resolves GameObject via UID or name.
 *     • LoadPrefab(assetPath, resourcesPath) – Loads prefab from AssetDatabase or Resources.
 *     • EnsureIfcUniqueId(GameObject, string) – Ensures IFC Unique ID is propagated to new object.
 * - Dependencies/Interactions:
 *     • ObjectRegistry, ifcProperties component.
 *     • Unity Editor’s AssetDatabase (editor-only).
 * - Special Considerations:
 *     • Supports selective retention: keepParent, keepTransform, keepLayerTag.
 *     • UID carried over into IFC metadata.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var instr = new AIInstruction {
 *     action = "replace",
 *     target = new AITarget { unique_id = "DOOR_123" },
 *     modification = new AIModification {
 *         replace = new ReplaceSpec {
 *             prefabPath = "Assets/Prefabs/NewDoor.prefab",
 *             keepTransform = true,
 *             keepParent = true
 *         }
 *     }
 * };
 * replaceApplier.Apply(instr);
 * ```
 */

using RenovAite.AI.Instructions.Models;
using UnityEngine;

namespace RenovAite.AI.Execution
{
    [CreateAssetMenu(menuName = "RenovAite/Appliers/ReplaceApplier")]
    public sealed class ReplaceApplier : ActionApplierBase
    {
        public override void Apply(AIInstruction i)
        {
            if (i == null || i.target == null) { Debug.LogWarning("[Exec][Strict] replace: missing target."); return; }
            var src = ResolveTarget(i); if (!src) return;

            var spec = i.modification?.replace;
            if (spec == null)
            {
                Debug.LogWarning("[Exec][Strict] replace: no 'replace' block."); return;
            }

            var prefab = LoadPrefab(spec.prefabPath, spec.resourcesPath);
            if (prefab == null)
            {
                Debug.LogError($"[Exec][Strict] replace: prefab not found. prefabPath='{spec.prefabPath}', resourcesPath='{spec.resourcesPath}'");
                return;
            }

            var parent = src.transform.parent;
            var pos = src.transform.position;
            var rot = src.transform.rotation;
            var scale = src.transform.localScale;
            var layer = src.layer;
            var tag = src.tag;
            var name = src.name;
            var uid = UniqueIdExtractor.TryGetUniqueId(src);

            var dst = Object.Instantiate(prefab);
            dst.name = name;

            if (spec.keepParent) dst.transform.SetParent(parent, true);
            if (spec.keepTransform) { dst.transform.position = pos; dst.transform.rotation = rot; dst.transform.localScale = scale; }
            if (spec.keepLayerTag) { dst.layer = layer; dst.tag = tag; }

            if (!string.IsNullOrEmpty(uid)) EnsureIfcUniqueId(dst, uid);

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.DestroyObjectImmediate(src);
            else Object.Destroy(src);
#else
            Object.Destroy(src);
#endif
            Debug.Log($"[Exec][Strict] REPLACE: '{name}' → '{prefab.name}' (uid:{uid ?? "n/a"})");
        }

        private static GameObject ResolveTarget(AIInstruction i)
        {
            GameObject target = null;
            if (!string.IsNullOrEmpty(i.target?.unique_id))
                target = ObjectRegistry._instance.GetObjectById(i.target.unique_id);
            if (!target && !string.IsNullOrEmpty(i.target?.name))
                target = GameObject.Find(i.target.name);
            if (!target) Debug.LogError($"[Exec][Strict] replace: target not found (name='{i.target?.name}', uid='{i.target?.unique_id}')");
            return target;
        }

        private static GameObject LoadPrefab(string assetPath, string resourcesPath)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(assetPath))
            {
                var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (p != null) return p;
            }
#endif
            if (!string.IsNullOrEmpty(resourcesPath))
            {
                var p = Resources.Load<GameObject>(resourcesPath);
                if (p != null) return p;
            }
            return null;
        }

        private static void EnsureIfcUniqueId(GameObject go, string uid)
        {
            var p = go.GetComponent<ifcProperties>();
            if (p == null) p = go.AddComponent<ifcProperties>();
            if (p.properties == null) p.properties = new System.Collections.Generic.List<string>();
            if (p.nominalValues == null) p.nominalValues = new System.Collections.Generic.List<string>();

            int idx = -1;
            for (int i = 0; i < Mathf.Min(p.properties.Count, p.nominalValues.Count); i++)
            {
                var k = p.properties[i] ?? "";
                if (k.ToLower().Contains("unique id")) { idx = i; break; }
            }
            if (idx >= 0) p.nominalValues[idx] = uid;
            else { p.properties.Add("Unique ID"); p.nominalValues.Add(uid); }
        }
    }
}
