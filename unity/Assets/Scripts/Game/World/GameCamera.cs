// GameCamera.cs - the RTS camera over a backend's map. WASD, the arrows,
// the screen edge or a Shift middle drag pan it, the wheel zooms, a middle
// drag tilts and raises it (up and down) and turns it (left and right), as
// do Page Up, Page Down, Q and E, and Home returns to the classic view:
// north up, looking steeply down. It rides the ground.
using System;
using UnityEngine;

namespace OpenKingdomsUnity.Game.World
{
    public sealed class GameCamera : MonoBehaviour
    {
        public const float ClassicPitch = 62f;

        public Vector3 focus;
        public float distance = 34f, pitch = ClassicPitch, yaw;
        // From a low cinematic angle to nearly straight down.
        public float minDistance = 8f, maxDistance = 110f, minPitch = 18f, maxPitch = 88f;
        public float panSpeed = 28f, turnSpeed = 90f, tiltSpeed = 40f;
        public Vector2 boundsMin, boundsMax = new Vector2(64, 0);
        public bool edgePan = true, keyboard = true;
        public Func<float, float, float> ground;
        // How far past the playable edge the focus may go, and how far the
        // land beyond it reaches (the edge ring), both in world units. The
        // view is kept from ever reaching past the land.
        public float focusMargin = 4f, landPastEdge = 32f;

        float targetDistance;
        Vector3 lastMouse;

        void Awake() => targetDistance = distance;

        public void Frame(Vector2 min, Vector2 max, Vector3 at)
        {
            boundsMin = min;
            boundsMax = max;
            focus = at;
            focus.y = SmoothGround(at);
            targetDistance = distance;
            lift = 0;
            Apply(1f);
        }

        // Jumps straight to a distance, for scripted views.
        public void Zoom(float d)
        {
            targetDistance = distance = Mathf.Clamp(d, minDistance, maxDistance);
        }

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            var m = Input.mousePosition;
            bool inside = m.x >= -1 && m.y >= -1 && m.x <= Screen.width + 1 && m.y <= Screen.height + 1;
            var move = Vector3.zero;
            if (keyboard)
            {
                // WASD pans too, bare keys only: with Ctrl they are orders.
                bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
                bool typing = UnityEngine.EventSystems.EventSystem.current != null &&
                    UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject != null &&
                    UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject.GetComponent<UnityEngine.UI.InputField>() != null;
                bool wasd = !ctrl && !typing;
                if (Input.GetKey(KeyCode.UpArrow) || wasd && Input.GetKey(KeyCode.W)) move.z += 1;
                if (Input.GetKey(KeyCode.DownArrow) || wasd && Input.GetKey(KeyCode.S)) move.z -= 1;
                if (Input.GetKey(KeyCode.RightArrow) || wasd && Input.GetKey(KeyCode.D)) move.x += 1;
                if (Input.GetKey(KeyCode.LeftArrow) || wasd && Input.GetKey(KeyCode.A)) move.x -= 1;
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

            // Middle drag: up and down tilt the camera and raise or lower it
            // together, left and right turn it. With Shift it pans instead.
            if (Input.GetMouseButton(2) && !Input.GetMouseButtonDown(2))
            {
                var d = m - lastMouse;
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                {
                    float perPixel = distance * 1.2f / Mathf.Max(1, Screen.height);
                    focus -= turn * new Vector3(d.x, 0, d.y) * perPixel;
                }
                else
                {
                    yaw += d.x * 0.25f;
                    pitch += d.y * 0.2f;
                    targetDistance = Mathf.Clamp(targetDistance * (1f + d.y * 0.003f), minDistance, maxDistance);
                    distance = targetDistance;
                }
            }
            lastMouse = m;

            // The wheel zooms toward the ground under the pointer, anywhere
            // over the view.
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0 && inside)
            {
                float before = targetDistance;
                targetDistance = Mathf.Clamp(targetDistance * (1f - scroll * 0.12f), minDistance, maxDistance);
                var cam = GetComponent<Camera>();
                if (cam != null && targetDistance < before && PointerGround(cam.ScreenPointToRay(m), out var hit))
                {
                    var toward = hit - focus;
                    toward.y = 0;
                    focus += toward * (1f - targetDistance / before);
                }
            }
            Apply(dt);
        }

        bool PointerGround(Ray ray, out Vector3 at)
        {
            at = default;
            for (float t = 0.5f; t < 1500f; t += t < 60f ? 0.5f : 2f)
            {
                var p = ray.GetPoint(t);
                float g = ground != null ? ground(p.x, p.z) : 0f;
                if (p.y <= g) { at = p; return true; }
            }
            return false;
        }

        void Apply(float dt)
        {
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            distance = Mathf.Lerp(distance, targetDistance, 1f - Mathf.Exp(-12f * dt));
            focus.x = Mathf.Clamp(focus.x, boundsMin.x - focusMargin, boundsMax.x + focusMargin);
            focus.z = Mathf.Clamp(focus.z, boundsMin.y - focusMargin, boundsMax.y + focusMargin);
            // The camera rides the ground averaged over a wide patch round
            // the focus, eased over most of a second, so a cliff, a ramp or a
            // tall prop passing underneath does not lift or drop the view.
            focus.y = Mathf.Lerp(focus.y, SmoothGround(focus), 1f - Mathf.Exp(-dt / HeightEase));
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            var pos = focus - transform.forward * distance;
            // A soft floor: only if the camera would really go underground
            // does it ease up, never snapping from one frame to the next.
            float need = ground != null ? Mathf.Max(0f, ground(pos.x, pos.z) + Clearance - pos.y) : 0f;
            lift = Mathf.Lerp(lift, need, 1f - Mathf.Exp(-dt / 0.8f));
            pos.y += lift;
            transform.position = pos;
        }

        public const float HeightEase = 0.9f, SmoothRadius = 16f, Clearance = 1.5f;
        float lift;

        // The mean ground height over a disc round a point, from a 7 by 7
        // grid of samples.
        public float SmoothGround(Vector3 at)
        {
            if (ground == null) return 0f;
            float sum = 0;
            int n = 0;
            for (int j = -3; j <= 3; j++)
                for (int i = -3; i <= 3; i++)
                {
                    if (i * i + j * j > 10) continue;
                    float x = at.x + i * SmoothRadius / 3f, z = at.z + j * SmoothRadius / 3f;
                    x = Mathf.Clamp(x, boundsMin.x, boundsMax.x);
                    z = Mathf.Clamp(z, boundsMin.y, boundsMax.y);
                    sum += ground(x, z);
                    n++;
                }
            return sum / n;
        }
    }
}
