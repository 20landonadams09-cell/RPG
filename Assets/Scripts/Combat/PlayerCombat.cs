using System.Collections;
using UnityEngine;
using BasicRPG.Stats;
using BasicRPG.Items;
using BasicRPG.Interaction;
using BasicRPG.Allomancy;

namespace BasicRPG.Combat
{
    /// <summary>
    /// Player melee combat: light attack (LMB), block (hold RMB), dodge dash (C).
    /// Routes incoming damage through ApplyDamage so block reduction and dodge
    /// i-frames apply. Attack damage scales with the equipped weapon's power.
    /// </summary>
    public class PlayerCombat : MonoBehaviour
    {
        [SerializeField] private Health health;
        [SerializeField] private Inventory inventory;
        [SerializeField] private Stamina stamina;
        [SerializeField] private CharacterController controller;

        [Header("Melee")]
        [SerializeField] private float baseDamage = 15f;
        [SerializeField] private float attackRange = 2.2f;
        [SerializeField] private float attackArc = 70f;
        [SerializeField] private float attackCooldown = 0.5f;
        [SerializeField] private float attackWindup = 0.12f;

        [Header("Block")]
        [SerializeField] private float blockReduction = 0.6f; // fraction of damage absorbed

        [Header("Dodge")]
        [SerializeField] private float dodgeDistance = 4f;
        [SerializeField] private float dodgeDuration = 0.25f;
        [SerializeField] private float dodgeIFrames = 0.25f;
        [SerializeField] private float dodgeStaminaCost = 25f;
        [SerializeField] private float dodgeCooldown = 0.6f;
        [SerializeField] private float gravity = -19.62f;

        private readonly Collider[] hitBuffer = new Collider[16];
        private float lastAttackTime = -10f;
        private float lastDodgeTime = -10f;
        private float iFrameEnd = -10f;
        private bool isBlocking;
        private bool isDodging;

        // Pewter writes these while burning: outgoing damage strength, incoming damage reduction.
        private float strengthMultiplier = 1f;
        private float incomingDamageMultiplier = 1f;

        public void SetPewterStrength(float m) => strengthMultiplier = m;
        public void SetDamageReduction(float takeFraction) => incomingDamageMultiplier = takeFraction;

        public bool IsDodging => isDodging;
        public bool IsBlocking => isBlocking;
        public bool IsInvulnerable => Time.time < iFrameEnd;
        // Block slows movement; PlayerController reads this.
        public float MoveSpeedScale => isBlocking ? 0.4f : 1f;

        void Awake()
        {
            CharacterController cc = controller;
            if (cc == null) cc = GetComponent<CharacterController>();
            controller = cc;
        }

        void Update()
        {
            // No combat while dialogue or inventory owns input.
            if (InteractionLock.IsLocked) { isBlocking = false; return; }

            isBlocking = !isDodging && Keybinds.BlockHeld();

            if (!isBlocking && !isDodging && Keybinds.AttackDown()
                && Time.time >= lastAttackTime + attackCooldown)
            {
                lastAttackTime = Time.time;
                StartCoroutine(AttackRoutine());
            }

            if (!isDodging && Keybinds.DodgeDown()
                && Time.time >= lastDodgeTime + dodgeCooldown
                && stamina != null && stamina.TryConsume(dodgeStaminaCost))
            {
                lastDodgeTime = Time.time;
                StartCoroutine(DodgeRoutine());
            }
        }

        IEnumerator AttackRoutine()
        {
            if (attackWindup > 0f) yield return new WaitForSeconds(attackWindup);
            DoMeleeHit();
        }

        void DoMeleeHit()
        {
            float dmg = (baseDamage + (inventory != null && inventory.EquippedWeapon != null
                ? inventory.EquippedWeapon.power : 0f)) * strengthMultiplier;

            Vector3 origin = transform.position + Vector3.up * 1f;
            int count = Physics.OverlapSphereNonAlloc(origin, attackRange, hitBuffer);
            for (int i = 0; i < count; i++)
            {
                Collider col = hitBuffer[i];
                if (col == null) continue;
                Enemy enemy = col.GetComponentInParent<Enemy>();
                if (enemy == null) continue;

                Vector3 to = enemy.transform.position - transform.position;
                to.y = 0f;
                if (to.sqrMagnitude < 0.0001f) { enemy.TakeDamage(Mathf.RoundToInt(dmg)); continue; }
                if (Vector3.Angle(transform.forward, to.normalized) <= attackArc * 0.5f)
                    enemy.TakeDamage(Mathf.RoundToInt(dmg));
            }
        }

        IEnumerator DodgeRoutine()
        {
            isDodging = true;
            iFrameEnd = Time.time + dodgeIFrames;

            Vector3 dir = GetDodgeDirection();
            float verticalVel = -2f;
            float elapsed = 0f;
            while (elapsed < dodgeDuration)
            {
                float dt = Time.deltaTime;
                elapsed += dt;
                if (controller != null && controller.isGrounded) verticalVel = -2f;
                else verticalVel += gravity * dt;

                Vector3 move = dir * (dodgeDistance / dodgeDuration) + Vector3.up * verticalVel;
                if (controller != null) controller.Move(move * dt);
                yield return null;
            }

            isDodging = false;
        }

        Vector3 GetDodgeDirection()
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 inputDir = new Vector3(h, 0f, v).normalized;
            if (inputDir.sqrMagnitude > 0.01f)
            {
                float camYaw = Camera.main != null ? Camera.main.transform.eulerAngles.y : 0f;
                float angle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + camYaw;
                return Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            }
            return transform.forward;
        }

        /// <summary>Called by enemies when their attack lands.</summary>
        public void ApplyDamage(int amount)
        {
            if (IsInvulnerable) return;
            if (isBlocking) amount = Mathf.RoundToInt(amount * (1f - blockReduction));
            // Pewter toughness stacks with block: take a fraction of whatever remains.
            amount = Mathf.RoundToInt(amount * incomingDamageMultiplier);
            if (health != null) health.TakeDamage(amount);
        }
    }
}