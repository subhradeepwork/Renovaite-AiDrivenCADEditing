/*
 * Script Summary:
 * ----------------
 * Extracts the raw JSON arguments of the FIRST tool call from an OpenAI chat completion response.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ResponseParser (static).
 * - Key Methods:
 *     • TryExtractToolArguments(string rawResponse) : string
 *       - Parses JSON and returns choices[0].message.tool_calls[0].function.arguments (or null if not present).
 * - Dependencies/Interactions:
 *     • SimpleJSON for lightweight traversal.
 *     • Consumed by your pipeline to obtain the AIInstruction JSON payload to deserialize/normalize/execute.
 * - Special Considerations:
 *     • Returns the first tool call only — upstream prompting ensures exactly one call.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var argsJson = ResponseParser.TryExtractToolArguments(raw);
 * if (!string.IsNullOrEmpty(argsJson)) {
 *     var instr = JsonUtility.FromJson<AIInstruction>(argsJson);
 *     executor.Execute(instr);
 * }
 * ```
 */

using RenovAite.AI.Instructions.Models;
using SimpleJSON;

namespace RenovAite.AI.OpenAI
{

    public static class ResponseParser
    {
        // Returns the first tool_call.function.arguments block (raw JSON string), or null if missing.
        public static string TryExtractToolArguments(string rawResponse)
        {
            if (string.IsNullOrEmpty(rawResponse)) return null;
            var rootRes = JSON.Parse(rawResponse);
            var argsNode = rootRes?["choices"]?[0]?["message"]?["tool_calls"]?[0]?["function"]?["arguments"];
            return argsNode?.Value;
        }
    }

}
