using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class IfcInspectorPanel : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_Text headerTMP;   // optional
    [SerializeField] private Text headerText;  // optional
    [SerializeField] private Transform listRoot;   // ScrollView/Content
    [SerializeField] private GameObject rowPrefab; // prefab with two labels inside
    [SerializeField] private Button closeButton;   // optional

    private Action _onClose;

    void Awake()
    {
        if (closeButton) closeButton.onClick.AddListener(() => _onClose?.Invoke());
    }

    public void Show(string title, IEnumerable<KeyValuePair<string, string>> rows, Action onClose)
    {
        ClearList();
        SetHeader(title);

        foreach (var kv in rows)
        {
            var row = Instantiate(rowPrefab, listRoot);
            SetRowTexts(row, kv.Key, kv.Value);
        }

        _onClose = onClose;
        if (!gameObject.activeSelf) gameObject.SetActive(true);
    }

    public void CloseAndDestroy()
    {
        Destroy(gameObject);
    }

    private void ClearList()
    {
        if (!listRoot) return;
        for (int i = listRoot.childCount - 1; i >= 0; i--)
            Destroy(listRoot.GetChild(i).gameObject);
    }

    private void SetHeader(string text)
    {
        if (headerTMP) { headerTMP.text = text; return; }
        if (headerText) { headerText.text = text; return; }

        // Auto-find if not wired
        var tmp = GetComponentInChildren<TMP_Text>(true);
        if (tmp) { headerTMP = tmp; headerTMP.text = text; return; }
        var legacy = GetComponentInChildren<Text>(true);
        if (legacy) { headerText = legacy; headerText.text = text; return; }
    }

    private static void SetRowTexts(GameObject row, string key, string value)
    {
        // Try TMP first
        var tmps = row.GetComponentsInChildren<TMP_Text>(true);
        if (tmps != null && tmps.Length >= 2)
        {
            tmps[0].text = key;
            tmps[1].text = value;
            return;
        }
        // Fallback to legacy Text
        var texts = row.GetComponentsInChildren<Text>(true);
        if (texts != null && texts.Length >= 2)
        {
            texts[0].text = key;
            texts[1].text = value;
            return;
        }
        Debug.LogWarning("[IfcInspectorPanel] Row prefab needs two text fields (key/value).");
    }
}
