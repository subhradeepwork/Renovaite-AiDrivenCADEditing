/*
 * Script Summary:
 * ----------------
 * Utility for procedurally creating basic architectural meshes (floor, wall, roof, door, window, pool).
 *
 * Developer Notes:
 * ----------------
 * - Main Class: MeshBuilder (static).
 * - Key Methods:
 *     • CreateFloor(size, mat) – Box mesh with enforced min thickness.
 *     • CreateWall(size, mat) – Standard vertical box mesh.
 *     • CreateRoof(size, mat) – Thin flat box mesh.
 *     • CreateWindow(size, mat), CreateDoor(size, mat) – Cube primitives scaled and optionally materialized.
 *     • CreateSwimmingPool(size, mat) – Generates hollow box (currently solid fallback).
 * - Helper Methods:
 *     • GenerateBox(width,height,depth) – Creates box mesh from scratch.
 *     • GenerateHollowBox(...) – Placeholder (returns solid for now).
 * - Special Considerations:
 *     • Logs errors for invalid or failed mesh generation.
 *     • Defaults to position at origin (0,0,0).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var wall = MeshBuilder.CreateWall(new Vector3(5,3,0.2f), concreteMat);
 * wall.transform.position = new Vector3(0,0,0);
 * ```
 */

using UnityEngine;

public static class MeshBuilder
{
    public static GameObject CreateFloor(Vector3 size, Material material)
    {
        GameObject floor = new GameObject("Floor");
        MeshFilter mf = floor.AddComponent<MeshFilter>();
        MeshRenderer mr = floor.AddComponent<MeshRenderer>();
        mr.material = material;

        float width = size.x;
        float depth = size.z;
        float height = Mathf.Max(size.y, 0.3f); // Enforce minimum thickness

        Mesh mesh = GenerateBox(width, height, depth);
        if (mesh == null)
        {
            Debug.LogError("Mesh generation failed for Floor!");
            return floor;
        }
        mf.mesh = mesh;

        floor.transform.position = Vector3.zero;
        return floor;
    }

    public static GameObject CreateWall(Vector3 size, Material material)
    {
        GameObject wall = new GameObject("Wall");
        MeshFilter mf = wall.AddComponent<MeshFilter>();
        MeshRenderer mr = wall.AddComponent<MeshRenderer>();
        mr.material = material;

        Mesh mesh = GenerateBox(size.x, size.y, size.z);
        if (mesh == null)
        {
            Debug.LogError("Mesh generation failed for Wall!");
            return wall;
        }
        mf.mesh = mesh;

        wall.transform.position = Vector3.zero;
        return wall;
    }

    public static GameObject CreateRoof(Vector3 size, Material material)
    {
        GameObject roof = new GameObject("Roof");
        MeshFilter mf = roof.AddComponent<MeshFilter>();
        MeshRenderer mr = roof.AddComponent<MeshRenderer>();
        mr.material = material;

        float width = size.x;
        float depth = size.z;
        float height = 0.3f; // Roofs are thin and flat

        Mesh mesh = GenerateBox(width, height, depth);
        if (mesh == null)
        {
            Debug.LogError("Mesh generation failed for Roof!");
            return roof;
        }
        mf.mesh = mesh;

        roof.transform.position = Vector3.zero;
        return roof;
    }

    public static GameObject CreateWindow(Vector3 size, Material mat)
    {
        GameObject window = GameObject.CreatePrimitive(PrimitiveType.Cube);
        window.name = "Window";
        window.transform.localScale = size;

        if (mat != null)
            window.GetComponent<Renderer>().material = mat;

        return window;
    }

    public static GameObject CreateDoor(Vector3 size, Material mat)
    {
        GameObject door = GameObject.CreatePrimitive(PrimitiveType.Cube);
        door.name = "Door";
        door.transform.localScale = size;

        if (mat != null)
            door.GetComponent<Renderer>().material = mat;

        return door;
    }

    public static GameObject CreateSwimmingPool(Vector3 size, Material material)
    {
        GameObject pool = new GameObject("SwimmingPool");
        MeshFilter mf = pool.AddComponent<MeshFilter>();
        MeshRenderer mr = pool.AddComponent<MeshRenderer>();
        mr.material = material;

        Mesh mesh = GenerateHollowBox(size.x, size.y, size.z, 0.3f);
        if (mesh == null)
        {
            Debug.LogError("Mesh generation failed for Swimming Pool!");
            return pool;
        }
        mf.mesh = mesh;

        pool.transform.position = Vector3.zero;
        return pool;
    }

    // ----------------------
    // Mesh Generator Helpers
    // ----------------------

    private static Mesh GenerateBox(float width, float height, float depth)
    {
        if (width <= 0 || height <= 0 || depth <= 0)
        {
            Debug.LogError($"GenerateBox error: Invalid size (width={width}, height={height}, depth={depth})");
            return null;
        }

        Mesh mesh = new Mesh();

        Vector3[] vertices = {
            new Vector3(0, 0, 0),
            new Vector3(width, 0, 0),
            new Vector3(width, 0, depth),
            new Vector3(0, 0, depth),

            new Vector3(0, height, 0),
            new Vector3(width, height, 0),
            new Vector3(width, height, depth),
            new Vector3(0, height, depth),
        };

        int[] triangles = {
            0, 2, 1, 0, 3, 2, // Bottom
            4, 5, 6, 4, 6, 7, // Top
            0, 1, 5, 0, 5, 4, // Front
            1, 2, 6, 1, 6, 5, // Right
            2, 3, 7, 2, 7, 6, // Back
            3, 0, 4, 3, 4, 7  // Left
        };

        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        return mesh;
    }

    private static Mesh GenerateHollowBox(float width, float height, float depth, float wallThickness)
    {
        // For now, still a solid box.
        return GenerateBox(width, height, depth);
    }
}
