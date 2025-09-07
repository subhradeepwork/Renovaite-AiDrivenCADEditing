/*
 * Script Summary:
 * ----------------
 * Utility for automatically injecting a "selection" block into normalized AIInstruction arguments
 * when the user’s natural prompt implies similarity-based application but the tool omitted it.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: SelectionHeuristic (static).
 * - Key Methods:
 *     • InjectIfMissing(ref string normalizedArgs, string userPrompt, int defaultTopK, float defaultThr)
 *       - Checks if user prompt includes "similar" keywords.
 *       - Parses optional top_k or threshold values from the prompt.
 *       - Mutates normalizedArgs JSON to include selection block with apply_to="similar".
 * - Dependencies/Interactions:
 *     • SimpleJSON for JSON manipulation.
 *     • UnityEngine.Debug for logging.
 * - Special Considerations:
 *     • Prevents duplicate injection if "selection" already exists.
 *     • Ensures parsed values are clamped (top_k: 1–50, threshold: 0–1).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string args = "{\"action\":\"scale\"}";
 * var changed = SelectionHeuristic.InjectIfMissing(ref args, "scale all similar walls", 5, 0.85f);
 * // args now includes a "selection" JSON block for similar application.
 * ```
 */

using System.Text.RegularExpressions;
using SimpleJSON;
using UnityEngine;

public static class SelectionHeuristic
{
    // Mutates normalizedArgs JSON if the user clearly asked for "similar" but tool omitted a selection block.
    public static bool InjectIfMissing(ref string normalizedArgs, string userPrompt, int defaultTopK, float defaultThr)
    {
        if (string.IsNullOrEmpty(normalizedArgs) || normalizedArgs.Contains("\"selection\"")) return false;
        if (string.IsNullOrEmpty(userPrompt)) return false;

        var wantsSimilar = Regex.IsMatch(userPrompt, @"\b(similar|apply to similar|all similar|same type)\b", RegexOptions.IgnoreCase);
        if (!wantsSimilar) return false;

        int topK = defaultTopK;
        var mK = Regex.Match(userPrompt, @"top[_\s]*k\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        if (mK.Success && int.TryParse(mK.Groups[1].Value, out var k)) topK = Mathf.Clamp(k, 1, 50);

        float thr = defaultThr;
        var mT = Regex.Match(userPrompt, @"(threshold|thresh|sim)\s*=\s*([0-1](?:\.\d+)?)", RegexOptions.IgnoreCase);
        if (mT.Success && float.TryParse(mT.Groups[2].Value, System.Globalization.NumberStyles.Float,
                                         System.Globalization.CultureInfo.InvariantCulture, out var t))
            thr = Mathf.Clamp01(t);

        var n = JSON.Parse(normalizedArgs);
        var sel = new JSONObject();
        sel["apply_to"] = "similar";
        sel["top_k"] = topK;
        sel["similarity_threshold"] = thr;
        n["selection"] = sel;

        normalizedArgs = n.ToString();
        Debug.Log($"[RAG] Injected selection block (top_k={topK}, threshold={thr}).");
        return true;
    }
}
