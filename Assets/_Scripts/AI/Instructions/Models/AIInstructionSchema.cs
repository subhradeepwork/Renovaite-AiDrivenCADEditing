/*
 * Script Summary:
 * ----------------
 * Serializable DTO schema representing AI instructions and modifications.  
 * Defines target, modification, and supporting nested specs (Material, Replace, etc.).
 *
 * Developer Notes:
 * ----------------
 * - Classes:
 *     • AIInstruction { action, AITarget target, AIModification modification }
 *     • AITarget { name, unique_id }
 *     • AIModification – { scale, position, rotation, material, replace, deltas (pos/rot/scale), width/height/depth }
 *     • MaterialSpec – Full PBR material spec.
 *     • ReplaceSpec – Prefab replacement metadata.
 *     • AIReplacement / SubInstruction – Legacy/composite support.
 *     • SerializableVector3 – float x,y,z + ToVector3().
 *     • SerializableDimensions – {width, height}.
 * - Dependencies/Interactions:
 *     • Matches OpenAI ToolSchemaProvider and AIInstructionNormalizer.
 *     • Consumed by InstructionExecutor + appliers.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var instr = new AIInstruction {
 *     action = "move",
 *     target = new AITarget { unique_id = "WALL_123" },
 *     modification = new AIModification {
 *         position_delta = new SerializableVector3 { x=1, y=0, z=0 }
 *     }
 * };
 * ```
 */

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AIInstruction
{
    public string action;
    public AITarget target;
    public AIModification modification;
}

[Serializable]
public class AITarget
{
    public string name;
    public string unique_id;
}

[Serializable]
public class MaterialSpec
{
    public string materialName;
    public string hex;
    public float metallic;
    public float smoothness;
}

[Serializable]
public class ReplaceSpec
{
    public string prefabPath;     // Editor
    public string resourcesPath;  // Runtime
    public bool keepParent = true;
    public bool keepTransform = true;
    public bool keepLayerTag = true;
}
[Serializable]
public class AIModification
{
    public SerializableVector3 scale;
    public SerializableVector3 position;
    public SerializableVector3 rotation;
    public string material;

    public SerializableVector3 position_delta; // meters to add to current world position
    public SerializableVector3 rotation_delta;
    public SerializableVector3 scale_delta;


    [Obsolete("Use 'mat' (MaterialSpec) or 'material' (string).")]
    public string materialString;

    [Obsolete("Use 'replace' (ReplaceSpec).")]
    public AIReplacement replace_with;

    [Obsolete("Not used by the strict tool path.")]
    public List<SubInstruction> instructions;


    public MaterialSpec mat;
    public ReplaceSpec replace;

    // NEW (optional): Some models return dimension deltas in meters
    // e.g. { "action":"modify", "modification":{"width":2} }
    public float width;   // +/− meters applied to the larger horizontal axis (X or Z)
    public float height;  // +/− meters applied to Y
    public float depth;   // +/− meters applied to the smaller horizontal axis (X or Z)
}

[Serializable]
public class AIReplacement
{
    public string type;
    public string material;
    public SerializableVector3 scale;
}

[Serializable]
public class SubInstruction
{
    public string action;
    public string object_type;
    public string shape;
    public SerializableDimensions dimensions;
    public string position;
    public string material;
}

[Serializable]
public class SerializableVector3
{
    public float x;
    public float y;
    public float z;

    public Vector3 ToVector3()
    {
        return new Vector3(x, y, z);
    }
}

[Serializable]
public class SerializableDimensions
{
    public float width;
    public float height;
}
