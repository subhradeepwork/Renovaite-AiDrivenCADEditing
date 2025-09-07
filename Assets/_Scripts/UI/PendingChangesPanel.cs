/*
 * Script Summary:
 * ----------------
 * UI component for previewing pending AI-driven changes before applying.
 * Displays a header and a list of affected objects (Name, ID, Action),
 * with Accept/Reject buttons wired to callbacks.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: PendingChangesPanel – MonoBehaviour for confirmation UI.
 * - Key Methods:
 *     • Show(header, rows, onAccept, onReject) – Builds UI list and wires callbacks.
 *     • CloseAndDestroy() – Closes and destroys the panel GameObject.
 * - Helper Methods:
 *     • ClearList() – Clears existing child rows from listRoot.
 *     • SetHeader(text) – Sets panel header (TMP or legacy Text).
 *     • SetTextOn(go, text) – Assigns text to row prefab (TMP or legacy Text).
 * - Key Fields:
 *     • headerText / headerTMP – Support for UGUI Text or TextMeshPro.
 *     • listRoot – Parent transform for row items (ScrollView content).
 *     • rowPrefab – Prefab with Name/ID/Action child text fields.
 *     • acceptButton / rejectButton – Action buttons for confirmation.
 * - Dependencies/Interactions:
 *     • Consumed by PendingChangesController, which builds RowData list and invokes Show().
 *     • Rows populated from RowData struct (Name, ID, Action).
 * - Special Considerations:
 *     • Supports both TMP_Text and legacy Text.
 *     • Prefab can start inactive; Show() ensures activation.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var panel = Instantiate(panelPrefab, canvasTransform);
 * var rows = new List<RowData> {
 *     new RowData { Name="Wall", ID="WALL_001", Action="scale ×2" }
 * };
 * panel.Show("Will apply to 1 object.", rows,
 *     onAccept: () => Debug.Log("Accepted"),
 *     onReject: () => Debug.Log("Rejected"));
 * ```
 */

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class PendingChangesPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text headerText;      // optional (UGUI)
    [SerializeField] private TMP_Text headerTMP;   // optional (TMP)
    [SerializeField] private Transform listRoot;   // ScrollView Content
    [SerializeField] private GameObject rowPrefab; // Prefab with Text or TMP_Text inside
    [SerializeField] private Button acceptButton;
    [SerializeField] private Button rejectButton;

    private Action _onAccept, _onReject;

    void Awake()
    {
        if (acceptButton != null) acceptButton.onClick.AddListener(() => _onAccept?.Invoke());
        if (rejectButton != null) rejectButton.onClick.AddListener(() => _onReject?.Invoke());
    }

    /// <summary>Builds the panel and wires callbacks. The prefab can be active or inactive; we force-enable it.</summary>


    public void Show(string header, IEnumerable<RowData> rows, Action onAccept, Action onReject)
    {
        ClearList();
        SetHeader(header);

        foreach (var row in rows)
        {
            var go = Instantiate(rowPrefab, listRoot);

            var nameText = go.transform.Find("Name")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var idText = go.transform.Find("ID")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var actionText = go.transform.Find("Action")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var typeText = go.transform.Find("Type")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var levelText = go.transform.Find("Level")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var parentPathText = go.transform.Find("ParentPath")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var selectionSourceText = go.transform.Find("SelectionSource")?.GetChild(0).GetComponent<TextMeshProUGUI>();
            var tagsText = go.transform.Find("Tags")?.GetChild(0).GetComponent<TextMeshProUGUI>();



            if (nameText != null) nameText.text = row.Name;
            if (idText != null) idText.text = row.ID;
            if (actionText != null) actionText.text = row.Action;
            if (typeText != null) typeText.text = row.Type;
            if (levelText != null) levelText.text = row.Level;
            if (parentPathText != null) parentPathText.text = row.ParentPath;
            if (selectionSourceText != null) selectionSourceText.text = row.SelectionSource;
            if (tagsText != null) tagsText.text = row.Tags;

        }

        _onAccept = onAccept;
        _onReject = onReject;

        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }




    public void CloseAndDestroy()
    {
        Destroy(gameObject);
    }

    private void ClearList()
    {
        if (listRoot == null) return;
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);
    }

    private void SetHeader(string text)
    {
        if (headerTMP != null) { headerTMP.text = text; return; }
        if (headerText != null) { headerText.text = text; return; }

        var tmp = GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { headerTMP = tmp; headerTMP.text = text; return; }
        var legacy = GetComponentInChildren<Text>(true);
        if (legacy != null) { headerText = legacy; headerText.text = text; return; }

        Debug.LogWarning("[PendingChangesPanel] No header text component found.");
    }

    private static void SetTextOn(GameObject go, string text)
    {
        var tmp = go.GetComponentInChildren<TMP_Text>(true);
        if (tmp != null) { tmp.text = text; return; }
        var legacy = go.GetComponentInChildren<Text>(true);
        if (legacy != null) { legacy.text = text; return; }
        Debug.LogWarning("[PendingChangesPanel] Row prefab has no Text or TMP_Text.");
    }
}

[System.Serializable]
public class RowData
{
    public string Name;
    public string ID;
    public string Action;
    public string Type;            // e.g., "Window"
    public string Level;           // e.g., "Level 2"
    public string ParentPath;      // e.g., "BuildingA/Level2"
    public string SelectionSource; // "Filters" | "Similar"
    public float? Similarity;      // 0..1 if Similar
    public string Delta;           // "Δpos(0,0,0.5m), Δrot(0,90,0), scale×(1,1,2)"
    public string MaterialChange;  // "Brick → Concrete"
    public string Replace;         // "Lib:Window_A → Window_B"
    public string Dimensions;      // "1.2×1.5×0.1 m"
    public string Tags;            // "external, glazed"
    public string Flags;           // "⚠ missing UID; 0 tris"
}
