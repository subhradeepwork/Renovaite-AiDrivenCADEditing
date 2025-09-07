/*
 * Script Summary:
 * ----------------
 * Applies AI-driven rotations to a target GameObject.
 * Supports delta rotations (additive euler) and absolute world-space euler assignment.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: RotateApplier – Executes "rotate" actions from AIInstruction.
 * - Key Methods:
 *     • Apply(AIInstruction) – Validates instruction, resolves target, applies rotation_delta (preferred) or rotation.
 *     • ApplierVectorPicker.TryPick(...) – Extracts "rotation" / "rotation_delta" SerializableVector3 (snake or camelCase).
 *     • ResolveTarget(AIInstruction) – unique_id via ObjectRegistry; fallback by name (GameObject.Find).
 * - Key Properties/Fields:
 *     • allowDeltaRotation (bool) – If true, use rotation_delta (applies via Transform.Rotate in Self space).
 *     • allowAbsoluteRotation (bool) – If true, set absolute euler via `transform.rotation = Quaternion.Euler(...)`.
 * - Dependencies/Interactions:
 *     • AIInstruction / AIModification / SerializableVector3
 *     • ObjectRegistry, AIInstructionValidator, Unity Transform/Quaternion
 * - Special Considerations:
 *     • Delta uses `Space.Self` so it compounds with existing local orientation.
 *     • Absolute uses world-space Euler → Quaternion.
 *     • No-op when neither rotation nor rotation_delta present.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var applier = /* reference to RotateApplier asset * /;
 * var instr = new AIInstruction {
 *     action = "rotate",
 *     target = new AITarget { unique_id = "COLUMN_007" },
 *     modification = new AIModification {
 *         rotation_delta = new SerializableVector3 { x = 0f, y = 15f, z = 0f }
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
    [CreateAssetMenu(menuName = "RenovAite/Appliers/RotateApplier")]
    public sealed class RotateApplier : ActionApplierBase
    {
        [Header("Hybrid Rotate Support")]
        [Tooltip("If true, will apply modification.rotation_delta (if present) as an offset before checking absolute rotation.")]
        [SerializeField] private bool allowDeltaRotation = true;

        [Tooltip("If true, will apply modification.rotation (absolute world euler) when no delta is provided.")]
        [SerializeField] private bool allowAbsoluteRotation = true;

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

            // Generic picker: prefer rotation_delta when present, else rotation (absolute)
            if (ApplierVectorPicker.TryPick(mod, baseName: "rotation", deltaPreferred: allowDeltaRotation, out var euler, out var isDelta))
            {
                if (isDelta && allowDeltaRotation)
                {
                    // World-space delta rotation in degrees
                    target.transform.Rotate(euler, Space.Self);
                    Debug.Log($"[Exec][Strict] ROTATE (delta) → eulerΔ({euler.x:0.###},{euler.y:0.###},{euler.z:0.###}) on '{target.name}'");
                    return;
                }
                if (!isDelta && allowAbsoluteRotation)
                {
                    // Absolute world-space Euler
                    target.transform.rotation = Quaternion.Euler(euler);
                    Debug.Log($"[Exec][Strict] ROTATE (absolute) → euler({euler.x:0.###},{euler.y:0.###},{euler.z:0.###}) on '{target.name}'");
                    return;
                }
            }

            Debug.Log("[Exec][Strict] Nothing to apply for action='rotate' (no rotation/rotation_delta).");
        }

        // ---------- shared picker (generic for position/rotation/scale) ----------
        private static class ApplierVectorPicker
        {
            public static bool TryPick(AIModification mod, string baseName, bool deltaPreferred,
                                       out Vector3 value, out bool isDelta)
            {
                value = default; isDelta = false;
                if (mod == null) return false;

                var abs = TryGetVec(mod, baseName);                       // e.g., "rotation"
                var delta = TryGetVec(mod, baseName + "_delta")             // e.g., "rotation_delta"
                         ?? TryGetVec(mod, baseName + "Delta");             // also accept camelCase

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
                    return f.GetValue(obj) as SerializableVector3;

                // property
                var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p != null && p.CanRead && p.PropertyType == typeof(SerializableVector3))
                    return p.GetValue(obj) as SerializableVector3;

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
