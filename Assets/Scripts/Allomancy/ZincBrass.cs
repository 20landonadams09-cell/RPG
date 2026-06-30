using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Combat;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Zinc (Rioter — inflame emotions) &amp; Brass (Soother — dampen emotions). An aura around
    /// the player that retunes nearby enemies' aggression. The active metal gates the effect:
    ///   - Zinc burning → nearby enemies turn **hyper-aggressive**: wider detect range, faster
    ///     attacks, quicker movement (they swarm you).
    ///   - Brass burning → nearby enemies turn **calm**: tiny detect range (they barely notice
    ///     you), slow attacks, sluggish movement (you can walk past them).
    /// Enemies outside the aura (or when no emotion metal is burning) snap back to normal — the
    /// aura rewrites <see cref="Enemy.SetEmotionScale"/> every frame, resetting anyone out of
    /// range. No key to hold: just burn the metal (B). Adapted from Ashwalker for BasicRPG's
    /// <see cref="Enemy"/> registry + CharacterController AI (no NavMesh/emotion-system deps).
    /// </summary>
    public class ZincBrass : MonoBehaviour
    {
        [SerializeField] private Allomancer allomancer;

        [Header("Aura")]
        public float auraRadius = 18f;

        [Header("Zinc (Riot) — hyper-aggressive")]
        public float zincDetectScale = 2.0f;
        public float zincCooldownScale = 0.6f;   // attacks more often
        public float zincSpeedScale = 1.4f;

        [Header("Brass (Soothe) — calm")]
        public float brassDetectScale = 0.25f;   // barely notices you
        public float brassCooldownScale = 2.0f;  // attacks slowly
        public float brassSpeedScale = 0.6f;

        void Update()
        {
            if (allomancer == null) return;

            bool zincBurning  = allomancer.IsMetalBurning(MetalType.Zinc)  &&
                                allomancer.GetReserve(MetalType.Zinc)  > 0f;
            bool brassBurning = allomancer.IsMetalBurning(MetalType.Brass) &&
                                allomancer.GetReserve(MetalType.Brass) > 0f;
            bool active = zincBurning || brassBurning;

            // Iterate a copy — Enemy.All may change if one dies mid-loop.
            List<Enemy> enemies = Enemy.All;
            Vector3 me = transform.position;
            // Flaring (hold Keybinds.Flare) widens the aura's reach — burns harder, reaches farther.
            float f = allomancer.FlareMultiplier;
            float radius = auraRadius * f;
            float sqrRadius = radius * radius;

            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null) continue;

                if (!active)
                {
                    enemy.SetEmotionScale(1f, 1f, 1f);
                    continue;
                }

                float sqr = (enemy.transform.position - me).sqrMagnitude;
                if (sqr > sqrRadius)
                {
                    enemy.SetEmotionScale(1f, 1f, 1f); // outside the aura → normal
                    continue;
                }

                if (zincBurning)
                    enemy.SetEmotionScale(zincDetectScale, zincCooldownScale, zincSpeedScale);
                else
                    enemy.SetEmotionScale(brassDetectScale, brassCooldownScale, brassSpeedScale);
            }
        }

        void OnDisable()
        {
            // Restore everyone if the component is disabled/destroyed mid-burn.
            foreach (Enemy enemy in Enemy.All)
                if (enemy != null) enemy.SetEmotionScale(1f, 1f, 1f);
        }
    }
}