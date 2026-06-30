using System;
using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Allomancy;
using BasicRPG.Interaction;

namespace BasicRPG.UI
{
    /// <summary>
    /// A guided, step-by-step tutorial that <b>freezes the game</b> while it teaches, so the
    /// player can't die over and over while reading. It walks a list of <see cref="TutorialStep"/>s
    /// one at a time: each step shows an instruction and only advances once the player completes a
    /// specific, checkable action (press a key, open the wheel, select a metal, start burning,
    /// push/pull an anchor, feel sensory overload, suppress a Thug…). Menu steps freeze the world
    /// (<c>Time.timeScale = 0</c>); action steps release it so the player can move/act. Built entirely
    /// in code (ugui, no prefab/TMP).
    ///
    /// Why time-freeze and NOT <see cref="InteractionLock"/>: freezing via timeScale halts enemies
    /// and player movement (which uses <c>Time.deltaTime</c>), but <b>input still works</b> — the
    /// player can press Tab/B to do the menu steps. InteractionLock is left unlocked on purpose so
    /// Allomancer/MetalWheel input keeps flowing; movement is blocked by the zero time scale, not
    /// by a lock. This is what lets the tutorial verify "press Tab → open wheel → select → burn"
    /// while the world is safely paused. The wheel manages its own timeScale/lock; this overlay
    /// re-asserts the freeze every frame and yields to the wheel while it's open.
    /// </summary>
    public class TutorialOverlay : MonoBehaviour
    {
        /// <summary>One step of the guided tutorial. Serialized into the scene by the builder.</summary>
        [Serializable]
        public class TutorialStep
        {
            [Tooltip("Instruction shown for this step.")]
            [TextArea] public string text = "";
            [Tooltip("How this step completes (advances to the next).")]
            public TutorialStepType type = TutorialStepType.AnyKey;
            [Tooltip("Key the player must press (PressKey type).")]
            public KeyCode key = KeyCode.None;
            [Tooltip("Metal the player must select (SelectMetal type).")]
            public MetalType metal = MetalType.Pewter;
            [Tooltip("If true, freeze the world (timeScale 0) for this step; if false, release it so the player can move/act.")]
            public bool freeze = true;
            [Tooltip("Seconds to wait before completing (Wait type).")]
            public float waitSeconds = 0f;
        }

        public enum TutorialStepType
        {
            AnyKey,        // advance on Input.anyKeyDown (after the per-step grace)
            PressKey,      // advance on Input.GetKeyDown(key)
            OpenWheel,     // advance when MetalWheel.IsOpen
            SelectMetal,   // advance when allomancer.ActiveMetal == metal
            StartBurning,  // advance when allomancer.IsBurning
            PushOrPull,    // advance when IronSteel performed a push/pull (ConsumeDidAct)
            FeelOverload,  // advance when Tin.GetVisualOverload() > overloadThreshold
            SuppressThug,  // advance when an EnemyAllomancer was suppressed (ConsumeDidSuppress)
            Wait,          // advance after waitSeconds (unscaled time)
        }

        [TextArea] public string title = "";
        public TutorialStep[] steps = Array.Empty<TutorialStep>();
        [Tooltip("Ignore completion inputs for this many seconds after a step begins (avoids the key that advanced the previous step instantly advancing this one).")]
        public float stepGrace = 0.35f;
        [Tooltip("Overload level that completes a FeelOverload step.")]
        public float overloadThreshold = 0.10f;
        [Tooltip("Press this to skip the whole tutorial at any time.")]
        public KeyCode skipKey = KeyCode.Backspace;

        private Canvas canvas;
        private CanvasGroup group;
        private Text instructionText;
        private Text counterText;
        private Text continueHint;       // the explicit "do THIS to advance" prompt, pulsed
        private Color continueHintBase;

        private Allomancer allomancer;
        private Tin tin;

        private int currentIndex = -1;
        private float stepAge;
        private float originalTimeScale = 1f;
        private bool finished;

        void Start()
        {
            // Find the scene's allomancy singletons once (the player carries them).
            allomancer = UnityEngine.Object.FindAnyObjectByType<Allomancer>();
            tin = UnityEngine.Object.FindAnyObjectByType<Tin>();
            originalTimeScale = Time.timeScale; // normally 1

            // Claim the tutorial lock so menu/interaction consumers (inventory, interact, save,
            // debug damage, drink/refill) ignore their keys while the tutorial is running — the
            // world is paused, but Input.GetKeyDown still fires at timeScale 0, so without this
            // the player could open the inventory mid-step. Allomancy input (Tab/B/F/Q) is left
            // flowing because the tutorial's own steps need it.
            InteractionLock.TutorialActive = true;

            Build();
            if (steps.Length > 0) ShowStep(0);
            else Finish();
        }

        void Build()
        {
            GameObject canvasObj = new GameObject("TutorialCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // Above the metal wheel (200) so the instruction stays visible while picking; below
            // the death overlay (300). Non-blocking (blocksRaycasts=false) so wheel clicks pass.
            canvas.sortingOrder = 250;
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            group = canvasObj.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            // Panel near the top so it doesn't clash with the centered wheel.
            GameObject panel = new GameObject("Panel");
            panel.transform.SetParent(canvasObj.transform, false);
            RectTransform prt = panel.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0.5f, 1f);
            prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(720f, 150f);
            prt.anchoredPosition = new Vector2(0f, -96f);
            Image bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.06f, 0.09f, 0.86f);
            bg.raycastTarget = false;

            // Title (top-left of panel).
            Text titleText = MakeText(panel.transform, "Title",
                new Vector2(0f, 1f), new Vector2(0.55f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, -8f), new Vector2(0f, 26f), 18, TextAnchor.UpperLeft);
            titleText.color = new Color(1f, 0.9f, 0.5f);
            titleText.text = title;

            // Step counter (top-right of panel).
            counterText = MakeText(panel.transform, "Counter",
                new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-20f, -8f), new Vector2(160f, 26f), 16, TextAnchor.UpperRight);
            counterText.color = new Color(0.7f, 0.75f, 0.8f, 0.9f);

            // Instruction (fills the panel above the continue hint, wraps, auto-shrinks to fit).
            // Bottom inset enlarged (-70 vs -44) to leave a strip for the continue hint below it.
            instructionText = MakeText(panel.transform, "Instruction",
                new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(0.5f, 1f),
                new Vector2(20f, -40f), new Vector2(-40f, -70f), 20, TextAnchor.UpperLeft);
            instructionText.color = new Color(0.92f, 0.94f, 1f);
            instructionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            instructionText.verticalOverflow = VerticalWrapMode.Overflow;
            instructionText.resizeTextForBestFit = true;
            instructionText.resizeTextMaxSize = 22;
            instructionText.resizeTextMinSize = 14;

            // "How to advance" hint — the EXPLICIT action this step waits for, pulsed to draw the
            // eye. The instruction paragraph buries the action in prose; this makes "do THIS to
            // continue" unmistakable. Sits in the bottom-left strip; the skip hint keeps the right.
            continueHint = MakeText(panel.transform, "ContinueHint",
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(20f, 8f), new Vector2(440f, 24f), 16, TextAnchor.MiddleLeft);
            continueHintBase = new Color(0.45f, 0.85f, 1f); // bright cyan
            continueHint.color = continueHintBase;
            continueHint.text = "";

            // Skip hint (bottom-right of panel).
            Text skipHint = MakeText(panel.transform, "SkipHint",
                new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f),
                new Vector2(-20f, 10f), new Vector2(200f, 20f), 13, TextAnchor.LowerRight);
            skipHint.color = new Color(0.6f, 0.62f, 0.68f, 0.85f);
            skipHint.text = "Backspace: skip tutorial";
        }

        void Update()
        {
            if (finished)
            {
                group.alpha = Mathf.Max(0f, group.alpha - Time.unscaledDeltaTime / 0.25f);
                if (group.alpha <= 0f) Destroy(gameObject);
                return;
            }

            group.alpha = Mathf.Min(1f, group.alpha + Time.unscaledDeltaTime / 0.3f);

            // Pulse the continue hint's alpha so the "do this to advance" prompt reads as an
            // active call to action, not static decoration (uses unscaled time so it animates
            // even while the world is frozen).
            if (continueHint != null)
            {
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 4f);
                continueHint.color = new Color(continueHintBase.r, continueHintBase.g, continueHintBase.b, pulse);
            }

            if (currentIndex < 0 || currentIndex >= steps.Length) return;

            TutorialStep step = steps[currentIndex];

            // Re-assert the freeze every frame so it survives the wheel opening/closing (the wheel
            // toggles timeScale on open/close). Yield to the wheel: while it's open, always frozen.
            Time.timeScale = (MetalWheel.IsOpen || step.freeze) ? 0f : originalTimeScale;

            stepAge += Time.unscaledDeltaTime;

            // Skip the whole tutorial.
            if (stepAge >= stepGrace && Input.GetKeyDown(skipKey))
            {
                Finish();
                return;
            }

            // Don't check completion during the per-step grace (prevents double-advance).
            if (stepAge < stepGrace) return;

            if (IsStepComplete(step))
                Advance();
        }

        bool IsStepComplete(TutorialStep step)
        {
            switch (step.type)
            {
                case TutorialStepType.AnyKey:
                    return Input.anyKeyDown || Input.GetMouseButtonDown(0);
                case TutorialStepType.PressKey:
                    return Input.GetKeyDown(step.key);
                case TutorialStepType.OpenWheel:
                    return MetalWheel.IsOpen;
                case TutorialStepType.SelectMetal:
                    return allomancer != null && allomancer.ActiveMetal == step.metal;
                case TutorialStepType.StartBurning:
                    return allomancer != null && allomancer.IsBurning;
                case TutorialStepType.PushOrPull:
                    return IronSteel.ConsumeDidAct();
                case TutorialStepType.FeelOverload:
                    return tin != null && tin.GetVisualOverload() > overloadThreshold;
                case TutorialStepType.SuppressThug:
                    return EnemyAllomancer.ConsumeDidSuppress();
                case TutorialStepType.Wait:
                    return stepAge >= step.waitSeconds;
                default:
                    return false;
            }
        }

        void ShowStep(int index)
        {
            currentIndex = index;
            stepAge = 0f;
            TutorialStep step = steps[index];
            if (instructionText != null) instructionText.text = step.text;
            if (counterText != null) counterText.text = $"Step {index + 1}/{steps.Length}";
            if (continueHint != null) continueHint.text = StepHint(step);
            // Apply the freeze immediately (Update keeps re-asserting it).
            Time.timeScale = (MetalWheel.IsOpen || step.freeze) ? 0f : originalTimeScale;
        }

        /// <summary>The short, action-first prompt shown in the continue hint — names the exact
        /// input/action the current step waits for, regardless of how the instruction paragraph
        /// words it, so "how do I advance?" is always answered at a glance.</summary>
        static string StepHint(TutorialStep step)
        {
            switch (step.type)
            {
                case TutorialStepType.AnyKey:      return "Press any key to continue  ▸";
                case TutorialStepType.PressKey:     return $"Press {PrettyKey(step.key)}  ▸";
                case TutorialStepType.OpenWheel:    return "Open the wheel (Tab)  ▸";
                case TutorialStepType.SelectMetal:  return "Click the metal in the wheel  ▸";
                case TutorialStepType.StartBurning: return "Press B to burn  ▸";
                case TutorialStepType.PushOrPull:   return "Hold F (push) or Q (pull)  ▸";
                case TutorialStepType.FeelOverload: return "Walk into the bright light  ▸";
                case TutorialStepType.SuppressThug: return "Approach a Thug (Copper burning)  ▸";
                case TutorialStepType.Wait:         return $"Wait… ({step.waitSeconds:F0}s)  ▸";
                default:                            return "▸";
            }
        }

        static string PrettyKey(KeyCode k) => k == KeyCode.None ? "a key" : k.ToString();

        void Advance()
        {
            int next = currentIndex + 1;
            if (next >= steps.Length)
                Finish();
            else
                ShowStep(next);
        }

        void Finish()
        {
            finished = true;
            Time.timeScale = originalTimeScale; // always release the game
            InteractionLock.TutorialActive = false; // release the menu/interaction lock
        }

        void OnDestroy()
        {
            // Safety: never leave the game frozen or the menu lock held if the overlay is
            // destroyed mid-tutorial (scene unload, etc.).
            Time.timeScale = originalTimeScale;
            InteractionLock.TutorialActive = false;
        }

        static Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 anchoredPos, Vector2 size, int fontSize, TextAnchor alignment)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = anchoredPos;
            Text t = obj.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = fontSize; t.alignment = alignment;
            t.raycastTarget = false;
            return t;
        }
    }
}