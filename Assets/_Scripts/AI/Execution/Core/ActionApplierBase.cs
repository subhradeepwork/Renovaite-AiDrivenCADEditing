/*
 * Script Summary:
 * ----------------
 * Abstract base class for all AI-driven "action appliers."
 * Defines a standard interface for applying modifications to GameObjects.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ActionApplierBase – Abstract ScriptableObject.
 * - Key Methods:
 *     • Apply(AIInstruction) – Must be implemented by subclasses (e.g., MoveApplier, RotateApplier).
 * - Dependencies/Interactions:
 *     • AIInstruction model from RenovAite.AI.Instructions.Models.
 *     • Used by InstructionExecutor to route actions.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * public sealed class CustomApplier : ActionApplierBase {
 *     public override void Apply(AIInstruction i) {
 *         // custom logic here
 *     }
 * }
 * ```
 */

using RenovAite.AI.Instructions.Models;
using UnityEngine;

namespace RenovAite.AI.Execution
{
    public abstract class ActionApplierBase : ScriptableObject
    {
        public abstract void Apply(AIInstruction instruction);
    }
}
