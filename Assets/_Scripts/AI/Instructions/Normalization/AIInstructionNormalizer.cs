/*
 * Script Summary:
 * ----------------
 * Normalizes raw tool-call JSON into a canonical AIInstruction shape.  
 * Handles unit conversions, vector canonicalization, delta inference, and pruning.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: AIInstructionNormalizer (static).
 * - Key Features:
 *     • Converts [x,y,z] arrays → {x,y,z} objects.
 *     • Converts "1m", "2ft", "45deg", "1.57rad" → numeric meters/degrees.
 *     • Auto-injects selected object's Unique ID if missing.
 *     • Infers action if not provided (based on present fields).
 *     • Converts width/height/depth deltas → scale multipliers.
 *     • Clamps scale (0.01–100), rotations (±360), positions (±10k).
 * - Dependencies/Interactions:
 *     • Uses ifcProperties and Renderer bounds for delta scaling.
 *     • Consumed by AIClient before execution/preview.
 * - Special Considerations:
 *     • Keeps only whitelisted keys: {scale, position, rotation, material, replace}.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string normalized = AIInstructionNormalizer.Normalize(rawJsonArgs, selectedGO);
 * var instr = JsonUtility.FromJson<AIInstruction>(normalized);
 * ```
 */

using System;
using System.Globalization;
using SimpleJSON;
using UnityEngine;

public static class AIInstructionNormalizer
{
    public static string Normalize(string rawArgsJson, GameObject selected)
    {
        var n = JSON.Parse(rawArgsJson) ?? new JSONObject();
        var mod = EnsureObject(n, "modification");
        var target = EnsureObject(n, "target");

        // Prefer provided target; else fall back to selected unique id
        if (string.IsNullOrEmpty(target["unique_id"]?.Value))
        {
            var sid = TryGetSelectedUniqueId(selected);
            if (!string.IsNullOrEmpty(sid)) target["unique_id"] = sid;
        }

        // Canonical action or infer from fields
        var action = CanonicalAction(n["action"]?.Value);

        // ---------- Vector normalization (arrays -> {x,y,z}) for base + delta ----------
        // Base vector keys we support generically
        var baseVec = new[] { "scale", "position", "rotation" };
        foreach (var k in baseVec)
        {
            ArrayToVec3(mod, k);
            ArrayToVec3(mod, k + "_delta");
        }

        // ---------- Unit conversions ----------
        ToMetersVec(mod, "position");
        ToMetersVec(mod, "position_delta");   // NEW: support delta meters too
        ToDegreesVec(mod, "rotation");
        ToDegreesVec(mod, "rotation_delta");  // NEW: support delta rotation too

        // Accept width/height/depth deltas (even if schema forbids) → scale multipliers
        var hadDeltas = mod.HasKey("width") || mod.HasKey("height") || mod.HasKey("depth");
        if (hadDeltas)
        {
            var scale = EnsureVec3(mod, "scale", 1, 1, 1);
            BuildScaleFromDeltas(selected, mod, ref scale);
            mod["scale"] = scale; mod.Remove("width"); mod.Remove("height"); mod.Remove("depth");
            if (string.IsNullOrEmpty(action) || action == "move" || action == "rotate") action = "scale";
        }

        // Infer action if still missing
        if (string.IsNullOrEmpty(action))
            action = HasVec(mod, "position") || HasVec(mod, "position_delta") ? "move" :
                     HasVec(mod, "rotation") || HasVec(mod, "rotation_delta") ? "rotate" :
                     HasVec(mod, "scale") || HasVec(mod, "scale_delta") ? "scale" :
                     mod.HasKey("material") ? "material" :
                     mod.HasKey("replace") ? "replace" : "move";

        // Safety clamps
        ClampScale(mod, 0.01f, 100f);
        ClampRotation(mod, -360f, 360f);
        ClampRotation(mod, -360f, 360f, key: "rotation_delta"); // reasonable guard for deltas too
        ClampPosition(mod, -10000f, 10000f);
        ClampPosition(mod, -10000f, 10000f, key: "position_delta");

        // ---------- Prune to allowed keys (base + delta kept) ----------
        KeepOnly(mod,
            "scale", "scale_delta",
            "position", "position_delta",
            "rotation", "rotation_delta",
            "material", "replace"
        );

        // keep 'selection' if you're using multi-target
        KeepOnly(n.AsObject, "action", "target", "modification", "selection");
        n["action"] = action; n["target"] = target; n["modification"] = mod;

        return n.ToString();
    }

    // ---------- helpers ----------
    private static JSONObject EnsureObject(JSONNode parent, string key) { var node = parent?[key]; if (node == null || !node.IsObject) { var o = new JSONObject(); parent[key] = o; return o; } return node.AsObject; }
    private static JSONObject EnsureVec3(JSONObject parent, string key, float x = 0, float y = 0, float z = 0) { var v = parent[key]; if (v == null || !v.IsObject || v["x"] == null || v["y"] == null || v["z"] == null) { var o = new JSONObject(); o["x"] = x; o["y"] = y; o["z"] = z; parent[key] = o; return o; } return v.AsObject; }
    private static bool HasVec(JSONObject parent, string key) { var v = parent[key]; return v != null && v.IsObject && v["x"] != null && v["y"] != null && v["z"] != null; }
    private static void ArrayToVec3(JSONObject parent, string key) { var v = parent[key]; if (v == null) return; if (v.IsArray && v.AsArray.Count == 3) { var a = v.AsArray; var o = new JSONObject(); o["x"] = a[0].AsFloat; o["y"] = a[1].AsFloat; o["z"] = a[2].AsFloat; parent[key] = o; } }
    private static void ToMetersVec(JSONObject parent, string key) { var v = parent[key]; if (v == null || !v.IsObject) return; v["x"] = (float)ToMeters(v["x"]?.Value); v["y"] = (float)ToMeters(v["y"]?.Value); v["z"] = (float)ToMeters(v["z"]?.Value); }
    private static void ToDegreesVec(JSONObject parent, string key) { var v = parent[key]; if (v == null || !v.IsObject) return; v["x"] = (float)ToDegrees(v["x"]?.Value); v["y"] = (float)ToDegrees(v["y"]?.Value); v["z"] = (float)ToDegrees(v["z"]?.Value); }
    private static double ToMeters(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0; s = s.Trim().ToLowerInvariant(); if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var val)) return val;
        s = s.Replace("meters", "m").Replace("meter", "m").Replace("centimeters", "cm").Replace("centimeter", "cm").Replace("millimeters", "mm").Replace("millimeter", "mm")
           .Replace("inches", "in").Replace("inch", "in").Replace("\"", " in").Replace("feet", "ft").Replace("foot", "ft").Replace("'", " ft");
        var parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
        {
            return parts[1] switch { "m" => v, "cm" => v / 100.0, "mm" => v / 1000.0, "in" => v * 0.0254, "ft" => v * 0.3048, _ => v };
        }
        return double.Parse(s, CultureInfo.InvariantCulture);
    }
    private static double ToDegrees(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0; s = s.Trim().ToLowerInvariant();
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var val)) return val;
        if (s.EndsWith("rad")) return double.Parse(s.Replace("rad", "").Trim(), CultureInfo.InvariantCulture) * (180.0 / Mathf.PI);
        if (s.EndsWith("deg")) return double.Parse(s.Replace("deg", "").Trim(), CultureInfo.InvariantCulture);
        return double.Parse(s, CultureInfo.InvariantCulture);
    }
    private static void BuildScaleFromDeltas(GameObject selected, JSONObject mod, ref JSONObject scale)
    {
        float? dx = mod.HasKey("width") ? (float?)(float)ToMeters(mod["width"]?.Value) : null;
        float? dy = mod.HasKey("height") ? (float?)(float)ToMeters(mod["height"]?.Value) : null;
        float? dz = mod.HasKey("depth") ? (float?)(float)ToMeters(mod["depth"]?.Value) : null;
        var rend = selected ? selected.GetComponentInChildren<Renderer>() : null;
        var w = rend ? Mathf.Max(rend.bounds.size.x, 1e-5f) : 1f;
        var h = rend ? Mathf.Max(rend.bounds.size.y, 1e-5f) : 1f;
        var d = rend ? Mathf.Max(rend.bounds.size.z, 1e-5f) : 1f;
        float sx = dx.HasValue ? Mathf.Max(0.01f, (w + dx.Value) / w) : 1f;
        float sy = dy.HasValue ? Mathf.Max(0.01f, (h + dy.Value) / h) : 1f;
        float sz = dz.HasValue ? Mathf.Max(0.01f, (d + dz.Value) / d) : 1f;
        var ex = scale["x"] != null ? scale["x"].AsFloat : 1f; var ey = scale["y"] != null ? scale["y"].AsFloat : 1f; var ez = scale["z"] != null ? scale["z"].AsFloat : 1f;
        scale["x"] = ex * sx; scale["y"] = ey * sy; scale["z"] = ez * sz;
    }
    private static void ClampScale(JSONObject mod, float lo, float hi) { var v = mod["scale"]; if (v == null || !v.IsObject) return; v["x"] = Mathf.Clamp(v["x"].AsFloat, lo, hi); v["y"] = Mathf.Clamp(v["y"].AsFloat, lo, hi); v["z"] = Mathf.Clamp(v["z"].AsFloat, lo, hi); }
    private static void ClampRotation(JSONObject mod, float lo, float hi, string key = "rotation") { var v = mod[key]; if (v == null || !v.IsObject) return; v["x"] = Mathf.Clamp(v["x"].AsFloat, lo, hi); v["y"] = Mathf.Clamp(v["y"].AsFloat, lo, hi); v["z"] = Mathf.Clamp(v["z"].AsFloat, lo, hi); }
    private static void ClampPosition(JSONObject mod, float lo, float hi, string key = "position") { var v = mod[key]; if (v == null || !v.IsObject) return; v["x"] = Mathf.Clamp(v["x"].AsFloat, lo, hi); v["y"] = Mathf.Clamp(v["y"].AsFloat, lo, hi); v["z"] = Mathf.Clamp(v["z"].AsFloat, lo, hi); }
    private static string CanonicalAction(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null; raw = raw.Trim().ToLowerInvariant();
        return raw switch
        {
            "translate" or "reposition" or "shift" => "move",
            "spin" or "yaw" or "pitch" or "roll" => "rotate",
            "resize" or "enlarge" or "shrink" => "scale",
            "recolor" or "paint" => "material",
            "swap" => "replace",
            _ => raw
        };
    }
    private static string TryGetSelectedUniqueId(GameObject go)
    {
        if (go == null) return null;
        var ifc = go.GetComponent<ifcProperties>();
        if (ifc == null || ifc.properties == null || ifc.nominalValues == null) return null;
        int n = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
        for (int i = 0; i < n; i++)
        {
            var k = ifc.properties[i]; var v = ifc.nominalValues[i];
            if (!string.IsNullOrEmpty(k) && k.ToLower().Contains("unique id")) return v;
        }
        return null;
    }
    private static void KeepOnly(JSONObject obj, params string[] keys)
    {
        if (obj == null) return;
        var keep = new System.Collections.Generic.HashSet<string>(keys);
        var rm = new System.Collections.Generic.List<string>();
        foreach (var kv in obj) if (!keep.Contains(kv.Key)) rm.Add(kv.Key);
        foreach (var k in rm) obj.Remove(k);
    }
}
