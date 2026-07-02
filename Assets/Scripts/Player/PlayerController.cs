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
        // Previous frame's grounded state, for the one-frame isAboutToLand pulse (see DriveAnimator).
        // Starts true so the spawn-snap (Start's grounding Move) isn't mistaken for a landing.
        private bool wasGrounded = true;

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

        // ── Steelpush hover channel (written by IronSteel while you push straight down on a metal
        // below you — see IronSteel.ApplyForce's hover regime). The allomantic push above is a
        // DECAYING velocity impulse + separately-integrated gravity, so on its own it launches/arcs
        // and never settles at a height — there is no natural hover equilibrium. The hover is therefore
        // an EXPLICIT underdamped harmonic oscillator about the equilibrium height H:
        //   a = -ω²·(y - H) - 2·ζ·ω·v
        // Below H the push exceeds gravity → rise; above H gravity exceeds the weakened push → fall —
        // so the initial push's INERTIA carries you past H, you swing back below, then above, over and
        // over, and the damping term settles you at H (exactly the behaviour the user described). H is
        // set by IronSteel and tuned so a FULL-FLARE push off one metal below settles at ~30 m (~100 ft)
        // — Vin's canonical hover (per community calculations + textual references; see
        // basicrpg-allomancy-canon-lockin memory). H ∝ flare/maxFlare, so burning harder hovers higher.
        // Gravity is NOT separately applied while hovering — it is already in the equilibrium balance.
        private bool hoverActive;
        private float hoverTargetWorldY = float.NaN;
        private const float HoverOmega = 1.2f;         // rad/s — hover spring frequency (period 2π/ω ≈ 5.2 s)
        private const float HoverDampingZeta = 0.35f;  // underdamped (ζ<1): oscillate above/below H, then settle

        /// <summary>Steelpush hover: oscillate about <paramref name="targetWorldY"/> (the equilibrium
        /// hover height H) and settle there. Called every frame by IronSteel while you push straight
        /// down on a metal below you; call <see cref="ClearHover"/> when the push stops or is angled
        /// away (gravity resumes → you fall/arc back down).</summary>
        public void SetHoverTarget(float targetWorldY) { hoverActive = true; hoverTargetWorldY = targetWorldY; }
        /// <summary>End the Steelpush hover — gravity resumes and the player falls/arcs back down.</summary>
        public void ClearHover() { hoverActive = false; hoverTargetWorldY = float.NaN; }

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

            // Gravity + jump, OR the Steelpush hover (an underdamped oscillator about the hover
            // equilibrium H — rises when below H, falls when above, inertia overshoots, settles).
            if (hoverActive && !float.IsNaN(hoverTargetWorldY))
            {
                float dy = transform.position.y - hoverTargetWorldY;        // <0 below H, >0 above
                float aSpring = -HoverOmega * HoverOmega * dy;              // restoring toward H
                float aDamp   = -2f * HoverDampingZeta * HoverOmega * verticalVel; // settles the swing
                verticalVel += (aSpring + aDamp) * Time.deltaTime;           // gravity is in the balance — not added
            }
            else if (controller.isGrounded)
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

            // The Erbium Character.controller models a fall + landing as TWO bools:
            //   isFalling      — airborne; an AnyState transition (isFalling && !isAboutToLand)
            //                    sends idle → fallingIdle.
            //   isAboutToLand  — a ONE-FRAME pulse on the frame we touch down. Its AnyState
            //                    transition (isAboutToLand && !isFalling) is the only way out of
            //                    fallingIdle into the fallingToIdle/fallingToRoling landing clips,
            //                    which then exit back to idle on their own exit time. Skip that
            //                    pulse and the humanoid lands but is stuck posing the fall forever —
            //                    the "stays in falling after a jump" bug. (wasGrounded starts true
            //                    so the spawn-snap isn't mistaken for a landing.)
            //
            // isGrounded isn't refreshed by a zero-length Move, so a frozen tutorial (timeScale 0)
            // can read false even when the player is plainly standing; a short downward probe
            // (works regardless of timeScale) backstops it so the humanoid stays in idle grounded.
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