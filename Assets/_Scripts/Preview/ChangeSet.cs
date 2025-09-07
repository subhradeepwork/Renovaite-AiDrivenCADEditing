/*
 * Script Summary:
 * ----------------
 * Lightweight container class for tracking a pending AIInstruction and its context.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: ChangeSet – Holds normalized instruction data before execution.
 * - Key Fields:
 *     • normalizedArgsJson (string) – Canonical JSON string representation of the instruction.
 *     • instruction (AIInstruction) – Parsed DTO form of the JSON.
 *     • selectedObject (GameObject) – Object selected when request originated.
 *     • targets (List<GameObject>) – Final resolved target objects (selected + similar, if any).
 * - Dependencies/Interactions:
 *     • Built/consumed by PendingChangesController.
 *     • Passed to highlighter and preview UI for user review.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var set = new ChangeSet {
 *     normalizedArgsJson = json,
 *     instruction = JsonUtility.FromJson<AIInstruction>(json),
 *     selectedObject = selectedGO,
 *     targets = resolvedTargets
 * };
 * ```
 */

using System.Collections.Generic;
using UnityEngine;

public sealed class ChangeSet
{
    public string normalizedArgsJson;         // canonical JSON we’ll apply
    public AIInstruction instruction;         // parsed DTO
    public GameObject selectedObject;         // selection at time of request
    public List<GameObject> targets = new();  // resolved targets (selected + similar/ids)
}
