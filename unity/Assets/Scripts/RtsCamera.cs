// RtsCamera.cs - a free RTS camera looking at a point on the ground.
// Arrow keys, the screen edge and a middle drag pan it, Q and E turn it,
// Page Up and Page Down tilt it, and the wheel zooms. The default is the
// classic view: north up, looking steeply down.
using System;
using UnityEngine;

namespace OpenKingdomsUnity
{
    public sealed class RtsCamera : MonoBehaviour
    {
        public Vector3 focus;
        public float height = 28f;
        public float pitch = 60f;
        public float yaw = 0f;
        public float panSpeed = 25f;
        public float turnSpeed = 90f;
        public float tiltSpeed = 40f;
        public float minHeight = 8f, maxHeight = 70f;
        public float minPitch = 25f, maxPitch = 89f;
        // The ground the focus may move over, x and z in world units.
        public Vector2 boundsMin = Vector2.zero;
        public Vector2 boundsMax = new Vector2(64, 64);
        // Kept for the capsule demo, which sets a square map.
        public float mapSize { set { boundsMin = Vector2.zero; boundsMax = new Vector2(value, value); } }
        // The ground height under a point, so the camera rides the hills.
        public Func<Vector3, float> groundHeight;
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
            var turn = Quaternion.Euler(0, yaw, 0);
            focus += turn * move * panSpeed * (height / 28f) * Time.unscaledDeltaTime;

            if (Input.GetKey(KeyCode.Q)) yaw += turnSpeed * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.E)) yaw -= turnSpeed * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.PageUp)) pitch += tiltSpeed * Time.unscaledDeltaTime;
            if (Input.GetKey(KeyCode.PageDown)) pitch -= tiltSpeed * Time.unscaledDeltaTime;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            if (Input.GetKeyDown(KeyCode.Home)) { yaw = 0; pitch = 60f; }

            // Middle drag moves the ground with the mouse.
            if (Input.GetMouseButton(2) && !Input.GetMouseButtonDown(2))
            {
                var d = m - lastMouse;
                float perPixel = height * 2f / Mathf.Max(1, Screen.height);
                focus -= turn * new Vector3(d.x, 0, d.y) * perPixel;
            }
            lastMouse = m;

            if (inside)
            {
                float scroll = Input.mouseScrollDelta.y;
                height = Mathf.Clamp(height * (1f - scroll * 0.1f), minHeight, maxHeight);
            }

            focus.x = Mathf.Clamp(focus.x, boundsMin.x, boundsMax.x);
            focus.z = Mathf.Clamp(focus.z, boundsMin.y, boundsMax.y);
            float ground = groundHeight != null ? groundHeight(focus) : 0f;
            focus.y = Mathf.Lerp(focus.y, ground, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = focus - transform.forward * (height / Mathf.Sin(pitch * Mathf.Deg2Rad));
        }
    }
}
