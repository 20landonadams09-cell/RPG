using System.Collections.Generic;
using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Copper (Smoker) — the coppercloud. While burning, the player emits a cloud that
    /// <b>suppresses nearby enemy allomancers</b> (they stop burning and lose their buffs — see
    /// <see cref="EnemyAllomancer"/>). It also "hides" the player's own allomancy from Seekers in
    /// lore; BasicRPG has no Bronze-burning enemies yet, so that side is flavour. No key to hold
    /// — burn Copper (B) and the cloud is up; stop and it drops. Flaring (hold R) widens the
    /// cloud. Adapted from Ashwalker's Copper for BasicRPG (no enemy Seeker AI — the testable
    /// effect is suppression).
    /// </summary>
    public class Copper : MonoBehaviour
    {
        /// <summary>Active copperclouds in the scene. EnemyAllomancer checks these each frame.</summary>
        public static readonly List<Copper> ActiveClouds = new List<Copper>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() => ActiveClouds.Clear();

        [SerializeField] private Allomancer allomancer;

        [Tooltip("Radius of the suppression cloud around the player.")]
        public float cloudRadius = 14f;

        public bool IsBurning { get; private set; }

        /// <summary>Current cloud radius — flaring (hold R) widens the coppercloud.</summary>
        public float EffectiveRadius => cloudRadius * (allomancer != null ? allomancer.FlareMultiplier : 1f);

        void OnEnable() => ActiveClouds.Add(this);
        void OnDisable() => ActiveClouds.Remove(this);

        void Update()
        {
            IsBurning = allomancer != null &&
                        allomancer.IsMetalBurning(MetalType.Copper) &&
                        allomancer.GetReserve(MetalType.Copper) > 0f;
        }

        /// <summary>True if `pos` lies inside any active, burning coppercloud — i.e. an enemy
        /// allomancer there is suppressed.</summary>
        public static bool IsWithinCloud(Vector3 pos)
        {
            for (int i = 0; i < ActiveClouds.Count; i++)
            {
                Copper c = ActiveClouds[i];
                if (c == null || !c.IsBurning) continue;
                if ((c.transform.position - pos).sqrMagnitude <= c.EffectiveRadius * c.EffectiveRadius)
                    return true;
            }
            return false;
        }
    }
}