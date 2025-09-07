/*
 * Script Summary:
 * ----------------
 * Validation logic for AIInstruction objects before execution.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: AIInstructionValidator (static).
 * - Key Checks:
 *     • action must be non-empty.
 *     • target must include at least one of {name, unique_id}.
 *     • modification must not be null.
 *     • composite → requires valid sub-instructions.
 *     • scale → vector magnitude > 0.
 *     • replace → must include replace_with object.
 * - Dependencies/Interactions:
 *     • Consumed by InstructionDispatcher before execution.
 * - Special Considerations:
 *     • Returns error string if invalid, else true.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * if (!AIInstructionValidator.Validate(instr, out var err))
 *     Debug.LogError("Invalid instruction: " + err);
 * ```
 */

using System;
using System.Collections.Generic;
using UnityEngine;

public static class AIInstructionValidator
{
    public static bool Validate(AIInstruction instruction, out string error)
    {
        if (instruction == null)
        {
            error = "Instruction is null.";
            return false;
        }

        if (string.IsNullOrEmpty(instruction.action))
        {
            error = "Missing 'action' field.";
            return false;
        }

        // ✅ MODIFIED: Accept if either name or unique_id is present
        if (instruction.target == null ||
            (string.IsNullOrEmpty(instruction.target.name) && string.IsNullOrEmpty(instruction.target.unique_id)))
        {
            error = "Missing both 'target.name' and 'target.unique_id'. At least one must be provided.";
            return false;
        }

        if (instruction.modification == null)
        {
            error = "Missing 'modification' object.";
            return false;
        }

        // ✅ Existing logic preserved
        if (instruction.action == "composite")
        {
            if (instruction.modification.instructions == null || instruction.modification.instructions.Count == 0)
            {
                error = "Missing 'instructions' for composite action.";
                return false;
            }

            foreach (var inst in instruction.modification.instructions)
            {
                if (string.IsNullOrEmpty(inst.action) ||
                    string.IsNullOrEmpty(inst.object_type))
                {
                    error = "Each composite instruction must have 'action' and 'object_type'.";
                    return false;
                }
            }
        }

        if (instruction.action == "scale" && instruction.modification.scale.ToVector3().magnitude == 0f)
        {
            error = "Scale vector cannot be zero.";
            return false;
        }

        if (instruction.action == "replace" && instruction.modification.replace_with == null)
        {
            error = "Missing 'replace_with' object for replace action.";
            return false;
        }

        error = null;
        return true;
    }
}
