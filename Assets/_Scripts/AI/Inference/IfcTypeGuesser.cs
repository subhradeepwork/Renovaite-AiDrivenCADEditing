using System;
using System.Linq;
using System.Text.RegularExpressions;

public static class IfcTypeGuesser
{
    // Returns the best-matching IFC type present in the scene/index, or null if no good match.
    public static string GuessFromUserText(string text, float threshold = 0.78f)
    {
        if (string.IsNullOrWhiteSpace(text) || IfcTypeRegistry.Types.Count == 0) return null;

        // Tokenize user text into candidate nouns
        var tokens = Regex.Matches(text.ToLowerInvariant(), "[a-z]+")
                          .Cast<Match>().Select(m => m.Value)
                          .Select(Singularize).Distinct().ToArray();

        string best = null;
        float bestScore = 0f;

        foreach (var type in IfcTypeRegistry.Types)
        {
            var typeKey = type.ToLowerInvariant(); // e.g., "window", "door", "morph"
            foreach (var tok in tokens)
            {
                var s = Similarity(tok, typeKey);
                if (s > bestScore) { bestScore = s; best = type; }
            }
        }

        return bestScore >= threshold ? best : null;
    }

    // Very small singularizer (windows -> window, doors -> door, slabs -> slab, stories -> story, etc.)
    private static string Singularize(string w)
    {
        if (w.EndsWith("ies")) return w.Substring(0, w.Length - 3) + "y";   // stories -> story
        if (w.EndsWith("sses") || w.EndsWith("ches") || w.EndsWith("shes") || w.EndsWith("xes"))
            return w.Substring(0, w.Length - 2);                             // boxes -> box, benches -> bench
        if (w.EndsWith("s") && w.Length > 3) return w.Substring(0, w.Length - 1); // windows -> window
        return w;
    }

    // Normalized Levenshtein similarity in [0,1]
    private static float Similarity(string a, string b)
    {
        if (a == b) return 1f;
        int dist = Levenshtein(a, b);
        int max = Math.Max(a.Length, b.Length);
        return max == 0 ? 1f : 1f - (float)dist / max;
    }

    private static int Levenshtein(string a, string b)
    {
        var n = a.Length; var m = b.Length;
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
