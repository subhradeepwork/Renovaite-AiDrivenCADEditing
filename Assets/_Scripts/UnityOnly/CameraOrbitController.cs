/*
 * Script Summary:
 * ----------------
 * Provides mouse-controlled orbit, pan, and zoom functionality for a scene camera.
 *
 * Developer Notes:
 * ----------------
 * - Main Class: CameraOrbitController – MonoBehaviour requiring a Camera component.
 * - Key Features:
 *     • Right-click drag → Rotate around focus point.
 *     • Scroll wheel → Zoom in/out (clamped between min/max).
 *     • Middle-click (or Shift+Left-click) drag → Pan focus point.
 * - Key Fields:
 *     • focusPoint – Transform to orbit around (auto-created if null).
 *     • distance / zoomSpeed / minDistance / maxDistance – Zoom control.
 *     • rotationSpeed / panSpeed / smoothTime – Control responsiveness.
 * - Special Considerations:
 *     • Smooth Lerp applied to both camera position and rotation.
 *     • Prevents camera flipping (clamps pitch to -89°..89°).
 *
 * Usage Example:
 * ----------------
 * ```csharp
 * // Attach to a Camera in the scene
 * var orbit = cam.gameObject.AddComponent<CameraOrbitController>();
 * orbit.focusPoint = someTarget.transform;
 * ```
 */

using UnityEngine;

[RequireComponent(typeof(Camera))]
public class CameraOrbitController : MonoBehaviour
{
    public Transform focusPoint;
    public float distance = 10f;
    public float zoomSpeed = 5f;
    public float minDistance = 2f;
    public float maxDistance = 50f;

    public float rotationSpeed = 5f;
    public float panSpeed = 0.5f;
    public float smoothTime = 0.1f;

    private Vector3 rotationVelocity;
    private Vector3 currentRotation;
    private Vector3 desiredPosition;
    private Vector3 panVelocity;

    private Camera cam;

    void Start()
    {
        cam = GetComponent<Camera>();

        if (focusPoint == null)
        {
            GameObject focus = new GameObject("Camera Focus");
            focus.transform.position = transform.position + transform.forward * distance;
            focusPoint = focus.transform;
        }

        currentRotation = transform.eulerAngles;
    }

    void LateUpdate()
    {
        HandleRotation();
        HandleZoom();
        HandlePan();

        Quaternion rotation = Quaternion.Euler(currentRotation);
        desiredPosition = focusPoint.position - (rotation * Vector3.forward * distance);
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothTime);
        transform.rotation = Quaternion.Lerp(transform.rotation, rotation, smoothTime);
    }

    void HandleRotation()
    {
        if (Input.GetMouseButton(1)) // Right-click drag
        {
            float mouseX = Input.GetAxis("Mouse X") * rotationSpeed;
            float mouseY = -Input.GetAxis("Mouse Y") * rotationSpeed;

            currentRotation.x += mouseY;
            currentRotation.y += mouseX;
            currentRotation.x = Mathf.Clamp(currentRotation.x, -89f, 89f); // Avoid flipping
        }
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel") * zoomSpeed;
        distance -= scroll;
        distance = Mathf.Clamp(distance, minDistance, maxDistance);
    }

    void HandlePan()
    {
        if (Input.GetMouseButton(2) || (Input.GetMouseButton(0) && Input.GetKey(KeyCode.LeftShift)))
        {
            float panX = -Input.GetAxis("Mouse X") * panSpeed;
            float panY = -Input.GetAxis("Mouse Y") * panSpeed;

            Vector3 pan = transform.right * panX + transform.up * panY;
            focusPoint.position += pan;
        }
    }
}
