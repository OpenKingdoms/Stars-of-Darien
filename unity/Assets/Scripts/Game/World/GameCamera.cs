// GameCamera.cs - the RTS camera over a backend's map. Arrows, the
// screen edge or a middle drag pan it, the wheel zooms, Q and E or an Alt
// middle drag turn it, Page Up and Page Down tilt it, and Home returns to
// the classic view: north up, looking steeply down. It rides the ground.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class GameCamera : MonoBehaviour
    {
        public const float ClassicPitch = 62f;

        public Vector3 focus;
        public float distance = 34f, pitch = ClassicPitch, yaw;
        public float minDistance = 8f, maxDistance = 110f, minPitch = 25f, maxPitch = 89f;
        public float panSpeed = 28f, turnSpeed = 90f, tiltSpeed = 40f;
        public Vector2 boundsMin, boundsMax = new Vector2(64, 0);
        public bool edgePan = true, keyboard = true;
        public Func<float, float, float> ground;

        float targetDistance;
        Vector3 lastMouse;

        void Awake() => targetDistance = distance;

        public void Frame(Vector2 min, Vector2 max, Vector3 at)
        {
            boundsMin = min;
            boundsMax = max;
            focus = at;
            targetDistance = distance;
            Apply(1f);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var m = Input.mousePosition;
            bool inside = m.x >= 0 && m.y >= 0 && m.x <= Screen.width && m.y <= Screen.height;
            var move = Vector3.zero;
            if (keyboard)
            {
                if (Input.GetKey(KeyCode.UpArrow)) move.z += 1;
                if (Input.GetKey(KeyCode.DownArrow)) move.z -= 1;
                if (Input.GetKey(KeyCode.RightArrow)) move.x += 1;
                if (Input.GetKey(KeyCode.LeftArrow)) move.x -= 1;
                if (Input.GetKey(KeyCode.Q)) yaw += turnSpeed * dt;
                if (Input.GetKey(KeyCode.E)) yaw -= turnSpeed * dt;
                if (Input.GetKey(KeyCode.PageUp)) pitch += tiltSpeed * dt;
                if (Input.GetKey(KeyCode.PageDown)) pitch -= tiltSpeed * dt;
                if (Input.GetKeyDown(KeyCode.Home)) { yaw = 0; pitch = ClassicPitch; }
            }
            if (edgePan && Application.isFocused && inside && !Application.isBatchMode)
            {
                const int edge = 6;
                if (m.x < edge) move.x -= 1;
                if (m.x > Screen.width - edge) move.x += 1;
                if (m.y < edge) move.z -= 1;
                if (m.y > Screen.height - edge) move.z += 1;
            }
            if (move.sqrMagnitude > 1) move.Normalize();
            var turn = Quaternion.Euler(0, yaw, 0);
            focus += turn * move * panSpeed * (distance / 34f) * dt;

            if (Input.GetMouseButton(2) && !Input.GetMouseButtonDown(2))
            {
                var d = m - lastMouse;
                if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
                {
                    yaw += d.x * 0.25f;
                    pitch -= d.y * 0.2f;
                }
                else
                {
                    float perPixel = distance * 1.2f / Mathf.Max(1, Screen.height);
                    focus -= turn * new Vector3(d.x, 0, d.y) * perPixel;
                }
            }
            lastMouse = m;

            if (inside) targetDistance = Mathf.Clamp(targetDistance * (1f - Input.mouseScrollDelta.y * 0.12f), minDistance, maxDistance);
            Apply(dt);
        }

        void Apply(float dt)
        {
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-12f * dt));
            focus.x = Mathf.Clamp(focus.x, boundsMin.x, boundsMax.x);
            focus.z = Mathf.Clamp(focus.z, boundsMin.y, boundsMax.y);
            float g = ground != null ? ground(focus.x, focus.z) : 0f;
            focus.y = Mathf.Lerp(focus.y, g, 1f - Mathf.Exp(-6f * dt));
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            var pos = focus - transform.forward * distance;
            // Never dip under the ground below the camera.
            if (ground != null) pos.y = Mathf.Max(pos.y, ground(pos.x, pos.z) + 2f);
            transform.position = pos;
        }
    }
}
