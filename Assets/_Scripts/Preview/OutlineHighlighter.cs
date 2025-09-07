/*
 * Script Summary:
 * ----------------
 * Provides visual highlighting of GameObjects using either an outline material
 * or emissive glow via MaterialPropertyBlock.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: OutlineHighlighter – MonoBehaviour for selection/preview feedback.
 * - Key Methods:
 *     • Highlight(IEnumerable<GameObject>) – Adds outlineMaterial or glow MPB to all child renderers.
 *     • ClearAll() – Restores original materials and clears emissive glow.
 * - Key Fields:
 *     • outlineMaterial – Optional override material for outline pass (else emissive yellow glow is used).
 * - Dependencies/Interactions:
 *     • Called by PendingChangesController when presenting previews.
 *     • Works on all Renderer components in target hierarchy.
 * - Special Considerations:
 *     • Stores original sharedMaterials in dictionary to restore later.
 *     • Safe against missing/null GameObjects and renderers.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * highlighter.Highlight(new[] { go1, go2 });
 * // ...later
 * highlighter.ClearAll();
 * ```
 */

using System.Collections.Generic;
using UnityEngine;

public sealed class OutlineHighlighter : MonoBehaviour
{
    [Tooltip("Optional override. If null, we use MaterialPropertyBlock emissive tint.")]
    public Material outlineMaterial;

    private readonly Dictionary<Renderer, Material[]> _original = new();

    public void Highlight(IEnumerable<GameObject> objects)
    {
        foreach (var go in objects)
        {
            if (!go) continue;
            foreach (var rend in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!_original.ContainsKey(rend))
                    _original[rend] = rend.sharedMaterials;

                if (outlineMaterial != null)
                {
                    var mats = rend.sharedMaterials;
                    var newMats = new Material[mats.Length + 1];
                    for (int i = 0; i < mats.Length; i++) newMats[i] = mats[i];
                    newMats[mats.Length] = outlineMaterial;
                    rend.sharedMaterials = newMats;
                }
                else
                {
                    // Non-destructive glow using MPB
                    var mpb = new MaterialPropertyBlock();
                    rend.GetPropertyBlock(mpb);
                    mpb.SetColor("_EmissionColor", Color.yellow);
                    rend.SetPropertyBlock(mpb);
                }
            }
        }
    }

    public void ClearAll()
    {
        foreach (var kv in _original)
        {
            if (!kv.Key) continue;
            kv.Key.sharedMaterials = kv.Value;
            // Clear MPB if we used glow path
            var mpb = new MaterialPropertyBlock();
            kv.Key.SetPropertyBlock(mpb);
        }
        _original.Clear();
    }
}
