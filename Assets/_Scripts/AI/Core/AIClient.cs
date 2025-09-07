/*
 * Script Summary:
 * ----------------
 * Central coordinator between Unity, OpenAI, and Qdrant.  
 * Builds prompts, queries the LLM, applies heuristics, and stages instructions for execution/preview.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: AIClient – MonoBehaviour controlling the AI pipeline.
 * - Modes:
 *     • Non-strict → Plain prompt → fine-tuned model.
 *     • Strict (Tool Calling) → Structured tool schema → gpt-4.1-mini (default).
 * - Key Methods:
 *     • SendInstructionToAI(prompt, onResponse) – Entry point (routes strict/non-strict).
 *     • SendWithRagThenPrompt(...) – Non-strict, appends object + RAG context, expects JSON text.
 *     • SendWithRagThenToolCall(...) – Strict, uses PromptBuilder + ToolSchemaProvider.
 *     • SendChatPromptToolCall(...) – Handles OpenAI request, heuristics, normalization, target resolution, preview.
 *     • RelativeMoveHeuristic / RelativeRotateHeuristic – Adjusts outputs when phrasing implies deltas.
 * - Dependencies/Interactions:
 *     • PromptBuilder, ToolSchemaProvider, ResponseParser, AIInstructionNormalizer.
 *     • SelectionManager, ObjectStructureSerializer, RagContextBuilder, RagCompactor, UniqueIdExtractor.
 *     • InstructionExecutor, PendingChangesController.
 *     • QdrantApiClient (for RAG context + target resolution).
 * - Special Considerations:
 *     • Can log requests/responses to file for debugging.
 *     • Guards against applying absolute moves on multiple targets.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * aiClient.SendInstructionToAI("scale wall by 2x", normalizedJson => {
 *     Debug.Log("Instruction JSON: " + normalizedJson);
 * });
 * ```
 */

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;
using Qdrant;
using RenovAite.AI.Execution;
using RenovAite.AI.OpenAI;
using RenovAite.AI.Prompting;
using System.Text.RegularExpressions;
using System.Linq;
using RenovAite.AI.Instructions.Models;
using System.Threading.Tasks;

public class AIClient : MonoBehaviour
{
    [Header("OpenAI Settings")]
    public string openAIEndpoint = "https://api.openai.com/v1/chat/completions";
    public string apiKey = "YOUR_OPENAI_API_KEY";
    public string fineTunedModel = "ft:gpt-3.5-turbo-0125:personal::BzsRfp2D";

    [Header("Strict Mode (Tool Calling)")]
    public bool strictMode = true;
    public string toolsModel = "gpt-4.1-mini";

    [Header("RAG Settings")]
    public bool enableRag = false;
    public QdrantSettings qdrantSettings; // assign in Inspector
    public int ragTopK = 5;
    public bool useSimilarOverSearch = true;
    public bool autoInjectSelectionHeuristic = true;

    private QdrantApiClient _qdrant;

    [Header("Execution")]
    public InstructionExecutor instructionExecutor; // drag in from scene (or auto-found)

    [Header("Logging (Tool Calling)")]
    [SerializeField] private bool logToolRequests = true;
    [SerializeField] private bool logToolResponses = true;
    [SerializeField] private bool logToFile = false;   // write full JSON bodies to disk

    [Header("Prompt Budgets")]
    [SerializeField] private int objectJsonCharBudget = 4000;
    [SerializeField] private int ragJsonCharBudget = 3000;
    [SerializeField] private int ragCompactorTopK = 12;   // how many neighbors to forward

    private void Awake()
    {
        if (qdrantSettings == null)
            qdrantSettings = Resources.Load<QdrantSettings>("Config/QdrantSettings");

        if (qdrantSettings != null)
        {
            _qdrant = new QdrantApiClient(qdrantSettings);
            _qdrant.AttachSettings(qdrantSettings);
            _qdrant.OverrideBaseUrl(qdrantSettings.baseUrl);
            Debug.Log($"[AIClient] Qdrant base set to: {qdrantSettings.baseUrl}");
        }
    }

    private InstructionExecutor GetExecutor()
    {
        if (instructionExecutor == null)
            instructionExecutor = FindObjectOfType<InstructionExecutor>();
        return instructionExecutor;
    }

    private static GameObject Selected() =>
        FindObjectOfType<SelectionManager>()?.GetSelectedGameObject();

    // ---------------- Public entry point ----------------
    public void SendInstructionToAI(string userPrompt, Action<string> onResponse)
    {
        if (strictMode) StartCoroutine(SendWithRagThenToolCall(userPrompt, onResponse));
        else StartCoroutine(SendWithRagThenPrompt(userPrompt, onResponse));
    }
    // ---------------- RAG + Prompt (non-strict path) ----------------
    private IEnumerator SendWithRagThenPrompt(string userPrompt, Action<string> onResponse)
    {
        var selectedObject = Selected();
        if (selectedObject == null)
        {
            Debug.LogWarning("⚠️ No object selected. Prompt will not be sent (non-strict path expects one).");
            onResponse?.Invoke(null);
            yield break;
        }

        string objectStructure = ObjectStructureSerializer.Serialize(selectedObject);

        string ragContext = "";
        if (enableRag && _qdrant != null)
        {
            var ragTask = RagContextBuilder.BuildAsync(selectedObject, userPrompt, _qdrant, useSimilarOverSearch, ragTopK);
            while (!ragTask.IsCompleted) yield return null;
            ragContext = ragTask.Result;
        }

        // Compact blobs
        string compactObject = ObjectJsonCompactor.CompactObjectJson(objectStructure, objectJsonCharBudget, userPrompt);
        string compactRag = RagCompactor.CompactRagJson(ragContext, ragCompactorTopK, ragJsonCharBudget);

        string selectedUid = UniqueIdExtractor.TryGetUniqueId(selectedObject) ?? "";
        string finalPrompt =
            "You are an AI building system. Respond ONLY with a valid JSON command (action, target, modification).\n" +
            $" - You MUST set target.unique_id to either \"{selectedUid}\" (the selected object) OR one of the ids in 'Retrieved scene context (JSON)'. Do not invent IDs.\n\n" +
            "Here is the current object structure:\n" + compactObject + "\n" +
            (string.IsNullOrEmpty(compactRag) ? "" : $"Retrieved scene context (JSON):\n{compactRag}\n") +
            $"User request: {userPrompt}\n";

        yield return StartCoroutine(SendChatPrompt(finalPrompt, onResponse));
    }

    // ---------------- STRICT MODE: RAG + tool calling ----------------
    private IEnumerator SendWithRagThenToolCall(string userPrompt, Action<string> onResponse)
    {
        var selectedObject = Selected();

        // 1) Gather structure + RAG (works with or without a selection)
        string objectStructure = selectedObject != null ? ObjectStructureSerializer.Serialize(selectedObject) : string.Empty;

        string ragContext = "";
        if (enableRag && _qdrant != null)
        {
            var ragTask = RagContextBuilder.BuildAsync(selectedObject, userPrompt, _qdrant, useSimilarOverSearch, ragTopK);
            while (!ragTask.IsCompleted) yield return null;
            ragContext = ragTask.Result;
        }

        // 2) Compact before building the user message
        string compactObject = string.IsNullOrEmpty(objectStructure)
            ? string.Empty
            : ObjectJsonCompactor.CompactObjectJson(objectStructure, objectJsonCharBudget, userPrompt);
        string compactRag = RagCompactor.CompactRagJson(ragContext, ragCompactorTopK, ragJsonCharBudget);

        // 3) Build strict messages (selection-aware)
        string userMsg;
        string systemMsg;
        if (selectedObject != null)
        {
            string selectedUid = UniqueIdExtractor.TryGetUniqueId(selectedObject) ?? "";
            userMsg = PromptBuilder.BuildStrictUserMessage(selectedUid, compactObject, compactRag, userPrompt);
            systemMsg = PromptBuilder.BuildStrictSystemMessage();
        }
        else
        {
            userMsg = BuildNoSelectionUserMessage(compactRag, userPrompt);
            systemMsg = BuildNoSelectionSystemMessage();
        }

        // 4) Send with tool-calling
        yield return StartCoroutine(SendChatPromptToolCall(selectedObject, userMsg, onResponse, userPrompt, systemMsg));
    }

    // ---------------- Tool-call sender & parser ----------------
    private IEnumerator SendChatPromptToolCall(GameObject selectedObject, string userMessage, Action<string> onResponse, string naturalLanguageTextForHeuristics, string systemMessageOverride = null)
    {
        // --- Build payload (helpers) ---
        var toolsSpecNode = JSON.Parse(ToolSchemaProvider.GetToolsSpecJson());
        var toolChoiceNode = JSON.Parse(ToolSchemaProvider.GetToolChoiceJson());
        var systemMsg = string.IsNullOrEmpty(systemMessageOverride)
            ? PromptBuilder.BuildStrictSystemMessage()
            : systemMessageOverride;
        var messages = ToolSchemaProvider.BuildMessages(systemMsg, userMessage);

        var root = new JSONObject();
        root["model"] = toolsModel;
        root["temperature"] = 0;
        root["messages"] = messages;
        root["tools"] = toolsSpecNode;
        root["tool_choice"] = toolChoiceNode;

#if UNITY_EDITOR
        if (logToolRequests)
        {
            Debug.Log($"📤 ToolCall Request Body:\n{root.ToString(2)}");
        }
#endif
        if (logToFile)
        {
            WriteDebugFile($"openai_toolcall_request_{DateTime.Now:yyyyMMdd_HHmmss}.json", root.ToString(2));
        }

        using (UnityWebRequest request = new UnityWebRequest(openAIEndpoint, "POST"))
        {
            var payloadJson = root.ToString();
            var bodyRaw = Encoding.UTF8.GetBytes(payloadJson);
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[StrictMode][OpenAI] ❌ Request failed: " + request.error);
                if (request.downloadHandler != null && !string.IsNullOrEmpty(request.downloadHandler.text))
                    Debug.LogError("[StrictMode][OpenAI] Body: " + request.downloadHandler.text);
                onResponse?.Invoke(null);
                yield break;
            }

            // --- Parse tool arguments ---
            var responseText = request.downloadHandler.text;

#if UNITY_EDITOR
            if (logToolResponses)
            {
                Debug.Log($"📥 Raw ToolCall Response:\n{responseText}");
            }
#endif
            if (logToFile && !string.IsNullOrEmpty(responseText))
            {
                WriteDebugFile($"openai_toolcall_response_{DateTime.Now:yyyyMMdd_HHmmss}.json", responseText);
            }

            var argsJson = ResponseParser.TryExtractToolArguments(responseText);
            if (string.IsNullOrEmpty(argsJson))
            {
                Debug.LogError("[StrictMode][OpenAI] ❌ No tool arguments found in response.");
                onResponse?.Invoke(null);
                yield break;
            }

            // --- Normalize (units, vector forms, etc.)
            var normalizedArgs = AIInstructionNormalizer.Normalize(argsJson, selectedObject);
            Debug.Log("[Normalizer] " + normalizedArgs);

            // --- Selection heuristic (existing)
            // NOTE: When no selection, avoid injecting a "similar" block automatically.
            if (autoInjectSelectionHeuristic && selectedObject != null)
                SelectionHeuristic.InjectIfMissing(ref normalizedArgs, naturalLanguageTextForHeuristics, ragTopK, 0.75f);

            // --- Relative move heuristic (NEW)
            if (RelativeMoveHeuristic.ShouldTreatAsDelta(naturalLanguageTextForHeuristics))
            {
                normalizedArgs = RelativeMoveHeuristic.ConvertPositionToDelta(normalizedArgs);
            }

            // NEW rotate heuristic
            if (RelativeRotateHeuristic.ShouldTreatAsDelta(naturalLanguageTextForHeuristics))
                normalizedArgs = RelativeRotateHeuristic.ConvertRotationToDelta(normalizedArgs);

            // (optional) log to verify what we will deserialize:
            Debug.Log("[HeuristicsApplied] " + normalizedArgs);

            // Inject a filters selection for category requests when NO object is selected and the model didn't add selection.
            {
                string updated = normalizedArgs;
                bool done = false;
                yield return StartCoroutine(MaybeInjectFilterSelectionForNoSelection(
                    selectedObject,
                    naturalLanguageTextForHeuristics,
                    normalizedArgs,
                    s => { updated = s; done = true; }
                ));
                if (done) normalizedArgs = updated;
            }


            // --- Deserialize canonical instruction ---
            var instr = JsonUtility.FromJson<AIInstruction>(normalizedArgs);

            // --- Resolve targets ---
            var sel = TargetResolver.TryParseSelection(normalizedArgs);
            var targetsJob = TargetResolver.ResolveAsync(sel, selectedObject, _qdrant);
            while (!targetsJob.IsCompleted) yield return null;
            var targets = targetsJob.Result ?? new List<GameObject>();
            Debug.Log($"[Targets] resolved={targets.Count}");

            // Safety: avoid absolute-positioning many objects
            if (targets.Count > 1 && instr.modification != null && instr.modification.position != null)
            {
                Debug.LogWarning("[Exec] Multiple targets + absolute position given; ignoring position and applying scale/rotation only.");
                instr.modification.position = null;
            }

            // --- Stage or apply ---
            var preview = PendingChangesController.Instance ?? FindObjectOfType<PendingChangesController>(true);

            if (preview == null)
            {
                Debug.LogWarning("[Preview] Controller missing — applying immediately as fallback.");
                var exec = GetExecutor(); // returns InstructionExecutor (concrete)
                var applied = ExecuteWithFanOut(exec, normalizedArgs, targets, instr);
                onResponse?.Invoke(normalizedArgs);
                Debug.Log($"[StrictMode][Unity] ✅ Instruction executed immediately (targets={targets.Count}, applied={applied}).");
            }

            else
            {
                preview.Present(normalizedArgs, selectedObject, targets);
                onResponse?.Invoke(normalizedArgs);
                Debug.Log($"[StrictMode][Unity] ⏸ Staged instruction (targets={targets.Count}). Awaiting user Accept/Reject.");
            }
        }
    }

    // ---------------- Non-strict sender ----------------
    private IEnumerator SendChatPrompt(string prompt, Action<string> onResponse)
    {
        var chatRequest = new ChatRequest
        {
            model = fineTunedModel,
            messages = new ChatMessage[]
            {
                new ChatMessage { role = "system", content = "You are an assistant that converts natural language building instructions into structured JSON commands." },
                new ChatMessage { role = "user", content = prompt }
            }
        };

        string jsonRequest = JsonUtility.ToJson(chatRequest);
        Debug.Log($"📤 Sending prompt to OpenAI: {prompt}");
        Debug.Log($"📤 Full JSON Request: {jsonRequest}");

        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonRequest);

        using (UnityWebRequest request = new UnityWebRequest(openAIEndpoint, "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(bodyRaw);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("❌ OpenAI request error: " + request.error);
                Debug.LogError("📥 Error Response Body: " + request.downloadHandler.text);
                onResponse?.Invoke(null);
            }
            else
            {
                string responseText = request.downloadHandler.text;
                Debug.Log($"📥 Raw OpenAI Response: {responseText}");

                try
                {
                    var response = JsonUtility.FromJson<ChatResponse>(responseText);
                    string json = response.choices[0].message.content.Trim();
                    Debug.Log($"🧠 Parsed AI Model Output: {json}");
                    onResponse?.Invoke(json);
                }
                catch (Exception e)
                {
                    Debug.LogError("⚠️ Failed to parse AI response: " + e.Message);
                    onResponse?.Invoke(null);
                }
            }
        }
    }

    private void WriteDebugFile(string filename, string contents)
    {
        try
        {
            var path = Path.Combine(Application.persistentDataPath, filename);
            File.WriteAllText(path, contents);
            Debug.Log($"📝 Wrote debug file: {path}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"⚠️ Failed to write debug file: {e.Message}");
        }
    }

    // ---------- Relative move heuristic ----------
    private static class RelativeMoveHeuristic
    {
        // basic relative phrasing detector
        private static readonly Regex Rel =
            new(@"\b(by|up|down|left|right|forward|back|backward|backwards|towards|away)\b",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static bool ShouldTreatAsDelta(string prompt) =>
            !string.IsNullOrEmpty(prompt) && Rel.IsMatch(prompt);

        // If model returned only absolute position for a relative prompt, reinterpret it as delta.
        public static string ConvertPositionToDelta(string normalizedJson)
        {
            var node = JSON.Parse(normalizedJson);
            if (node == null) return normalizedJson;

            var mod = node["modification"];
            if (mod == null) return normalizedJson;

            if (mod["position_delta"] != null) return normalizedJson; // already delta

            if (mod["position"] != null)
            {
                mod["position_delta"] = mod["position"];
                mod.Remove("position");
                return node.ToString(0);
            }
            return normalizedJson;
        }
    }

    // ---------- Relative rotate heuristic ----------
    private static class RelativeRotateHeuristic
    {
        // "by", "right/left", "clockwise/counterclockwise", "turn"
        private static readonly System.Text.RegularExpressions.Regex Rel =
            new(@"\b(by|clockwise|counter[- ]?clockwise|to the right|to the left|right|left|turn)\b",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        public static bool ShouldTreatAsDelta(string prompt) =>
            !string.IsNullOrEmpty(prompt) && Rel.IsMatch(prompt);

        // If model returned only absolute rotation for a relative prompt, reinterpret it as delta
        public static string ConvertRotationToDelta(string normalizedJson)
        {
            var node = SimpleJSON.JSON.Parse(normalizedJson);
            if (node == null) return normalizedJson;
            var mod = node["modification"];
            if (mod == null) return normalizedJson;

            if (mod["rotation_delta"] != null) return normalizedJson; // already delta
            if (mod["rotation"] != null)
            {
                mod["rotation_delta"] = mod["rotation"];
                mod.Remove("rotation");
                return node.ToString(0);
            }
            return normalizedJson;
        }
    }

    // ---------------- No-selection helpers ----------------
    private static string BuildNoSelectionSystemMessage()
    {
        return
            "You convert natural language building instructions into exactly one AIInstruction using the tool. Do not write text; only call the tool. Do not use 'composite'. " +
            "When the user requests multiple changes (scale/rotate/move), include ALL fields in ONE tool call. " +
            "For 'move': prefer DELTA (modification.position_delta) for relative phrasing (by/up/down/left/right), and ABSOLUTE (modification.position) for explicit target coordinates or 'move to'. Use exactly one. " +
            "CRITICAL: If an object is selected, set target.unique_id to the SELECTED object's ID I gave you. If no object is selected and the user targets a category (e.g., all doors/windows), set selection.apply_to='filters' and include selection.filters (e.g., { ifc_type: 'Door' } or tags). Do NOT invent IDs. " +
            "All lengths must be METERS (numeric only). Rotations are EULER DEGREES (numeric). For scale, return per-axis multiplier {x,y,z}. If changing width/height/depth, use numeric deltas in meters in modification.width/height/depth. Always return vectors as OBJECTS with x,y,z (NOT arrays).";
    }

    private static string BuildNoSelectionUserMessage(string compactRag, string userPrompt)
    {
        var sb = new StringBuilder();
        sb.AppendLine("You are an AI building system. Return exactly ONE AIInstruction via the tool.");
        sb.AppendLine("Rules:");
        sb.AppendLine(" - If NO object is selected and the request targets a category, use selection.apply_to='filters' and selection.filters to specify { ifc_type, tags }.");
        sb.AppendLine(" - Do NOT set target.unique_id when nothing is selected.");
        sb.AppendLine(" - All lengths are meters; rotations are Euler degrees.");
        if (!string.IsNullOrEmpty(compactRag))
        {
            sb.AppendLine();
            sb.AppendLine("Retrieved scene context (JSON):");
            sb.AppendLine(compactRag);
        }
        sb.AppendLine();
        sb.Append("User request: ").Append(userPrompt).Append('\n');
        return sb.ToString();
    }


    // Fan-out executor that works with your existing AIInstruction shape.
    // It clones the normalized JSON per target and just swaps target.unique_id.
    private int ExecuteWithFanOut(object executor, string normalizedArgs, IList<GameObject> targetObjects, AIInstruction instrFallback)
    {
        if (executor == null) return 0;

        // Collect unique_ids from resolved GameObjects
        var ids = new List<string>();
        if (targetObjects != null && targetObjects.Count > 0)
        {
            foreach (var go in targetObjects)
            {
                var uid = UniqueIdExtractor.TryGetUniqueId(go);
                if (!string.IsNullOrEmpty(uid)) ids.Add(uid);
            }
        }

        // Fallback: single target from the instruction if resolver returned none
        if (ids.Count == 0 && !string.IsNullOrEmpty(instrFallback?.target?.unique_id))
            ids.Add(instrFallback.target.unique_id);

        var finalIds = ids.Where(s => !string.IsNullOrEmpty(s)).Distinct().ToList();
        if (finalIds.Count == 0)
        {
            Debug.LogWarning("[Exec] Nothing to apply (no target ids).");
            return 0;
        }

        // Try to find an Execute(AIInstruction) method on whatever executor we got
        var execType = executor.GetType();
        var execMethod = execType.GetMethod("Execute", new[] { typeof(AIInstruction) });
        if (execMethod == null)
        {
            Debug.LogError($"[Exec] Executor of type {execType.Name} has no Execute(AIInstruction) method.");
            return 0;
        }

        var applied = 0;
        foreach (var id in finalIds)
        {
            var node = JSON.Parse(normalizedArgs) ?? new JSONObject();
            if (node["target"] == null) node["target"] = new JSONObject();
            node["target"]["unique_id"] = id;

            var perTarget = JsonUtility.FromJson<AIInstruction>(node.ToString());
            execMethod.Invoke(executor, new object[] { perTarget });
            applied++;
        }

        return applied;
    }

    public int FanOutAndExecute(object executor, string normalizedArgs, IList<GameObject> targetObjects, AIInstruction instrFallback)
    {
        return ExecuteWithFanOut(executor, normalizedArgs, targetObjects, instrFallback);
    }

    // -------- Fuzzy, data-driven IFC-type inference (no hardcoded words) --------
    private static HashSet<string> _knownIfcTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // Discover distinct ifc_type_final values by sampling the index.
    // Uses reflection to read properties from the typed response (avoids coupling to DTO shape).
    private async Task<HashSet<string>> DiscoverIfcTypesAsync(int sample = 2000)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_qdrant == null) return set;
            var req = new Qdrant.SearchRequest
            {
                query = "",            // empty -> rely on payload filters only
                top_k = sample,        // sample a bunch to see what's in the index
                require_geometry = true
            };
            var res = await _qdrant.SearchAsync(req);
            if (res == null) return set;

            var resultsProp = res.GetType().GetProperty("results");
            var items = resultsProp?.GetValue(res) as System.Collections.IEnumerable;
            if (items == null) return set;

            foreach (var item in items)
            {
                var typeProp = item.GetType().GetProperty("ifc_type_final");
                var t = typeProp?.GetValue(item) as string;
                if (!string.IsNullOrWhiteSpace(t)) set.Add(t.Trim());
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[AIClient] Ifc type discovery failed: {ex.Message}");
        }
        return set;
    }

    // Main nudge: if nothing is selected and the model didn't add selection, inject filters with a fuzzily-guessed IFC type.
    private IEnumerator MaybeInjectFilterSelectionForNoSelection(
        GameObject selectedObject,
        string userText,
        string normalizedArgsIn,
        Action<string> onUpdatedJson)
    {
        if (selectedObject != null || string.IsNullOrEmpty(normalizedArgsIn))
        {
            onUpdatedJson?.Invoke(normalizedArgsIn);
            yield break;
        }

        var node = JSON.Parse(normalizedArgsIn) ?? new JSONObject();
        // If the model already provided selection, do nothing.
        if (node["selection"] != null && node["selection"]["apply_to"] != null)
        {
            onUpdatedJson?.Invoke(normalizedArgsIn);
            yield break;
        }

        // Ensure we know the available IFC types (lazy fetch once)
        if (_knownIfcTypes.Count == 0)
        {
            var task = DiscoverIfcTypesAsync(2000);
            while (!task.IsCompleted) yield return null;
            _knownIfcTypes = task.Result ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var guessed = GuessIfcTypeFromUserText(userText, _knownIfcTypes, 0.75f);
        if (string.IsNullOrEmpty(guessed))
        {
            onUpdatedJson?.Invoke(normalizedArgsIn);
            yield break;
        }

        var sel = new JSONObject();
        sel["apply_to"] = "filters";
        var filters = new JSONObject();
        filters["ifc_type"] = guessed;
        sel["filters"] = filters;
        node["selection"] = sel;

        var updated = node.ToString(0);
        Debug.Log($"[Heuristic] Injected filters selection (ifc_type={guessed}) for no-selection request.");
        onUpdatedJson?.Invoke(updated);
    }

    // Fuzzy guess using singularization + normalized Levenshtein similarity against discovered types.
    private static string GuessIfcTypeFromUserText(string text, HashSet<string> typeSet, float threshold = 0.75f)
    {
        if (string.IsNullOrWhiteSpace(text) || typeSet == null || typeSet.Count == 0) return null;

        var tokens = Regex.Matches(text.ToLowerInvariant(), "[a-z]+")
                          .Cast<Match>().Select(m => Singularize(m.Value))
                          .Distinct().ToArray();

        string best = null;
        float bestScore = 0f;

        foreach (var type in typeSet)
        {
            var typeKey = type.ToLowerInvariant(); // e.g., "window", "morph", "door"
            foreach (var tok in tokens)
            {
                var s = Similarity(tok, typeKey);
                if (s > bestScore) { bestScore = s; best = type; }
            }
        }
        return bestScore >= threshold ? best : null;
    }

    private static string Singularize(string w)
    {
        if (w.EndsWith("ies")) return w.Substring(0, w.Length - 3) + "y"; // stories -> story
        if (w.EndsWith("sses") || w.EndsWith("ches") || w.EndsWith("shes") || w.EndsWith("xes"))
            return w.Substring(0, w.Length - 2);                           // boxes -> box
        if (w.EndsWith("s") && w.Length > 3) return w.Substring(0, w.Length - 1); // windows -> window
        return w;
    }

    private static float Similarity(string a, string b)
    {
        if (a == b) return 1f;
        int d = Levenshtein(a, b);
        int max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1f : 1f - (float)d / max;
    }

    private static int Levenshtein(string a, string b)
    {
        int n = a.Length, m = b.Length;
        if (n == 0) return m; if (m == 0) return n;
        var d = new int[n + 1, m + 1];
        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost
                );
            }
        }
        return d[n, m];
    }





}
