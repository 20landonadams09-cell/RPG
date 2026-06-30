using UnityEngine;
using BasicRPG.Stats;
using BasicRPG.Interaction;
using BasicRPG.Combat;
using BasicRPG.Allomancy;

namespace BasicRPG.Player
{
    /// <summary>
    /// CharacterController-based third-person movement: camera-relative WASD walk,
    /// Left-Shift sprint gated by Stamina, Space to jump, smooth turn toward move dir.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField] private float walkSpeed = 5f;
        [SerializeField] private float sprintSpeed = 8f;
        [SerializeField] private float turnSmoothTime = 0.1f;
        [SerializeField] private float jumpHeight = 1.2f;
        [SerializeField] private float gravity = -19.62f;
        [SerializeField] private float sprintStaminaCost = 18f; // per second

        [Header("References")]
        [SerializeField] private Transform cameraTransform;
        [SerializeField] private Stamina stamina;

        private CharacterController controller;
        private PlayerCombat combat;
        private float turnSmoothVel;
        private float verticalVel;
        private readonly Collider[] probeBuffer = new Collider[8];

        // Optional humanoid locomotion animator (Erbium model + Character.controller). The builder
        // wires it when the Erbium assets are present; null falls back to the plain capsule visual.
        // Driven each frame from movement state: horInput/verInput (raw camera-relative axes),
        // inputMagnitude (idle↔run blend), groundVelocity (fall/roll transitions), isFalling.
        [SerializeField] private Animator anim;

        // Allomancy multipliers written by metal-effect scripts (Pewter boosts, Tin penalizes).
        private float allomancySpeedScale = 1f;
        private float allomancyJumpScale = 1f;
        private float tinSpeedPenalty = 1f; // ≤1; Tin overload slows, never boosts

        // Iron/Steel writes an external velocity impulse here (Steelpush off / Ironpull toward
        // an anchor). It's added to the per-frame move and decays with drag, so a push feels
        // like a launch that bleeds off — not a permanent speed boost.
        private Vector3 allomanticVelocity;
        private const float AllomanticDrag = 3f;    // per-second decay
        private const float AllomanticMaxSpeed = 20f;

        /// <summary>Pewter writes speed/jump boost multipliers here while burning.</summary>
        public void SetAllomancyScale(float speed, float jump)
        {
            allomancySpeedScale = speed;
            allomancyJumpScale = jump;
        }

        /// <summary>Tin writes a speed penalty (≤1) here during sensory overload.</summary>
        public void SetTinSpeedPenalty(float penalty) => tinSpeedPenalty = Mathf.Clamp01(penalty);

        /// <summary>Iron/Steel add a velocity impulse (push off / pull toward an anchor).</summary>
        public void AddAllomanticVelocity(Vector3 v)
        {
            allomanticVelocity += v;
            if (allomanticVelocity.sqrMagnitude > AllomanticMaxSpeed * AllomanticMaxSpeed)
                allomanticVelocity = allomanticVelocity.normalized * AllomanticMaxSpeed;
        }

        void Awake()
        {
            controller = GetComponent<CharacterController>();
            combat = GetComponent<PlayerCombat>();
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        void Start()
        {
            // Snap the player onto whatever is below at spawn. The test scenes place the player a
            // hair above a platform; during a frozen tutorial (timeScale 0) the per-frame Move is
            // zero-length so CharacterController.isGrounded never flips true, the Animator's
            // isFalling stays true, and the humanoid freezes in a Falling pose for the whole
            // tutorial. Move() runs a real collision query regardless of timeScale, so one
            // downward Move here grounds the player before the first Update/Animator tick — the
            // model starts (and stays) in idle instead of a perpetual fall.
            if (controller != null) controller.Move(Vector3.down * 3f);
        }

        void Update()
        {
            // A dodge owns the player's movement; let PlayerCombat drive it.
            if (combat != null && combat.IsDodging) return;

            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            // While a dialogue is open, halt movement input (gravity below still applies).
            bool locked = InteractionLock.IsLocked;
            if (locked) { h = 0f; v = 0f; }
            Vector3 inputDir = new Vector3(h, 0f, v).normalized;
            bool hasInput = inputDir.sqrMagnitude > 0.01f;

            bool sprintHeld = Keybinds.SprintHeld();
            bool isSprinting = hasInput && sprintHeld && stamina != null &&
                               stamina.TryConsume(sprintStaminaCost * Time.deltaTime);
            float speed = isSprinting ? sprintSpeed : walkSpeed;
            if (combat != null) speed *= combat.MoveSpeedScale; // blocking slows you
            speed *= allomancySpeedScale * tinSpeedPenalty;     // Pewter boost / Tin overload

            Vector3 horizVel = Vector3.zero;
            if (hasInput)
            {
                float targetAngle = Mathf.Atan2(inputDir.x, inputDir.z) * Mathf.Rad2Deg + CamYaw();
                float angle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref turnSmoothVel, turnSmoothTime);
                transform.rotation = Quaternion.Euler(0f, angle, 0f);
                horizVel = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward * speed;
            }

            // Gravity + jump
            if (controller.isGrounded)
            {
                verticalVel = -2f; // keep the controller pinned to the ground
                if (!locked && Keybinds.JumpDown())
                    verticalVel = Mathf.Sqrt(jumpHeight * allomancyJumpScale * -2f * gravity);
            }
            else
            {
                verticalVel += gravity * Time.deltaTime;
            }

            Vector3 velocity = horizVel + Vector3.up * verticalVel;
            controller.Move((velocity + allomanticVelocity) * Time.deltaTime);

            // Bleed off the allomantic impulse (drag). Gravity already acts via verticalVel,
            // so an upward push arcs back down naturally as the impulse fades.
            allomanticVelocity *= Mathf.Exp(-AllomanticDrag * Time.deltaTime);
            if (allomanticVelocity.sqrMagnitude < 0.0004f) allomanticVelocity = Vector3.zero;

            DriveAnimator(h, v);
        }

        /// <summary>Feed movement state into the humanoid Animator's params (Erbium
        /// Character.controller): horInput/verInput (raw camera-relative), inputMagnitude
        /// (idle↔run blend), groundVelocity (horizontal speed, drives fall/roll transitions),
        /// isFalling (airborne). No-op when no Animator is wired (capsule fallback). The Animator
        /// runs in Normal update mode, so it pauses with the world at timeScale 0 — no need to
        /// special-case the tutorial freeze here.</summary>
        void DriveAnimator(float h, float v)
        {
            if (anim == null) return;
            Vector3 groundVel = controller.velocity; groundVel.y = 0f;
            anim.SetFloat("horInput", h);
            anim.SetFloat("verInput", v);
            anim.SetFloat("inputMagnitude", Mathf.Clamp01(new Vector3(h, 0f, v).magnitude));
            anim.SetFloat("groundVelocity", groundVel.magnitude);
            // isGrounded isn't refreshed by a zero-length Move, so during a timeScale=0 freeze it
            // can read false even when the player is plainly standing — and the Animator would then
            // sit in the Falling pose for the whole tutorial. A short downward probe (which works
            // regardless of timeScale) backstops it so the humanoid stays in idle while grounded.
            anim.SetBool("isFalling", !(controller.isGrounded || GroundProbe()));
        }

        /// <summary>True if there's solid ground just under the capsule's base. Used to keep the
        /// Animator out of the Falling pose at spawn / during a frozen tutorial when
        /// CharacterController.isGrounded hasn't been refreshed. Ignores the player's own
        /// colliders and triggers.</summary>
        bool GroundProbe()
        {
            if (controller == null) return false;
            Vector3 center = transform.position + Vector3.up * (-controller.height * 0.5f + 0.1f);
            int n = Physics.OverlapSphereNonAlloc(center, 0.2f, probeBuffer);
            for (int i = 0; i < n; i++)
            {
                Collider c = probeBuffer[i];
                if (c == null || c.isTrigger) continue;
                if (c == controller) continue;                 // own CharacterController
                if (c.transform == transform || c.transform.IsChildOf(transform)) continue; // own model
                return true;
            }
            return false;
        }

        float CamYaw()
        {
            return cameraTransform != null ? cameraTransform.eulerAngles.y : 0f;
        }
    }
}