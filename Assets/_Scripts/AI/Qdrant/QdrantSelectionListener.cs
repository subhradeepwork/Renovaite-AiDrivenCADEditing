/*
 * Script Summary:
 * ----------------
 * Unity MonoBehaviour that listens to selection changes and automatically upserts
 * the selected GameObject into Qdrant.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: QdrantSelectionListener.
 * - Key Methods:
 *     • OnEnable() – Initializes QdrantApiClient with QdrantSettings and subscribes to SelectionManager.SelectionChanged.
 *     • OnDisable() – Unsubscribes from SelectionManager.SelectionChanged.
 *     • OnSelectionChanged(GameObject go) – Builds an UpsertRequest from the object and posts it to Qdrant.
 * - Dependencies/Interactions:
 *     • Relies on QdrantUpsertBuilder to extract IFC + mesh metadata.
 *     • Uses QdrantApiClient to send upserts.
 *     • Requires QdrantSettings (loaded from Resources/Config/QdrantSettings if not assigned).
 * - Special Considerations:
 *     • Debounces rapid selection changes (50ms delay).
 *     • Logs success or error (with payload JSON on failure).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // Attach to a Unity scene object
 * var listener = gameObject.AddComponent<QdrantSelectionListener>();
 * listener.settings = Resources.Load<QdrantSettings>("Config/QdrantSettings");
 * ```
 */

using System;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Qdrant
{
    public class QdrantSelectionListener : MonoBehaviour
    {
        public QdrantSettings settings;
        private QdrantApiClient _client;
        private int _version; // tiny debounce to coalesce rapid changes

        private void OnEnable()
        {
            if (settings == null)
                settings = Resources.Load<QdrantSettings>("Config/QdrantSettings");
            _client = new QdrantApiClient(settings);
            _client.AttachSettings(settings);
            _client.OverrideBaseUrl(settings.baseUrl); // belt & suspenders


            SelectionManager.SelectionChanged += OnSelectionChanged;
        }

        private void OnDisable()
        {
            SelectionManager.SelectionChanged -= OnSelectionChanged;
        }

        private async void OnSelectionChanged(GameObject go)
        {
            var ticket = ++_version;
            // optional tiny debounce in case selection changes multiple times quickly
            await Task.Delay(50);
            if (ticket != _version) return;

            if (!QdrantUpsertBuilder.TryBuild(go, settings.defaultSceneId, out var req))
            {
                Debug.LogWarning("[Qdrant] Could not build /upsert payload (missing ifcProperties or mesh).:- "+go.name);
                return;
            }

            IfcTypeRegistry.Register(req.ifc_type_final);

            try
            {
                var res = await _client.UpsertAsync(req);
                Debug.Log($"[Qdrant] Upsert OK → {res.updated} | point={res.point_id}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Qdrant] Upsert failed: {ex.Message}\nPayload: {JsonConvert.SerializeObject(req)}");
            }
        }
    }
}
