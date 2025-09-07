/*
 * Script Summary:
 * ----------------
 * Interface for normalizing AIInstruction objects.
 * Used to transform or sanitize instructions into a consistent form.
 *
 * Developer Notes:
 * ----------------
 * - Interface: IInstructionNormalizer.
 * - Key Methods:
 *     • Normalize(AIInstruction instruction) – Returns a cleaned/normalized AIInstruction.
 * - Dependencies/Interactions:
 *     • AIInstruction model.
 *     • Implemented by normalizers to standardize data for appliers/executors.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * public class DefaultNormalizer : IInstructionNormalizer {
 *     public AIInstruction Normalize(AIInstruction i) {
 *         // Example: ensure action string is lowercase
 *         i.action = i.action?.ToLowerInvariant();
 *         return i;
 *     }
 * }
 * ```
 */

using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Instructions
{
    public interface IInstructionNormalizer
    {
        AIInstruction Normalize(AIInstruction instruction);
    }
}