using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Marks an object as a source of sensory input for an Allomancer burning Tin, used to
    /// trigger sensory overload (the cost of enhanced senses). Ported verbatim from Ashwalker.
    /// A bright light or loud noise within `radius` fills Tin's overload meters.
    /// </summary>
    public class SensorySource : MonoBehaviour
    {
        public static System.Collections.Generic.List<SensorySource> ActiveSources =
            new System.Collections.Generic.List<SensorySource>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() => ActiveSources = new System.Collections.Generic.List<SensorySource>();

        public enum SourceType { BrightLight, LoudNoise }

        [Header("Settings")]
        public SourceType type = SourceType.BrightLight;

        [Tooltip("Intensity of the sensory input (0-1)")]
        [Range(0f, 1f)] public float intensity = 0.5f;

        [Tooltip("Maximum distance at which this source affects a Tin-burner")]
        public float radius = 10f;

        [Tooltip("How much the effect falls off over distance (1 = linear)")]
        public float falloff = 1f;

        void OnEnable() => ActiveSources.Add(this);
        void OnDisable() => ActiveSources.Remove(this);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = type == SourceType.BrightLight ? Color.yellow : Color.cyan;
            Gizmos.DrawWireSphere(transform.position, radius);
        }
    }
}