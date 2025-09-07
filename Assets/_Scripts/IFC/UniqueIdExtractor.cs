/*
 * Script Summary:
 * ----------------
 * Utility for retrieving the IFC Unique ID from a GameObject’s ifcProperties.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: UniqueIdExtractor (static).
 * - Key Methods:
 *     • TryGetUniqueId(GameObject go) – Scans ifcProperties for a key containing "Unique ID" and returns its value.
 * - Dependencies/Interactions:
 *     • ifcProperties – Must exist on GameObject for extraction.
 *     • Used by ObjectRegistry, IfcPropertyExtractor, QdrantUpsertBuilder, and Preview pipeline.
 * - Special Considerations:
 *     • Returns null if no matching property/value found.
 *     • Case-insensitive search for "unique id".
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * string uid = UniqueIdExtractor.TryGetUniqueId(myGo);
 * if (!string.IsNullOrEmpty(uid))
 *     Debug.Log("Unique ID: " + uid);
 * ```
 */

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

public static class UniqueIdExtractor
{
    // Accept these key names for the IFC id (case-insensitive, substring ok)
    private static readonly string[] IdKeys = new[]
    {
        "unique id", "uniqueid", "unique_id",
        "globalid", "global id",
        "guid"
    };

    /// <summary>
    /// Try to get the IFC Unique ID from exactly this GameObject.
    /// Accepts multiple key names (Unique ID / GlobalId / GUID), case-insensitive.
    /// Returns null when not found.
    /// </summary>
    public static string TryGetUniqueId(GameObject go)
    {
        if (go == null) return null;
        var ifc = go.GetComponent<ifcProperties>();
        if (ifc == null || ifc.properties == null || ifc.nominalValues == null) return null;

        int count = Mathf.Min(ifc.properties.Count, ifc.nominalValues.Count);
        for (int i = 0; i < count; i++)
        {
            var key = ifc.properties[i];
            var val = ifc.nominalValues[i];
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(val)) continue;

            var lk = key.ToLowerInvariant();
            if (IdKeys.Any(k => lk.Contains(k)))
                return val.Trim();
        }
        return null;
    }

    /// <summary>
    /// Walk up parents to find an IFC id if the leaf doesn't have one.
    /// </summary>
    public static string TryGetUniqueIdWalkUp(GameObject leaf)
    {
        var n = leaf;
        while (n != null)
        {
            var uid = TryGetUniqueId(n);
            if (!string.IsNullOrWhiteSpace(uid)) return uid;
            n = n.transform?.parent ? n.transform.parent.gameObject : null;
        }
        return null;
    }

    /// <summary>
    /// Returns a non-empty ID for this GameObject:
    /// 1) IFC id on self or parents (Unique ID / GlobalId / GUID).
    /// 2) Otherwise a deterministic "stable" id derived from scene path + geometry signature.
    /// No "unknown" is ever returned.
    /// </summary>
    public static string GetOrStable(GameObject go)
    {
        // 1) Prefer IFC id on self/parents
        var uid = TryGetUniqueId(go) ?? TryGetUniqueIdWalkUp(go);
        if (!string.IsNullOrWhiteSpace(uid) && !uid.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            return uid;

        // 2) Deterministic fallback (UUID-like) from stable seed
        var sceneName = go.scene.IsValid() ? go.scene.name : "UnknownScene";
        var path = GetHierarchyPath(go);

        // Geometry signature (sharedMesh; safe in edit/runtime)
        //int vtx = 0, tri = 0;
        //Bounds? b = null;
        var mf = go.GetComponent<MeshFilter>();
        var smr = go.GetComponent<SkinnedMeshRenderer>();
        /*Mesh m = mf?.sharedMesh ?? smr?.sharedMesh;
        if (m != null)
        {
            vtx = m.vertexCount;
            tri = m.triangles != null ? m.triangles.Length / 3 : 0;
            b = m.bounds;
        }
        */

        Mesh m = mf?.sharedMesh ?? smr?.sharedMesh;
        int vtx = 0, tri = 0;
        Bounds? b = null;

        if (m != null)
        {
            vtx = m.vertexCount;      // OK even if not readable
            if (m.isReadable)         // <-- use isReadable
                tri = (m.triangles != null) ? m.triangles.Length / 3 : 0;

            b = m.bounds;             // OK even if not readable
        }



        var seed = $"{sceneName}|{path}|{vtx}|{tri}|{(b.HasValue ? $"{b.Value.size.x:F4},{b.Value.size.y:F4},{b.Value.size.z:F4}" : "no-bounds")}";
        return DeterministicGuid(seed);
    }

    private static string GetHierarchyPath(GameObject go)
    {
        var sb = new StringBuilder(go.name);
        var p = go.transform.parent;
        while (p != null)
        {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }
        return sb.ToString();
    }

    /// <summary>
    /// Create a deterministic GUID-like string from a seed. Uses SHA1→GUID formatting.
    /// </summary>
    private static string DeterministicGuid(string seed)
    {
        using (var sha1 = SHA1.Create())
        {
            var hash = sha1.ComputeHash(Encoding.UTF8.GetBytes(seed ?? ""));
            // format first 16 bytes into a GUID
            var bytes = new byte[16];
            Array.Copy(hash, bytes, 16);
            var g = new Guid(bytes);
            return g.ToString();
        }
    }
}
