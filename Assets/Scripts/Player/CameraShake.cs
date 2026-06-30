using UnityEngine;

namespace BasicRPG.Player
{
    /// <summary>
    /// Tiny camera-shake helper. Lives on the Main Camera and runs its LateUpdate AFTER
    /// ThirdPersonCamera (via [DefaultExecutionOrder(1000)]) so it adds a decaying positional
    /// jitter on top of the position the follow script just set — the follow resets position
    /// every frame, so fresh jitter is clean (no drift). Used by Tin (vibration + overload).
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CameraShake : MonoBehaviour
    {
        private float timer;
        private float duration;
        private float magnitude;

        /// <summary>Trigger a shake of `magnitude` world-units lasting `duration` seconds.</summary>
        public void Shake(float duration, float magnitude)
        {
            // Take the stronger of an ongoing shake and the new one.
            if (duration > this.duration - timer)
            {
                this.duration = duration;
                this.timer = duration;
            }
            this.magnitude = Mathf.Max(this.magnitude, magnitude);
        }

        void LateUpdate()
        {
            if (timer <= 0f) { magnitude = 0f; return; }
            timer -= Time.deltaTime;
            float currentMag = magnitude * Mathf.Clamp01(timer / Mathf.Max(0.0001f, duration));
            transform.position += Random.insideUnitSphere * currentMag;
            if (timer <= 0f) magnitude = 0f;
        }
    }
}