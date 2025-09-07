/*
 * Script Summary:
 * ----------------
 * Handles deletion of target GameObjects in response to AI instructions.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: DeleteApplier – Executes "delete" actions.
 * - Key Methods:
 *     • Apply(AIInstruction) – Resolves target, unregisters UID (future), and destroys GameObject.
 *     • ResolveTarget(AIInstruction) – Resolves by unique_id via ObjectRegistry or by name.
 * - Dependencies/Interactions:
 *     • AIInstruction model, ObjectRegistry, UniqueIdExtractor.
 *     • Unity Undo system for editor-time deletion.
 * - Special Considerations:
 *     • Editor vs PlayMode destruction handled separately.
 *     • TODO: Remove object mapping from ObjectRegistry in Phase-3.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var instr = new AIInstruction {
 *     action = "delete",
 *     target = new AITarget { unique_id = "COLUMN_007" }
 * };
 * deleteApplier.Apply(instr);
 * ```
 */

using RenovAite.AI.Instructions.Models;
using UnityEngine;

namespace RenovAite.AI.Execution
{
    [CreateAssetMenu(menuName = "RenovAite/Appliers/DeleteApplier")]
    public sealed class DeleteApplier : ActionApplierBase
    {
        public override void Apply(AIInstruction i)
        {
            if (i == null || i.target == null) { Debug.LogWarning("[Exec][Strict] delete: missing target."); return; }
            var go = ResolveTarget(i);
            if (!go) return;

            var uid = UniqueIdExtractor.TryGetUniqueId(go);

            // TODO (Phase-3): ObjectRegistry: remove mapping for uid
            // ObjectRegistry._instance?.Unregister(uid);

#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.Undo.DestroyObjectImmediate(go);
            else Object.Destroy(go);
#else
            Object.Destroy(go);
#endif
            Debug.Log($"[Exec][Strict] DELETE: '{go.name}' (uid: {uid ?? "n/a"})");
        }

        private static GameObject ResolveTarget(AIInstruction i)
        {
            GameObject target = null;
            if (!string.IsNullOrEmpty(i.target?.unique_id))
                target = ObjectRegistry._instance.GetObjectById(i.target.unique_id);

            if (!target && !string.IsNullOrEmpty(i.target?.name))
                target = GameObject.Find(i.target.name);

            if (!target) Debug.LogError($"[Exec][Strict] delete: target not found (name='{i.target?.name}', uid='{i.target?.unique_id}')");
            return target;
        }
    }
}
