// RtsCamera.cs - a camera that looks down at a point on the ground. Arrow
// keys, the screen edge and a middle drag pan it, the wheel zooms.
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class RtsCamera : MonoBehaviour
    {
        public Vector3 focus;
        public float height = 28f;
        public float pitch = 60f;
        public float panSpeed = 25f;
        public float minHeight = 8f, maxHeight = 70f;
        public float mapSize = 64f;
        public bool edgePan = true;
        public int edgePixels = 8;

        Vector3 lastMouse;

        void LateUpdate()
        {
            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow)) move.z += 1;
            if (Input.GetKey(KeyCode.DownArrow)) move.z -= 1;
            if (Input.GetKey(KeyCode.RightArrow)) move.x += 1;
            if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1;

            var m = Input.mousePosition;
            bool inside = m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height;
            if (edgePan && Application.isFocused && inside)
            {
                if (m.x < edgePixels) move.x -= 1;
                if (m.x > Screen.width - edgePixels) move.x += 1;
                if (m.y < edgePixels) move.z -= 1;
                if (m.y > Screen.height - edgePixels) move.z += 1;
            }
            if (move.sqrMagnitude > 1) move.Normalize();
            focus += move * panSpeed * (height / 28f) * Time.unscaledDeltaTime;

            // Middle drag moves the ground with the mouse.
            if (Input.GetMouseButton(2) && !Input.GetMouseButtonDown(2))
            {
                var d = m - lastMouse;
                float perPixel = height * 2f / Mathf.Max(1, Screen.height);
                focus -= new Vector3(d.x, 0, d.y) * perPixel;
            }
            lastMouse = m;

            if (inside)
            {
                float scroll = Input.mouseScrollDelta.y;
                height = Mathf.Clamp(height * (1f - scroll * 0.1f), minHeight, maxHeight);
            }

            focus.x = Mathf.Clamp(focus.x, 0, mapSize);
            focus.z = Mathf.Clamp(focus.z, 0, mapSize);
            focus.y = 0;
            transform.rotation = Quaternion.Euler(pitch, 0, 0);
            transform.position = focus - transform.forward * (height / Mathf.Sin(pitch * Mathf.Deg2Rad));
        }
    }
}
