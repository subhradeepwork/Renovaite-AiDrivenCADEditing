/*
 * Script Summary:
 * ----------------
 * ScriptableObject configuration for Qdrant connectivity.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: QdrantSettings (ScriptableObject).
 * - Key Fields:
 *     • baseUrl (string) – FastAPI proxy URL (e.g., http://127.0.0.1:8000).
 *     • defaultSceneId (string) – Scene identifier to tag objects with during upsert.
 *     • timeoutSeconds (int) – Request timeout (default 10s).
 *     • retryCount (int) – Retry attempts for failed requests.
 * - Dependencies/Interactions:
 *     • Consumed by QdrantApiClient and QdrantSelectionListener.
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * var settings = ScriptableObject.CreateInstance<QdrantSettings>();
 * settings.baseUrl = "http://localhost:8000";
 * settings.defaultSceneId = "OfficeScene";
 * ```
 */

using UnityEngine;

[CreateAssetMenu(fileName = "QdrantSettings", menuName = "Qdrant/Settings")]
public class QdrantSettings : ScriptableObject
{
    [Tooltip("FastAPI base URL, e.g., http://127.0.0.1:8000")]
    public string baseUrl = "http://127.0.0.1:8000";

    [Tooltip("Default scene id to include in /upsert")]
    public string defaultSceneId = "SampleScene";

    [Min(3)] public int timeoutSeconds = 10;
    [Min(0)] public int retryCount = 1;
}
