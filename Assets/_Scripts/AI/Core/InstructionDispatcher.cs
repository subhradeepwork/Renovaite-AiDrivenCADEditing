/*
 * Script Summary:
 * ----------------
 * Orchestrates the end-to-end flow of an AIInstruction: validate → normalize → execute.
 * Central pipeline node that decouples policy (validator/normalizer) from effects (executor).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: InstructionDispatcher – Lightweight façade coordinating three collaborators.
 * - Constructor:
 *     • InstructionDispatcher(IInstructionValidator v, IInstructionNormalizer n, IInstructionExecutor e)
 *       - Inject concrete strategies for validation, normalization, and execution.
 * - Key Methods:
 *     • Dispatch(AIInstruction raw)
 *       - Calls _validator.ValidateOrThrow(raw) to enforce schema/semantics.
 *       - Passes to _normalizer.Normalize(raw) to canonicalize action/fields.
 *       - Forwards normalized instruction to _executor.Execute(norm).
 * - Dependencies/Interactions:
 *     • IInstructionValidator – e.g., checks action present, target resolvable, field ranges, etc.
 *     • IInstructionNormalizer – e.g., lowercases action, maps synonyms, fills *_delta vs absolute fields.
 *     • IInstructionExecutor – routes to specific ActionAppliers (Move/Rotate/Scale/Material/Replace/Delete).
 * - Special Considerations:
 *     • Keep dispatch pure (no side-effects) besides calling collaborators; easy to unit test by mocking interfaces.
 *     • Swap any stage independently (e.g., stricter validator or different normalization policy) without touching others.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // Wire up once (e.g., in a bootstrapper/DI container)
 * var dispatcher = new InstructionDispatcher(
 *     validator: new StrictInstructionValidator(),
 *     normalizer: new DefaultInstructionNormalizer(),
 *     executor:   FindObjectOfType<InstructionExecutor>()  // scene component
 * );
 *
 * // Later, when a tool-call payload arrives:
 * dispatcher.Dispatch(new AIInstruction {
 *     action = "move",
 *     target = new ActionTarget { unique_id = "WALL_00123" },
 *     modification = new Modification {
 *         position_delta = new Vector3DTO { x = 0.5f, y = 0f, z = 0f }
 *     }
 * });
 * ```
 */

using RenovAite.AI.Execution;
using RenovAite.AI.Instructions;
using RenovAite.AI.Instructions.Models;

namespace RenovAite.AI.Orchestration
{
    public sealed class InstructionDispatcher
    {
        private readonly IInstructionValidator _validator;
        private readonly IInstructionNormalizer _normalizer;
        private readonly IInstructionExecutor _executor;

        public InstructionDispatcher(IInstructionValidator v, IInstructionNormalizer n, IInstructionExecutor e)
        {
            _validator = v; _normalizer = n; _executor = e;
        }

        public void Dispatch(AIInstruction raw)
        {
            _validator.ValidateOrThrow(raw);
            var norm = _normalizer.Normalize(raw);
            _executor.Execute(norm);
        }
    }
}
