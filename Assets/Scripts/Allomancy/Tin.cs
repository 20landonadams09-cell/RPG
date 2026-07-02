using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using BasicRPG.Player;
using BasicRPG.Combat;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Tin (Tineye) — enhanced senses. Adapted from Ashwalker's Tin.cs for URP + BasicRPG:
    ///   - Night vision / contrast via a URP Volume (ColorAdjustments.postExposure + contrast/
    ///     saturation), a silver-blue Vignette, and Bloom for overload glare. (HDRP Fog has no
    ///     URP equivalent — mist-piercing is dropped.)
    ///   - Camera FOV focus + far-clip extension.
    ///   - AudioListener volume boost (enhanced hearing).
    ///   - Scent: directional 3D pings at enemy positions (through walls).
    ///   - Vibration: enemy CharacterController.velocity → CameraShake.
    ///   - Sensory overload from SensorySource (bright light / loud noise) — the cost of Tin.
    ///   - "World goes dull" desaturate when the reserve runs out.
    /// Flaring (hold Keybinds.Flare) heightens every sense — brighter night vision, louder audio,
    /// wider scent/vibration reach — but also accumulates sensory overload faster (the cost) and
    /// drains the reserve quicker. Heartbeat is still deferred.
    /// </summary>
    public class Tin : MonoBehaviour
    {
        public enum TinState { Off, Burning }
        public TinState CurrentState { get; private set; } = TinState.Off;

        [SerializeField] private Allomancer allomancer;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private CameraShake shake;

        [Header("Vision")]
        [Range(0f, 8f)] public float fovFocusDegrees = 3f;
        [Range(0f, 2000f)] public float farClipBonus = 600f;
        [Range(0f, 1f)] public float nightVisionIntensity = 0.45f; // postExposure EV boost

        [Header("Audio")]
        [Range(1f, 2.5f)] public float audioVolumeBase = 1.3f;

        [Header("Tin Vignette")]
        [Range(0f, 0.35f)] public float tinVignetteIntensity = 0.11f;
        public Color tinVignetteColor = new Color(0.65f, 0.82f, 1.0f, 1f);

        [Header("Scent Detection")]
        [Range(5f, 40f)] public float scentRadius = 22f;
        [Range(0.5f, 3f)] public float scentPingInterval = 1.5f;
        public AudioClip scentPingClip; // optional — silent if null
        [Range(0f, 0.4f)] public float scentPingVolume = 0.10f;

        [Header("Vibration Detection")]
        [Range(3f, 25f)] public float vibrationRadius = 14f;
        [Range(0.1f, 2f)] public float vibrationSpeedThreshold = 0.35f;
        [Range(0f, 0.04f)] public float vibrationShakeMagnitude = 0.008f;

        [Header("Sensory Overload")]
        public float overloadRecoveryRate = 2.5f;
        [Range(0f, 1f)] public float overloadImpairThreshold = 0.3f;

        [Header("Metal Cost")]
        public float baseMetalCostPerSecond = MetallurgyConstants.TinDrainRate;

        // ── Runtime state ──────────────────────────────────────────────────────────
        private float originalFOV;
        private float originalFarClip;
        private float originalAudioVolume;
        private bool hasCameraBaselines;

        // Tin enhances senses → the player sees through their own eyes (first person).
        // Resolved from playerCamera (ThirdPersonCamera lives on the same GameObject).
        private ThirdPersonCamera thirdPersonCamera;

        private float currentOverloadVisual;
        private float currentOverloadAudio;

        private float scentTimer;
        private float vibrationScanTimer;
        private const float VibrationScanInterval = 0.1f;

        private readonly HashSet<SensorySource> knownSourcesLastFrame = new HashSet<SensorySource>();

        private bool metalRanOut;
        private Coroutine worldGoesDullCoroutine;

        private AudioLowPassFilter lowPass;
        private AudioHighPassFilter highPass;

        // Tin's own URP Volume (weight 0 until burning) + overrides.
        private Volume tinVolume;
        private ColorAdjustments colorAdjustments;
        private Vignette vignette;
        private Bloom bloom;

        // Full-screen white overexposure overlay (ugui) for overload.
        private Image overexposureOverlay;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void Start()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            if (playerCamera != null)
            {
                originalFOV = playerCamera.fieldOfView;
                originalFarClip = playerCamera.farClipPlane;
                hasCameraBaselines = true;

                lowPass = playerCamera.GetComponent<AudioLowPassFilter>();
                if (lowPass == null) lowPass = playerCamera.gameObject.AddComponent<AudioLowPassFilter>();
                lowPass.enabled = false;

                highPass = playerCamera.GetComponent<AudioHighPassFilter>();
                if (highPass == null) highPass = playerCamera.gameObject.AddComponent<AudioHighPassFilter>();
                highPass.enabled = false;
            }
            // ThirdPersonCamera shares the camera GameObject (scene builder wires both on camObj).
            thirdPersonCamera = playerCamera != null ? playerCamera.GetComponent<ThirdPersonCamera>() : null;
            originalAudioVolume = AudioListener.volume;

            SetupVolume();
            SetupOverexposureOverlay();
        }

        void SetupVolume()
        {
            GameObject volObj = new GameObject("Tin_Volume");
            tinVolume = volObj.AddComponent<Volume>();
            tinVolume.isGlobal = true;
            tinVolume.priority = 2f;
            tinVolume.weight = 0f;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            tinVolume.profile = profile;

            colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.overrideState = true;
            colorAdjustments.postExposure.value = 0f;
            colorAdjustments.contrast.overrideState = true;
            colorAdjustments.contrast.value = 0f;
            colorAdjustments.saturation.overrideState = true;
            colorAdjustments.saturation.value = 0f;

            vignette = profile.Add<Vignette>(true);
            vignette.color.overrideState = true;
            vignette.color.value = tinVignetteColor;
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0f;

            bloom = profile.Add<Bloom>(true);
            bloom.intensity.overrideState = true;
            bloom.intensity.value = 0f;
            bloom.threshold.overrideState = true;
            bloom.threshold.value = 1.5f;
            bloom.active = false;
        }

        void SetupOverexposureOverlay()
        {
            GameObject canvasObj = new GameObject("TinOverexposureCanvas");
            Canvas canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasObj.AddComponent<CanvasScaler>();

            GameObject obj = new GameObject("OverexposureImage");
            obj.transform.SetParent(canvasObj.transform, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            overexposureOverlay = obj.AddComponent<Image>();
            overexposureOverlay.color = new Color(1f, 1f, 1f, 0f);
            overexposureOverlay.raycastTarget = false;
        }

        void Update()
        {
            float reserve = allomancer != null ? allomancer.GetReserve(MetalType.Tin) : 0f;
            bool burningTin = allomancer != null && allomancer.IsMetalBurning(MetalType.Tin) && reserve > 0f;

            if (CurrentState != TinState.Off && reserve <= 0f)
            {
                metalRanOut = true;
            }

            TinState target = burningTin ? TinState.Burning : TinState.Off;

            if (target != CurrentState)
            {
                if (target == TinState.Off)
                {
                    OnStopBurning(metalRanOut);
                    metalRanOut = false;
                }
                else if (CurrentState == TinState.Off)
                {
                    OnStartBurning();
                }
                CurrentState = target;
            }

            if (CurrentState == TinState.Burning)
            {
                ApplyVisionEffects();
                ApplyAudioVolumeEffect();
                ApplyTinVignette();
                HandleSensoryOverload();
                UpdateScentDetection();
                UpdateVibrationDetection();
                DrainMetal();
            }
            else
            {
                // While off, still track sources so a sudden stimulus on burn-start spikes.
                TrackKnownSources();
            }

            // Overload recovers even while Off (lingering pain), slower while still burning.
            float recoveryScale = CurrentState == TinState.Burning ? 0.5f : 1.5f;
            currentOverloadVisual = Mathf.Max(0f, currentOverloadVisual - overloadRecoveryRate * recoveryScale * Time.deltaTime);
            currentOverloadAudio  = Mathf.Max(0f, currentOverloadAudio  - overloadRecoveryRate * recoveryScale * Time.deltaTime);

            ApplyOverloadVisuals();
            ApplyOverloadAudio();
            ApplyPhysicalOverload();
        }

        // ── State transitions ─────────────────────────────────────────────────────

        void OnStartBurning()
        {
            if (tinVolume != null) tinVolume.weight = 1f;
            if (hasCameraBaselines && playerCamera != null)
                playerCamera.farClipPlane = originalFarClip + farClipBonus;
            // Enhanced senses → see through your own eyes (first person).
            if (thirdPersonCamera != null) thirdPersonCamera.SetFirstPerson(true);
        }

        void OnStopBurning(bool ranOut)
        {
            if (hasCameraBaselines && playerCamera != null)
            {
                playerCamera.fieldOfView = originalFOV;
                playerCamera.farClipPlane = originalFarClip;
            }
            // Senses back to normal → return to the third-person orbit.
            if (thirdPersonCamera != null) thirdPersonCamera.SetFirstPerson(false);
            AudioListener.volume = originalAudioVolume;
            if (lowPass != null) lowPass.enabled = false;
            if (highPass != null) highPass.enabled = false;
            if (tinVolume != null) tinVolume.weight = 0f;
            if (ranOut && worldGoesDullCoroutine == null)
                worldGoesDullCoroutine = StartCoroutine(WorldGoesDullCoroutine());
        }

        // ── Vision ────────────────────────────────────────────────────────────────

        void ApplyVisionEffects()
        {
            if (playerCamera == null) return;
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            float targetFOV = originalFOV - fovFocusDegrees;
            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFOV, Time.deltaTime * 4f);

            if (colorAdjustments != null)
            {
                // Night vision only when not in visual overload (overload owns exposure then).
                // Flaring brightens it further (burn harder → see better, but closer to overload).
                if (currentOverloadVisual <= 0f)
                    colorAdjustments.postExposure.value = nightVisionIntensity * f;
                colorAdjustments.contrast.value = 8f * f;
                colorAdjustments.saturation.value = 6f * f;
            }
        }

        void ApplyTinVignette()
        {
            if (vignette == null || currentOverloadVisual > 0.05f) return; // overload owns the vignette
            vignette.color.value = tinVignetteColor;
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, tinVignetteIntensity, Time.deltaTime * 5f);
        }

        // ── Audio ──────────────────────────────────────────────────────────────────

        void ApplyAudioVolumeEffect()
        {
            if (currentOverloadAudio > 0.25f) return; // overload owns audio
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            AudioListener.volume = originalAudioVolume * audioVolumeBase * f;
        }

        // ── Scent ──────────────────────────────────────────────────────────────────

        void UpdateScentDetection()
        {
            scentTimer -= Time.deltaTime;
            if (scentTimer > 0f) return;
            scentTimer = scentPingInterval;
            if (scentPingClip == null) return;

            // Flaring extends how far you can smell (and pings a touch louder).
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            float radius = scentRadius * f;
            Vector3 me = transform.position;
            foreach (Enemy enemy in Enemy.All)
            {
                if (enemy == null) continue;
                float dist = Vector3.Distance(me, enemy.transform.position);
                if (dist > radius) continue;
                float vol = scentPingVolume * f * (1f - dist / radius);
                AudioSource.PlayClipAtPoint(scentPingClip, enemy.transform.position, vol);
            }
        }

        // ── Vibration ──────────────────────────────────────────────────────────────

        void UpdateVibrationDetection()
        {
            vibrationScanTimer -= Time.deltaTime;
            if (vibrationScanTimer > 0f) return;
            vibrationScanTimer = VibrationScanInterval;

            // Flaring extends how far you can feel movement.
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            float radius = vibrationRadius * f;
            Vector3 me = transform.position;
            foreach (Enemy enemy in Enemy.All)
            {
                if (enemy == null) continue;
                float dist = Vector3.Distance(me, enemy.transform.position);
                if (dist > radius) continue;
                float speed = enemy.Velocity.magnitude;
                if (speed < vibrationSpeedThreshold) continue;
                float proximity = 1f - Mathf.Clamp01(dist / radius);
                if (shake != null) shake.Shake(0.06f, vibrationShakeMagnitude * speed * proximity);
            }
        }

        // ── Sensory Overload ───────────────────────────────────────────────────────

        void HandleSensoryOverload()
        {
            // Flaring heightens senses → stimuli hit harder and overload builds faster (the cost
            // of burning Tin harder). Reserve also drains faster (see DrainMetal).
            float f = allomancer != null ? allomancer.FlareMultiplier : 1f;
            Vector3 me = transform.position;
            foreach (SensorySource source in SensorySource.ActiveSources)
            {
                if (source == null) continue;
                float dist = Vector3.Distance(me, source.transform.position);
                if (dist >= source.radius) continue;

                float falloff = source.falloff > 0f
                    ? Mathf.Pow(1f - dist / source.radius, source.falloff)
                    : 1f - dist / source.radius;

                // Sudden stimulus (new this frame) → immediate spike.
                if (!knownSourcesLastFrame.Contains(source))
                {
                    float spike = falloff * source.intensity * 0.5f * f;
                    if (source.type == SensorySource.SourceType.BrightLight)
                        currentOverloadVisual = Mathf.Clamp01(currentOverloadVisual + spike);
                    else
                        currentOverloadAudio = Mathf.Clamp01(currentOverloadAudio + spike);
                }

                // Persistent source → gradual accumulation.
                float input = falloff * source.intensity * f;
                if (source.type == SensorySource.SourceType.BrightLight)
                    currentOverloadVisual = Mathf.Clamp01(currentOverloadVisual + input * Time.deltaTime * 5f);
                else
                    currentOverloadAudio = Mathf.Clamp01(currentOverloadAudio + input * Time.deltaTime * 5f);
            }
            TrackKnownSources();
        }

        void TrackKnownSources()
        {
            knownSourcesLastFrame.Clear();
            foreach (SensorySource s in SensorySource.ActiveSources)
                if (s != null) knownSourcesLastFrame.Add(s);
        }

        void ApplyOverloadVisuals()
        {
            if (currentOverloadVisual <= 0.02f)
            {
                if (overexposureOverlay != null)
                    overexposureOverlay.color = new Color(1f, 1f, 1f, 0f);
                if (bloom != null) bloom.active = false;
                if (CurrentState == TinState.Off && vignette != null)
                    vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, 0f, Time.deltaTime * 4f);
                return;
            }

            if (vignette != null)
            {
                vignette.color.value = Color.white;
                vignette.intensity.value = currentOverloadVisual * 0.6f;
            }

            if (colorAdjustments != null)
            {
                float nightVision = CurrentState == TinState.Burning ? nightVisionIntensity : 0f;
                colorAdjustments.postExposure.value = Mathf.Max(nightVision, currentOverloadVisual * 8f);
            }

            if (overexposureOverlay != null)
            {
                float alpha = Mathf.Clamp01((currentOverloadVisual - 0.3f) / 0.7f);
                overexposureOverlay.color = new Color(1f, 1f, 1f, alpha);
            }

            if (bloom != null)
            {
                bloom.active = true;
                bloom.intensity.value = currentOverloadVisual * 6f;
                bloom.threshold.value = Mathf.Lerp(1.5f, 0.2f, currentOverloadVisual);
            }

            if (currentOverloadVisual > 0.45f && shake != null)
                shake.Shake(0.1f, currentOverloadVisual * 0.12f);
        }

        void ApplyOverloadAudio()
        {
            if (lowPass == null || highPass == null) return;
            if (currentOverloadAudio > 0.1f)
            {
                lowPass.enabled = true;
                highPass.enabled = true;
                lowPass.cutoffFrequency = Mathf.Lerp(22000f, 800f, currentOverloadAudio);
                highPass.cutoffFrequency = Mathf.Lerp(10f, 3500f, currentOverloadAudio);
                AudioListener.volume = Mathf.Lerp(AudioListener.volume, originalAudioVolume * 0.25f, currentOverloadAudio);
            }
            else
            {
                lowPass.enabled = false;
                highPass.enabled = false;
            }
        }

        void ApplyPhysicalOverload()
        {
            float total = Mathf.Clamp01(currentOverloadVisual + currentOverloadAudio);
            var mover = GetComponentInParent<PlayerController>();

            if (total > overloadImpairThreshold)
            {
                float factor = Mathf.Lerp(1f, 0.4f, (total - overloadImpairThreshold) / (1f - overloadImpairThreshold));
                if (total > 0.9f && Mathf.Sin(Time.time * 7f) > 0.5f) factor *= 0.1f; // stagger
                if (mover != null) mover.SetTinSpeedPenalty(factor);
            }
            else
            {
                if (mover != null) mover.SetTinSpeedPenalty(1f);
            }
        }

        // ── World Goes Dull (run-out) ──────────────────────────────────────────────

        IEnumerator WorldGoesDullCoroutine()
        {
            const float duration = 0.6f;
            if (tinVolume != null) tinVolume.weight = 1f;
            if (colorAdjustments != null) colorAdjustments.saturation.value = -45f;
            if (lowPass != null) { lowPass.enabled = true; lowPass.cutoffFrequency = 1100f; }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                if (colorAdjustments != null)
                    colorAdjustments.saturation.value = Mathf.Lerp(-45f, 0f, t);
                if (lowPass != null)
                    lowPass.cutoffFrequency = Mathf.Lerp(1100f, 22000f, t);
                yield return null;
            }

            if (lowPass != null) lowPass.enabled = false;
            if (tinVolume != null) tinVolume.weight = 0f;
            worldGoesDullCoroutine = null;
        }

        // ── Drain ──────────────────────────────────────────────────────────────────

        void DrainMetal()
        {
            if (allomancer == null) return;
            // Flaring burns Tin harder — drains faster (the Allomancer's passive drain is also
            // flare-scaled; this is Tin's own active-effect cost).
            float f = allomancer.FlareMultiplier;
            allomancer.DrainMetal(MetalType.Tin, baseMetalCostPerSecond * f * Time.deltaTime);
        }

        // ── Cleanup ────────────────────────────────────────────────────────────────

        void OnDestroy()
        {
            if (CurrentState != TinState.Off) OnStopBurning(false);
            if (tinVolume != null) Destroy(tinVolume.gameObject);
        }

        public float GetVisualOverload() => currentOverloadVisual;
        public float GetAudioOverload() => currentOverloadAudio;
        public bool IsBurningTin() => CurrentState == TinState.Burning;
    }
}