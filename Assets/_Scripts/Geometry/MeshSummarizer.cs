/*
 * Script Summary:
 * ----------------
 * Analyzes a Unity Mesh and produces a metadata summary (bounding box, counts, holes, orientation).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MeshSummarizer (static).
 * - Key Methods:
 *     • Summarize(mesh) – Returns Dictionary with boundingBox, vertexCount, triangleCount, holes, meshSummaryText.
 *     • DescribeMesh(...) – Builds human-readable description string.
 *     • DetectMeshHoles(mesh) – Finds boundary edge loops representing holes.
 * - Helper Structs:
 *     • Edge – Unique representation of triangle edges for boundary detection.
 * - Special Considerations:
 *     • Orientation guess: vertical / horizontal / depth-facing / complex.
 *     • Holes reported with count and loop sizes.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var summary = MeshSummarizer.Summarize(myMesh);
 * Debug.Log(summary["meshSummaryText"]);
 * ```
 */

using UnityEngine;
using System.Collections.Generic;
using System.Text;

public static class MeshSummarizer
{
    public static Dictionary<string, object> Summarize(Mesh mesh)
    {
        var summary = new Dictionary<string, object>();

        if (mesh == null)
        {
            summary["error"] = "Mesh is null";
            return summary;
        }

        var bounds = mesh.bounds;
        var min = bounds.min;
        var max = bounds.max;

        summary["boundingBox"] = new Dictionary<string, float[]>
        {
            { "min", new float[] { min.x, min.y, min.z } },
            { "max", new float[] { max.x, max.y, max.z } }
        };

        int vertexCount = mesh.vertexCount;
        //int triangleCount = mesh.triangles.Length / 3;

        int triangleCount = 0;
        if (mesh.isReadable)           // <-- guard
        {
            triangleCount = (mesh.triangles != null) ? mesh.triangles.Length / 3 : 0;
            // any other index-based analysis stays inside this block
        }
        // else: skip index work; keep bounds + vertexCount

        summary["vertexCount"] = vertexCount;
        summary["triangleCount"] = triangleCount;

        var holes = DetectMeshHoles(mesh);
        var holeSizes = holes.ConvertAll(loop => loop.Count);
        summary["holes"] = new Dictionary<string, object>
        {
            { "count", holes.Count },
            { "loopSizes", holeSizes }
        };

        float[] bboxSize = new float[]
        {
            max.x - min.x,
            max.y - min.y,
            max.z - min.z
        };

        summary["meshSummaryText"] = DescribeMesh(vertexCount, triangleCount, holeSizes, bboxSize);

        return summary;
    }

    public static string DescribeMesh(int vertexCount, int triangleCount, List<int> holeSizes, float[] bboxSize)
    {
        StringBuilder sb = new StringBuilder();

        sb.Append($"This mesh has {vertexCount} vertices and {triangleCount} triangles. ");

        // Bounding box
        sb.Append($"Its bounding box spans approximately {bboxSize[0]:0.##}m (W) × {bboxSize[1]:0.##}m (H) × {bboxSize[2]:0.##}m (D). ");

        // Orientation
        string orientation = GetOrientation(bboxSize);
        sb.Append($"It is likely a {orientation}-oriented surface. ");

        // Holes
        if (holeSizes.Count > 0)
        {
            sb.Append($"It contains {holeSizes.Count} hole");
            if (holeSizes.Count > 1) sb.Append("s");
            sb.Append(" with edge loops of ");
            sb.Append(string.Join(", ", holeSizes));
            sb.Append(" segments respectively.");
        }
        else
        {
            sb.Append("It contains no visible holes.");
        }

        return sb.ToString();
    }

    private static string GetOrientation(float[] size)
    {
        float x = size[0], y = size[1], z = size[2];

        if (y > x && y > z)
            return "vertically standing";
        else if (x > y && x > z)
            return "horizontal (floor-like)";
        else if (z > y && z > x)
            return "depth-facing (like a wall)";
        else
            return "complex 3D shape";
    }

    private struct Edge
    {
        public int v1, v2;

        public Edge(int a, int b)
        {
            if (a < b) { v1 = a; v2 = b; }
            else { v1 = b; v2 = a; }
        }

        public override bool Equals(object obj)
        {
            return obj is Edge edge && v1 == edge.v1 && v2 == edge.v2;
        }

        public override int GetHashCode()
        {
            return v1.GetHashCode() ^ v2.GetHashCode();
        }
    }

    private static List<List<Edge>> DetectMeshHoles(Mesh mesh)
    {
        // Always return a valid list; treat unreadable meshes as having no detectable holes.
        var holes = new List<List<Edge>>();
        if (mesh == null) return holes;

        // Important: some runtime/instance meshes are not readable even if the asset is.
        // You cannot access triangles/indices when isReadable == false.
        if (!mesh.isReadable)
        {
            // Optional: uncomment once if you want a hint in the Console.
            // Debug.LogWarning($"[MeshSummarizer] Mesh '{mesh.name}' is not readable; skipping hole detection.");
            return holes; // hole count -> 0
        }

        // Use submesh-aware indices (safer than .triangles for some assets)
        var edgeUsage = new Dictionary<Edge, int>(1024);

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            var indices = mesh.GetIndices(sub); // safe when isReadable == true
            if (indices == null || indices.Length < 3) continue;

            // Ensure we only process full triangles
            int count = (indices.Length / 3) * 3;
            for (int i = 0; i < count; i += 3)
            {
                AddEdge(edgeUsage, new Edge(indices[i], indices[i + 1]));
                AddEdge(edgeUsage, new Edge(indices[i + 1], indices[i + 2]));
                AddEdge(edgeUsage, new Edge(indices[i + 2], indices[i]));
            }
        }

        // Boundary edges are those used exactly once
        var boundaryEdges = new HashSet<Edge>();
        foreach (var kvp in edgeUsage)
            if (kvp.Value == 1) boundaryEdges.Add(kvp.Key);

        // Trace boundary loops
        while (boundaryEdges.Count > 0)
        {
            // pick an arbitrary start
            Edge start = default;
            using (var it = boundaryEdges.GetEnumerator())
            {
                if (!it.MoveNext()) break;
                start = it.Current;
            }

            var loop = new List<Edge>();
            var current = start;
            loop.Add(current);
            boundaryEdges.Remove(current);

            while (true)
            {
                bool found = false;
                Edge next = default;

                // Find an edge whose v1 matches current.v2
                foreach (var e in boundaryEdges)
                {
                    if (e.v1 == current.v2)
                    {
                        next = e;
                        found = true;
                        break;
                    }
                }

                if (!found) break;

                current = next;
                loop.Add(current);
                boundaryEdges.Remove(current);

                // Closed the loop?
                if (current.v2 == start.v1) break;
            }

            if (loop.Count >= 3) // ignore tiny/noise loops
                holes.Add(loop);
        }

        return holes;
    }


    private static void AddEdge(Dictionary<Edge, int> edgeUsage, Edge edge)
    {
        if (!edgeUsage.ContainsKey(edge))
            edgeUsage[edge] = 1;
        else
            edgeUsage[edge]++;
    }
}
