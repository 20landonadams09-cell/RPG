using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Combat;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// An enemy that burns an allomantic metal to buff itself. Default is a Pewter-thug: while
    /// burning it is faster and hits harder (writes <see cref="Enemy.SetEnemyBuff"/>). It burns
    /// continuously unless suppressed by a nearby Coppercloud (<see cref="Copper"/>) — then it
    /// loses its buff. A Bronze-burning player (<see cref="Bronze"/>) can sense its pulse.
    /// Adapted from Ashwalker's allomancer-enemy concept for BasicRPG (no FlareManager: base burn
    /// only; no enemy inventory: it never runs dry on its own — only suppression stops it).
    /// </summary>
    public class EnemyAllomancer : MonoBehaviour
    {
        public static readonly List<EnemyAllomancer> All = new List<EnemyAllomancer>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() { All.Clear(); didSuppressFlag = false; }

        // One-shot signal that an allomancer enemy was just suppressed (burning → not, from a
        // Coppercloud). Consumed by the tutorial so a "suppress a Thug" step completes on the
        // actual event. Set-only here; the consumer clears it.
        private static bool didSuppressFlag;
        /// <summary>Returns true once if an EnemyAllomancer was suppressed since the last call, then clears it.</summary>
        public static bool ConsumeDidSuppress() { bool v = didSuppressFlag; didSuppressFlag = false; return v; }

        [SerializeField] private Enemy enemy;
        public MetalType metal = MetalType.Pewter;

        [Header("Pewter-thug buff (applied while burning)")]
        public float buffSpeed = 1.5f;
        public float buffDamage = 1.8f;

        [Header("Suppression feedback (Coppercloud)")]
        public Color suppressedTint = new Color(0.40f, 0.46f, 0.55f, 1f); // grey-blue "smoked" look

        public bool IsBurning { get; private set; } = true;

        private Renderer rend;
        private Color baseTint = Color.white;

        void OnEnable() => All.Add(this);
        void OnDisable()
        {
            All.Remove(this);
            if (enemy != null) enemy.SetEnemyBuff(1f, 1f); // restore normal stats on shutdown
        }

        void Start()
        {
            if (enemy == null) enemy = GetComponent<Enemy>();
            rend = GetComponent<Renderer>();
            if (rend != null && rend.material != null) baseTint = rend.material.color;
        }

        void Update()
        {
            // Suppressed by a Coppercloud → stop burning (lose the buff) while inside it.
            bool suppressed = Copper.IsWithinCloud(transform.position);
            bool wasBurning = IsBurning;
            IsBurning = !suppressed;

            // Visible feedback: tint the enemy grey-blue while suppressed (enforced each frame so
            // it survives the enemy's attack-telegraph flash), restore the base tint on resume.
            if (rend != null && rend.material != null)
            {
                if (suppressed) rend.material.color = suppressedTint;
                else if (!wasBurning) rend.material.color = baseTint; // just resumed burning
            }

            if (wasBurning != IsBurning)
            {
                if (!IsBurning) didSuppressFlag = true; // just suppressed → tutorial listens for this
                NotificationUI.Show(IsBurning ? "Thug resumes burning (Pewter)" : "Coppercloud suppresses a Thug");
            }

            if (enemy != null)
                enemy.SetEnemyBuff(IsBurning ? buffSpeed : 1f, IsBurning ? buffDamage : 1f);
        }
    }
}