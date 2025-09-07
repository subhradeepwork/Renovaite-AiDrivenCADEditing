/*
 * Script Summary:
 * ----------------
 * Applies AI-driven position changes to a target GameObject.
 * Supports both absolute world-space positioning and delta-based offsets, preferring deltas when enabled.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MoveApplier – Executes "move" actions from AIInstruction.
 * - Key Methods:
 *     • Apply(AIInstruction) – Validates instruction, resolves target, and applies position (absolute) or position_delta (offset).
 *     • ApplierVectorPicker.TryPick(...) – Generic extractor that reads SerializableVector3 from AIModification
 *       for "<base>" and "<base>_delta" (also supports camelCase twins), returning Vector3 + isDelta flag.
 *     • ResolveTarget(AIInstruction) – Finds target by target.unique_id via ObjectRegistry, else by target.name via GameObject.Find.
 * - Key Properties/Fields:
 *     • allowDeltaPosition (bool) – If true, apply modification.position_delta before absolute.
 *     • allowAbsolutePosition (bool) – If true, apply modification.position when delta not used/found.
 * - Dependencies/Interactions:
 *     • AIInstruction / AIModification / SerializableVector3 models.
 *     • ObjectRegistry for unique_id → GameObject resolution.
 *     • AIInstructionValidator for preflight validation.
 *     • UnityEngine.Transform for world-space position changes.
 * - Special Considerations:
 *     • World-space delta is applied via `transform.position += v`.
 *     • Absolute position is set via `transform.position = v`.
 *     • Gracefully no-ops if neither position nor position_delta is present.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // ScriptableObject usage: assign this asset in your execution pipeline
 * // and call Apply(...) with a valid AIInstruction.
 * var applier = /* reference to MoveApplier asset * /;
 * var instr = new AIInstruction {
 *     action = "move",
 *     target = new AITarget { unique_id = "WALL_001" },
 *     modification = new AIModification {
 *         position_delta = new SerializableVector3 { x = 0.5f, y = 0f, z = 0f }
 *     }
 * };
 * applier.Apply(instr);
 * ```
 */
using System.Reflection;
using RenovAite.AI.Instructions.Models;
using UnityEngine;

namespace RenovAite.AI.Execution
{
    [CreateAssetMenu(menuName = "RenovAite/Appliers/MoveApplier")]
    public sealed class MoveApplier : ActionApplierBase
    {
        [Header("Hybrid Move Support")]
        [Tooltip("If true, will apply modification.position_delta (if present) as an offset before checking absolute position.")]
        [SerializeField] private bool allowDeltaPosition = true;

        [Tooltip("If true, will apply modification.position (absolute world position) when no delta is provided.")]
        [SerializeField] private bool allowAbsolutePosition = true;

        public override void Apply(AIInstruction i)
        {
            if (!AIInstructionValidator.Validate(i, out var error))
            {
                Debug.LogError($"Invalid instruction: {error}");
                return;
            }

            var target = ResolveTarget(i);
            if (target == null) return;

            var mod = i.modification ?? new AIModification();

            // Use a generic picker so we don't hardcode field names beyond "position" and "position_delta"
            if (ApplierVectorPicker.TryPick(mod, baseName: "position", deltaPreferred: allowDeltaPosition, out var v, out var isDelta))
            {
                if (isDelta && allowDeltaPosition)
                {
                    target.transform.position += v;   // world delta
                    Debug.Log($"[Exec][Strict] MOVE (delta) → ({v.x:0.###}, {v.y:0.###}, {v.z:0.###}) on '{target.name}'");
                    return;
                }
                if (!isDelta && allowAbsolutePosition)
                {
                    target.transform.position = v;    // world absolute
                    Debug.Log($"[Exec][Strict] MOVE (absolute) → ({v.x:0.###}, {v.y:0.###}, {v.z:0.###}) on '{target.name}'");
                    return;
                }
            }

            Debug.Log("[Exec][Strict] Nothing to apply for action='move' (no position/position_delta).");
        }

        // ---------- shared picker (generic for position/rotation/scale) ----------
        private static class ApplierVectorPicker
        {
            public static bool TryPick(AIModification mod, string baseName, bool deltaPreferred,
                                       out Vector3 value, out bool isDelta)
            {
                value = default; isDelta = false;
                if (mod == null) return false;

                // Try to fetch SerializableVector3 from fields or properties:
                // supports snake_case and camelCase twins.
                var abs = TryGetVec(mod, baseName);                 // e.g., "position"
                var delta = TryGetVec(mod, baseName + "_delta") ??
                            TryGetVec(mod, baseName + "Delta");       // also accept camelCase

                if (deltaPreferred && IsNonZero(delta)) { value = ToVec(delta); isDelta = true; return true; }
                if (IsNonZero(abs)) { value = ToVec(abs); isDelta = false; return true; }
                if (!deltaPreferred && IsNonZero(delta)) { value = ToVec(delta); isDelta = true; return true; }
                return false;
            }

            private static SerializableVector3 TryGetVec(object obj, string name)
            {
                if (obj == null) return null;
                var t = obj.GetType();

                // field
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null && f.FieldType == typeof(SerializableVector3))
                {
                    return f.GetValue(obj) as SerializableVector3;
                }

                // property
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanRead && p.PropertyType == typeof(SerializableVector3))
                {
                    return p.GetValue(obj) as SerializableVector3;
                }

                return null;
            }

            private static bool IsNonZero(SerializableVector3 v) =>
                v != null && (Mathf.Abs(v.x) > 1e-6f || Mathf.Abs(v.y) > 1e-6f || Mathf.Abs(v.z) > 1e-6f);

            private static Vector3 ToVec(SerializableVector3 v) => new Vector3(v.x, v.y, v.z);
        }

        private static GameObject ResolveTarget(AIInstruction instruction)
        {
            GameObject target = null;

            if (!string.IsNullOrEmpty(instruction.target?.unique_id))
            {
                target = ObjectRegistry._instance.GetObjectById(instruction.target.unique_id);
                if (target != null)
                    Debug.Log($"✅ Found GameObject via Unique ID: {instruction.target.unique_id}");
            }

            if (target == null && !string.IsNullOrEmpty(instruction.target?.name))
            {
                target = GameObject.Find(instruction.target.name);
            }

            if (target == null)
            {
                Debug.LogError($"❌ Target GameObject not found. Name: '{instruction.target?.name}', Unique ID: '{instruction.target?.unique_id}'");
            }
            return target;
        }
    }
}
