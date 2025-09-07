/*
 * Script Summary:
 * ----------------
 * Utility class for resolving material keys/aliases from a MaterialLibrary
 * based on a query string. Supports multiple match strategies with confidence scoring.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MaterialResolver (static).
 * - Key Structs:
 *     • Result – { Material material, string keyMatched, string matchType, float score }.
 * - Key Methods:
 *     • Resolve(MaterialLibrary lib, string query) – Attempts to find the best matching Material.
 *       Match order:
 *         1. Exact key match (score 1.0).
 *         2. Exact alias match (score 0.95).
 *         3. StartsWith match (score 0.9).
 *         4. Contains match (score 0.85).
 *         5. Alias contains match (score 0.8).
 * - Helper Methods:
 *     • Norm(string) – Normalizes input (lowercase, trims, replaces underscores/dashes with spaces).
 * - Dependencies/Interactions:
 *     • Requires MaterialLibrary entries (key, material, aliases).
 *     • Used by MaterialApplier to resolve materials from LLM-provided keys.
 * - Special Considerations:
 *     • Returns default Result if no match found.
 *     • Query normalization ensures robustness against naming variations.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var result = MaterialResolver.Resolve(materialLibrary, "polished concrete");
 * if (result.material != null)
 *     renderer.sharedMaterial = result.material;
 * ```
 */

using System;
using System.Collections.Generic;
using UnityEngine;

public static class MaterialResolver
{
    public struct Result
    {
        public Material material;
        public string keyMatched;
        public string matchType; // exact/alias/startsWith/contains
        public float score;
    }

    public static Result Resolve(MaterialLibrary lib, string query)
    {
        if (lib == null || string.IsNullOrWhiteSpace(query))
            return default;

        string q = Norm(query);

        // 1) Exact key match
        foreach (var e in lib.entries)
            if (e != null && e.material && Norm(e.key) == q)
                return new Result { material = e.material, keyMatched = e.key, matchType = "exact", score = 1f };

        // 2) Exact alias match
        foreach (var e in lib.entries)
        {
            if (e == null || !e.material || e.aliases == null) continue;
            foreach (var a in e.aliases)
                if (!string.IsNullOrWhiteSpace(a) && Norm(a) == q)
                    return new Result { material = e.material, keyMatched = e.key, matchType = "alias", score = 0.95f };
        }

        // 3) StartsWith (wood → wood_oak, clear glass → glass)
        foreach (var e in lib.entries)
            if (e != null && e.material && Norm(e.key).StartsWith(q))
                return new Result { material = e.material, keyMatched = e.key, matchType = "startsWith", score = 0.9f };

        // 4) Token contains (e.g., "dark wood plank" → wood)
        foreach (var e in lib.entries)
            if (e != null && e.material && q.Contains(Norm(e.key)))
                return new Result { material = e.material, keyMatched = e.key, matchType = "contains", score = 0.85f };

        // 5) Alias contains
        foreach (var e in lib.entries)
        {
            if (e == null || !e.material || e.aliases == null) continue;
            foreach (var a in e.aliases)
                if (!string.IsNullOrWhiteSpace(a) && q.Contains(Norm(a)))
                    return new Result { material = e.material, keyMatched = e.key, matchType = "alias-contains", score = 0.8f };
        }

        return default;
    }

    private static string Norm(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.ToLowerInvariant().Trim();
        // remove common separators
        s = s.Replace("_", " ").Replace("-", " ");
        // squeeze spaces
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }
}
