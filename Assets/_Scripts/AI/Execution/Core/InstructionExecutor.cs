/*
 * Script Summary:
 * ----------------
 * Central dispatcher for executing AI instructions at runtime.
 * Delegates actions (scale, move, rotate, material, replace, delete) to the appropriate Applier.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: InstructionExecutor – MonoBehaviour attached to scene object.
 * - Key Methods:
 *     • Execute(AIInstruction) – Switch-based routing of actions to appliers.
 * - Key Properties/Fields:
 *     • scaleApplier, moveApplier, rotateApplier – Transform-related.
 *     • materialApplier, replaceApplier, deleteApplier – Domain-specific.
 * - Dependencies/Interactions:
 *     • Works with ActionApplierBase subclasses (ScaleApplier, MoveApplier, etc.).
 *     • Implements IInstructionExecutor pattern.
 * - Special Considerations:
 *     • Null appliers log warnings instead of failing silently.
 *     • Unknown actions log errors.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var exec = gameObject.AddComponent<InstructionExecutor>();
 * exec.Execute(new AIInstruction { action = "move", ... });
 * ```
 */

using UnityEngine;
using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Execution
{
    [DisallowMultipleComponent]
    public sealed class InstructionExecutor : MonoBehaviour
    {
        [Header("Transform")]
        [SerializeField] private ScaleApplier scaleApplier;
        [SerializeField] private MoveApplier moveApplier;
        [SerializeField] private RotateApplier rotateApplier;

        [Header("Domain")]
        [SerializeField] private MaterialApplier materialApplier;
        [SerializeField] private ReplaceApplier replaceApplier;
        [SerializeField] private DeleteApplier deleteApplier;

        public void Execute(AIInstruction i)
        {
            if (i == null || string.IsNullOrEmpty(i.action))
            {
                Debug.LogWarning("[Exec] Missing instruction/action.");
                return;
            }

            switch (i.action)
            {
                case "scale":
                    if (scaleApplier != null) scaleApplier.Apply(i);
                    else Debug.LogWarning("[Exec] ScaleApplier is not assigned.");
                    break;

                case "move":
                    if (moveApplier != null) moveApplier.Apply(i);
                    else Debug.LogWarning("[Exec] MoveApplier is not assigned.");
                    break;

                case "rotate":
                    if (rotateApplier != null) rotateApplier.Apply(i);
                    else Debug.LogWarning("[Exec] RotateApplier is not assigned.");
                    break;

                case "material":
                    if (materialApplier != null) materialApplier.Apply(i);
                    else Debug.LogWarning("[Exec] MaterialApplier is not assigned.");
                    break;

                case "replace":
                    if (replaceApplier != null) replaceApplier.Apply(i);
                    else Debug.LogWarning("[Exec] ReplaceApplier is not assigned.");
                    break;

                case "delete":
                    if (deleteApplier != null) deleteApplier.Apply(i);
                    else Debug.LogWarning("[Exec] DeleteApplier is not assigned.");
                    break;

                default:
                    Debug.LogError($"[Exec] Unknown action: {i.action}");
                    break;
            }
        }
    }
}
