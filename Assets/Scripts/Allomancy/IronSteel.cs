using UnityEngine;
using BasicRPG.Player;
using BasicRPG.Combat;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Iron (Ironpull, hold <b>Q</b>) &amp; Steel (Steelpush, hold <b>F</b>) — the iconic Mistborn
    /// locomotion/combat ability. The force model is ported verbatim from Ashwalker's
    /// IronPull.cs / SteelPush.cs (the carefully-tuned physics math), adapted to BasicRPG's
    /// CharacterController player (no Rigidbody): the player's force becomes a velocity impulse
    /// via <see cref="PlayerController.AddAllomanticVelocity"/> (decays with drag, clamped to a
    /// max), and armored enemy anchors are shoved via <see cref="Enemy.AddPush"/>.
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
    /// Force model (Ashwalker PHYSICS-MATH-BOOK):
    ///   origin    = the Mistborn's chest bone — live, follows the model's animation (the body is
    ///               the source of the force). Falls back to root + chestOffset if no Animator.
    ///   dir       = (targetCoM - origin).normalized. Push applies -dir (recoil away from the
    ///               anchor); Pull applies +dir (yank toward it).
    ///   distMult  = DistanceAttenuation(r, plateauRange, effectiveRange) — a plateau + smoothstep
    ///               tail (hard 1.0 inside plateauRange, smooth falloff, hard 0 at effectiveRange),
    ///               where effectiveRange = maxRange * (1 + 0.5 * anchorBonus),
    ///               anchorBonus = Clamp(Log10(max(1, mass)), 0, 1) — heavier anchors reach farther.
    ///               A sub-precision guard (ForceEpsilon) treats a below-epsilon force as exactly
    ///               zero — silent jitter, not corruption, is the real danger in this system.
    ///   anchored (static metal: walls/cubes) → full recoil/pull, capped at maxRecoilSpeed /
    ///               maxPullSpeed. The anchor doesn't move; you launch off it.
    ///   loose (armored enemy) → mass-split impulse: the enemy is shoved one way and you recoil
    ///               the other, each velocity capped — Newton's third law for two movable bodies.
    ///   frameScale = dt / cooldown spreads the impulse smoothly across frames (no pulsed
    ///               saw-tooth) while preserving the same net impulse/second as the old per-cooldown
    ///               system. Direction is recomputed each frame so diagonal/arced pushes produce
    ///               smooth parabolic paths as gravity combines with the push vector.
    ///   Flare (hold R) multiplies force and drain — burn harder, push harder, drain faster.
    ///
    /// Targeting (metal-only): only objects carrying a <see cref="MetalAnchor"/> (metal cubes +
    /// armored enemies) are ever considered; non-metal scenery is ignored by both the camera
    /// raycast and the in-range scan. The PRIMARY target is the anchor the camera points at
    /// (raycast, ignoring your own body) — precise, and line-of-sight by nature. If nothing is
    /// clearly aimed at, the primary is the best-scored anchor in range using a power-law
    /// <c>score = forceProxy * alignment^assistStrength</c> (camera intent weighted by a power so
    /// off-centre anchors fall off sharply; forceProxy = distMult so nearer anchors win ties).
    /// In-range SECONDARY anchors then bend the applied force DIRECTION slightly (metal-field
    /// awareness), weighted by relative force and capped — they never add force magnitude, only
    /// shape the vector. This keeps the intent-vs-field distinction: the camera chooses what you
    /// focus on; physics decides what else exists in the field.
    ///
    /// Metallurgic sight: while burning Iron or Steel, a line is drawn to EVERY anchor in range
    /// (you perceive all metal, through walls). The primary is bright/thick (cool blue for
    /// Steel/push, warm gold for Iron/pull); secondaries are dim/thin, brightness encodes camera
    /// alignment, thickness encodes force. Applying the force requires line-of-sight (chest→target
    /// raycast, skipping your own body): if the primary is occluded, its line tints red and no
    /// force is applied — you can SEE metal through a wall, but you can't push/pull through it.
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
        [Tooltip("Hard full-force radius before smoothstep falloff begins (near-field safety zone).")]
        public float plateauRange = 1f;
        [Tooltip("Chest origin offset above the player root (where push/pull lines emanate).")]
        public Vector3 chestOffset = new Vector3(0f, 0.9f, 0f);

        [Header("Push (Steel)")]
        public float pushCooldown = 0.2f;       // impulse spread window
        public float pushSpeed = 25f;           // anchored recoil target speed
        public float maxRecoilSpeed = 20f;     // cap on player recoil
        public float loosePushForce = 35f;      // loose (enemy) push magnitude
        public bool  inverseDistanceScaling = true;

        [Header("Pull (Iron)")]
        public float pullCooldown = 0.2f;
        public float pullSpeed = 20f;           // anchored pull target speed
        public float maxPullSpeed = 18f;
        public float loosePullForce = 30f;

        [Header("Loose-anchor mass split (armored enemies)")]
        [Tooltip("Mass used for the two-body recoil/shove split when pushing/pulling an enemy.")]
        public float playerMass = 1f;
        public float enemyMass = 1f;

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

        private float scanTimer;
        private const float ScanInterval = 0.1f;
        private const float ForceEpsilon = 1e-6f; // sub-precision force gate — skip negligible impulses
        private MetalAnchor[] cachedAnchors = System.Array.Empty<MetalAnchor>();

        private MetalAnchor currentTarget;
        private Enemy currentTargetEnemy;   // non-null when the target is a movable enemy (loose)
        private Transform chestBone;        // resolved lazily from the Animator (cached once found)

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
        }

        void Update()
        {
            // Never push/pull while a menu/dialogue/wheel holds the interaction lock.
            if (InteractionLock.IsLocked || MetalWheel.IsOpen)
            {
                HideSightLines();
                return;
            }
            if (allomancer == null || mover == null || playerCamera == null) return;

            bool steelBurning = allomancer.IsMetalBurning(MetalType.Steel) &&
                                allomancer.GetReserve(MetalType.Steel) > 0f;
            bool ironBurning  = allomancer.IsMetalBurning(MetalType.Iron) &&
                                allomancer.GetReserve(MetalType.Iron) > 0f;

            // Sight perceives all metal while either burns (even before pressing F/Q).
            if (!steelBurning && !ironBurning)
            {
                HideSightLines();
                return;
            }

            // Throttle the anchor scan (FindObjectsByType) but re-score every frame.
            scanTimer -= Time.deltaTime;
            if (scanTimer <= 0f)
            {
                scanTimer = ScanInterval;
                cachedAnchors = Object.FindObjectsByType<MetalAnchor>();
            }

            Vector3 origin = ChestOrigin();
            Vector3 camForward = playerCamera.transform.forward;

            GatherCandidates(origin, camForward);

            // Primary: the anchor the camera points at (precise, LOS by nature); else best-scored.
            MetalAnchor aimed = RaycastAimedAnchor();
            MetalAnchor primary = aimed != null ? aimed
                                  : (candidateCount > 0 ? candidates[BestScoredIndex()].anchor : null);

            bool primaryLOS = primary != null && HasLineOfSight(primary, origin);

            DrawSightLines(origin, primary, primaryLOS, steelBurning);

            bool wantPush = steelBurning && Keybinds.PushHeld();
            bool wantPull = ironBurning  && Keybinds.PullHeld();
            if (primary == null || (!wantPush && !wantPull)) return;

            // Sight sees through walls; force does not (unless disabled).
            if (requireLineOfSight && !primaryLOS) return;

            ApplyForce(primary, origin, wantPush, allomancer.FlareMultiplier);
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
                if (dist < minDistance || dist > maxRange) continue;
                Vector3 dir = d / dist;
                float alignment = Mathf.Max(0f, Vector3.Dot(camForward, dir));
                float anchorBonus = Mathf.Clamp(Mathf.Log10(Mathf.Max(1f, a.GetComponentInParent<Enemy>() != null ? enemyMass : 10f)), 0f, 1f);
                float effectiveRange = maxRange * (1f + anchorBonus * 0.5f);
                float distMult = inverseDistanceScaling ? DistanceAttenuation(dist, plateauRange, effectiveRange) : 1f;
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

        int BestScoredIndex()
        {
            int best = -1;
            float bestScore = float.MinValue;
            for (int i = 0; i < candidateCount; i++)
            {
                if (candidates[i].score > bestScore) { bestScore = candidates[i].score; best = i; }
            }
            return best;
        }

        /// <summary>The nearest MetalAnchor the camera points at (raycast through the screen
        /// centre, ignoring the player's own body and anchors closer than minDistance). LOS by
        /// nature — the ray only hits what's visible.</summary>
        MetalAnchor RaycastAimedAnchor()
        {
            Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            RaycastHit[] hits = Physics.RaycastAll(ray, maxRange);
            MetalAnchor aimed = null;
            float aimedDist = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue; // skip player
                if (h.distance < minDistance) continue;
                MetalAnchor a = h.collider.GetComponentInParent<MetalAnchor>();
                if (a != null && h.distance < aimedDist) { aimedDist = h.distance; aimed = a; }
            }
            return aimed;
        }

        /// <summary>True if nothing obstructs the chest→target ray (your own body is skipped).
        /// Ray stops just short of the target so the target's own collider isn't counted as a block.</summary>
        bool HasLineOfSight(MetalAnchor target, Vector3 origin)
        {
            Vector3 to = target.transform.position - origin;
            float dist = to.magnitude;
            if (dist <= 0.05f) return true;
            Vector3 dir = to / dist;
            RaycastHit[] hits = Physics.RaycastAll(origin, dir, dist - 0.05f);
            foreach (RaycastHit h in hits)
            {
                if (h.transform == transform || h.transform.IsChildOf(transform)) continue; // skip player
                return false; // something solid is between chest and target
            }
            return true;
        }

        // ── Sight rendering ─────────────────────────────────────────────────────────

        void DrawSightLines(Vector3 origin, MetalAnchor primary, bool primaryLOS, bool steel)
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
                Color col;
                float width;
                if (isPrimary)
                {
                    bool occluded = !primaryLOS;
                    col = occluded ? occludedColor : (steel ? steelSightColor : ironSightColor);
                    col.a = occluded ? 0.85f : 1f;
                    width = maxLineWidth;
                }
                else
                {
                    // Brightness = camera alignment (intent); thickness = force (physics).
                    float brightness = Mathf.Lerp(minBrightness, 1f, c.alignment);
                    col = secondarySightColor * brightness;
                    col.a = 0.6f * brightness;
                    float t = Mathf.Clamp01(c.forceProxy / maxForceProxy);
                    width = Mathf.Lerp(minLineWidth, maxLineWidth * 0.6f, t);
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

        // ── Force application (Ashwalker math, gated by LOS) ─────────────────────────

        void ApplyForce(MetalAnchor primary, Vector3 origin, bool wantPush, float flare)
        {
            currentTarget = primary;
            currentTargetEnemy = primary.GetComponentInParent<Enemy>();

            Vector3 targetCoM = primary.transform.position;
            Vector3 toTarget = targetCoM - origin;
            float distance = toTarget.magnitude;
            if (distance < minDistance) return;
            Vector3 dir = toTarget / distance; // chest → target (the Pull vector; Push negates it)

            // Plateau + smoothstep-tail distance falloff with an anchor-mass range bonus (heavier
            // anchors reach farther). Single ownership: this is the only place spatial falloff is
            // computed — never duplicate it elsewhere (a prior bug came from leaking it into a
            // second layer and double-counting the falloff).
            float anchorBonus = Mathf.Clamp(Mathf.Log10(Mathf.Max(1f, TargetMass())), 0f, 1f);
            float effectiveRange = maxRange * (1f + anchorBonus * 0.5f);
            float distMult = inverseDistanceScaling ? DistanceAttenuation(distance, plateauRange, effectiveRange) : 1f;
            // Sub-precision guard: a force below epsilon is treated as exactly zero — not as a
            // tiny nonzero jitter the solver may overreact to. Silent instability is the real
            // danger here, not NaN/Infinity.
            if (distMult <= ForceEpsilon) return;
            bool loose = currentTargetEnemy != null;

            // Secondary anchors shape DIRECTION only (never force magnitude) — "metal field
            // awareness": the push/pull vector bends slightly toward other in-range anchors,
            // weighted by their relative force (physics truth, NOT camera score — the camera
            // decides intent, physics decides what exists in the field), capped at 1 for
            // stability. With one anchor (or influence 0) forceDir == dir, a pure primary lock.
            // Sign is applied once at the boundary below (Push = -forceDir, Pull = +forceDir) —
            // the secondary blend uses +dir for both, so negating for Push symmetrically repels
            // from primary AND secondaries.
            Vector3 forceDir = dir;
            if (secondaryInfluence > 0f && candidateCount > 1)
            {
                for (int i = 0; i < candidateCount; i++)
                {
                    if (candidates[i].anchor == primary) continue;
                    float w = distMult > ForceEpsilon ? Mathf.Min(candidates[i].forceProxy / distMult, 1f) : 0f;
                    forceDir += candidates[i].dir * (w * secondaryInfluence);
                }
                if (forceDir.sqrMagnitude > ForceEpsilon) forceDir.Normalize();
                else forceDir = dir;
            }

            if (wantPush)
            {
                // Push = recoil away from the anchor (-forceDir). The sign flip lives here at the
                // application boundary, never in the attenuation math.
                // Anchored (static metal): full recoil, capped. Loose (enemy): mass-split shove.
                float frameScale = Time.deltaTime / pushCooldown;
                if (!loose)
                {
                    float recoilMag = Mathf.Min(pushSpeed * flare * distMult, maxRecoilSpeed * flare);
                    mover.AddAllomanticVelocity(-forceDir * recoilMag * frameScale);
                }
                else
                {
                    float total = playerMass + enemyMass;
                    float pushMag = loosePushForce * flare * distMult;
                    float targetV = Mathf.Min(pushMag * (playerMass / total), loosePushForce * 3f);
                    float playerV = Mathf.Min(pushMag * (enemyMass / total), maxRecoilSpeed);
                    currentTargetEnemy.AddPush(forceDir * targetV * frameScale);
                    mover.AddAllomanticVelocity(-forceDir * playerV * frameScale);
                }
                allomancer.DrainMetal(MetalType.Steel, activeDrainPerSecond * flare * Time.deltaTime);
                if (Time.deltaTime > 0f) didActFlag = true; // only count a real (non-frozen) impulse
            }
            else // wantPull — yank toward the anchor (+forceDir)
            {
                float frameScale = Time.deltaTime / pullCooldown;
                if (!loose)
                {
                    float speed = Mathf.Min(pullSpeed * flare * distMult, maxPullSpeed * flare);
                    mover.AddAllomanticVelocity(forceDir * speed * frameScale);
                }
                else
                {
                    float total = playerMass + enemyMass;
                    float pullMag = loosePullForce * flare * distMult;
                    float playerV = Mathf.Min(pullMag * (enemyMass / total), maxPullSpeed);
                    float objectV = Mathf.Min(pullMag * (playerMass / total), loosePullForce * 2f);
                    mover.AddAllomanticVelocity(forceDir * playerV * frameScale);
                    currentTargetEnemy.AddPush(-forceDir * objectV * frameScale);
                }
                allomancer.DrainMetal(MetalType.Iron, activeDrainPerSecond * flare * Time.deltaTime);
                if (Time.deltaTime > 0f) didActFlag = true; // only count a real (non-frozen) impulse
            }
        }

        /// <summary>Plateau + smoothstep-tail distance attenuation — the single owner of spatial
        /// falloff. Hard 1.0 inside <paramref name="plateauRange"/>, smoothstep falloff to 0 at
        /// <paramref name="effectiveRange"/>, hard 0 beyond. Avoids both a discontinuous cutoff
        /// and a mushy curve with no near-field safety zone.</summary>
        static float DistanceAttenuation(float distance, float plateauRange, float effectiveRange)
        {
            if (effectiveRange <= plateauRange) return distance >= effectiveRange ? 0f : 1f;
            if (distance <= plateauRange) return 1f;
            if (distance >= effectiveRange) return 0f;
            float t = (distance - plateauRange) / (effectiveRange - plateauRange);
            float smooth = t * t * (3f - 2f * t); // smoothstep, 0→1
            return 1f - smooth; // 1 at plateau edge, 0 at effectiveRange
        }

        // Static anchors are treated as heavy (so their range bonus is meaningful and they don't
        // move). Enemy anchors use enemyMass for the loose two-body split.
        float TargetMass() => currentTargetEnemy != null ? enemyMass : 10f;

        void OnDestroy()
        {
            if (sightParent != null) Destroy(sightParent);
            if (sightMaterial != null) Destroy(sightMaterial);
        }
    }
}