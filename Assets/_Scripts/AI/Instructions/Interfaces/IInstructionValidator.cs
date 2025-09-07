/*
 * Script Summary:
 * ----------------
 * Interface for implementing instruction validators.
 * Ensures AIInstruction objects meet validity rules before execution.
 *
 * Developer Notes:
 * ----------------
 * - Interface: IInstructionValidator.
 * - Key Methods:
 *     • ValidateOrThrow(AIInstruction instruction) – Throws exception if invalid.
 * - Dependencies/Interactions:
 *     • AIInstruction model.
 *     • Implemented by custom validator classes in the RenovAite pipeline.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * public class MyValidator : IInstructionValidator {
 *     public void ValidateOrThrow(AIInstruction i) {
 *         if (i == null || string.IsNullOrEmpty(i.action))
 *             throw new ArgumentException("Invalid instruction");
 *     }
 * }
 * ```
 */

using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Instructions
{
    public interface IInstructionValidator
    {
        void ValidateOrThrow(AIInstruction instruction);
    }
}
