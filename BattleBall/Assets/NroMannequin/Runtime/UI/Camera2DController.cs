using UnityEngine;

namespace NroRig.UI
{
    public class Camera2DController : MonoBehaviour
    {
        public Camera targetCamera;
        public float minSize = 0.5f;
        public float maxSize = 6.0f;
        public float zoomSensitivity = 1.0f;
        public float defaultSize = 2.5f;
        public Vector3 defaultPosition = new Vector3(0, 0.5f, -10f);

        private Vector3 _dragOrigin;
        private bool _isDragging = false;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = GetComponent<Camera>();
        }

        private void Update()
        {
            if (targetCamera == null) return;

            // Scroll zoom
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (Mathf.Abs(scroll) > 0.01f)
            {
                targetCamera.orthographicSize = Mathf.Clamp(
                    targetCamera.orthographicSize - scroll * zoomSensitivity,
                    minSize,
                    maxSize
                );
            }

            // Right-click drag pan
            if (Input.GetMouseButtonDown(1))
            {
                _dragOrigin = targetCamera.ScreenToWorldPoint(Input.mousePosition);
                _isDragging = true;
            }

            if (Input.GetMouseButton(1) && _isDragging)
            {
                Vector3 currentPos = targetCamera.ScreenToWorldPoint(Input.mousePosition);
                Vector3 diff = _dragOrigin - currentPos;
                targetCamera.transform.position += diff;
            }

            if (Input.GetMouseButtonUp(1))
            {
                _isDragging = false;
            }

            // Home key resets view
            if (Input.GetKeyDown(KeyCode.Home))
            {
                ResetView();
            }
        }

        public void ResetView()
        {
            if (targetCamera != null)
            {
                targetCamera.orthographicSize = defaultSize;
                targetCamera.transform.position = defaultPosition;
            }
        }
    }
}
