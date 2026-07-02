using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BasicRPG.Combat;
using BasicRPG.Interaction;

namespace BasicRPG.AI
{
    /// <summary>
    /// EXAMPLE / REFERENCE consumer of the <see cref="BehaviorTree"/> framework — a complex
    /// multi-phase enemy (a Mistborn Steel Inquisitor archetype) whose decisions are a behavior
    /// tree rather than the inline `switch (state)` FSM in <see cref="Enemy"/>. The BT lets one
    /// enemy express prioritized, nested behavior the flat FSM can't: flee when wounded AND
    /// cornered, else close on the player, else strike when in range, else hold ground and patrol.
    ///
    /// Movement, gravity, facing, attack telegraph and loot are lifted verbatim from the verified
    /// <see cref="Enemy"/> pattern so the locomotion is known-good; only the DECISION layer is the
    /// behavior tree. It is self-contained: drop it on a GameObject with a CharacterController + a
    /// capsule/child model and it runs.
    ///
    /// ALLOMANCY-INTEGRATION TODO (intentionally NOT done here, to avoid touching canon allomancy
    /// code per the project's build-from-scratch stance): this enemy does not yet register with the
    /// allomancy systems the way <see cref="Enemy"/> does, so it is not Steelpush-shoveable, not
    /// Tin-sensed, and not Zinc/Brass-emotion-affected. To wire it in WITHOUT editing allomancy,
    /// either (a) make the allomancy systems iterate a shared `IAllomancyTarget` interface that both
    /// <see cref="Enemy"/> and this class implement, or (b) refactor <see cref="Enemy"/>'s tick to
    /// be `protected virtual` and subclass it. Both are follow-ups; this file is the AI reference.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class InquisitorEnemy : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float moveSpeed = 3.4f;
        [SerializeField] private float fleeSpeed = 5.5f;
        [SerializeField] private float gravity = -19.62f;

        [Header("Detection / Attack")]
        [SerializeField] private float detectRange = 11f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackDamage = 18f;
        [SerializeField] private float attackCooldown = 1.5f;
        [SerializeField] private float attackWindup = 0.35f;

        [Header("Survival (flee branch)")]
        [SerializeField] private float fleeHealthFraction = 0.3f;   // below 30% HP → consider fleeing
        [SerializeField] private float safeDistance = 16f;            // flee until this far from player

        [Header("Patrol home")]
        [SerializeField] private Vector3 homePosition;                // returns here when idle; 0,0,0 = spawn point
        [SerializeField] private float waypointReach = 0.8f;

        [Header("Loot")]
        [SerializeField] private BasicRPG.Items.ItemSO lootItem;
        [SerializeField] private int lootCount = 1;

        private CharacterController controller;
        private BehaviorTree bt;
        private Transform player;
        private PlayerCombat playerCombat;
        private BasicRPG.Stats.Health health;

        private float verticalVel = -2f;
        private float lastAttackTime = -10f;
        private bool attacking;
        private Vector3 spawnPos;
        private Color baseTint = Color.white;   // cached so FlashTelegraph restores the model, not white

        // Blackboard keys (centralized so the tree and the movement actions agree on names).
        private const string K_PLAYER = "player";
        private const string K_PLAYER_COMBAT = "playerCombat";
        private const string K_DIST = "distToPlayer";
        private const string K_HP_FRAC = "hpFraction";
        private const string K_HOME = "home";

        private static readonly Color Telegraph = new Color(1f, 0.35f, 0.35f, 1f); // red — Inquisitor menace

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            bt = gameObject.AddComponent<BehaviorTree>();
            spawnPos = transform.position;
            if (homePosition == Vector3.zero) homePosition = spawnPos;
        }

        void Start()
        {
            // Start, not Awake, so PlayerCombat/Health are created by the builder first.
            PlayerCombat pc = FindAnyObjectByType<PlayerCombat>();
            if (pc != null) { player = pc.transform; playerCombat = pc; }
            health = GetComponent<BasicRPG.Stats.Health>();
            if (health != null) health.OnDeath += Die;
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && r.material != null) baseTint = r.material.color;

            // Prime the blackboard with sensed state the tree will read each tick.
            bt.Blackboard.Set(K_PLAYER, player);
            bt.Blackboard.Set(K_PLAYER_COMBAT, playerCombat);
            bt.Blackboard.Set(K_HOME, homePosition);

            bt.SetRoot(BuildTree());
        }

        void Update()
        {
            // Sense → write the per-frame world state the BT reads, then let the BT move us.
            if (player != null)
                bt.Blackboard.Set(K_DIST, Vector3.Distance(transform.position, player.position));
            if (health != null)
                bt.Blackboard.Set(K_HP_FRAC, health.Normalized);
            // (the BehaviorTree component ticks the root itself)
        }

        /// <summary>Build the Inquisitor's decision tree. Selector = prioritized fallback:
        /// survive first, then fight, then hold ground.</summary>
        Node BuildTree()
        {
            return new Selector(
                // ── 1. Flee: wounded AND player too close. Kiting is highest priority.
                new Sequence(
                    new Condition(bb => Wounded(bb) && bb.Get<float>(K_DIST, float.MaxValue) < safeDistance * 0.6f),
                    new Action(bb => Flee(bb))
                ),
                // ── 2. Combat: player is detected.
                new Sequence(
                    new Condition(bb => bb.Get<float>(K_DIST, float.MaxValue) < detectRange),
                    new Selector(
                        // 2a. In melee range → strike.
                        new Sequence(
                            new Condition(bb => bb.Get<float>(K_DIST, float.MaxValue) <= attackRange),
                            new Action(bb => Attack(bb))
                        ),
                        // 2b. Out of melee but detected → close the gap.
                        new Action(bb => Approach(bb))
                    )
                ),
                // ── 3. Hold ground: return home and idle.
                new Action(bb => ReturnHome(bb))
            );
        }

        bool Wounded(Blackboard bb) => bb.Get<float>(K_HP_FRAC, 1f) <= fleeHealthFraction;

        // ── BT action bodies (each drives the CharacterController like Enemy.cs) ─────────

        NodeStatus Approach(Blackboard bb)
        {
            Transform p = bb.Get<Transform>(K_PLAYER);
            if (p == null) return NodeStatus.Failure;
            MoveAndFace((p.position - transform.position).NormalizedIgnoreY(), moveSpeed);
            return NodeStatus.Running;
        }

        NodeStatus Flee(Blackboard bb)
        {
            Transform p = bb.Get<Transform>(K_PLAYER);
            if (p == null) return NodeStatus.Failure;
            Vector3 away = (transform.position - p.position).NormalizedIgnoreY();
            if (away.sqrMagnitude < 0.0001f) away = transform.forward.Flat();
            MoveAndFace(away, fleeSpeed);
            // Keep fleeing (Running) until we're safely far; the tree re-evaluates the condition.
            return bb.Get<float>(K_DIST, 0f) < safeDistance ? NodeStatus.Running : NodeStatus.Success;
        }

        NodeStatus Attack(Blackboard bb)
        {
            Transform p = bb.Get<Transform>(K_PLAYER);
            // Face the player while in melee (no horizontal slide during the windup).
            if (p != null)
            {
                Vector3 to = (p.position - transform.position).NormalizedIgnoreY();
                if (to.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation,
                        Quaternion.LookRotation(to), 540f * Time.deltaTime);
            }
            ApplyGravity();
            controller.Move((Vector3.up * verticalVel) * Time.deltaTime);

            if (!attacking && Time.time >= lastAttackTime + attackCooldown)
            {
                lastAttackTime = Time.time;
                StartCoroutine(AttackRoutine(bb));
            }
            return NodeStatus.Running;
        }

        NodeStatus ReturnHome(Blackboard bb)
        {
            Vector3 home = bb.Get<Vector3>(K_HOME, spawnPos);
            Vector3 to = (home - transform.position).NormalizedIgnoreY();
            if (to.sqrMagnitude <= waypointReach * waypointReach)
            {
                // Already home: stand still (apply gravity only) and consider the branch done.
                ApplyGravity();
                controller.Move(Vector3.up * verticalVel * Time.deltaTime);
                return NodeStatus.Success;
            }
            MoveAndFace(to, moveSpeed);
            return NodeStatus.Running;
        }

        IEnumerator AttackRoutine(Blackboard bb)
        {
            attacking = true;
            FlashTelegraph(true);
            yield return new WaitForSeconds(attackWindup);
            FlashTelegraph(false);

            Transform p = bb.Get<Transform>(K_PLAYER);
            PlayerCombat pc = bb.Get<PlayerCombat>(K_PLAYER_COMBAT);
            if (p != null)
            {
                float dist = Vector3.Distance(transform.position, p.position);
                if (dist <= attackRange * 1.15f && pc != null)
                    pc.ApplyDamage(Mathf.RoundToInt(attackDamage));
            }
            attacking = false;
        }

        // ── Locomotion (verbatim from the verified Enemy.cs pattern) ──────────────────

        void MoveAndFace(Vector3 dir, float speed)
        {
            if (dir.sqrMagnitude > 0.0001f)
            {
                Quaternion look = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, 540f * Time.deltaTime);
            }
            ApplyGravity();
            Vector3 move = dir * speed + Vector3.up * verticalVel;
            controller.Move(move * Time.deltaTime);
        }

        void ApplyGravity()
        {
            if (controller.isGrounded) verticalVel = -2f;
            else verticalVel += gravity * Time.deltaTime;
        }

        void FlashTelegraph(bool on)
        {
            Renderer r = GetComponentInChildren<Renderer>();
            if (r != null && r.material != null)
                r.material.color = on ? Telegraph : baseTint;
        }

        void Die()
        {
            if (lootItem != null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Loot_" + lootItem.displayName;
                cube.transform.position = transform.position + Vector3.up * 0.3f;
                cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
                Renderer rr = cube.GetComponent<Renderer>();
                Shader lit = Shader.Find("Universal Render Pipeline/Lit");
                if (lit != null) { Material mat = new Material(lit); mat.color = new Color(0.5f, 0.1f, 0.1f, 1f); rr.sharedMaterial = mat; }
                var pickup = cube.AddComponent<BasicRPG.Interaction.ItemPickup>();
                pickup.Init(lootItem, lootCount);
            }
            NotificationUI.Show("Inquisitor defeated");
            Destroy(gameObject);
        }
    }

    /// <summary>Local vector helpers (kept here so the file is fully self-contained).</summary>
    internal static class InquisitorVecExt
    {
        public static Vector3 NormalizedIgnoreY(this Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v.normalized : Vector3.zero; }
        public static Vector3 Flat(this Vector3 v) { v.y = 0f; return v; }
    }
}