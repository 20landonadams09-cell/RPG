using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Player;
using BasicRPG.Combat;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Iron (Ironpull, hold <b>Q</b> or <b>LMB</b>) &amp; Steel (Steelpush, hold <b>F</b> or <b>LMB</b>) —
    /// the iconic Mistborn locomotion/combat ability. The force model is a Newton's-3rd EQUAL-FORCE
    /// model (Stage 0): one mass-independent force F on allomancer and metal, accelerations a=F/m.
    /// It is NOT ported verbatim from Ashwalker — Ashwalker's IronPull.cs / SteelPush.cs use a
    /// velocity-share (equal-MOMENTUM) model, which the user explicitly rejected as non-canon (the
    /// 17th-Shard Q&amp;A: the blue line is a force acting EQUALLY on both bodies regardless of mass).
    /// Adapted to BasicRPG's CharacterController player (no Rigidbody): the player's force becomes a
    /// velocity impulse via <see cref="PlayerController.AddAllomanticVelocity"/> (decays with drag,
    /// clamped to AllomanticMaxSpeed), and armored enemy anchors are shoved via <see cref="Enemy.AddPush"/>.
    ///
    /// Lore (locked-in design intent — the blue threads &amp; the push/pull):
    /// • The blue lines are a MENTAL CUE only — a visual the Mistborn relates to, in their own
    ///   mind. They are not physical, don't stick to anything, aren't seen by anyone else, and
    ///   exist only in the Misting's perception. So they're drawn client-side (a LineRenderer from
    ///   the chest to every anchor in range) and NEVER affect physics — the push/pull does the
    ///   physics; the line just shows the player where the metal is.
    /// • Ironpull/Steelpush are a magic FORCE on metal, with the Mistborn's BODY as the source.
    ///   The force originates at the chest (the live chest bone — see <see cref="ChestOrigin"/>);
    ///   physics (gravity + drag + the CharacterController) does the rest. Push an immovable
    ///   (anchored) metal and the full recoil transfers to you — you launch off it; push a loose
    ///   (movable) one and it's a mass-split (both bodies move). Push straight down on a fixed
    ///   metal on the ground and you hover/launch upward (the metal can't move through the ground,
    ///   so the force comes back to you). Sustained "flight" is a series of these hover-jumps —
    ///   push off fixed metal at an angle to bound in a direction. (Throwing coins to push off —
    ///   the Mistborn's portable anchors — is canon but not implemented yet.)
    ///
    /// Canon mechanics this model already honours (confirmed via r/Cosmere lore discussion):
    /// • Momentum is conserved — the push/pull ADDS force to the Mistborn's existing velocity, it
    ///   doesn't replace it. Run sideways and push down on an anchor below you and you pole-vault
    ///   (the piston/rod extends while you carry your sideways momentum) — arcing, Spider-Man-ish
    ///   swings emerge from horizontal momentum + a toward/away force. (AddAllomanticVelocity adds
    ///   to the allomantic vector; the player's velocity = input + gravity + allomantic, summed.)
    /// • Pushing does NOT cancel gravity — gravity always applies (verticalVel), so a horizontal
    ///   push down a hallway would drop you unless the anchor is slightly BELOW your centre (so the
    ///   recoil has an upward component that counters gravity), or you use multiple anchors / jump.
    ///   This falls out for free: recoil = -(chest→anchor), so a below-centre anchor pushes you up.
    /// • Hovering: a FULL push STRAIGHT DOWN off an ANCHORED metal below sustains a hover at ~30 m
    ///   (~100 ft) — Vin's canonical hover (community calculations + textual references). The hover
    ///   height is the anchored-equilibrium CLOSED FORM (Allomantic Force Kernel Addendum, Prop 3):
    ///   r_eq = sqrt(k·m_metal·B/(M·g)) − δ, derived from the force balance F(r_eq)=M·g — not a
    ///   hardcoded number. The initial push's inertia carries you ABOVE r_eq, gravity swings you
    ///   slightly below, the sustained push sends you back up — oscillating with shrinking amplitude
    ///   until you settle. The closed form is Lyapunov-stable but NOT asymptotically stable (an
    ///   undamped center — oscillates forever), so PlayerController's hover channel adds ζ=0.35
    ///   damping about H to supply the energy dissipation the settle requires (proof gives the
    ///   equilibrium LOCATION; damping gives the SETTLING). It engages only for a near-vertical push
    ///   on an ANCHORED metal below the chest (Prop 2: a loose coin has no hover equilibrium — pushing
    ///   down on one flings the coin, mass-ratio recoil, no hover). An ANGLED push still launches/arcs
    ///   (the pole-vault/Spider-Man locomotion) — a normal push off to the side doesn't hang, you arc
    ///   back down. r_eq ∝ sqrt(flare)·sqrt(m_metal), so burning harder / pushing off a bigger metal
    ///   hovers higher; release the push and gravity resumes and you fall.
    ///
    /// Force model (Newton-symmetric with smooth mobility — the locked canon design, per the user's
    /// exact equations):
    ///   origin    = the Mistborn's chest bone — live, follows the model's animation (the body is
    ///               the source of the force). Falls back to root + chestOffset if no Animator.
    ///   r̂         = (targetCoM - origin).normalized, the chest→metal unit vector. Push applies +r̂ to
    ///               the metal and −r̂ to the Mistborn (recoil away from the anchor); Pull flips both.
    ///   F         = k · m_metal · B / (r_m + δ_m)² — ONE raw allomantic force, Newton-symmetric: the SAME
    ///               F drives BOTH bodies (Newton's 3rd). SI: r & δ are in METRES (converted from Unity units
    ///               via <see cref="AllomancyUnits"/> — Vin 2u = 1.53m → 1u = 0.765m), m in kg, so F is in
    ///               Newtons and k is in m³/s². The force scales with the METAL's mass (heavier metal = stronger
    ///               push, canon) and the burn strength B (= the flare multiplier; 1.0 standard, >1 flared).
    ///               (r_m+δ_m) is inverse-square distance falloff; δ clamps the near-field so r→0 never NaNs
    ///               (the mobility term prevents div-by-zero at full lock — no hard state transitions, see below).
    ///   s         = the push/pull SIGN FLAG (bookkeeping, NOT a physical postulate): s = +1 Push, −1 Pull.
    ///               The single force equation: F⃗_allo = s · F · r̂, r̂ = chest→metal FIXED (never flipped by
    ///               push/pull). F⃗_allo is the force ON THE TARGET (Push → +r̂ away, Pull → −r̂ in); the
    ///               Mistborn takes −F⃗_allo (Push → −r̂ away, Pull → +r̂ in). s is orthogonal to mobility M_i.
    ///   mobility  = M_i = 1 / (1 + α · N_i). N_i = the contact NORMAL FORCE on body i (read from the
    ///               physics step as a ground-support proxy: ≈ m·g when grounded/braced, 0 airborne).
    ///               N = 0 → M = 1 (fully free, airborne — launches); N → ∞ → M → 0 (braced against
    ///               something massive — barely moves). Continuous in N — a Mistborn landing ramps
    ///               smoothly instead of snapping between airborne/grounded (the user's explicit ask).
    ///   accelerations (the actual shove — per-body, divided by that body's mass AND mobility; in m/s²):
    ///     a_target   = F / [ m_target   · (1 + α·N_target)   ] · r̂
    ///     a_Mistborn = F / [ m_Mistborn · (1 + α·N_Mistborn) ] · (−r̂ push / +r̂ pull)
    ///   The velocity impulse fed to the CharacterController/Rigidbody is a·dt converted back to Unity
    ///   units/s (× AllomancyUnits.UnitsPerMeter). N is in NEWTONS (m·g with the real g, AllomancyUnits.
    ///   RealGravity = 9.81 — distinct from PlayerController's gameplay gravity 19.62 units/s²); α is in
    ///   1/N. The SI retune (k=134.3, α=1.0, bracingGravity=9.81) preserves the prior game-unit feel EXACTLY
    ///   — see <see cref="AllomancyUnits"/> for the derivation.
    ///   m_Mistborn = bodyMass + gearMass (the allomancer: body + carried gear). Encumbrance matters:
    ///               heavier gear → bigger m_Mistborn → smaller recoil (canon: weight affects flight).
    ///               Capped at AllomanticMaxSpeed.
    ///   object side (the ONLY branch — sets m_target + N_target):
    ///     • anchored (MetalAnchor.anchored==true, no enemy, no loose body — nailed/immovable):
    ///               M_target = 0 → a_target = 0. The object can't move; the planet absorbs the
    ///               reaction. No shove — the player's a_Mistborn is the whole story, you launch off it.
    ///     • attached enemy (MetalAnchor on an Enemy — armor strapped to a body): m_target = m_metal +
    ///               enemyBodyMass (combined); N_target = (grounded ? m_target·g : 0) via Enemy.IsGrounded.
    ///               A grounded armored enemy is braced (low mobility → hard to shove); an airborne one
    ///               is free. Shoved via Enemy.AddPush (capped at PushMaxSpeed). Raise enemyBodyMass
    ///               (~80) for a realistic body mass (heavy armor barely budges — canon: you can't
    ///               yoik armor heavier than you).
    ///     • loose free body (MetalAnchor.anchored==false, a Rigidbody — coins/crates/blocks):
    ///               m_target = m_metal; N_target = LooseBodyN (a ground probe just below the body →
    ///               m_metal·g if resting, 0 airborne). Shoved directly via ShoveRigidbody (capped at
    ///               looseObjectMaxSpeed). A grounded coin is less braced than the grounded Mistborn
    ///               (lighter → smaller N → higher mobility → it moves while you barely do); an
    ///               airborne coin launches. This is the "coin moves but I don't" feel the user wanted.
    ///   range     = the per-anchor effective range (AnchorEffectiveRange) gates ONLY candidacy
    ///               (GatherCandidates / the sight filter), NOT the force falloff — the force now uses
    ///               pure 1/(r+δ)² instead of the removed plateau/smoothstep tail. So a metal is only a
    ///               TARGET when in reach; once targeted the force falls off inverse-square with no
    ///               hard plateau cutoff. Loose free bodies connect over range ∝ mass^1.6 clamped
    ///               [0.06, 2] (a coin reaches only ~5m); anchored/enemy use a Log10 mass bonus.
    ///   impulse   = pure a·dt (acceleration × deltaTime) on both bodies — no per-cooldown frameScale.
    ///               Direction is recomputed each frame so diagonal/arced pushes produce smooth
    ///               parabolic paths as gravity combines with the push vector.
    ///   Flare (hold R / RMB while burning Iron-Steel) multiplies force and drain via B — burn harder,
    ///            push harder, drain faster.
    ///
    /// Targeting (metal-only): only objects carrying a <see cref="MetalAnchor"/> (metal cubes +
    /// armored enemies) are ever considered; non-metal scenery is ignored by the in-range scan.
    /// The PRIMARY target is the NEAREST metal in range — a single click+hold (LMB) pushes/pulls the
    /// closest metal. A "BUBBLE" (double-click LMB then hold) instead pushes/pulls EVERY in-range
    /// metal at once — each gets the per-metal F (Newton-symmetric) and the allomancer takes the sum
    /// of a_Mistborn recoils (see <see cref="ApplyForceBubble"/>). A Zelda-style FREEZE-TIME aim mode
    /// (hold MMB / gamepad L1) lets you lock a different metal: time freezes, the mouse highlights any
    /// in-range metal, and releasing the aim key LOCKS it as the target (kept until it leaves range
    /// or Iron/Steel stops burning). The lock is the bright target line even if it isn't the nearest.
    /// A camera-aim "look at the metal to target it" system was prototyped and is DEFERRED to later
    /// (gated behind <c>useCameraAim</c>, off by default) — single-click is nearest for now, with the
    /// bubble as the all-metals option. F/Q keyboard alternates stay single-target (nearest); only
    /// the mouse double-click gesture triggers the bubble.
    /// In-range SECONDARY anchors still bend the applied force DIRECTION slightly (metal-field
    /// awareness), weighted by relative inverse-square reach and capped — they never add force
    /// magnitude, only shape the vector. (The bubble path skips this bending — each metal is pushed
    /// along its pure r̂.)
    ///
    /// Metallurgic sight: while burning Iron or Steel, a line is drawn to EVERY anchor in range
    /// (you perceive all metal, through walls). LINE THICKNESS encodes NEARNESS — the thickest
    /// line points to the NEAREST metal ("the closest to me"). The TARGET is shown by COLOUR, not
    /// thickness: cool blue for a Steel push target, warm gold for an Iron pull target; secondaries
    /// are dim; a freeze-aim-hovered metal flashes white. Applying the force requires line-of-sight
    /// (chest→target raycast, skipping your own body): if the target is occluded, its line tints red
    /// and no force is applied — you can SEE metal through a wall, but you can't push/pull through it.
    /// </summary>
    public class IronSteel : MonoBehaviour
    {
        [SerializeField] private Allomancer allomancer;
        [SerializeField] private PlayerController mover;
        [SerializeField] private Camera playerCamera;
        // Optional: the player's humanoid Animator (for the live chest-bone origin). Auto-found on
        // the player's model child if unset; null → the root + chestOffset fallback is used.
        [SerializeField] private Animator playerAnimator;

        [Header("Range / Targeting")]
        public float minDistance = 1f;
        public float maxRange = 60f;            // ~200 ft — lore canon: "a few hundred feet"
        [Tooltip("Chest origin offset above the player root (where push/pull lines emanate).")]
        public Vector3 chestOffset = new Vector3(0f, 0.9f, 0f);

        [Header("Force model — Newton-symmetric SI: F = k·m_metal·B/(r_m+δ_m)², mobility M = 1/(1+α·N)")]
        [Tooltip("k — the allomantic force scale, in SI units (m³/s²). F[Newton] = allomanticK · m_metal[kg] · B / (r_m+δ_m)²[m²]. The force scales with the METAL's mass (heavier metal = stronger push, canon) — NOT mass-independent. B = the burn strength (flare multiplier; 1.0 standard, >1 flared). (r_m+δ_m) is inverse-square falloff in METRES (see AllomancyUnits; r & δ are converted from Unity units via MetersPerUnit); δ clamps the near-field so r→0 never NaNs. DEFAULT 134.3 = the old game-unit value 300 × MetersPerUnit³ — it preserves the prior feel EXACTLY after the SI conversion (same per-frame impulse in units/s); retune only if you also change MetersPerUnit or the masses.")]
        public float allomanticK = 134.3f;
        [Tooltip("δ — near-field range clamp, in UNITY UNITS (converted to metres internally via AllomancyUnits.MetersPerUnit). (r+δ) prevents div-by-zero at r=0 and softens the point-blank push. Smaller = sharper near-field.")]
        public float rangeDelta = 0.5f;
        [Tooltip("α — mobility tuning [1/Newton]. M = 1/(1+α·N): how fast mobility drops as the body's contact force N rises. N ≈ m·g (Newtons) when grounded (the ground supports the body's weight → braced, low M → barely moves); N = 0 airborne (M = 1 → free). Higher α = a grounded body is more locked-down. DEFAULT 1.0 = the old game-unit value 0.5 × (g_game/g_real = 19.62/9.81) — preserves the prior feel EXACTLY after switching N to real Newtons.")]
        public float mobilityAlpha = 1.0f;
        [Tooltip("Gravity (m/s²) for the SI ground-support contact-force estimate: N = m·g (Newtons) when grounded. This is the REAL g (AllomancyUnits.RealGravity = 9.81), distinct from PlayerController.gravity (19.62 units/s², a 2× gameplay feel value) — the force/mobility model uses real g; the movement controller keeps its gameplay gravity. The Mistborn + enemies are CharacterControllers (no collision-impulse API), so N is a ground-support proxy; loose bodies use a ground probe.")]
        public float bracingGravity = 9.81f;
        [Tooltip("Smoothing rate (1/s) for the Mistborn's contact-force estimate — the lerp toward N_target prevents the discrete airborne/grounded snap the user rejected (smooth mobility). Higher = snappier transition.")]
        public float mobilitySmoothing = 12f;

        [Header("Mass (m_Mistborn / m_metal / enemy body — feeds the force + mobility model)")]
        [Tooltip("The allomancer's BODY mass. m_Mistborn = bodyMass + gearMass; the player's recoil a = F/(m_Mistborn·(1+α·N_Mistborn)), so carried gear reduces recoil (canon: weight affects flight) and being grounded (N_Mistborn≈m·g) braces you. Single source of truth, read raw by the push AND by the Pewter-modulated movement penalty in PlayerController.")]
        public float bodyMass = 1f;
        [Tooltip("Carried gear mass (weapons, vials, coins, inventory, pouch). 0 for now; the inventory weight aggregator feeds this so encumbrance flows into m_Mistborn and the recoil.")]
        public float gearMass = 0f;
        [Tooltip("An attached enemy's BODY mass (the metal is strapped to it). m_target = m_metal + enemyBodyMass (combined); the enemy's shove a = F/(m_target·(1+α·N_enemy)). Raise (~80) so heavy armor barely budges (canon: you can't yoik a suit of armor heavier than you). Placeholder 1 for now; tune up later.")]
        public float enemyBodyMass = 1f;
        [Tooltip("Cap on a loose free-metal body's shove velocity (so a light coin doesn't run away to absurd speed).")]
        public float looseObjectMaxSpeed = 25f;

        [Header("Steelpush hover (anchored, straight-down push → settle at the derived equilibrium)")]
        [Tooltip("Canon hover height at FULL flare off a `hoverReferenceAnchorMass` metal for a " +
                 "`hoverReferencePlayerMass` Mistborn, in UNITY UNITS (~30 m / 39.2 units = Vin's ~100 ft hover; " +
                 "30 m ÷ AllomancyUnits.MetersPerUnit 0.765). This is the CALIBRATION TARGET for the anchored-hover " +
                 "closed form (Allomantic Force Kernel Addendum, Prop 3): the equilibrium is the closed form " +
                 "r_eq = sqrt(k_hover·m_metal·B/(M·g)) − δ, derived from the force balance F(r_eq)=M·g — NOT a " +
                 "hardcoded ceiling. k_hover is derived (once) from this target + the two reference masses so the " +
                 "closed form reproduces this height at the reference; k_hover is then FIXED, so the LIVE anchor " +
                 "mass, flare, and Mistborn mass all shift r_eq per canon (heavier anchor → higher, more flare → " +
                 "higher, heavier Mistborn → lower; r_eq ∝ sqrt(m_metal)·sqrt(B)/sqrt(M)). Engages only for a " +
                 "near-VERTICAL Steelpush on an ANCHORED metal below the chest (r̂.y < -hoverConeMinBelow); angled " +
                 "pushes and loose coins keep the launch/arc (Prop 2: no free-body hover).")]
        public float hoverHeightAtFullFlare = 39.2f;   // 30 m in Unity units — Vin's ~100 ft hover (calibration target)
        [Tooltip("Reference anchor mass (kg) for the hover calibration — a full-flare straight-down Steelpush off a " +
                 "metal of this mass (with a `hoverReferencePlayerMass` Mistborn) hovers at `hoverHeightAtFullFlare`. " +
                 "One of the two reference masses the proof's Remark says to pick ('determine one of k, m_metal, or B " +
                 "given the other two'); the actual equilibrium still uses the LIVE anchor mass, so heavier/lighter " +
                 "anchors hover higher/lower. DEFAULT 1 = the `MetalAnchor.mass` default that `MakeAnchor` uses for " +
                 "every anchored anchor (wall/plate/cube), so the canon 30 m hover reproduces off the actual sandbox " +
                 "anchors. (Bumping the anchor masses themselves is NOT an option — F ∝ m_metal, so heavier anchors " +
                 "would also ~N×-scale every launch impulse and break the tuned launch feel.)")]
        public float hoverReferenceAnchorMass = 1f;
        [Tooltip("Reference Mistborn mass (kg) for the hover calibration — the player mass at which a full-flare push " +
                 "off a `hoverReferenceAnchorMass` metal hovers at `hoverHeightAtFullFlare`. Kept FIXED when deriving " +
                 "k_hover (NOT the live player mass), so a heavier Mistborn correctly hovers LOWER per canon (weight " +
                 "affects flight; r_eq ∝ 1/sqrt(M)). If this tracked the live mass, M would cancel out of r_eq and the " +
                 "weight-affects-flight dependence would be lost. DEFAULT 1 = bodyMass + gearMass default.")]
        public float hoverReferencePlayerMass = 1f;
        [Tooltip("A Steelpush on a metal whose chest→metal unit vector r̂ has r̂.y < -this counts as 'below me, straight up-and-down' and engages the hover; else the push is an angled launch/arc (canon locomotion). 0.85 = within ~32° of straight down.")]
        [Range(0f, 0.98f)] public float hoverConeMinBelow = 0.85f;

        [Header("Drain while actively pushing/pulling (extra, on top of passive burn)")]
        public float activeDrainPerSecond = 5f;

        [Header("Metallurgic sight (see-all-metal, primary highlighted)")]
        public int maxSightAnchors = 32;
        public float minLineWidth = 0.008f;
        public float maxLineWidth = 0.05f;
        [Range(0.1f, 1f)] public float minBrightness = 0.15f; // floor so low-alignment lines stay visible
        public Color steelSightColor = new Color(0.55f, 0.78f, 1f, 1f);   // primary — push
        public Color ironSightColor  = new Color(1f, 0.82f, 0.45f, 1f);    // primary — pull
        public Color secondarySightColor = new Color(0.35f, 0.55f, 0.95f, 1f); // non-primary lines
        public Color occludedColor = new Color(0.85f, 0.30f, 0.30f, 1f);  // primary when LOS-blocked

        [Header("Targeting score (fallback when nothing is clearly aimed at)")]
        [Tooltip("Power-law weight on camera alignment: score = forceProxy * alignment^assistStrength. Higher = stricter aim.")]
        [Range(1f, 6f)] public float assistStrength = 3f;

        [Tooltip("How much in-range secondary anchors bend the push/pull DIRECTION (metal-field awareness). 0 = pure primary lock; 0.1–0.3 recommended. Secondaries shape direction only — never add force magnitude.")]
        [Range(0f, 1f)] public float secondaryInfluence = 0.2f;

        [Header("Line of sight")]
        [Tooltip("Sight always shows all metal (through walls). If true, APPLYING push/pull also requires an unobstructed chest→target ray.")]
        public bool requireLineOfSight = true;

        [Header("Freeze-time target aim (hold MMB / gamepad L1)")]
        [Tooltip("While burning Iron/Steel, hold the aim key to freeze time and pick a specific metal in range as the push/pull target (Zelda-style). The hovered metal's line turns white; release to lock it.")]
        public bool allowFreezeAim = true;
        [Tooltip("Screen-pixel speed of the gamepad-driven aim cursor (mouse uses the real cursor).")]
        public float aimCursorSpeed = 1600f;
        [Tooltip("If the candidate nearest the cursor is within this many screen pixels, the reticle snaps onto it.")]
        public float aimGrabRadius = 90f;
        [Tooltip("Reticle square size in pixels (snaps onto the hovered metal).")]
        public float aimReticleSize = 18f;

        [Header("Targeting mode")]
        [Tooltip("If true, single-click targets the metal you're LOOKING at (camera-aim). If false (DEFAULT), single-click targets the NEAREST metal — the simple default for now. The camera-aim targeting system is deferred to 'later'; flip this to re-enable it.")]
        public bool useCameraAim = false;

        [Header("Camera-aim (deferred — set useCameraAim=true to use)")]
        [Tooltip("How tight the aim cone is to ACQUIRE a new target, in degrees off the camera forward. Smaller = you must look more directly at the metal. Angular (not a raycast) so a small off-centre coin is still targeted — a ray would miss its few-pixel footprint.")]
        public float aimConeDegrees = 12f;
        [Tooltip("Wider cone to HOLD the current aim target (hysteresis): once you're aimed at a metal, you keep it until it leaves this wider cone. Stops the pick flickering between two near-equally-aligned metals.")]
        public float aimHoldConeDegrees = 22f;
        [Tooltip("A new metal must beat the current aim target by this many degrees to steal the pick (hysteresis de-flicker).")]
        public float aimSwitchMargin = 5f;

        [Header("Bubble — double-click then hold = push/pull ALL in-range metals")]
        [Tooltip("Two LMB presses within this many seconds, then hold, triggers the 'bubble': push/pull ALL in-range metals at once (each metal gets the equal-force F; the allomancer takes the SUM of recoils, Newton's 3rd per metal). A single click+hold pushes/pulls only the NEAREST metal. The targeting system (camera-aim/lock) is deferred to later. Only the mouse gesture triggers the bubble — F/Q keyboard alternates stay single-target.")]
        public float doubleClickWindow = 0.3f;

        [Header("Debug readout (gate diagnostic)")]
        [Tooltip("On-screen text (top-left) showing live allomancy pipeline state — active metal, burn, candidate count, sight-line count, mouse buttons, target + LOS. A diagnostic instrument, not gameplay; toggle off to hide.")]
        public bool debugReadout = false;

        private float scanTimer;
        private const float ScanInterval = 0.1f;
        private const float ForceEpsilon = 1e-6f; // sub-precision force gate — skip negligible impulses
        private MetalAnchor[] cachedAnchors = System.Array.Empty<MetalAnchor>();

        private MetalAnchor currentTarget;
        private Enemy currentTargetEnemy;   // non-null when the target is a movable enemy (loose)
        private Rigidbody looseBody;        // non-null when the target is a free movable metal body (loose)
        private Transform chestBone;        // resolved lazily from the Animator (cached once found)
        private CharacterController playerCC;  // the Mistborn's CC (for the ground-support mobility estimate)
        private float N_mistborn;            // smoothed contact normal force on the Mistborn (ground-support proxy)
        private readonly Collider[] overlapBuf = new Collider[8]; // reused for the loose-body ground probe (no alloc)

        // Height tracker — diagnose the push-off-a-coin launch/float arc (canon: Vin pushes a coin
        // and the recoil launches/floats her). Logs Y, height-above-ground, peak since last grounded,
        // and a derived vertical velocity (dY/dt — allomanticVelocity is private in PlayerController,
        // so this is read from position change). The [Height] console trace fires while airborne so the
        // whole rise+fall arc is visible in the log; values also show in the on-screen debug readout.
        private float heightPrevY = float.NaN; // last frame's Y (NaN = first frame, skip the velocity sample)
        private float heightFloorY;            // Y captured when last grounded (the "ground" reference for `above`)
        private float heightPeakY;             // peak Y since last grounded (the apex of the current launch)
        private float heightVelY;              // derived vertical velocity (Δy/Δt), m/s
        private float heightLogTimer;          // throttle for the [Height] console trace (10 Hz while airborne)
        private float heightAboveGround;      // y - heightFloorY (cached for the debug readout)
        private bool  heightGrounded;          // cached isGrounded (for the debug readout)

        // Targeting: by default the push/pull acts on the NEAREST metal in range (the thickest
        // sight line). The freeze-time aim mode (Chunk C) can lock a different metal as the target;
        // that lock persists until the metal leaves range or Iron/Steel stops burning.
        private MetalAnchor lockedTarget;
        private MetalAnchor aimTarget;      // sticky camera-aim pick (held by hysteresis until it leaves the cone/range)
        private float lastMouseDownTime = -1f;  // double-click detection for the bubble
        private bool bubbleMode;                  // true while LMB is held after a double-click → push/pull ALL in-range metals
        private MetalAnchor hoveredAnchor;  // metal under the cursor during freeze-time aim (Chunk C)

        // Freeze-time aim state (Chunk C). While aiming, timeScale=0 + this component owns the
        // InteractionLock, so its Update must keep running (the lock-bail below skips when we own it).
        private bool aiming;
        private bool aimWeLocked;
        private Vector2 aimCursor;          // screen-space cursor (mouse pos or gamepad right-stick driven)
        private Canvas aimCanvas;            // ScreenSpaceOverlay canvas for the reticle (built in Start)
        private RectTransform aimReticle;    // small square that snaps onto the hovered metal
        private bool aimCursorFromStick;     // true once the right-stick has moved (gamepad takes over)

        // Debug readout (gate diagnostic): a ScreenSpaceOverlay Text top-left showing the live
        // pipeline state. Built once in Start, refreshed in LateUpdate. Non-interactive.
        private Canvas debugCanvas;
        private Text debugText;
        // Console log of the same state — written only when the state CHANGES (an event trace of
        // every transition while you play, not per-frame spam). Tagged [Allomancy] for easy filter.
        private string lastLoggedKey;

        // Sight-line pool (one LineRenderer per anchor in range), under a child parent.
        private LineRenderer[] sightLines;
        private Material sightMaterial;
        private GameObject sightParent;

        // Reusable candidate buffer (no per-frame allocation).
        private struct SightCandidate
        {
            public MetalAnchor anchor;
            public Vector3 position;
            public float distance;
            public Vector3 dir;
            public float alignment;  // Max(0, Dot(camForward, dir)) — camera intent
            public float forceProxy; // distMult — physics reach at this distance
            public float score;      // forceProxy * alignment^assistStrength
        }
        private readonly SightCandidate[] candidates = new SightCandidate[32];
        private int candidateCount;
        private float maxForceProxy; // for relative secondary-line thickness

        // One-shot signal that a push or pull was applied this frame — consumed by the tutorial
        // (so a "push an anchor" step completes only when the player actually does it). Set-only
        // here; the consumer clears it, so frame-ordering between IronSteel.Update and the
        // tutorial's Update never drops the event.
        private static bool didActFlag;
        /// <summary>Returns true once if Iron/Steel applied a push or pull since the last call, then clears it.</summary>
        public static bool ConsumeDidAct() { bool v = didActFlag; didActFlag = false; return v; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStaticState() => didActFlag = false;

        void Start()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            playerCC = GetComponent<CharacterController>(); // for the Mistborn's ground-support mobility estimate
            scanTimer = ScanInterval; // scan on first frame

            // Build the sight-line pool (children of a parent so they don't clutter the root).
            sightParent = new GameObject("AllomancySightLines");
            sightParent.transform.SetParent(transform, false);
            int n = Mathf.Clamp(maxSightAnchors, 1, 64);
            sightLines = new LineRenderer[n];
            Shader s = Shader.Find("Sprites/Default"); // built-in; honours vertex colours
            if (s != null) sightMaterial = new Material(s);
            for (int i = 0; i < n; i++)
            {
                GameObject lineObj = new GameObject($"SightLine_{i}");
                lineObj.transform.SetParent(sightParent.transform, false);
                LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                lr.useWorldSpace = true;
                lr.positionCount = 2;
                lr.numCornerVertices = 4;
                lr.numCapVertices = 4;
                if (sightMaterial != null) lr.material = sightMaterial;
                lr.enabled = false;
                sightLines[i] = lr;
            }

            BuildAimReticle();
            BuildDebugReadout();
        }

        /// <summary>Builds the freeze-time aim reticle: a ScreenSpaceOverlay canvas (ConstantPixelSize
        /// so 1 unit = 1 pixel) with one small square Image, anchored at screen centre, hidden until
        /// aim mode opens. Non-interactive (no GraphicRaycaster); all Images raycastTarget=false.</summary>
        void BuildAimReticle()
        {
            GameObject canvasGo = new GameObject("AllomancyAimCanvas");
            aimCanvas = canvasGo.AddComponent<Canvas>();
            aimCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            aimCanvas.sortingOrder = 210; // above the metal wheel (200) and HUD
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; // pixel-accurate positioning
            // No GraphicRaycaster — the overlay never swallows input.

            GameObject reticleGo = new GameObject("AimReticle");
            reticleGo.transform.SetParent(canvasGo.transform, false);
            Image img = reticleGo.AddComponent<Image>(); // adding Image auto-creates the RectTransform
            aimReticle = img.rectTransform;
            aimReticle.anchorMin = aimReticle.anchorMax = new Vector2(0.5f, 0.5f);
            aimReticle.pivot = new Vector2(0.5f, 0.5f);
            aimReticle.sizeDelta = new Vector2(aimReticleSize, aimReticleSize);
            img.color = new Color(1f, 1f, 1f, 0.9f);
            img.raycastTarget = false;
            // The white square + the white sight line on the hovered metal together mark the pick.
            canvasGo.SetActive(false);
        }

        void ShowReticle(bool show)
        {
            if (aimCanvas != null && aimCanvas.gameObject.activeSelf != show)
                aimCanvas.gameObject.SetActive(show);
        }

        // ── Debug readout (gate diagnostic) ─────────────────────────────────────────
        // A small top-left ScreenSpaceOverlay Text showing the live allomancy pipeline state.
        // Exists to localize the "mouse does nothing" report to a specific pipeline stage without
        // eyeballing line colour: each field probes one stage (burn/metal → scan → sight draw →
        // input → target/LOS). Reads state only; never mutates physics or targeting.
        void BuildDebugReadout()
        {
            GameObject canvasGo = new GameObject("AllomancyDebugCanvas");
            debugCanvas = canvasGo.AddComponent<Canvas>();
            debugCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            debugCanvas.sortingOrder = 220; // above the aim reticle (210) and wheel (200)
            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            // No GraphicRaycaster — purely a readout, never swallows input.

            GameObject textGo = new GameObject("DebugText");
            textGo.transform.SetParent(canvasGo.transform, false);
            RectTransform rt = textGo.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f); // top-left
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(8f, -8f);
            rt.sizeDelta = new Vector2(560f, 140f);
            debugText = textGo.AddComponent<Text>();
            debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            debugText.fontSize = 15;
            debugText.supportRichText = false;
            debugText.alignment = TextAnchor.UpperLeft;
            debugText.raycastTarget = false;
            debugText.color = new Color(1f, 1f, 0.6f, 0.95f); // pale yellow so it reads over any scene
            debugText.text = "allomancy debug (waiting for first frame)";
            canvasGo.SetActive(debugReadout);
        }

        /// <summary>Number of sight lines actually enabled (visible) this frame — the "sight"
        /// field of the debug readout. Distinct from <see cref="candidateCount"/>: cand>0 with
        /// sight==0 isolates a draw-path break, while cand==0 isolates an upstream scan break.</summary>
        int EnabledSightCount()
        {
            if (sightLines == null) return 0;
            int n = 0;
            for (int i = 0; i < sightLines.Length; i++)
                if (sightLines[i] != null && sightLines[i].enabled) n++;
            return n;
        }

        void LateUpdate()
        {
            // Height tracker runs every frame (independent of the debug-readout gate) so the [Height]
            // console trace keeps firing while airborne even if the on-screen readout is off.
            TrackHeight();

            // Re-pin BOTH ends of every enabled sight line to LIVE positions in LateUpdate (the last
            // script callback before render). The lines are drawn in Update, but the chest moves
            // later in the frame (other scripts' Update / the animation pass) AND a loose body drifts
            // within its physics step, so without this a line can trail the body by a frame — the
            // "blue lines lag the centre of my chest" complaint. Live chest via ChestOrigin() (live
            // bone), live anchor end via the candidate's current transform.position. Only touches
            // enabled lines; line i maps to candidates[i] (stable between Update and LateUpdate).
            if (candidateCount > 0 && sightLines != null)
            {
                Vector3 o = ChestOrigin();
                for (int i = 0; i < sightLines.Length; i++)
                {
                    LineRenderer lr = sightLines[i];
                    if (!lr.enabled) continue;
                    lr.SetPosition(0, o);
                    if (i < candidateCount && candidates[i].anchor != null)
                        lr.SetPosition(1, candidates[i].anchor.transform.position);
                }
            }

            if (debugCanvas != null && debugCanvas.gameObject.activeSelf != debugReadout)
                debugCanvas.gameObject.SetActive(debugReadout);
            if (!debugReadout || debugText == null) return;
            if (allomancer == null) { debugText.text = "allomancy: no Allomancer wired"; return; }

            string active = allomancer.ActiveMetal.ToString();
            bool burn = allomancer.IsBurning;
            bool steel = allomancer.IsMetalBurning(MetalType.Steel) && allomancer.GetReserve(MetalType.Steel) > 0f;
            bool iron  = allomancer.IsMetalBurning(MetalType.Iron)  && allomancer.GetReserve(MetalType.Iron)  > 0f;
            float flare = allomancer.FlareMultiplier;
            int cand = candidateCount;
            int sight = EnabledSightCount();
            bool lmb = Input.GetMouseButton(0);
            bool rmb = Input.GetMouseButton(1);
            bool mmb = Input.GetMouseButton(2);

            // What WOULD be pushed/pulled right now: the primary target (lock if valid, else nearest).
            // ResolvePrimary clears a stale lock — correct, idempotent with the Update call.
            string targetName = "none";
            string los = "-";
            MetalAnchor primary = (steel || iron) ? ResolvePrimary() : null;
            if (primary != null)
            {
                targetName = primary.gameObject.name;
                los = HasLineOfSight(primary, ChestOrigin()) ? "LOS" : "blocked";
            }
            bool locked = lockedTarget != null;

            string chestSrc = chestBone != null ? chestBone.name : "fallback";
            bool bub = bubbleMode;

            debugText.text =
                $"active={active}  burn={YN(burn)}  flare={flare:0.00}\n" +
                $"steel={YN(steel)}  iron={YN(iron)}  steelRes={allomancer.GetReserve(MetalType.Steel):0}\n" +
                $"cand={cand}  sight={sight}  chest={chestSrc}\n" +
                $"LMB={YN(lmb)}  RMB={YN(rmb)}  MMB={YN(mmb)}  bubble={YN(bub)}\n" +
                $"target={targetName}  {los}  locked={YN(locked)}\n" +
                $"height y={transform.position.y:F2}  above={heightAboveGround:F2}  peak={heightPeakY:F2}  vY={heightVelY:F2}  grd={YN(heightGrounded)}";

            // Console event log: one line per STATE CHANGE (quantized so the flare ramp doesn't
            // spam). Gives a chronological trace of exactly when the burn/metal/scan/target/input
            // transitions happen as you play — copy the [Allomancy] lines back for the full picture.
            float flareQ = Mathf.Round(flare * 5f) / 5f; // 0.2 steps — at most ~4 lines across a full ramp
            string line =
                $"[Allomancy] active={active} burn={YN(burn)} flare={flareQ:0.0} " +
                $"steel={YN(steel)} iron={YN(iron)} cand={cand} sight={sight} chest={chestSrc} " +
                $"LMB={YN(lmb)} RMB={YN(rmb)} MMB={YN(mmb)} bubble={YN(bub)} " +
                $"target={targetName} {los} locked={YN(locked)}";
            if (line != lastLoggedKey)
            {
                lastLoggedKey = line;
                Debug.Log(line);
            }
        }

        static string YN(bool b) => b ? "Y" : "N";

        void Update()
        {
            // Never push/pull while a menu/dialogue/wheel holds the interaction lock.
            if (MetalWheel.IsOpen)
            {
                if (aiming) ExitAim(false);   // wheel opened mid-aim → cancel, don't change target
                HideSightLines();
                return;
            }
            if (allomancer == null || mover == null || playerCamera == null) return;

            // Default: not hovering this frame. ApplyForce re-enables the hover (SetHoverTarget) only
            // in the straight-down-push regime; every other frame the player is under gravity, so
            // releasing the push (or an angled push) drops/arcs them back down.
            mover.ClearHover();

            bool steelBurning = allomancer.IsMetalBurning(MetalType.Steel) &&
                                allomancer.GetReserve(MetalType.Steel) > 0f;
            bool ironBurning  = allomancer.IsMetalBurning(MetalType.Iron) &&
                                allomancer.GetReserve(MetalType.Iron) > 0f;

            // Sight perceives all metal while either burns (even before pressing LMB).
            if (!steelBurning && !ironBurning)
            {
                HideSightLines();
                lockedTarget = null;   // a burn toggle clears any aim lock (no stale push later)
                aimTarget = null;       // and the sticky camera-aim pick
                bubbleMode = false;     // and the bubble (no stale AoE push after a burn toggle)
                return;
            }

            // Another system owns the lock (dialogue/inventory)? Bail — but not when WE own it from
            // aiming (we must keep drawing the hover highlight + reticle).
            if (InteractionLock.IsLocked && !aimWeLocked)
            {
                HideSightLines();
                return;
            }

            // Throttle the anchor scan (FindObjectsByType) but re-score every frame. Done before aim
            // handling so the hover has data on the entry frame; under timeScale=0 the throttle
            // freezes (no re-scan) which is fine — the world is frozen, the last set is still valid.
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = ScanInterval;
                cachedAnchors = Object.FindObjectsByType<MetalAnchor>();
            }

            Vector3 origin = ChestOrigin();
            Vector3 camForward = playerCamera.transform.forward;

            // ── Mistborn contact normal force N_mistborn for the mobility model (M = 1/(1+α·N)). ─
            // A grounded allomancer is braced (the ground supports their weight → N ≈ (body+gear)·g
            // → low mobility → barely moves on push); airborne → N = 0 → M = 1 (fully free, launches).
            // Smoothed toward the target so the airborne↔grounded transition ramps instead of snapping
            // (the user explicitly wanted continuous mobility, no hard state transitions).
            float N_mistbornTarget = (playerCC != null && playerCC.isGrounded)
                ? (bodyMass + gearMass) * bracingGravity : 0f;
            N_mistborn = Mathf.Lerp(N_mistborn, N_mistbornTarget, 1f - Mathf.Exp(-mobilitySmoothing * Time.deltaTime));

            GatherCandidates(origin, camForward);

            // Freeze-time aim (hold MMB / L1): may enter/exit aim this frame. Evaluated before the
            // force section; while active it owns the InteractionLock + timeScale=0, so the lock-bail
            // above uses aimWeLocked to let us through.
            HandleAim();

            // Primary = the NEAREST metal in range by default (the freeze-time aim mode can lock a
            // different one, kept until it leaves range). The thickest sight line = the nearest.
            MetalAnchor primary = ResolvePrimary();

            bool primaryLOS = primary != null && HasLineOfSight(primary, origin);

            // Double-click-then-hold = "bubble" (push/pull ALL in-range metals). A single click+hold
            // = the nearest metal only. The bubble is a MOUSE gesture — F/Q keyboard alternates stay
            // single-target. Detected here so the sight lines can colour every metal as a target while
            // the bubble is active (visual cue that all metals are being affected).
            if (Input.GetMouseButtonDown(0))
            {
                float now = Time.time;
                if (now - lastMouseDownTime < doubleClickWindow) bubbleMode = true;
                lastMouseDownTime = now;
            }
            if (Input.GetMouseButtonUp(0)) bubbleMode = false;

            DrawSightLines(origin, primary, primaryLOS, steelBurning, bubbleMode);

            // While aiming, time is frozen — never apply force; just show the hover.
            if (aiming) return;

            bool wantPush = steelBurning && Keybinds.PushHeld();
            bool wantPull = ironBurning  && Keybinds.PullHeld();
            if (!wantPush && !wantPull) return;

            if (bubbleMode)
            {
                // Bubble: push/pull EVERY in-range metal at once (each gets the equal-force F; the
                // allomancer takes the SUM of recoils). No single primary; per-metal LOS gates each.
                ApplyForceBubble(origin, wantPush, allomancer.FlareMultiplier);
                return;
            }

            if (primary == null) return;

            // Sight sees through walls; force does not (unless disabled).
            if (requireLineOfSight && !primaryLOS) return;

            ApplyForce(primary, origin, wantPush, allomancer.FlareMultiplier);
        }

        // ── Freeze-time aim mode (Zelda-style target pick) ─────────────────────────────
        // Hold MMB (or gamepad L1) while burning Iron/Steel: time freezes, the mouse highlights any
        // in-range metal, release locks it as the push/pull target. Mirrors MetalWheel's freeze/
        // lock/cursor lifecycle. Runs under timeScale=0 via Time.unscaledDeltaTime for the cursor.
        void HandleAim()
        {
            if (!allowFreezeAim) return;
            bool wantAim = Keybinds.AimHeld();

            if (wantAim && !aiming)
            {
                if (InteractionLock.IsLocked) return; // someone else owns input — don't grab it
                aiming = true;
                aimWeLocked = true;
                InteractionLock.IsLocked = true;
                Time.timeScale = 0f;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                aimCursor = Input.mousePosition;
                aimCursorFromStick = false;
                ShowReticle(true);
            }

            if (aiming) UpdateHover();

            if (!wantAim && aiming) ExitAim(true);
        }

        /// <summary>Exit freeze-time aim: if <paramref name="applyLock"/>, lock the hovered metal as
        /// the target (null → revert to nearest); else cancel (wheel opened, etc.). Always restores
        /// timeScale, cursor, and the InteractionLock we owned.</summary>
        void ExitAim(bool applyLock)
        {
            if (!aiming) return;
            SetLockedTarget(applyLock ? hoveredAnchor : null);
            aiming = false;
            hoveredAnchor = null;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (aimWeLocked) { InteractionLock.IsLocked = false; aimWeLocked = false; }
            ShowReticle(false);
        }

        /// <summary>Each frozen frame: drive the aim cursor (mouse, or gamepad right-stick once it
        /// moves) and set <see cref="hoveredAnchor"/> to the in-range metal nearest the cursor in
        /// screen space. Snaps the reticle onto the hovered metal when it's within grab radius.</summary>
        void UpdateHover()
        {
            float sx = Input.GetAxisRaw("RightStickX");
            float sy = Input.GetAxisRaw("RightStickY");
            if (sx * sx + sy * sy > 0.04f)
            {
                aimCursorFromStick = true;
                aimCursor += new Vector2(sx, sy) * aimCursorSpeed * Time.unscaledDeltaTime;
                aimCursor.x = Mathf.Clamp(aimCursor.x, 0f, Screen.width);
                aimCursor.y = Mathf.Clamp(aimCursor.y, 0f, Screen.height);
            }
            else if (!aimCursorFromStick)
            {
                aimCursor = Input.mousePosition;
            }

            hoveredAnchor = null;
            float bestDistSqr = float.MaxValue;
            Vector2 bestScreen = aimCursor;
            for (int i = 0; i < candidateCount; i++)
            {
                Vector3 sp = playerCamera.WorldToScreenPoint(candidates[i].position);
                if (sp.z <= 0f) continue; // behind the camera
                Vector2 p = new Vector2(sp.x, sp.y);
                float d = (p - aimCursor).sqrMagnitude;
                if (d < bestDistSqr) { bestDistSqr = d; bestScreen = p; hoveredAnchor = candidates[i].anchor; }
            }

            Vector2 reticlePos = aimCursor;
            if (hoveredAnchor != null && bestDistSqr <= aimGrabRadius * aimGrabRadius) reticlePos = bestScreen;
            if (aimReticle != null)
                aimReticle.anchoredPosition = new Vector2(reticlePos.x - Screen.width * 0.5f,
                                                           reticlePos.y - Screen.height * 0.5f);
        }

        /// <summary>The world position the push/pull (and its sight line) emanates from — the
        /// Mistborn's chest, because the body is the source of the force. Resolved lazily from the
        /// player's humanoid Animator so it tracks the live chest bone as the model animates (run,
        /// jump, lean, land), instead of a fixed "idle stance" point that floats off the real chest.
        /// GetBoneTransform can return null until the Animator has bound its avatar, so we re-try
        /// each frame and cache once found. Falls back to root + chestOffset when there's no
        /// Animator (capsule player / model not imported), so it never breaks the capsule path.</summary>
        Vector3 ChestOrigin()
        {
            if (chestBone == null)
            {
                if (playerAnimator == null) playerAnimator = GetComponentInChildren<Animator>();
                if (playerAnimator != null)
                {
                    chestBone = playerAnimator.GetBoneTransform(HumanBodyBones.Chest)
                             ?? playerAnimator.GetBoneTransform(HumanBodyBones.UpperChest)
                             ?? playerAnimator.GetBoneTransform(HumanBodyBones.Spine)
                             ?? playerAnimator.GetBoneTransform(HumanBodyBones.Hips);
                }
            }
            if (chestBone != null) return chestBone.position;
            return transform.position + chestOffset;
        }

        // ── Candidate gathering + scoring ──────────────────────────────────────────

        void GatherCandidates(Vector3 origin, Vector3 camForward)
        {
            candidateCount = 0;
            maxForceProxy = 0.0001f;
            for (int i = 0; i < candidates.Length; i++)
            {
                MetalAnchor a = i < cachedAnchors.Length ? cachedAnchors[i] : null;
                if (a == null) break;
                Vector3 d = a.transform.position - origin;
                float dist = d.magnitude;
                // A metal is a candidate (visible + targetable) ONLY within its OWN force reach.
                // AnchorEffectiveRange is the single owner (shared with ApplyForce), so the set of
                // metals you can TARGET/lock is exactly the set you can push/pull — you can never
                // aim at a coin 8m away and get zero force. (The old trap: the candidate filter used
                // a flat maxRange + the anchored Log10 bonus for every anchor, so a coin was a valid
                // target out to ~90m while its actual force range was ~5m → ApplyForce early-returned
                // with 0 force, 0 recoil, 0 shove. "None of the masses ever move.")
                bool isEnemy = a.GetComponentInParent<Enemy>() != null;
                bool isLoose = !isEnemy && !a.anchored && a.GetComponentInParent<Rigidbody>() != null;
                float eRange = AnchorEffectiveRange(a, isEnemy, isLoose);
                if (dist < minDistance || dist > eRange) continue;
                Vector3 dir = d / dist;
                float alignment = Mathf.Max(0f, Vector3.Dot(camForward, dir));
                float distMult = 1f / ((dist + rangeDelta) * (dist + rangeDelta)); // inverse-square reach proxy (matches the force model F = k·m_metal·B/(r+δ)²)
                float score = distMult * Mathf.Pow(alignment, assistStrength);
                candidates[candidateCount] = new SightCandidate
                {
                    anchor = a, position = a.transform.position, distance = dist,
                    dir = dir, alignment = alignment, forceProxy = distMult, score = score
                };
                if (distMult > maxForceProxy) maxForceProxy = distMult;
                candidateCount++;
                if (candidateCount >= candidates.Length) break;
            }
        }

        /// <summary>Index of the NEAREST candidate (smallest distance). -1 if none. This is the
        /// default push/pull target — the thickest sight line points to it.</summary>
        int NearestIndex()
        {
            int best = -1;
            float bestDist = float.MaxValue;
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidates[i].distance < bestDist) { bestDist = candidates[i].distance; best = i; }
            }
            return best;
        }

        /// <summary>The push/pull target this frame. Priority: (1) the freeze-aim lock (MMB pick) if
        /// still in range; (2) by default the NEAREST metal (single-click push/pull targets the
        /// closest — the simple default). The camera-aim "look at the metal" system is DEFERRED to
        /// later — it lives behind <see cref="useCameraAim"/> (off by default); flip that to bring
        /// back the aim-cone + sticky hysteresis pick. Clears a stale lock / aim pick (left range or
        /// burn toggled) so a moving-away metal or a burn toggle never leaves a dangling target.
        /// The thickest sight line always points to the nearest metal (a nearness cue); the TARGET is
        /// shown by colour (bright = the push/pull pick). The double-click "bubble" bypasses this
        /// entirely — it pushes/pulls ALL in-range metals at once (see <see cref="ApplyForceBubble"/>).</summary>
        MetalAnchor ResolvePrimary()
        {
            // 1) Freeze-aim lock wins (an explicit MMB pick), while it's still in range.
            if (lockedTarget != null)
            {
                for (int i = 0; i < candidateCount; i++)
                    if (candidates[i].anchor == lockedTarget) return lockedTarget;
                lockedTarget = null; // left range — fall through to aim/nearest
            }

            if (candidateCount == 0) { aimTarget = null; return null; }

            // Default = NEAREST metal (single-click push/pull targets the closest). The camera-aim
            // targeting system (look-at-the-metal) is deferred to "later" — set useCameraAim=true to
            // bring it back; it's kept below, untouched, for when we wire it to its own key/mode.
            if (!useCameraAim)
            {
                aimTarget = null;
                int n = NearestIndex();
                return n >= 0 ? candidates[n].anchor : null;
            }

            // (Deferred) Camera-aim: the metal you're LOOKING at, if any in-range metal is within the
            // aim cone. Origin is the CAMERA (not the chest) — "looking at" is about the view ray, and
            // the camera sits behind/above the chest, so the chest origin would mis-aim nearby metals.
            // Angular threshold (not a raycast): a coin is ~0.4m, so a ray would need the crosshair
            // inside its few-pixel footprint to hit; the cone accepts anything within aimConeDegrees
            // regardless of the coin's size/distance, and only considers metals already in range
            // (no non-metal geometry or the player's own model can steal the pick).
            Vector3 camPos = playerCamera.transform.position;
            Vector3 camFwd = playerCamera.transform.forward;
            int best = -1;
            float bestAngle = aimConeDegrees;          // tight cone to ACQUIRE a new target
            for (int i = 0; i < candidateCount; i++)
            {
                Vector3 to = candidates[i].position - camPos;
                float d = to.magnitude;
                if (d < 0.01f) continue;
                float ang = Vector3.Angle(camFwd, to / d);
                if (ang < bestAngle) { bestAngle = ang; best = i; }
            }

            // Sticky hysteresis (de-flicker): once you're aimed at a metal, KEEP it while it stays
            // within the wider HOLD cone and no other metal beats it by the switch margin. This is
            // the supplement that stops the pick flipping every frame when two metals are
            // near-equally aligned with your look — acquire is strict (aimCone), hold is loose
            // (aimHoldCone), and switching requires a clear win (aimSwitchMargin).
            if (aimTarget != null)
            {
                int cur = -1;
                for (int i = 0; i < candidateCount; i++)
                    if (candidates[i].anchor == aimTarget) { cur = i; break; }
                if (cur >= 0)
                {
                    Vector3 to = candidates[cur].position - camPos;
                    float curAng = to.magnitude > 0.01f ? Vector3.Angle(camFwd, to / to.magnitude) : aimConeDegrees;
                    if (curAng < aimHoldConeDegrees && (best < 0 || curAng <= bestAngle + aimSwitchMargin))
                        return candidates[cur].anchor; // still looking roughly at it, nothing clearly better
                }
                aimTarget = null; // left range / out of hold cone → drop the sticky pick
            }

            if (best >= 0)
            {
                aimTarget = candidates[best].anchor;  // newly acquired aim target
                return candidates[best].anchor;
            }

            // Nothing in the aim cone → fall back to NEAREST (reuses the existing single algorithm,
            // no second selection path). The thickest sight line still points to it.
            aimTarget = null;
            int nearest = NearestIndex();
            return nearest >= 0 ? candidates[nearest].anchor : null;
        }

        /// <summary>Lock a specific metal as the push/pull target (set by the freeze-time aim mode,
        /// Chunk C). Pass null to clear the lock and revert to nearest.</summary>
        public void SetLockedTarget(MetalAnchor anchor) => lockedTarget = anchor;
        public MetalAnchor LockedTarget => lockedTarget;

        /// <summary>True if nothing obstructs the chest→target ray. Skips BOTH the player's own
        /// body AND the target's whole hierarchy — reaching the metal/enemy you're pushing is not
        /// "being blocked by" it. Only OTHER geometry (a wall between chest and metal) blocks.
        /// The ray stops a hair short of the target point; for a large/flat metal whose center is
        /// inside its own collider this alone is not enough, so the target-hierarchy skip is what
        /// actually prevents the target from blocking LOS to itself.</summary>
        bool HasLineOfSight(MetalAnchor target, Vector3 origin)
        {
            Vector3 to = target.transform.position - origin;
            float dist = to.magnitude;
            if (dist <= 0.05f) return true;
            Vector3 dir = to / dist;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, dist - 0.05f);
            Transform player = transform;
            Transform tt = target.transform;
            foreach (RaycastHit h in hits)
            {
                Transform ht = h.transform;
                if (ht == player || ht.IsChildOf(player)) continue;                 // skip the player's body
                if (ht == tt || ht.IsChildOf(tt) || tt.IsChildOf(ht)) continue;      // skip the target's whole hierarchy
                return false; // something ELSE solid is between chest and target
            }
            return true;
        }

        // ── Sight rendering ─────────────────────────────────────────────────────────

        void DrawSightLines(Vector3 origin, MetalAnchor primary, bool primaryLOS, bool steel, bool bubble)
        {
            for (int i = 0; i < sightLines.Length; i++)
            {
                LineRenderer lr = sightLines[i];
                if (i >= candidateCount) { lr.enabled = false; continue; }
                SightCandidate c = candidates[i];
                lr.enabled = true;
                lr.SetPosition(0, origin);
                lr.SetPosition(1, c.position);

                bool isPrimary = (c.anchor == primary);
                bool isHovered = (c.anchor == hoveredAnchor);
                // Thickness encodes NEARNESS (the user's cue: the thickest line = the closest metal),
                // independent of whether it's the target. Nearest → maxLineWidth, far → minLineWidth.
                float nearness = 1f - Mathf.Clamp01(c.distance / maxRange);
                float width = Mathf.Lerp(minLineWidth, maxLineWidth, nearness);

                Color col;
                if (isHovered)
                {
                    // Freeze-time aim cursor is on this metal: white highlight, full thickness.
                    col = Color.white;
                    col.a = 1f;
                    width = maxLineWidth;
                }
                else if (bubble)
                {
                    // Bubble: EVERY in-range metal is a target (push/pull ALL). Colour them all
                    // steel-blue / iron-gold so the AoE is obvious. Thickness still marks nearness;
                    // per-metal LOS is gated in ApplyForceBubble (not re-raycast here for the line).
                    col = steel ? steelSightColor : ironSightColor;
                    col.a = 0.85f;
                }
                else if (isPrimary)
                {
                    // The target (what LMB will push/pull): bright steel-blue / iron-gold. Colour
                    // marks the target; thickness still marks nearness — so a far locked target is
                    // bright-coloured but thin, while the nearest is the thickest line.
                    bool occluded = !primaryLOS;
                    col = occluded ? occludedColor : (steel ? steelSightColor : ironSightColor);
                    col.a = occluded ? 0.85f : 1f;
                }
                else
                {
                    // Dim secondary: nearness sets thickness; the sight shows ALL metal (through walls).
                    col = secondarySightColor;
                    col.a = 0.6f;
                }
                lr.startColor = col;
                lr.endColor = col;
                lr.startWidth = width;
                lr.endWidth = width * 0.4f;
            }
        }

        void HideSightLines()
        {
            if (sightLines == null) return;
            for (int i = 0; i < sightLines.Length; i++)
                if (sightLines[i] != null) sightLines[i].enabled = false;
        }

        // ── Force application — Newton-symmetric model with smooth mobility ─────────
        // The user's exact equations (Steelpush/Ironpull):
        //   F          = k · m_metal · B / (r + δ)²              — one raw force, Newton-symmetric (drives BOTH bodies)
        //   a_target   = F / [ m_target   · (1 + α·N_target)   ] · r̂
        //   a_Mistborn = F / [ m_Mistborn · (1 + α·N_Mistborn) ] · (−r̂ push / +r̂ pull)
        //   M_i        = 1 / (1 + α·N_i)                        — mobility; N = contact normal force (0 airborne → M=1; ∞ braced → M=0)
        // The force scales with the METAL's mass (heavier metal = stronger push) — NOT mass-independent.
        // Inverse-square distance falloff ((r+δ) avoids NaN at r=0). Mobility makes a braced (grounded)
        // body barely move and an airborne one move freely — smoothly, no discrete airborne/grounded
        // snap. Velocity caps live in AddAllomanticVelocity (AllomanticMaxSpeed), Enemy.AddPush
        // (PushMaxSpeed), and ShoveRigidbody (looseObjectMaxSpeed). Pure a·dt impulses.

        void ApplyForce(MetalAnchor primary, Vector3 origin, bool wantPush, float flare)
        {
            currentTarget = primary;
            currentTargetEnemy = primary.GetComponentInParent<Enemy>();

            // LOOSE vs ANCHORED. (a) an armored enemy (a CharacterController body the metal is strapped
            // to — shoved via Enemy.AddPush); (b) a non-enemy MetalAnchor flagged !anchored — a free
            // Rigidbody body (coin/crate/block) shoved directly. ANCHORED (anchored==true, no enemy,
            // no loose body) is "nailed to the ground": it never moves, the full reaction launches the
            // Mistborn (M_target = 0 → a_target = 0).
            looseBody = (currentTargetEnemy == null && !primary.anchored)
                ? primary.GetComponentInParent<Rigidbody>() : null;

            // Mass model. m_Mistborn = body + gear (the allomancer) drives the player's recoil;
            // m_metal = the metal piece (scales the force, canon: heavier metal = stronger push);
            // an attached enemy adds its body mass so rigid armor moves as a combined mass.
            float m_mistborn = Mathf.Max(bodyMass + gearMass, 0.05f);  // m_Mistborn (the allomancer)
            float m_metal   = Mathf.Max(primary.mass, 0.05f);         // m_metal   (the metal piece — scales F)
            bool  attachedEnemy = currentTargetEnemy != null;          // metal strapped to an enemy body
            bool  anchored = (currentTargetEnemy == null && primary.anchored && looseBody == null);

            Vector3 toTarget = primary.transform.position - origin;
            float r = toTarget.magnitude;
            if (r < minDistance) return;
            Vector3 rHat = toTarget / r;   // chest → metal (the Push vector on the metal; Pull negates it)

            // ── Hover regime: a near-VERTICAL Steelpush on an ANCHORED metal below the chest ────
            // Per the anchored-hover proof (Allomantic Force Kernel Addendum, Prop 2 / Prop 3 / Cor 1):
            // a hover equilibrium exists ONLY against an ANCHORED metal (m_metal→∞ — the world absorbs
            // the reaction, so A's equation becomes M·r̈ = F(r) − M·g). Two FREE bodies have NO
            // equilibrium (Prop 2: gravity cancels out of the relative equation, r̈ = F·(1/M+1/m) > 0
            // always — pushing down on a loose coin just flings the coin and barely moves you). Hence
            // this gate requires `anchored`; a straight-down push on a loose coin falls through to the
            // impulse path below (coin punted down, mass-ratio upward recoil) — canon.
            //
            // The equilibrium HEIGHT is the proof's closed form (Prop 3), derived from the force
            // balance F(r_eq) = M·g — not a hardcoded number:
            //     r_eq = sqrt( k_hover · m_metal · B / (M · g) ) − δ    (metres; B = flare)
            // This is self-consistent with the force model and yields the canon scalings for free:
            // r_eq ∝ sqrt(flare) (burn harder → hover higher), r_eq ∝ sqrt(m_metal) (bigger metal source →
            // hover higher), r_eq ∝ 1/sqrt(M) (heavier Mistborn → lower; canon: weight affects flight).
            // k_hover is the CALIBRATION CONSTANT (the proof's Remark: "determine one of k, m_metal, or B
            // given the other two" — we pick k). It is derived ONCE from the reference condition (canon
            // target `hoverHeightAtFullFlare` at full flare off a `hoverReferenceAnchorMass` metal for a
            // `hoverReferencePlayerMass` Mistborn) and then FIXED, so the LIVE anchor mass / flare /
            // Mistborn mass all shift r_eq per canon. The equation itself is used directly — NOT a
            // normalized ratio (a ratio would cancel k and distort the sqrt scaling via δ).
            //
            // STABILITY — honest caveat (the proof's one wording error): it calls r_eq "asymptotically
            // stable," but its own linearization M·δr̈ = h'(r_eq)·δr with h'(r_eq)<0 gives δr̈ = −ω²·δr —
            // a simple-harmonic UNDAMPED CENTER: stable (Lyapunov), but NOT asymptotically stable; the
            // amplitude never decays. The "stabilises at ~30 m" behaviour the user specified needs an
            // energy-dissipation term the force law does not provide, so PlayerController's hover channel
            // adds ζ=0.35 damping about this H (overshoot → swing below → above → settle). The proof
            // supplies the equilibrium LOCATION; the damping supplies the SETTLING. They compose.
            //
            // The launch impulse + metal shove are SKIPPED here (the hover owns vertical; the anchored
            // metal is the immovable ground reference). Still drains Steel (sustained hover burns the
            // metal). Angled pushes (r̂.y ≥ -hoverConeMinBelow) and Ironpull fall through to the impulse
            // launch/arc below (a below-metal pull is DOWN, never a hover).
            //
            // AIRBORNE gate (canon + fixes the LaunchPlate): hover is an AIRBORNE sustained push — you
            // launch off the ground first, then sustain-hover once up. So this requires !isGrounded: a
            // grounded straight-down push (e.g. standing on the Anchor_LaunchPlate and pushing it) gets
            // the LAUNCH impulse below instead of being hijacked into a hover. The launch puts you
            // airborne; next frame isGrounded flips false and the sustained push engages the hover.
            if (wantPush && anchored && rHat.y < -hoverConeMinBelow
                && playerCC != null && !playerCC.isGrounded)
            {
                float maxB    = Mathf.Max(allomancer.maxFlareMultiplier, 0.0001f);
                float M       = Mathf.Max(m_mistborn, 0.05f);                          // LIVE Mistborn mass (canon: weight affects flight)
                float delta_m = rangeDelta * AllomancyUnits.MetersPerUnit;              // δ in metres
                float burnB   = Mathf.Max(flare, 0f);                                  // B = flare (burn strength)
                // k_hover — the calibration constant (the proof's Remark: "determine one of k, m_metal, or B
                // given the other two"; we calibrate k). Derived from the reference condition then FIXED, so the
                // closed form reproduces `hoverHeightAtFullFlare` at the reference while the LIVE m_metal / B / M
                // still shift r_eq per canon. From F(r_eq)=M·g at the reference (target metres T, ref masses
                // m_ref / M_ref, full flare maxB):  k_hover = (T + δ)² · M_ref · g / (m_ref · maxB).
                // FIXED reference masses (NOT the live Mistborn mass) are essential: deriving k_hover with the
                // live M would cancel M out of r_eq and erase the canon weight-affects-flight dependence.
                float target_m = hoverHeightAtFullFlare * AllomancyUnits.MetersPerUnit;               // canon hover target (~30 m)
                float kHover = (target_m + delta_m) * (target_m + delta_m)
                             * Mathf.Max(hoverReferencePlayerMass, 0.05f) * bracingGravity
                             / Mathf.Max(hoverReferenceAnchorMass * maxB, 0.0001f);
                // Prop 3 closed form (metres) — the EQUATION itself, with calibrated k_hover and the LIVE
                // anchor mass / flare / Mistborn mass (NOT a normalized ratio): r_eq = sqrt(k·m·B/(M·g)) − δ.
                float rEqMetres = Mathf.Sqrt(kHover * m_metal * burnB / (M * bracingGravity)) - delta_m;
                float hUnits = Mathf.Max(rEqMetres, 0f) * AllomancyUnits.UnitsPerMeter;  // hover height above the metal (units)
                mover.SetHoverTarget(primary.transform.position.y + hUnits);
                allomancer.DrainMetal(MetalType.Steel, activeDrainPerSecond * flare * Time.deltaTime);
                if (Time.deltaTime > 0f) didActFlag = true;
                return;
            }

            // ── ONE raw allomantic force (Newton-symmetric: the SAME F drives both bodies). ──
            // SI: r & δ are converted to metres via AllomancyUnits (Vin 2u = 1.53m → 1u = 0.765m). F in Newtons:
            //   F = k · m_metal · B / (r_m + δ_m)². Mass-proportional to the metal; inverse-square in r;
            //   (r+δ) clamps the near-field so r→0 never NaNs. B = burn strength (the flare multiplier).
            // rClamp (units) is retained for the secondary-influence reach RATIO below (forceProxy is in units,
            // so the ratio stays unit-consistent); rClamp_m (metres) drives the SI force.
            float B = flare;
            float rClamp = r + rangeDelta;                                  // units — secondary reach ratio only
            float rClamp_m = rClamp * AllomancyUnits.MetersPerUnit;         // metres — the SI force
            float F = allomanticK * m_metal * B / (rClamp_m * rClamp_m);    // Newtons

            // ── Object side — m_target + N_target per branch (anchored / attached enemy / loose body).
            //   a_target = F / [ m_target · (1 + α·N_target) ]   (= F/m_target · M_target).
            float aTarget;
            if (anchored)
            {
                // Anchored (immovable): the planet absorbs the reaction. The object can't move, so no
                // shove — the player's recoil (below) is the whole story (you launch off an immovable metal).
                aTarget = 0f;
            }
            else if (attachedEnemy)
            {
                float m_target = Mathf.Max(m_metal + enemyBodyMass, 0.05f);        // metal + body (combined)
                float N_target = currentTargetEnemy.IsGrounded ? m_target * bracingGravity : 0f; // grounded enemy braced
                aTarget = F / (m_target * (1f + mobilityAlpha * N_target));
            }
            else // loose free body
            {
                float m_target = m_metal;                                          // the metal IS the body
                float N_target = LooseBodyN(looseBody, m_metal);                   // ground probe → braced if resting
                aTarget = F / (m_target * (1f + mobilityAlpha * N_target));
            }

            // Secondary anchors shape the push/pull DIRECTION only (never force magnitude) — "metal-
            // field awareness": the vector bends slightly toward other in-range anchors, weighted by
            // relative inverse-square reach, capped at 1. With one anchor (or influence 0) forceDir == rHat.
            Vector3 forceDir = rHat;
            if (secondaryInfluence > 0f && candidateCount > 1)
            {
                float primaryReach = 1f / (rClamp * rClamp); // the primary's inverse-square reach (= F/(k·m_metal·B))
                for (int i = 0; i < candidateCount; i++)
                {
                    if (candidates[i].anchor == primary) continue;
                    float w = primaryReach > ForceEpsilon ? Mathf.Min(candidates[i].forceProxy / primaryReach, 1f) : 0f;
                    forceDir += candidates[i].dir * (w * secondaryInfluence);
                }
                if (forceDir.sqrMagnitude > ForceEpsilon) forceDir.Normalize();
                else forceDir = rHat;
            }

            // ── Accelerations → impulses (pure a·dt). ──
            // One equation, one sign flag (the user's locked-in form):
            //   F⃗_allo = s · (k·m_metal·B)/(r+δ)² · r̂,   r̂ = chest→metal (FIXED — never flipped by push/pull),
            //   s = +1 Push (target +r̂ = away, Mistborn −r̂ = away),  s = −1 Pull (target −r̂ = in, Mistborn +r̂ = in).
            // F⃗_allo is the force ON THE TARGET; the Mistborn takes −F⃗_allo (Newton's 3rd). s is a BOOKKEEPING
            // sign flag, NOT a physical postulate, and has NO relationship to mobility M_i = 1/(1+α·N_i): s sets
            // the force's DIRECTION (intent: push vs pull), M_i sets how much of that force becomes motion vs is
            // absorbed by a constraint (grounded/free/locked). They multiply into the same final acceleration
            // but come from unrelated parts of the system, so they stay separate inputs — bundling them would
            // conflate directionality with constraint topology (a Push vs a Pull against a locked object share
            // the same M_i but have opposite s). a_Mistborn = F / [ m_Mistborn · (1 + α·N_Mistborn) ].
            float aMistborn = F / (m_mistborn * (1f + mobilityAlpha * N_mistborn));
            float s = wantPush ? +1f : -1f;            // +1 Push, −1 Pull (orthogonal to mobility)
            Vector3 targetSign =  s * forceDir;         // force on the target = s·r̂ (Push +r̂ away / Pull −r̂ in)
            Vector3 mistSign   = -s * forceDir;         // Newton's 3rd: Mistborn = −s·r̂ (Push −r̂ away / Pull +r̂ in)

            // Accelerations are in m/s²; the velocity impulse is converted back to Unity units/s (× UnitsPerMeter).
            float u = AllomancyUnits.UnitsPerMeter;
            mover.AddAllomanticVelocity(ClampGroundedLoosePull(mistSign * (aMistborn * Time.deltaTime * u), wantPush, looseBody));

            if (attachedEnemy)
                currentTargetEnemy.AddPush(targetSign * (aTarget * Time.deltaTime * u));
            else if (looseBody != null)
                ShoveRigidbody(looseBody, targetSign, aTarget * Time.deltaTime * u, looseObjectMaxSpeed);
            // anchored: aTarget = 0 → no shove.

            MetalType drainMetal = wantPush ? MetalType.Steel : MetalType.Iron;
            allomancer.DrainMetal(drainMetal, activeDrainPerSecond * flare * Time.deltaTime);
            if (Time.deltaTime > 0f) didActFlag = true; // only count a real (non-frozen) impulse
        }

        /// <summary>Estimate the contact NORMAL FORCE N on a loose free-metal Rigidbody for the
        /// mobility model (M = 1/(1+α·N)). A body resting on the ground is braced (the ground supports
        /// its weight → N ≈ m·g → low mobility → barely moves); an airborne body is free (N = 0 →
        /// M = 1). Detected with a small box probe just below the body's bottom (robust — independent
        /// of collision events / sleeping). Binary supported/not (the Mistborn's transition is the one
        /// smoothed; a coin lifting off is already moving, so the snap is imperceptible).</summary>
        float LooseBodyN(Rigidbody rb, float mass)
        {
            if (rb == null) return 0f;
            Collider col = rb.GetComponent<Collider>();
            if (col == null) return 0f;
            // Thin box just below the body's bottom; any non-self collider there = supported (braced).
            Vector3 below = new Vector3(col.bounds.center.x, col.bounds.min.y - 0.06f, col.bounds.center.z);
            Vector3 half  = new Vector3(col.bounds.extents.x * 0.9f, 0.05f, col.bounds.extents.z * 0.9f);
            int n = Physics.OverlapBoxNonAlloc(below, half, overlapBuf);
            for (int i = 0; i < n; i++)
            {
                Collider c = overlapBuf[i];
                if (c == null) continue;
                if (c.attachedRigidbody == rb) continue;              // own collider
                return mass * bracingGravity;                        // something solid below → braced (contact force ≈ weight)
            }
            return 0f; // airborne → free
        }

        /// <summary>Canon-preserving anti-float clamp on the Mistborn's recoil impulse.</summary>
        /// <remarks>A grounded Mistborn PULLING a LOOSE free body cannot be lifted off the ground by
        /// their own pull — canonically the loose block slides toward them; they do NOT float up.
        /// Without this, a marginal upward pull component (a block whose centre is at / slightly above
        /// the chest) lifts the player a hair off the ground → <see cref="CharacterController.isGrounded"/>
        /// flips false → <see cref="N_mistborn"/> ramps to 0 (unbraced) → the same pull now hits at full
        /// mobility and escalates into a launch (the yanked block also rises past the chest, flipping
        /// r̂ upward, sustaining it). This zeroes ONLY the upward component of a grounded pull against a
        /// loose body, so the canon push-launch (push a grounded coin → −r̂ up → launch) and anchor-climb
        /// (pull an ANCHORED immovable or an airborne anchor above you → +r̂ up → climb) are untouched:
        /// a push is not a pull, and an anchored/airborne pull is not a grounded loose-body pull.</remarks>
        Vector3 ClampGroundedLoosePull(Vector3 impulse, bool wantPush, Rigidbody looseBody)
        {
            if (wantPush || looseBody == null || playerCC == null || !playerCC.isGrounded) return impulse;
            if (impulse.y > 0f) impulse.y = 0f;
            return impulse;
        }

        /// <summary>Height tracker — diagnose the push-off-a-coin launch/float arc (canon: Vin pushes
        /// a coin and the recoil launches/floats her). Captures Y, height-above-ground (Y minus the Y
        /// captured when last grounded), peak Y since last grounded (the apex), and a derived vertical
        /// velocity (Δy/Δt — PlayerController.allomanticVelocity is private, so velocity is read from
        /// position change, which is the visible result anyway). Emits a throttled [Height] console
        /// line WHILE AIRBORNE so the whole rise + apex + fall shows as a trace you can copy back; the
        /// same values also appear in the on-screen debug readout. Runs every frame from LateUpdate.</summary>
        void TrackHeight()
        {
            float y = transform.position.y;
            bool grounded = playerCC != null && playerCC.isGrounded;
            float dt = Time.deltaTime;

            // Derived vertical velocity (m/s). Skip the very first frame (heightPrevY is NaN) so the
            // spawn-snap doesn't produce a wild spike.
            if (!float.IsNaN(heightPrevY) && dt > 0f)
                heightVelY = (y - heightPrevY) / dt;
            heightPrevY = y;

            // Reset the ground reference + peak on landing; while airborne track the apex.
            if (grounded) { heightFloorY = y; heightPeakY = y; }
            else if (y > heightPeakY) heightPeakY = y;

            heightAboveGround = y - heightFloorY;
            heightGrounded = grounded;

            // [Height] console trace — only while airborne (the launch/float), 10 Hz so it doesn't spam.
            // The first airborne frame logs at once (timer resets to 0 on landing); grounded frames are
            // silent so the trace cleanly brackets the airborne arc.
            if (!grounded)
            {
                heightLogTimer -= dt;
                if (heightLogTimer <= 0f)
                {
                    heightLogTimer = 0.1f;
                    Debug.Log($"[Height] y={y:F2} above={heightAboveGround:F2} peak={heightPeakY:F2} vY={heightVelY:F2} grd=N");
                }
            }
            else heightLogTimer = 0f;
        }

        /// <summary>The "bubble" — push/pull EVERY in-range metal at once (double-click then hold LMB).
        /// Each metal receives the SAME Newton-symmetric + mobility model as <see cref="ApplyForce"/>
        /// (F = k·m_metal·B/(r+δ)²; a_target = F/[m_target·(1+α·N_target)]; a_Mistborn =
        /// F/[m_Mistborn·(1+α·N_Mistborn)]), and the allomancer takes the SUM of every metal's recoil
        /// (Newton's 3rd per metal — each metal pushes/pulls you back, and vectors from differently-
        /// placed metals partially cancel, so a surrounded allomancer isn't instantly flattened).
        /// Per-metal LOS gates each (you can't push through a wall, but you push every metal you have a
        /// clear line to). No secondary-influence direction bending — each metal is pushed/pulled along
        /// its pure chest→metal r̂. Drains ONCE for the whole bubble (not per metal). Anchored metals
        /// launch you; loose metals fly; attached enemies shove.</summary>
        void ApplyForceBubble(Vector3 origin, bool wantPush, float flare)
        {
            if (candidateCount == 0) return;
            float m_mistborn = Mathf.Max(bodyMass + gearMass, 0.05f); // m_Mistborn (the allomancer)
            MetalType drainMetal = wantPush ? MetalType.Steel : MetalType.Iron;

            for (int i = 0; i < candidateCount; i++)
            {
                MetalAnchor a = candidates[i].anchor;
                if (a == null) continue;

                // Per-metal LOS: the bubble pushes every metal you have a clear line to (you still
                // can't push through a wall — you just push all the visible metals at once).
                if (requireLineOfSight && !HasLineOfSight(a, origin)) continue;

                Enemy enemy = a.GetComponentInParent<Enemy>();
                Rigidbody lb = (enemy == null && !a.anchored) ? a.GetComponentInParent<Rigidbody>() : null;
                bool attachedEnemy = enemy != null;
                bool anchored = (enemy == null && a.anchored && lb == null);

                Vector3 to = a.transform.position - origin;     // live anchor position
                float r = to.magnitude;
                if (r < minDistance) continue;
                Vector3 rHat = to / r;                          // chest → metal (Pull vector; Push negates)

                // ONE raw allomantic force (Newton-symmetric, mass-proportional to the metal, inverse-square).
                // SI: r & δ in metres via AllomancyUnits (Vin 2u = 1.53m). F = k · m_metal · B / (r_m + δ_m)² in Newtons.
                float m_metal = Mathf.Max(a.mass, 0.05f);
                float B = flare;
                float rClamp_m = (r + rangeDelta) * AllomancyUnits.MetersPerUnit;   // metres
                float F = allomanticK * m_metal * B / (rClamp_m * rClamp_m);        // Newtons

                // Object side — a_target per branch (anchored = 0 / attached enemy / loose body).
                float aTarget;
                if (anchored) aTarget = 0f;
                else if (attachedEnemy)
                {
                    float m_target = Mathf.Max(m_metal + enemyBodyMass, 0.05f);
                    float N_target = enemy.IsGrounded ? m_target * bracingGravity : 0f;
                    aTarget = F / (m_target * (1f + mobilityAlpha * N_target));
                }
                else
                {
                    float m_target = m_metal;
                    float N_target = LooseBodyN(lb, m_metal);
                    aTarget = F / (m_target * (1f + mobilityAlpha * N_target));
                }

                // Per-metal directions from the single s-flag equation (see ApplyForce):
                //   F⃗_allo = s·(k·m_metal·B)/(r+δ)²·r̂, target = s·r̂, Mistborn = −s·r̂ (Newton's 3rd).
                // s = +1 Push / −1 Pull is orthogonal to mobility M_i; r̂ = chest→metal is FIXED.
                // The Mistborn's recoil is summed across all metals by accumulation in
                // AddAllomanticVelocity; vectors from differently-placed metals partially cancel, so a
                // surrounded allomancer isn't instantly flattened. No secondary bending here.
                float aMistborn = F / (m_mistborn * (1f + mobilityAlpha * N_mistborn));
                float s = wantPush ? +1f : -1f;
                Vector3 targetSign =  s * rHat;
                Vector3 mistSign   = -s * rHat;
                // a is in m/s²; impulse → Unity units/s (× UnitsPerMeter). Mistborn's recoil accumulates
                // across all metals in AddAllomanticVelocity (vectors from differently-placed metals cancel).
                float u = AllomancyUnits.UnitsPerMeter;
                mover.AddAllomanticVelocity(ClampGroundedLoosePull(mistSign * (aMistborn * Time.deltaTime * u), wantPush, lb));

                if (attachedEnemy)
                    enemy.AddPush(targetSign * (aTarget * Time.deltaTime * u));
                else if (lb != null)
                    ShoveRigidbody(lb, targetSign, aTarget * Time.deltaTime * u, looseObjectMaxSpeed);
                // anchored: aTarget = 0 → no shove.
            }

            // One drain for the whole bubble (not one per metal — that would drain n× too fast).
            allomancer.DrainMetal(drainMetal, activeDrainPerSecond * flare * Time.deltaTime);
            if (Time.deltaTime > 0f) didActFlag = true;
        }

        /// <summary>Apply a per-frame velocity delta <paramref name="deltaV"/> (= acceleration × dt)
        /// to a loose free-metal Rigidbody along <paramref name="shoveDir"/> — the a=F/m velocity
        /// change from the allomantic force. Only the along-<paramref name="shoveDir"/> component
        /// is changed (preserving any perpendicular motion — gravity fall, a prior sideways shove —
        /// never cancelled), and the along-component is capped at <paramref name="maxSpeed"/> so a
        /// light coin doesn't run away to absurd speed. Wakes a sleeping body so it reacts at once.</summary>
        static void ShoveRigidbody(Rigidbody rb, Vector3 shoveDir, float deltaV, float maxSpeed)
        {
            if (rb == null || rb.isKinematic) return;
            float along = Vector3.Dot(rb.linearVelocity, shoveDir);
            float newAlong = Mathf.Min(along + deltaV, maxSpeed); // apply the impulse delta, cap the along-component
            rb.linearVelocity += shoveDir * (newAlong - along);
            rb.WakeUp();
        }

        /// <summary>The per-anchor effective connection range — the SINGLE owner of which metals are
        /// targetable (gates <see cref="GatherCandidates"/> candidacy). Shared by GatherCandidates so
        /// the set of metals you can TARGET/lock is exactly the set in reach. Canon: larger items
        /// connect from farther away. A loose free body's range scales with its own mass
        /// (power-law, clamped): a coin (0.2) reaches only ~5m, a crate (1) reaches maxRange, a
        /// heavy block (20) reaches 2×maxRange. An anchored metal or an attached enemy's armor gets
        /// the Log10 mass bonus (anchored treated as mass 10 — immovable, full reach).</summary>
        float AnchorEffectiveRange(MetalAnchor a, bool attachedEnemy, bool isLoose)
        {
            float mass = Mathf.Max(a.mass, 0.02f);
            if (isLoose)
            {
                float rangeFactor = Mathf.Clamp(Mathf.Pow(mass, 1.6f), 0.06f, 2f);
                return maxRange * rangeFactor;
            }
            // Anchored (mass 10) or attached-enemy armor (its metal mass): Log10 mass bonus.
            float bonusMass = attachedEnemy ? mass : 10f;
            float anchorBonus = Mathf.Clamp(Mathf.Log10(Mathf.Max(1f, bonusMass)), 0f, 1f);
            return maxRange * (1f + anchorBonus * 0.5f);
        }

        void OnDestroy()
        {
            // Never leave the world frozen or input-locked if we're destroyed mid-aim.
            if (aiming)
            {
                if (aimWeLocked) InteractionLock.IsLocked = false;
                Time.timeScale = 1f;
            }
            if (sightParent != null) Destroy(sightParent);
            if (sightMaterial != null) Destroy(sightMaterial);
            if (aimCanvas != null) Destroy(aimCanvas.gameObject);
            if (debugCanvas != null) Destroy(debugCanvas.gameObject);
        }
    }
}