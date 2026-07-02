using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Stats;
using BasicRPG.Items;
using BasicRPG.Interaction;

namespace BasicRPG.Combat
{
    public enum EnemyState { Patrol, Chase, Attack, Dead }

    /// <summary>
    /// Simple kinematic enemy AI on a CharacterController (no NavMesh). Patrols between
    /// waypoints; detects the player and chases; winds up and attacks when in range.
    /// Reuses Health for HP and drops a metal pickup on death.
    /// </summary>
    public class Enemy : MonoBehaviour
    {
        /// <summary>Live enemies in the scene — Tin iterates this for scent/vibration senses.</summary>
        public static readonly List<Enemy> All = new List<Enemy>();

        /// <summary>Current horizontal+vertical velocity from the CharacterController (for Tin
        /// vibration detection — BasicRPG enemies have no NavMeshAgent).</summary>
        public Vector3 Velocity => controller != null ? controller.velocity : Vector3.zero;

        /// <summary>Grounded (for the Iron/Steel mobility model): the enemy's CharacterController
        /// reports ground contact, OR the <see cref="GroundProbe"/> backstop says so. Read by IronSteel
        /// to estimate the enemy's contact normal force N_target (a grounded, armored enemy is
        /// braced against the earth → low mobility → hard to shove; an airborne one is free).</summary>
        public bool IsGrounded => controller != null && (controller.isGrounded || GroundProbe());
        [SerializeField] private Health health;
        [SerializeField] private CharacterController controller;
        [SerializeField] private Renderer rend;
        // Optional humanoid Animator (Erbium model + Character.controller wired by the scene
        // builder). Null falls back to the plain capsule visual. Driven each frame from the
        // enemy's movement so it walks/idles/falls like the player rather than sliding as a blob.
        [SerializeField] private Animator anim;
        // The humanoid model's renderers (set by the builder when the enemy gets a model). The
        // attack telegraph flashes these; null falls back to the single capsule `rend`.
        [SerializeField] private Renderer[] tintRenderers;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3f;
        [SerializeField] private float gravity = -19.62f;

        [Header("Detection / Attack")]
        [SerializeField] private float detectRange = 8f;
        [SerializeField] private float attackRange = 1.8f;
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackCooldown = 1.2f;
        [SerializeField] private float attackWindup = 0.3f;

        [Header("Patrol")]
        [SerializeField] private Transform[] patrolWaypoints;
        [SerializeField] private float waypointReach = 0.6f;

        [Header("Loot")]
        [SerializeField] private ItemSO lootItem;
        [SerializeField] private int lootCount = 1;

        // Runtime emotion multipliers written by Zinc (riot) / Brass (soothe) allomancy aura.
        // 1 = normal. Zinc raises detect/speed and lowers cooldown (hyper-aggressive); Brass
        // does the inverse (calm — barely notices you, attacks slowly, moves sluggishly).
        private float emotionDetectScale = 1f;
        private float emotionCooldownScale = 1f;
        private float emotionSpeedScale = 1f;

        // Self-buff multipliers written by an EnemyAllomancer (e.g. a Pewter-thug enemy burning
        // Pewter). 1 = normal. Suppressed by a Coppercloud (EnemyAllomancer stops burning).
        private float enemyBuffSpeed = 1f;
        private float enemyBuffDamage = 1f;

        private static readonly Color Telegraph = new Color(1f, 0.9f, 0.2f, 1f);

        private EnemyState state = EnemyState.Patrol;
        private Transform player;
        private PlayerCombat playerCombat;
        private int waypointIndex;
        private float lastAttackTime = -10f;
        private float verticalVel = -2f;
        private bool attacking;
        private Color baseTint = Color.white;
        private readonly Collider[] probeBuffer = new Collider[8];
        // Previous frame's grounded state for the one-frame isAboutToLand pulse (see
        // DriveEnemyAnimator). Starts true so the spawn-snap isn't mistaken for a landing.
        private bool wasGrounded = true;

        // External shove from Steelpush (Iron/Steel). Integrated into Move and decays.
        private Vector3 externalPushVel;
        private const float PushDrag = 3f;
        private const float PushMaxSpeed = 12f;

        void Start()
        {
            // Start, not Awake, so PlayerCombat has been created by the builder.
            PlayerCombat pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null) { player = pc.transform; playerCombat = pc; }
            if (health != null) health.OnDeath += Die;
            if (rend != null && rend.material != null) baseTint = rend.material.color;
            // Snap onto the ground at spawn (see PlayerController.Start): a frozen tutorial never
            // gives the per-frame Move a real length, so without this the humanoid would lock into
            // a Falling pose. Move() grounds regardless of timeScale.
            if (controller != null) controller.Move(Vector3.down * 3f);
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>Steelpush shoves the enemy (Iron/Steel allomancy). Velocity integrates into
        /// the CharacterController Move and decays with drag.</summary>
        public void AddPush(Vector3 v)
        {
            externalPushVel += v;
            if (externalPushVel.sqrMagnitude > PushMaxSpeed * PushMaxSpeed)
                externalPushVel = externalPushVel.normalized * PushMaxSpeed;
        }

        /// <summary>Zinc/Brass aura writes emotion multipliers (detect range, attack cooldown,
        /// move speed). The aura resets enemies out of range back to (1,1,1) each frame.</summary>
        public void SetEmotionScale(float detect, float cooldown, float speed)
        {
            emotionDetectScale = detect;
            emotionCooldownScale = cooldown;
            emotionSpeedScale = speed;
        }

        /// <summary>An EnemyAllomancer writes its own buff (Pewter-thug speed/damage) here.
        /// Suppressed (Coppercloud) → the allomancer writes (1,1).</summary>
        public void SetEnemyBuff(float speed, float damage)
        {
            enemyBuffSpeed = speed;
            enemyBuffDamage = damage;
        }

        public void TakeDamage(int amount)
        {
            if (state == EnemyState.Dead) return;
            if (health != null) health.TakeDamage(amount);
        }

        void Die()
        {
            if (state == EnemyState.Dead) return;
            state = EnemyState.Dead;
            SpawnLoot();
            NotificationUI.Show("Enemy defeated");
            Destroy(gameObject);
        }

        void SpawnLoot()
        {
            if (lootItem == null) return;
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Loot_" + lootItem.displayName;
            cube.transform.position = transform.position + Vector3.up * 0.3f;
            cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            Renderer r = cube.GetComponent<Renderer>();
            Shader lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null)
            {
                Material mat = new Material(lit);
                mat.color = new Color(0.6f, 0.6f, 0.65f, 1f);
                r.sharedMaterial = mat;
            }
            ItemPickup pickup = cube.AddComponent<ItemPickup>();
            pickup.Init(lootItem, lootCount);
        }

        void Update()
        {
            if (state == EnemyState.Dead || controller == null) return;

            if (player == null)
            {
                PatrolMove();
                DriveEnemyAnimator();
                return;
            }

            float dist = Vector3.Distance(transform.position, player.position);
            float effDetect = detectRange * emotionDetectScale;

            switch (state)
            {
                case EnemyState.Patrol:
                    PatrolMove();
                    if (dist < effDetect) state = EnemyState.Chase;
                    break;
                case EnemyState.Chase:
                    if (dist < attackRange) state = EnemyState.Attack;
                    else if (dist > effDetect * 1.2f) state = EnemyState.Patrol; // hysteresis
                    else ChaseMove();
                    break;
                case EnemyState.Attack:
                    if (dist > attackRange) { state = EnemyState.Chase; ChaseMove(); }
                    else TryAttack();
                    break;
            }

            // Bleed off a Steelpush shove (drag) regardless of state.
            externalPushVel *= Mathf.Exp(-PushDrag * Time.deltaTime);
            if (externalPushVel.sqrMagnitude < 0.0004f) externalPushVel = Vector3.zero;

            DriveEnemyAnimator();
        }

        /// <summary>Feed this enemy's movement into the humanoid Animator (same params the player
        /// uses: horInput/verInput/inputMagnitude/groundVelocity/isFalling). The enemy faces its
        /// move direction (MoveAndFace), so in local space it walks forward. No-op without an
        /// Animator (capsule fallback).</summary>
        void DriveEnemyAnimator()
        {
            if (anim == null || controller == null) return;
            Vector3 v = controller.velocity; v.y = 0f;
            float speed = v.magnitude;
            float mag = moveSpeed > 0.001f ? Mathf.Clamp01(speed / moveSpeed) : 0f;
            anim.SetFloat("horInput", 0f);
            anim.SetFloat("verInput", mag);
            anim.SetFloat("inputMagnitude", mag);
            anim.SetFloat("groundVelocity", speed);

            // Same fall/landing model as PlayerController.DriveAnimator: the Erbium controller
            // needs a one-frame isAboutToLand pulse to exit fallingIdle into a landing clip, else
            // a shoved/jumping enemy lands and is stuck posing the fall. GroundProbe backstops
            // isGrounded during a frozen tutorial (zero-length Moves don't refresh it) so a
            // grounded enemy reads grounded and stays in idle at spawn instead of a perpetual fall.
            bool grounded = controller.isGrounded || GroundProbe();
            if (grounded)
            {
                anim.SetBool("isFalling", false);
                anim.SetBool("isAboutToLand", !wasGrounded); // pulse exactly on the landing frame
            }
            else
            {
                anim.SetBool("isFalling", true);
                anim.SetBool("isAboutToLand", false);
            }
            wasGrounded = grounded;
        }

        /// <summary>True if there's solid ground just under the capsule's base — backstops
        /// CharacterController.isGrounded during a frozen tutorial (a zero-length Move doesn't
        /// refresh it). Ignores the enemy's own colliders, its model's renderers, and triggers.</summary>
        bool GroundProbe()
        {
            if (controller == null) return false;
            Vector3 center = transform.position + Vector3.up * (-controller.height * 0.5f + 0.1f);
            int n = Physics.OverlapSphereNonAlloc(center, 0.2f, probeBuffer);
            for (int i = 0; i < n; i++)
            {
                Collider c = probeBuffer[i];
                if (c == null || c.isTrigger) continue;
                if (c == controller) continue;                  // own CharacterController
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue; // own model
                return true;
            }
            return false;
        }

        void PatrolMove()
        {
            if (patrolWaypoints == null || patrolWaypoints.Length == 0) return;
            Transform wp = patrolWaypoints[waypointIndex];
            Vector3 to = wp.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude <= waypointReach * waypointReach)
            {
                waypointIndex = (waypointIndex + 1) % patrolWaypoints.Length;
                return;
            }
            MoveAndFace(to.normalized);
        }

        void ChaseMove()
        {
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            MoveAndFace(to.normalized);
        }

        void MoveAndFace(Vector3 dir)
        {
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * Time.deltaTime);
            }
            ApplyGravity();
            Vector3 move = dir * (moveSpeed * emotionSpeedScale * enemyBuffSpeed) + Vector3.up * verticalVel + externalPushVel;
            controller.Move(move * Time.deltaTime);
        }

        void TryAttack()
        {
            // Face the player while in melee.
            Vector3 to = player.position - transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(to.normalized);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * Time.deltaTime);
            }
            ApplyGravity();
            controller.Move((Vector3.up * verticalVel + externalPushVel) * Time.deltaTime);

            if (!attacking && Time.time >= lastAttackTime + attackCooldown * emotionCooldownScale)
            {
                lastAttackTime = Time.time;
                StartCoroutine(AttackRoutine());
            }
        }

        void ApplyGravity()
        {
            if (controller.isGrounded) verticalVel = -2f;
            else verticalVel += gravity * Time.deltaTime;
        }

        IEnumerator AttackRoutine()
        {
            attacking = true;
            FlashTelegraph(true);
            yield return new WaitForSeconds(attackWindup);
            FlashTelegraph(false);

            if (state != EnemyState.Dead && player != null)
            {
                float dist = Vector3.Distance(transform.position, player.position);
                if (dist <= attackRange * 1.15f && playerCombat != null)
                    playerCombat.ApplyDamage(Mathf.RoundToInt(attackDamage * enemyBuffDamage));
            }
            attacking = false;
        }

        /// <summary>Flash the humanoid model (or the capsule fallback) yellow during the attack
        /// windup so the hit is telegraphed. Restores the enemy's base tint afterward.</summary>
        void FlashTelegraph(bool on)
        {
            Color c = on ? Telegraph : baseTint;
            if (tintRenderers != null && tintRenderers.Length > 0)
            {
                foreach (var r in tintRenderers)
                    if (r != null && r.material != null) r.material.color = c;
                return;
            }
            if (rend != null && rend.material != null) rend.material.color = c;
        }
    }
}