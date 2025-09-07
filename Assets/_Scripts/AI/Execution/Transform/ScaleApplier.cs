/*
 * Script Summary:
 * ----------------
 * Applies AI-driven scaling to a target GameObject.
 * Supports dimension deltas in meters (width/height/depth) → relative multipliers using world bounds, and direct multiplicative scale vectors.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ScaleApplier – Executes "scale" actions from AIInstruction.
 * - Key Methods:
 *     • Apply(AIInstruction) – Validates instruction, resolves target, applies Δ→scale and/or direct scale vector.
 *     • ApplyDimensionDeltasAsScale(GameObject, AIModification) – Converts meters to relative factors using combined world bounds.
 *     • GetCombinedWorldBounds(GameObject) – Aggregates bounds from enabled Renderers (fallback: MeshFilter + lossyScale).
 *     • ApplyScale(GameObject, SerializableVector3) – Multiplies localScale component-wise.
 *     • ResolveTarget(AIInstruction) – unique_id via ObjectRegistry; fallback by name.
 * - Key Properties/Fields:
 *     • useDefaultAxisMapping (bool) – Defaults to width→X, height→Y, depth→Z for Δ→scale mapping.
 * - Dependencies/Interactions:
 *     • AIInstruction / AIModification / SerializableVector3
 *     • ObjectRegistry, AIInstructionValidator, Unity Renderers/MeshFilter/Transform
 * - Special Considerations:
 *     • Δ→scale clamps each axis to [0.01, 10] to prevent invalid/degenerate scales.
 *     • Skips zero-size/disabled renderers to stabilize bounds.
 *     • `mod.scale` must be positive per-axis; unit vector (1,1,1) is treated as no-op.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var applier = /* reference to ScaleApplier asset * /;
 * var instr = new AIInstruction {
 *     action = "scale",
 *     target = new AITarget { unique_id = "BEAM_003" },
 *     modification = new AIModification {
 *         width = 0.2f,   // +20cm along world X relative to current size
 *         height = 0.0f,
 *         depth = -0.1f,  // -10cm along world Z
 *         scale = new SerializableVector3 { x = 1f, y = 1.1f, z = 1f } // optional extra multiplicative scale
 *     }
 * };
 * applier.Apply(instr);
 * ```
 */
using RenovAite.AI.Instructions.Models;
using UnityEngine;
using System.Linq;

namespace RenovAite.AI.Execution
{
    [CreateAssetMenu(menuName = "RenovAite/Appliers/ScaleApplier")]
    public sealed class ScaleApplier : ActionApplierBase
    {
        [Header("Delta→Scale mapping (meters→multipliers)")]
        [Tooltip("Width→X, Height→Y, Depth→Z. Keep default unless your project uses a different axis convention.")]
        public bool useDefaultAxisMapping = true; // reserved for future custom mappings

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

            bool hasAnyDelta = HasAnyDelta(mod);
            bool hasScaleVector = mod.scale != null && !IsUnit(mod.scale) && IsValidScale(mod.scale);

            bool didSomething = false;

            // 1) width/height/depth (meters) → multiplicative scale along X/Y/Z
            if (hasAnyDelta)
            {
                ApplyDimensionDeltasAsScale(target, mod);
                didSomething = true;
            }

            // 2) multiplicative scale vector
            if (hasScaleVector)
            {
                ApplyScale(target, mod.scale);
                Debug.Log($"[Exec][Strict] SCALE ×= ({mod.scale.x:0.###},{mod.scale.y:0.###},{mod.scale.z:0.###}) on '{target.name}'");
                didSomething = true;
            }

            if (!didSomething)
            {
                Debug.Log("[Exec][Strict] Nothing to apply for action='scale' (no deltas/scale provided).");
            }
        }

        // ---------- helpers ----------

        private static bool HasAnyDelta(AIModification m)
        {
            return m != null &&
                   (Mathf.Abs(m.width) > 1e-6f ||
                    Mathf.Abs(m.height) > 1e-6f ||
                    Mathf.Abs(m.depth) > 1e-6f);
        }

        private static bool IsUnit(SerializableVector3 v)
        {
            return v != null &&
                   Mathf.Abs(v.x - 1f) < 1e-6f &&
                   Mathf.Abs(v.y - 1f) < 1e-6f &&
                   Mathf.Abs(v.z - 1f) < 1e-6f;
        }

        private static bool IsValidScale(SerializableVector3 v)
        {
            return v.x > 0f && v.y > 0f && v.z > 0f;
        }

        // Convert width/height/depth deltas (meters) into multiplicative scale along X/Y/Z using world bounds.
        private void ApplyDimensionDeltasAsScale(GameObject target, AIModification mod)
        {
            var b = GetCombinedWorldBounds(target);
            if (b.size == Vector3.zero)
            {
                Debug.LogWarning("[Exec][Strict] No bounds found for delta → scale conversion.");
                return;
            }

            // Mapping: width→X, height→Y, depth→Z (default)
            float w = Mathf.Max(b.size.x, 1e-6f);
            float h = Mathf.Max(b.size.y, 1e-6f);
            float d = Mathf.Max(b.size.z, 1e-6f);

            float sx = 1f + (mod.width / w);
            float sy = 1f + (mod.height / h);
            float sz = 1f + (mod.depth / d);

            // keep components with no delta at 1
            if (Mathf.Abs(mod.width) < 1e-6f) sx = 1f;
            if (Mathf.Abs(mod.height) < 1e-6f) sy = 1f;
            if (Mathf.Abs(mod.depth) < 1e-6f) sz = 1f;

            // sanity clamp (avoid negative or extreme scales)
            sx = Mathf.Clamp(sx, 0.01f, 10f);
            sy = Mathf.Clamp(sy, 0.01f, 10f);
            sz = Mathf.Clamp(sz, 0.01f, 10f);

            var rel = new SerializableVector3 { x = sx, y = sy, z = sz };
            Debug.Log($"[Exec][Strict] Δ→scale using world bounds {b.size:0.###}: +W={mod.width:0.###}m +H={mod.height:0.###}m +D={mod.depth:0.###}m ⇒ ×({sx:0.###},{sy:0.###},{sz:0.###}) on '{target.name}'");
            ApplyScale(target, rel);
        }

        // Combines bounds from all renderers in the hierarchy for a stable world-space size
        private static Bounds GetCombinedWorldBounds(GameObject go)
        {
            var rends = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (rends != null && rends.Length > 0)
            {
                var first = rends.FirstOrDefault(r => r.enabled && r.bounds.size != Vector3.zero) ?? rends[0];
                var b = first.bounds;
                foreach (var r in rends)
                {
                    // Skip disabled or zero-size renderers to reduce noise
                    if (!r.enabled) continue;
                    var rb = r.bounds;
                    if (rb.size == Vector3.zero) continue;
                    b.Encapsulate(rb);
                }
                return b;
            }

            // Fallback: try MeshFilter on the object itself
            var mf = go.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var b = mf.sharedMesh.bounds;     // local
                var ls = go.transform.lossyScale; // world-ish approximation
                var size = Vector3.Scale(b.size, ls);
                return new Bounds(go.transform.position, size);
            }

            return new Bounds(go.transform.position, Vector3.zero);
        }

        private static void ApplyScale(GameObject target, SerializableVector3 scale)
        {
            // multiplicative relative scale (local)
            target.transform.localScale = Vector3.Scale(target.transform.localScale, scale.ToVector3());
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
