using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class IfcInspectorController : MonoBehaviour
{
    public static IfcInspectorController Instance { get; private set; }

    [Header("Prefab & Placement")]
    public IfcInspectorPanel inspectorPrefab; // assign prefab asset
    public Canvas targetCanvas;               // optional; auto-find/create if null

    [Header("Optional")]
    public Button infoButton;                 // wire this if you want automatic onClick hookup

    private IfcInspectorPanel _instance;
    private Canvas _ownedCanvas;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (infoButton) infoButton.onClick.AddListener(ShowForSelected);
    }

    public void ShowForSelected()
    {
        var sel = FindObjectOfType<SelectionManager>()?.GetSelectedGameObject();
        if (sel == null) { Debug.LogWarning("[Inspector] No selected object."); return; }

        ShowForObject(sel);
    }

    public void ShowForObject(GameObject go)
    {
        DestroyIfAny();

        var kv = IfcPropertyExtractor.Extract(go);
        string title = $"Inspector — {go.name}";

        var parent = GetCanvasTransform();
        _instance = Instantiate(inspectorPrefab, parent);
        _instance.gameObject.name = "IfcInspectorPanel (Instance)";

        _instance.Show(title, kv, onClose: DestroyIfAny);
    }

    private void DestroyIfAny()
    {
        if (_instance != null)
        {
            _instance.CloseAndDestroy();
            _instance = null;
        }
        if (_ownedCanvas != null)
        {
            Destroy(_ownedCanvas.gameObject);
            _ownedCanvas = null;
        }
    }

    private Transform GetCanvasTransform()
    {
        if (targetCanvas != null)
        {
            if (!targetCanvas.gameObject.activeSelf) targetCanvas.gameObject.SetActive(true);
            EnsureEventSystem();
            return targetCanvas.transform;
        }

        // Try find an existing scene canvas (include inactive)
        var canvases = Resources.FindObjectsOfTypeAll<Canvas>();
        foreach (var c in canvases)
        {
            if (c.gameObject.scene.IsValid())
            {
                if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
                EnsureEventSystem();
                return c.transform;
            }
        }

        // Create a temporary overlay canvas we own
        var go = new GameObject("InspectorCanvas (Temp)", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _ownedCanvas = canvas;

        EnsureEventSystem();
        return canvas.transform;
    }

    private void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }
}
