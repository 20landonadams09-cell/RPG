using UnityEngine;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Bronze (Seeker) — hear allomantic pulses. While burning, nearby <see cref="EnemyAllomancer"/>
    ///s that are themselves burning emit a directional ping (a pulse "sound" at their position,
    /// like Tin's scent) and a toast reports how many are in range. Suppressed enemies (inside a
    /// coppercloud) are silent. No key to hold — burn Bronze (B). Adapted from Ashwalker's Bronze
    /// for BasicRPG (no Seeker-vs-Smoker minigame; the testable effect is detecting enemy
    /// allomancers through the Bronze pulse).
    /// </summary>
    public class Bronze : MonoBehaviour
    {
        [SerializeField] private Allomancer allomancer;

        [Header("Pulse Sense")]
        public float senseRadius = 30f;
        public float pingInterval = 1.2f;
        public AudioClip pingClip;          // optional — silent if null
        [Range(0f, 0.5f)] public float pingVolume = 0.12f;
        public float toastInterval = 3f;    // how often to report the count

        private float pingTimer;
        private float toastTimer;
        private int lastReportedCount = -1;

        void Update()
        {
            if (allomancer == null) return;
            bool burning = allomancer.IsMetalBurning(MetalType.Bronze) &&
                           allomancer.GetReserve(MetalType.Bronze) > 0f;
            if (!burning) { lastReportedCount = -1; return; }

            Vector3 me = transform.position;
            // Flaring (hold R) extends how far you can hear allomantic pulses.
            float f = allomancer.FlareMultiplier;
            float radius = senseRadius * f;
            float sqrRadius = radius * radius;

            // Count burning, un-suppressed enemy allomancers in range.
            int count = 0;
            foreach (EnemyAllomancer a in EnemyAllomancer.All)
            {
                if (a == null || !a.IsBurning) continue;
                if ((a.transform.position - me).sqrMagnitude <= sqrRadius) count++;
            }

            // Directional pings at each burning allomancer's position.
            pingTimer -= Time.deltaTime;
            if (pingTimer <= 0f)
            {
                pingTimer = pingInterval;
                if (pingClip != null)
                {
                    foreach (EnemyAllomancer a in EnemyAllomancer.All)
                    {
                        if (a == null || !a.IsBurning) continue;
                        float sqr = (a.transform.position - me).sqrMagnitude;
                        if (sqr > sqrRadius) continue;
                        float vol = pingVolume * (1f - Mathf.Sqrt(sqr) / radius);
                        AudioSource.PlayClipAtPoint(pingClip, a.transform.position, vol);
                    }
                }
            }

            // Report the count when it changes (or on a slow interval as a heartbeat).
            toastTimer -= Time.deltaTime;
            if (count != lastReportedCount || toastTimer <= 0f)
            {
                toastTimer = toastInterval;
                lastReportedCount = count;
                if (count > 0)
                    NotificationUI.Show($"Bronze pulse: {count} allomancer{(count == 1 ? "" : "s")} nearby");
                else
                    NotificationUI.Show("Bronze pulse: silence");
            }
        }
    }
}