/*
 * Script Summary:
 * ----------------
 * Builds strict system/user prompt strings for the LLM, encoding RenovAite action rules
 * (units, rotation conventions, move delta vs absolute, selection semantics, and target UID requirements).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: PromptBuilder (static).
 * - Key Methods:
 *     • BuildStrictUserMessage(selectedUid, objectStructureJson, ragContextJson, userPrompt)
 *       - Renders a user message that: enforces meters & Euler degrees; requires either position_delta OR position;
 *         encodes delta-vs-absolute guidance for move/rotate; injects current object structure and optional RAG context;
 *         and mandates using the provided selectedUid or an ID from RAG context.
 *     • BuildStrictSystemMessage()
 *       - Renders a system message instructing the model to return exactly ONE tool call, include multi-change fields in one call,
 *         prefer deltas for relative phrasing, and include a 'selection' block for “similar” requests.
 * - Dependencies/Interactions:
 *     • Consumed by your OpenAI client before CreateChatCompletion.
 *     • Aligns with ToolSchemaProvider parameter schema and downstream executors (Move/Rotate/Scale/etc.).
 * - Special Considerations:
 *     • Keeps target.unique_id invariant (never changed by “apply to similar”); selection block augments scope only.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var sys = PromptBuilder.BuildStrictSystemMessage();
 * var usr = PromptBuilder.BuildStrictUserMessage(selectedUid, objectJson, ragJson, userPrompt);
 * var raw = await client.CreateChatCompletionAsync(sys, usr, ToolSchemaProvider.GetToolsSpecJson(), ct);
 * ```
 */

using RenovAite.AI.Instructions.Models;
using static UnityEngine.UIElements.UxmlAttributeDescription;
using Unity.VisualScripting;

namespace RenovAite.AI.OpenAI
{
    public static class PromptBuilder
    {
        public static string BuildStrictUserMessage(
            string selectedUid,
            string objectStructureJson,
            string ragContextJson,
            string userPrompt)
        {
            return
              "You are an AI building system. Return exactly ONE AIInstruction via the tool.\n" +
              "Rules:\n" +
              " - All lengths must be METERS (numeric only).\n" +
              " - All rotations are EULER DEGREES (numeric only).\n" +
              " - For 'move':\n" +
              "     • If phrasing is relative (e.g., 'by', 'up', 'down', 'left', 'right'): use DELTA as modification.position_delta {x,y,z} (add to current position).\n" +
              "     • If phrasing is absolute (e.g., 'move to', explicit coordinates): use ABSOLUTE as modification.position {x,y,z} (world position).\n" +
              "     • Choose exactly one of position_delta OR position (do not include both).\n" +
              " - For scale, return per-axis multiplier as {x,y,z}.\n" +
              " - For 'rotate':\n" +
              "     • If phrasing is relative(e.g., 'by', 'right/left', 'clockwise/counterclockwise', 'turn'): use DELTA as modification.rotation_delta { x,y,z}. \n" +
              "     • If phrasing is absolute(e.g., 'set yaw to 45°', 'rotate to 0,90,0'): use ABSOLUTE as modification.rotation { x,y,z}. \n" +
              "     • Choose exactly one of rotation_delta OR rotation(do not include both). \n" + 
              " - If changing width/height/depth, use numeric deltas in meters in modification.width/height/depth.\n" +
              " - Always return vectors as OBJECTS with x,y,z (NOT arrays).\n" +
              $" - You MUST set target.unique_id to either \"{selectedUid}\" (the selected object) OR one of the ids in 'Retrieved scene context (JSON)'. Do not invent IDs.\n\n" +
              "Current object structure:\n" + objectStructureJson + "\n" +
              (string.IsNullOrEmpty(ragContextJson) ? "" : $"Retrieved scene context (JSON):\n{ragContextJson}\n") +
              $"User request: {userPrompt}\n";
        }

        public static string BuildStrictSystemMessage() =>
            "You convert natural language building instructions into exactly one AIInstruction using the tool. " +
            "Do not write text; only call the tool. Do not use 'composite'. " +
            "When the user requests multiple changes (scale/rotate/move), include ALL fields in ONE tool call. " +
            "For 'move': prefer DELTA (modification.position_delta) for relative phrasing (by/up/down/left/right), " +
            "and ABSOLUTE (modification.position) for explicit target coordinates or 'move to'. Use exactly one. " +
            "CRITICAL: Set target.unique_id to the SELECTED object's ID I gave you. " +
            "If the user says 'all similar / apply to similar / all X', you MUST include a 'selection' block " +
            "with apply_to='similar' and numeric top_k and similarity_threshold. Never change target.unique_id.";
    }
}
