/*
 * Script Summary:
 * ----------------
 * Provides the JSON schema for the "apply_action" tool and helpers to assemble tool specs,
 * tool choice, and (system,user) message arrays for OpenAI chat completions.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ToolSchemaProvider (static).
 * - Key Methods:
 *     • GetApplyActionParamsJson() : string
 *       - JSON Schema for AIInstruction parameters including:
 *         action enum (scale/move/rotate/material/replace/delete),
 *         target {unique_id,name},
 *         modification with scale/position/rotation (+ *_delta pattern), width/height/depth, material/replace,
 *         and selection {apply_to, ids, top_k, similarity_threshold, filters}.
 *     • GetToolsSpecJson() : string
 *       - Wraps the parameters into a tools array for the API.
 *     • GetToolChoiceJson() : string
 *       - Forces the model to call apply_action.
 *     • BuildMessages(string system, string user) : JSONArray
 *       - Convenience builder for OpenAI messages payload.
 * - Dependencies/Interactions:
 *     • Designed to pair with PromptBuilder’s rules and the downstream ActionAppliers.
 *     • SimpleJSON for lightweight message assembly.
 * - Special Considerations:
 *     • patternProperties ensures future *_delta fields remain valid without schema breakage.
 *     • AdditionalProperties=false keeps payloads strict and predictable for Unity-side deserialization.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var tools = ToolSchemaProvider.GetToolsSpecJson();
 * var choice = ToolSchemaProvider.GetToolChoiceJson();
 * var messages = ToolSchemaProvider.BuildMessages(sys, usr);
 * // pass tools & messages to your OpenAI client (implementation-dependent)
 * ```
 */

namespace RenovAite.AI.OpenAI
{
    using SimpleJSON;

    public static class ToolSchemaProvider
    {
        // Parameters for the apply_action tool, now including generic *_delta via patternProperties.
        public static string GetApplyActionParamsJson() => @"
    {
      ""type"": ""object"",
      ""required"": [""action"", ""target""],
      ""properties"": {
        ""action"": { ""type"": ""string"", ""enum"": [""scale"",""move"",""rotate"",""material"",""replace"",""delete""] },
        ""target"": {
          ""type"": ""object"",
          ""properties"": { ""unique_id"": { ""type"": ""string"" }, ""name"": { ""type"": ""string"" } },
          ""additionalProperties"": false
        },
        ""modification"": {
          ""type"": ""object"",
          ""properties"": {
            ""scale"": {
              ""oneOf"": [
                { ""type"": ""object"", ""properties"": { ""x"":{""type"":""number""},""y"":{""type"":""number""},""z"":{""type"":""number""} }, ""required"": [""x"",""y"",""z""], ""additionalProperties"": false },
                { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""minItems"": 3, ""maxItems"": 3 }
              ]
            },
            ""position"": {
              ""oneOf"": [
                { ""type"": ""object"", ""properties"": { ""x"":{""type"":""number""},""y"":{""type"":""number""},""z"":{""type"":""number""} }, ""required"": [""x"",""y"",""z""], ""additionalProperties"": false },
                { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""minItems"": 3, ""maxItems"": 3 }
              ],
              ""description"": ""ABSOLUTE world position in meters (use when user says 'move to' or gives coordinates)""
            },
            ""position_delta"": {
              ""type"": ""object"",
              ""properties"": { ""x"":{""type"":""number""},""y"":{""type"":""number""},""z"":{""type"":""number""} },
              ""required"": [""x"",""y"",""z""],
              ""additionalProperties"": false,
              ""description"": ""DELTA meters to add to current world position (use for 'up/down/by/left/right/forward/back')""
            },
            ""rotation"": {
              ""oneOf"": [
                { ""type"": ""object"", ""properties"": { ""x"":{""type"":""number""},""y"":{""type"":""number""},""z"":{""type"":""number""} }, ""required"": [""x"",""y"",""z""], ""additionalProperties"": false },
                { ""type"": ""array"", ""items"": { ""type"": ""number"" }, ""minItems"": 3, ""maxItems"": 3 }
              ],
              ""description"": ""Euler degrees""
            },
            ""width"":  { ""type"": ""number"", ""description"": ""Delta meters"" },
            ""height"": { ""type"": ""number"", ""description"": ""Delta meters"" },
            ""depth"":  { ""type"": ""number"", ""description"": ""Delta meters"" },
            ""material"": { ""type"": ""object"" },
            ""replace"":  { ""type"": ""object"" }
          },
          ""patternProperties"": {
            ""^(position|rotation|scale)_delta$"": {
              ""type"": ""object"",
              ""properties"": {
                ""x"": { ""type"": ""number"" },
                ""y"": { ""type"": ""number"" },
                ""z"": { ""type"": ""number"" }
              },
              ""required"": [""x"", ""y"", ""z""],
              ""additionalProperties"": false
            }
          },
          ""additionalProperties"": false
        },
        ""selection"": {
          ""type"": ""object"",
          ""properties"": {
            ""apply_to"": { ""type"": ""string"", ""enum"": [""selected"", ""similar"", ""ids""] },
            ""ids"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } },
            ""top_k"": { ""type"": ""integer"", ""minimum"": 1, ""maximum"": 50 },
            ""similarity_threshold"": { ""type"": ""number"", ""minimum"": 0, ""maximum"": 1 },
            ""filters"": {
              ""type"": ""object"",
              ""properties"": {
                ""ifc_type"": { ""type"": ""string"" },
                ""tags"": { ""type"": ""array"", ""items"": { ""type"": ""string"" } }
              },
              ""additionalProperties"": false
            }
          },
          ""additionalProperties"": false
        }
      },
      ""additionalProperties"": false
    }";

        public static string GetToolsSpecJson()
        {
            var apply = GetApplyActionParamsJson();
            return @"[
          {
            ""type"": ""function"",
            ""function"": {
              ""name"": ""apply_action"",
              ""description"": ""Return a single canonical AIInstruction for the requested scene change."",
              ""parameters"": " + apply + @"
            }
          }
        ]";
        }

        public static string GetToolChoiceJson() =>
            @"{ ""type"": ""function"", ""function"": { ""name"": ""apply_action"" } }";

        public static JSONArray BuildMessages(string system, string user)
        {
            var messages = new JSONArray();
            var sys = new JSONObject(); sys["role"] = "system"; sys["content"] = system; messages.Add(sys);
            var usr = new JSONObject(); usr["role"] = "user"; usr["content"] = user; messages.Add(usr);
            return messages;
        }
    }

}
