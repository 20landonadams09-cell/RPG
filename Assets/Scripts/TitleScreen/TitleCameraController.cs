/* TitleCameraController.cs
 *
 * Camera for the title sequence. DESIGN RULE:
 *   The camera moves with a slow forward DOLLY within the field and street
 *   phases (a traveling shot — down the alleyway), a slow ORBIT in the aerial
 *   and title phases, and only a tiny drift sway otherwise. No hard cuts within
 *   a phase — the dolly/orbit carries the cinematic momentum (matching the
 *   builder's "field dolly → street dolly → aerial orbit" intent).
 *
 *   Phase 1 (Field): Slow forward dolly through the misty field. Tiny drift.
 *   Phase 3 (Streets): Slow forward dolly down the alleyway. Tiny drift.
 *   Phase 4 (Thornspire): Slow orbit around Thornspire — aerial establishing shot.
 *   Phase 5 (Title): Continues the slow orbit with FOV zoom.
 *
 * Ported from Ashwalker verbatim (pure Transform/Camera — pipeline-agnostic) into the
 * BasicRPG.TitleScreen namespace.
 */

using UnityEngine;

namespace BasicRPG.TitleScreen
{
    public class TitleCameraController : MonoBehaviour
    {
        public enum Phase { MistyField, CinderholdStreets, ThornspireAerial, TitleHold }

        [Header("Phase 1 -- Misty Field (static shot)")]
        public Vector3 fieldPosition = new Vector3(0f, 2.5f, -8f);
        public Vector3 fieldLookAt   = new Vector3(0f, 1.5f, 30f);

        [Header("Phase 3 -- Cinderhold Streets (dolly down the alleyway)")]
        public Vector3 streetPosition = new Vector3(0f, 3f, -5f);
        public Vector3 streetLookAt   = new Vector3(0f, 2.5f, 20f);

        [Header("Phase 1 dolly (forward travel through the field)")]
        public Vector3 fieldDollyEnd      = new Vector3(0f, 2.5f, 14f);
        public Vector3 fieldDollyEndLook  = new Vector3(0f, 1.5f, 40f);
        public float   fieldDollyDuration = 24f;   // spans most of the 0–28s field phase

        [Header("Phase 3 dolly (forward travel down the alleyway)")]
        public Vector3 streetDollyEnd      = new Vector3(0f, 3f, 12f);
        public Vector3 streetDollyEndLook  = new Vector3(0f, 2.5f, 30f);
        public float   streetDollyDuration = 20f;  // spans most of the 28–48s street phase

        [Header("Phase 4 -- Thornspire (slow orbit)")]
        public Vector3 aerialCenter     = new Vector3(0f, 0f, 0f);
        public float   aerialHeight     = 55f;
        public float   aerialRadius     = 35f;
        public float   aerialOrbitSpeed = 0.025f;

        [Header("Phase 5 -- Title Hold")]
        public float   titleZoomStart    = 55f;
        public float   titleZoomEnd      = 42f;
        public float   titleZoomDuration = 12f;

        [Header("Subtle Drift (all phases)")]
        public float driftAmount = 0.008f;
        public float driftSpeed  = 0.15f;

        private Phase currentPhase = Phase.MistyField;
        private float phaseTimer;
        private float orbitAngle;
        private Camera cam;

        void Start()
        {
            cam = GetComponent<Camera>();
            transform.position = fieldPosition;
            transform.LookAt(fieldLookAt);
        }

        public void SetPhase(Phase phase)
        {
            currentPhase = phase;
            phaseTimer = 0f;

            // Snap to the new phase position immediately.
            // This is called DURING a fade-to-black, so the player can't see the snap.
            switch (phase)
            {
                case Phase.MistyField:
                    transform.position = fieldPosition;
                    transform.LookAt(fieldLookAt);
                    break;

                case Phase.CinderholdStreets:
                    transform.position = streetPosition;
                    transform.LookAt(streetLookAt);
                    break;

                case Phase.ThornspireAerial:
                    orbitAngle = 0f;
                    UpdateAerialPosition();
                    break;

                case Phase.TitleHold:
                    // Continue from current aerial position — no snap
                    Vector3 dir = transform.position - aerialCenter;
                    orbitAngle = Mathf.Atan2(dir.z, dir.x);
                    break;
            }
        }

        void Update()
        {
            phaseTimer += Time.deltaTime;

            switch (currentPhase)
            {
                case Phase.MistyField:
                    // Slow forward dolly through the field
                    TravelDolly(fieldPosition, fieldDollyEnd, fieldLookAt, fieldDollyEndLook, fieldDollyDuration);
                    break;

                case Phase.CinderholdStreets:
                    // Slow forward dolly down the alleyway
                    TravelDolly(streetPosition, streetDollyEnd, streetLookAt, streetDollyEndLook, streetDollyDuration);
                    break;

                case Phase.ThornspireAerial:
                    // Slow orbit — the only phase with real movement
                    orbitAngle += aerialOrbitSpeed * Time.deltaTime;
                    UpdateAerialPosition();
                    break;

                case Phase.TitleHold:
                    // Continue orbiting + slow zoom
                    orbitAngle += aerialOrbitSpeed * Time.deltaTime;
                    UpdateAerialPosition();
                    if (cam != null)
                    {
                        float t = Mathf.Clamp01(phaseTimer / titleZoomDuration);
                        cam.fieldOfView = Mathf.Lerp(titleZoomStart, titleZoomEnd, t);
                    }
                    break;
            }
        }

        void UpdateAerialPosition()
        {
            // Slow descent over time
            float descentT = Mathf.Clamp01(phaseTimer / 30f);
            float h = Mathf.Lerp(aerialHeight, aerialHeight * 0.7f, descentT);
            float r = Mathf.Lerp(aerialRadius, aerialRadius * 0.75f, descentT);

            float x = aerialCenter.x + Mathf.Cos(orbitAngle) * r;
            float z = aerialCenter.z + Mathf.Sin(orbitAngle) * r;
            transform.position = new Vector3(x, h, z);
            transform.LookAt(aerialCenter + new Vector3(0f, 10f, 0f));
        }

        /// <summary>
        /// Slow traveling dolly: lerps position + look target from start→end over
        /// `duration` (smoothstep, then holds at the end), with the same subtle drift
        /// sway layered on top so the motion never feels mechanical. The SetPhase snap
        /// resets phaseTimer to 0, so each phase begins its dolly from the start pose.
        /// </summary>
        void TravelDolly(Vector3 startPos, Vector3 endPos, Vector3 startLook, Vector3 endLook, float duration)
        {
            float t = duration > 0f ? Mathf.Clamp01(phaseTimer / duration) : 1f;
            float e = t * t * (3f - 2f * t); // smoothstep
            Vector3 pos  = Vector3.Lerp(startPos,  endPos,  e);
            Vector3 look = Vector3.Lerp(startLook, endLook, e);

            float time = Time.time;
            float dx = Mathf.Sin(time * driftSpeed) * driftAmount;
            float dy = Mathf.Sin(time * driftSpeed * 0.7f) * driftAmount * 0.5f;

            transform.position = pos + new Vector3(dx, dy, 0f);
            transform.LookAt(look);
        }
    }
}