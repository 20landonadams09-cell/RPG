using UnityEngine;
using BasicRPG.Interaction;

namespace BasicRPG.Player
{
    /// <summary>
    /// Orbiting third-person follow camera. Mouse X orbits yaw, Mouse Y orbits pitch
    /// (clamped). Position is computed behind the target at a fixed distance/height.
    /// Locks and hides the cursor on start.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 6f;
        [SerializeField] private float height = 2.5f;
        [SerializeField] private float mouseSensitivity = 3f;
        [SerializeField] private float padSensitivity = 3f;   // right-stick look (DualSense)
        [SerializeField] private float pitchMin = -10f;
        [SerializeField] private float pitchMax = 60f;
        [SerializeField] private float followSmooth = 10f;

        private float yaw;
        private float pitch = 20f;

        void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (target != null) yaw = target.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (target == null) return;

            // Don't orbit while a dialogue/inventory UI is open (cursor is free for clicking).
            if (!InteractionLock.IsLocked)
            {
                // Mouse look + right-stick look (DualSense). The pad axes ("RightStickX/Y") are
                // added to InputManager.asset by the scene builder; if absent, GetAxis returns 0
                // and mouse-only play is unaffected. RightStickY is inverted in the Input Manager so
                // up on the stick looks up (pitch decreases) — same sign as Mouse Y.
                yaw += Input.GetAxis("Mouse X") * mouseSensitivity
                       + Input.GetAxis("RightStickX") * padSensitivity;
                pitch -= Input.GetAxis("Mouse Y") * mouseSensitivity
                         + Input.GetAxis("RightStickY") * padSensitivity;
                pitch = Mathf.Clamp(pitch, pitchMin, pitchMax);
            }

            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 targetPos = target.position + Vector3.up * height;
            Vector3 desiredPos = targetPos - (rotation * Vector3.forward * distance);

            transform.position = Vector3.Lerp(transform.position, desiredPos, followSmooth * Time.deltaTime);
            transform.LookAt(targetPos);
        }

        public void SetTarget(Transform t) => target = t;
    }
}