/*
 * Script Summary:
 * ----------------
 * Canonical data contract for AI-driven actions in RenovAite.
 * Encapsulates the action verb, target resolution hints, and per-action parameters (modification DTOs).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: AIInstruction – One instruction = one action to perform.
 * - Key Fields:
 *     • action (string) – "scale" | "move" | "rotate" | "material" | "replace" | "delete".
 *     • target (ActionTarget) – Resolution hints: unique_id and/or name.
 *     • modification (Modification) – Action parameters (vectors, material, replace, delete flag).
 *     • schemaVersion (string) – Optional version tag for forward/backward compatibility.
 * - Target DTO:
 *     • ActionTarget: { unique_id, name } – Prefer unique_id; name is a fallback.
 * - Modification DTO:
 *     • scale (Vector3DTO) – Multiplicative scale vector (x,y,z).
 *     • position (Vector3DTO) – Absolute world position.
 *     • rotation (RotationDTO) – Euler degrees (x,y,z), already normalized for executors.
 *     • material (MaterialDTO) – Library/material key + optional PBR params (hex, metallic, smoothness).
 *     • replace (ReplaceDTO) – Prefab/library references (e.g., prefabPath or libraryId).
 *     • delete (bool?) – Optional flag for delete actions.
 * - DTO Shapes:
 *     • Vector3DTO / RotationDTO: { float x, y, z }.
 *     • MaterialDTO: { string materialName, string hex, float? metallic, float? smoothness }.
 *     • ReplaceDTO: { string libraryId, string prefabPath }.
 * - Dependencies/Interactions:
 *     • Consumed by InstructionExecutor and ActionApplierBase subclasses (Scale/Move/Rotate/Material/Replace/Delete).
 *     • Produced by LLM/tool-calling pipeline; validators/normalizers may pre-process before execution.
 * - Special Considerations:
 *     • Keep action values lowercase and consistent with executors.
 *     • Prefer unique_id in target to avoid ambiguous name lookups.
 *     • Extend Modification cautiously to preserve backward compatibility (use schemaVersion when adding fields).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var instr = new AIInstruction {
 *     action = "material",
 *     target = new ActionTarget { unique_id = "WALL_00123" },
 *     modification = new Modification {
 *         material = new MaterialDTO { materialName = "Concrete_Polished", metallic = 0.2f, smoothness = 0.7f }
 *     },
 *     schemaVersion = "1.0"
 * };
 * // executor.Execute(instr);
 * ```
 */
using System;
using System.Collections.Generic;
using UnityEngine;

namespace RenovAite.AI.Instructions.Models
{
    [Serializable]
    public sealed class AIInstruction
    {
        public string action;                    // "scale" | "move" | "rotate" | "material" | "replace" | "delete"
        public ActionTarget target;              // single target (kept for back-compat)
        public List<ActionTarget> targets;       // NEW: explicit multi-targets
        public Selection selection;              // NEW: selector that expands to many
        public Modification modification;        // params for the action
        public string schemaVersion;             // optional version tag
    }

    [Serializable] public sealed class ActionTarget { public string unique_id; public string name; }

    [Serializable]
    public sealed class Selection
    {
        // matches your tool schema in the logs
        public string apply_to;                  // "selected" | "similar" | "ids"
        public List<string> ids;                 // explicit list (when apply_to="ids")
        public int? top_k;                       // for "similar"
        public float? similarity_threshold;      // for "similar"
        public SelectionFilters filters;         // optional attribute filtering
    }

    [Serializable]
    public sealed class SelectionFilters
    {
        public string ifc_type;                  // e.g., "Window"
        public List<string> tags;                // e.g., ["has_cutout","Door"]
    }

    [Serializable]
    public sealed class Modification
    {
        public Vector3DTO scale;
        public Vector3DTO position;
        public RotationDTO rotation;

        // keep optional extras (don’t break existing executors if they ignore them)
        public Vector3DTO position_delta;
        public RotationDTO rotation_delta;
        public float? width;
        public float? height;
        public float? depth;

        public MaterialDTO material;
        public ReplaceDTO replace;
        public bool? delete;
    }

    [Serializable] public sealed class Vector3DTO { public float x; public float y; public float z; }
    [Serializable] public sealed class RotationDTO { public float x; public float y; public float z; }
    [Serializable] public sealed class MaterialDTO { public string materialName; public string hex; public float? metallic; public float? smoothness; }
    [Serializable] public sealed class ReplaceDTO { public string libraryId; public string prefabPath; }
}
