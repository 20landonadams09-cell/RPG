using UnityEngine;
using UnityEngine.UI;
using BasicRPG.Interaction;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Radial metal-SELECTION wheel (rebuilt in code, no prefab). Press Tab: the world mists over,
    /// time freezes, and a ring of all 16 metals appears. Hover one (by direction from screen
    /// center) and CLICK to toggle it in/out of the burn set — toggle as many as you want (multi-
    /// burn is canon: Mistborn allomancers regularly burn 2+ metals at once). Press Tab again to
    /// APPLY the set (those metals start/stop burning) and close; Esc to discard and close. The
    /// burn set is applied only on close, so no allomantic effects (e.g. Tin's first-person flip)
    /// fire while the wheel is open. The flare wheel (MetalRingDriver) then shows one arc per
    /// burning metal with its reserve.
    ///
    /// Self-building: constructs its own ScreenSpaceOverlay canvas (mist background + drifting
    /// ribbons + slots + center readout) in Awake, so the scene builder only wires `allomancer`.
    /// All animation uses Time.unscaledDeltaTime so it stays smooth while Time.timeScale is 0.
    /// </summary>
    public class MetalWheel : MonoBehaviour
    {
        [SerializeField] private Allomancer allomancer;

        // ── Layout ───────────────────────────────────────────────────────────────
        private const float Radius = 200f;
        private const float SlotSize = 56f;
        private const float HoverDeadzone = 40f;   // px from screen center before hover engages
        private const int MistRibbons = 10;
        private const float MistStripWidth = 2800f;

        // ── Runtime state ────────────────────────────────────────────────────────
        public static bool IsOpen { get; private set; }

        private Canvas canvas;
        private CanvasGroup group;
        private float targetAlpha;
        private bool isOpen;
        private int currentSlotIndex;
        private bool weLocked;        // true if THIS wheel set InteractionLock.IsLocked

        // Pending burn set while the wheel is open (a per-metal toggle buffer). Snapshotted from
        // the Allomancer on Open, mutated by clicks, applied to the Allomancer only on a confirmed
        // close (Tab). A cancelled close (Esc) discards it — no live effects while open.
        private bool[] pendingBurn;

        private Font font;
        private Slot[] slots;
        private Text centerText;
        private Text descriptionText;   // one-line Allomantic effect, shown under the metal name

        // Mist ribbons
        private Image[] mistImages;
        private RectTransform[] mistRTs;
        private float[] mistSpeeds;
        private float[] mistBaseAlpha;
        private Image mistBg;

        private static readonly Color MistBgColor = new Color(0.02f, 0.02f, 0.06f, 1f);
        private static readonly Color MistColor   = new Color(0.65f, 0.78f, 0.92f, 1f);

        private enum SlotState { Available, Selected, Active, Empty, Locked, Low }

        private class Slot
        {
            public MetalType metal;
            public RectTransform rt;
            public Image bg;
            public RectTransform fill;
            public Image fillImg;
            public Text label;
            public Color theme;
            public float currentScale = 0.5f;
            public float targetScale = 1f;
            public SlotState state;
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildCanvas();
            BuildSlots();
            if (canvas != null) canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            // If destroyed while open, restore time + cursor + lock so play doesn't hang.
            if (isOpen) Close(confirm: false);
        }

        void Update()
        {
            // Fade the whole overlay.
            if (group != null)
            {
                group.alpha = Mathf.Lerp(group.alpha, targetAlpha, Time.unscaledDeltaTime * 15f);
                if (targetAlpha == 0f && group.alpha < 0.02f)
                {
                    group.alpha = 0f;
                    if (canvas != null) canvas.gameObject.SetActive(false); // save render cost when hidden
                }
                if (targetAlpha == 1f && group.alpha > 0.98f) group.alpha = 1f;
            }

            // Open / close input (runs even while InteractionLock is held by us).
            if (Keybinds.WheelDown())
            {
                if (!isOpen && !InteractionLock.IsLocked) Open();
                else if (isOpen) Close(confirm: true);    // Tab/Share-again APPLY the burn set + close
                return;
            }

            if (!isOpen) { AnimateMist(); return; }

            if (Input.GetKeyDown(KeyCode.Escape)) { Close(confirm: false); return; }  // discard
            if (Keybinds.WheelConfirmDown()) { TogglePending(); return; }             // click = toggle hovered (stay open)

            HandleHover();
            RefreshAll();
            AnimateMist();
        }

        // ── Open / Close ──────────────────────────────────────────────────────────

        void Open()
        {
            isOpen = true;
            IsOpen = true;
            targetAlpha = 1f;
            if (canvas != null) canvas.gameObject.SetActive(true);

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            Time.timeScale = 0f;
            if (!InteractionLock.IsLocked) { InteractionLock.IsLocked = true; weLocked = true; }

            // Snapshot the current burn set into the pending buffer (toggles mutate this; applied
            // only on a confirmed close). Snap hover to the selected metal for convenience.
            pendingBurn = allomancer != null ? allomancer.SaveBurningSet() : new bool[Metals.Count];
            if (allomancer != null)
            {
                int idx = IndexOf(allomancer.ActiveMetal);
                if (idx >= 0) currentSlotIndex = idx;
            }
            UpdateCenter();
        }

        void Close(bool confirm)
        {
            isOpen = false;
            IsOpen = false;
            targetAlpha = 0f;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            Time.timeScale = 1f;
            if (weLocked) { InteractionLock.IsLocked = false; weLocked = false; }

            // Confirm (Tab) → apply the pending burn set to the Allomancer (starts/stops metals to
            // match). Cancel (Esc) → discard; the Allomancer's burn set is untouched.
            if (confirm && allomancer != null && pendingBurn != null)
                allomancer.SetBurningSet(pendingBurn);
            pendingBurn = null;
        }

        // Click while open: toggle the hovered metal in/out of the pending burn set (visual only —
        // applied on close). Locked / empty metals can't be toggled on.
        void TogglePending()
        {
            if (allomancer == null || pendingBurn == null) return;
            if (currentSlotIndex < 0 || currentSlotIndex >= slots.Length) return;
            MetalType m = slots[currentSlotIndex].metal;
            int i = (int)m;
            if (pendingBurn[i])
            {
                pendingBurn[i] = false;   // always allow toggling OFF
            }
            else if (allomancer.IsUnlocked(m) && allomancer.GetReserve(m) > 0f)
            {
                pendingBurn[i] = true;    // toggle ON only if unlocked + has reserve
            }
            else
            {
                NotificationUI.Show(MetalDatabase.Get(m)?.displayName + " is locked/empty");
            }
            UpdateCenter();
        }

        // ── Hover / Refresh ───────────────────────────────────────────────────────

        void HandleHover()
        {
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = (Vector2)Input.mousePosition - center;

            // Gamepad: when the left stick is deflected past its deadzone, it drives the wheel
            // instead of the mouse. The default "Horizontal"/"Vertical" Input Manager entries
            // already aggregate the left stick (axis 0/1, Vertical inverted so up=+1), matching
            // the wheel's screen-space convention (up = +y). Gated on a joystick actually being
            // connected (Input.GetJoystickNames) so keyboard-only players don't accidentally steer
            // the wheel by holding WASD while picking.
            bool padConnected = false;
            foreach (var n in Input.GetJoystickNames())
                if (!string.IsNullOrEmpty(n)) { padConnected = true; break; }
            if (padConnected)
            {
                Vector2 stick = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                if (stick.sqrMagnitude > 0.25f) dir = stick;
            }

            if (dir.magnitude <= HoverDeadzone) return;
            dir.Normalize();

            float maxDot = -2f;
            int best = currentSlotIndex;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null) continue;
                float dot = Vector2.Dot(dir, ((Vector2)slots[i].rt.localPosition).normalized);
                if (dot > maxDot) { maxDot = dot; best = i; }
            }

            if (best != currentSlotIndex)
            {
                currentSlotIndex = best;
                UpdateCenter();
            }
        }

        void RefreshAll()
        {
            if (allomancer == null) return;
            float max = MetallurgyConstants.DefaultMaxReserve;
            for (int i = 0; i < slots.Length; i++)
            {
                Slot s = slots[i];
                if (s == null) continue;

                bool unlocked = allomancer.IsUnlocked(s.metal);
                float norm = Mathf.Clamp01(allomancer.GetReserve(s.metal) / max);
                bool burningThis = pendingBurn != null && pendingBurn[(int)s.metal];

                SlotState st;
                if (!unlocked)               st = SlotState.Locked;
                else if (norm <= 0f)         st = SlotState.Empty;
                else if (burningThis)        st = (norm < 0.2f) ? SlotState.Low : SlotState.Active;
                else if (i == currentSlotIndex) st = SlotState.Selected;
                else                         st = SlotState.Available;
                ApplyState(s, st, norm);
            }
        }

        void ApplyState(Slot s, SlotState st, float reserveNorm)
        {
            s.state = st;
            Color c = s.theme;
            switch (st)
            {
                case SlotState.Locked:   c = new Color(0.4f, 0.4f, 0.4f, 0.25f); s.targetScale = 1f;  break;
                case SlotState.Empty:    c = new Color(0.4f, 0.4f, 0.4f, 0.35f); s.targetScale = 1f;  break;
                case SlotState.Available:c = s.theme * 0.7f;                    s.targetScale = 1f;  break;
                case SlotState.Selected: c = s.theme * 1.0f;                    s.targetScale = 1.15f; break;
                case SlotState.Active:   c = s.theme * 1.4f;                    s.targetScale = 1.15f; break;
                case SlotState.Low:       s.targetScale = 1f;  break; // color pulsed below
            }
            // Always show the metal's short name; locked/empty are distinguished by dim color.
            s.label.text = ShortName(s.metal);
            s.label.color = (st == SlotState.Locked || st == SlotState.Empty)
                ? new Color(1f, 1f, 1f, 0.35f) : Color.white;

            if (st == SlotState.Low)
            {
                float pp = Mathf.PingPong(Time.unscaledTime * 3f, 1f);
                c = Color.Lerp(s.theme * 0.5f, Color.red, pp);
            }

            s.bg.color = c;
            s.fillImg.color = c;

            // Fuel bar
            Vector2 a = s.fill.anchorMax; a.x = reserveNorm; s.fill.anchorMax = a;

            // Scale lerp
            if (Mathf.Abs(s.currentScale - s.targetScale) > 0.005f)
            {
                s.currentScale = Mathf.Lerp(s.currentScale, s.targetScale, 15f * Time.unscaledDeltaTime);
                s.rt.localScale = Vector3.one * s.currentScale;
            }
        }

        void UpdateCenter()
        {
            if (centerText == null || currentSlotIndex < 0 || currentSlotIndex >= slots.Length) return;
            Slot s = slots[currentSlotIndex];
            MetalDefinition def = MetalDatabase.Get(s.metal);
            centerText.text = def != null ? def.displayName : s.metal.ToString();
            centerText.color = s.theme;
            if (descriptionText != null)
                descriptionText.text = def != null && !string.IsNullOrEmpty(def.description) ? def.description : "";
        }

        // ── Build ─────────────────────────────────────────────────────────────────

        void BuildCanvas()
        {
            GameObject canvasObj = new GameObject("MetalWheelCanvas");
            canvasObj.transform.SetParent(transform, false);

            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;

            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            group = canvasObj.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            // Mist background (fullscreen dark)
            GameObject bgObj = new GameObject("MistBg");
            bgObj.transform.SetParent(canvasObj.transform, false);
            mistBg = bgObj.AddComponent<Image>();
            mistBg.color = new Color(MistBgColor.r, MistBgColor.g, MistBgColor.b, 0f);
            mistBg.raycastTarget = false;
            RectTransform bgRT = bgObj.GetComponent<RectTransform>();
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;

            // Mist ribbons
            mistImages = new Image[MistRibbons];
            mistRTs = new RectTransform[MistRibbons];
            mistSpeeds = new float[MistRibbons];
            mistBaseAlpha = new float[MistRibbons];
            for (int i = 0; i < MistRibbons; i++)
            {
                GameObject obj = new GameObject("Mist" + i);
                obj.transform.SetParent(canvasObj.transform, false);
                Image img = obj.AddComponent<Image>();
                img.color = new Color(MistColor.r, MistColor.g, MistColor.b, 0f);
                img.raycastTarget = false;
                mistImages[i] = img;

                float yFrac = Mathf.Clamp01((i + 0.5f) / MistRibbons + Random.Range(-0.04f, 0.04f));
                float hFrac = Random.Range(0.04f, 0.20f);
                RectTransform rt = obj.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, Mathf.Clamp01(yFrac - hFrac * 0.5f));
                rt.anchorMax = new Vector2(0f, Mathf.Clamp01(yFrac + hFrac * 0.5f));
                rt.pivot = new Vector2(0f, 0.5f);
                rt.sizeDelta = new Vector2(MistStripWidth, 0f);
                rt.anchoredPosition = new Vector2(Random.Range(-MistStripWidth, 0f), 0f);
                mistRTs[i] = rt;
                mistSpeeds[i] = Random.Range(20f, 85f) * (Random.value > 0.25f ? 1f : -1f);
                mistBaseAlpha[i] = Random.Range(0.12f, 0.35f);
            }

            // Center readout + hint
            centerText = MakeText(canvasObj.transform, "Center", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 24f),
                new Vector2(600f, 40f), 30, TextAnchor.MiddleCenter);
            centerText.color = Color.white;

            // One-line Allomantic effect, wrapped + auto-shrunk, sitting just under the metal name
            // inside the wheel's empty center (clear of the slot ring). Neutral light-grey so it
            // reads regardless of the hovered metal's theme color (the name above already carries it).
            descriptionText = MakeText(canvasObj.transform, "Description", new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -58f),
                new Vector2(360f, 96f), 15, TextAnchor.MiddleCenter);
            descriptionText.color = new Color(0.82f, 0.85f, 0.92f, 0.95f);
            descriptionText.horizontalOverflow = HorizontalWrapMode.Wrap;
            descriptionText.verticalOverflow = VerticalWrapMode.Overflow;
            descriptionText.resizeTextForBestFit = true;
            descriptionText.resizeTextMaxSize = 16;
            descriptionText.resizeTextMinSize = 11;

            Text hint = MakeText(canvasObj.transform, "Hint", new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 60f),
                new Vector2(600f, 28f), 16, TextAnchor.MiddleCenter);
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            hint.text = "Click: toggle burn    Tab: apply    Esc: cancel";
        }

        void BuildSlots()
        {
            GameObject container = new GameObject("Slots");
            container.transform.SetParent(canvas.transform, false);
            RectTransform crt = container.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.5f, 0.5f);
            crt.anchorMax = new Vector2(0.5f, 0.5f);
            crt.pivot = new Vector2(0.5f, 0.5f);
            crt.sizeDelta = Vector2.zero;
            crt.anchoredPosition = Vector2.zero;

            // Wheel order: all 16 metals, slot 0 at top (Ashwalker convention).
            slots = new Slot[Metals.Count];
            for (int i = 0; i < Metals.Count; i++)
            {
                MetalType metal = (MetalType)i;
                float angle = i * (Mathf.PI * 2f / Metals.Count) + (Mathf.PI * 0.5f);
                Vector3 pos = new Vector3(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * -Radius, 0f);
                slots[i] = CreateSlot(container.transform, metal, pos);
            }
        }

        Slot CreateSlot(Transform parent, MetalType metal, Vector3 pos)
        {
            GameObject obj = new GameObject(metal.ToString());
            obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(SlotSize, SlotSize);
            rt.anchoredPosition = pos;

            Slot s = new Slot { metal = metal, rt = rt };
            MetalDefinition def = MetalDatabase.Get(metal);
            s.theme = def != null ? def.hudColor : Color.white;

            s.bg = obj.AddComponent<Image>();
            s.bg.color = s.theme * 0.7f;
            s.bg.raycastTarget = false;

            // Fuel bar background (bottom strip of the slot)
            GameObject barBg = new GameObject("FuelBg");
            barBg.transform.SetParent(obj.transform, false);
            RectTransform barBgRT = barBg.AddComponent<RectTransform>();
            barBgRT.anchorMin = new Vector2(0f, 0f); barBgRT.anchorMax = new Vector2(1f, 0f);
            barBgRT.pivot = new Vector2(0.5f, 0f);
            barBgRT.sizeDelta = new Vector2(-6f, 6f);
            barBgRT.anchoredPosition = new Vector2(0f, 3f);
            Image barBgImg = barBg.AddComponent<Image>();
            barBgImg.color = new Color(0f, 0f, 0f, 0.6f);
            barBgImg.raycastTarget = false;

            // Fuel fill (anchorMax.x driven by reserve %)
            GameObject fill = new GameObject("FuelFill");
            fill.transform.SetParent(barBg.transform, false);
            RectTransform fillRT = fill.AddComponent<RectTransform>();
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.pivot = new Vector2(0f, 0.5f);
            fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;
            Image fillImg = fill.AddComponent<Image>();
            fillImg.color = s.theme;
            fillImg.raycastTarget = false;
            s.fill = fillRT; s.fillImg = fillImg;

            // Label: metal short name
            GameObject lbl = new GameObject("Label");
            lbl.transform.SetParent(obj.transform, false);
            RectTransform lblRT = lbl.AddComponent<RectTransform>();
            lblRT.anchorMin = new Vector2(0f, 0f); lblRT.anchorMax = new Vector2(1f, 1f);
            lblRT.pivot = new Vector2(0.5f, 0.5f);
            lblRT.offsetMin = new Vector2(0f, 6f); lblRT.offsetMax = new Vector2(0f, -2f);
            s.label = lbl.AddComponent<Text>();
            s.label.font = font;
            s.label.fontSize = 14;
            s.label.alignment = TextAnchor.LowerCenter;
            s.label.text = ShortName(metal);
            s.label.raycastTarget = false;

            return s;
        }

        void AnimateMist()
        {
            if (mistBg == null) return;
            // Fade the dark background toward the canvas group alpha (group drives overall fade).
            Color bg = mistBg.color;
            bg.a = group.alpha;
            mistBg.color = bg;

            float screenW = Screen.width;
            for (int i = 0; i < MistRibbons; i++)
            {
                if (mistRTs[i] == null) continue;
                Vector3 p = mistRTs[i].anchoredPosition;
                p.x += mistSpeeds[i] * Time.unscaledDeltaTime;
                if (mistSpeeds[i] > 0f && p.x > screenW) p.x = -MistStripWidth;
                else if (mistSpeeds[i] < 0f && p.x < -MistStripWidth) p.x = screenW;
                mistRTs[i].anchoredPosition = p;

                float pulse = mistBaseAlpha[i] * (0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 0.45f + i * 1.05f));
                Color c = mistImages[i].color;
                c.a = group.alpha * pulse;
                mistImages[i].color = c;
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        static int IndexOf(MetalType m) => (int)m;

        static string ShortName(MetalType m)
        {
            switch (m)
            {
                case MetalType.Iron: return "Fe";
                case MetalType.Steel: return "St";
                case MetalType.Pewter: return "Pw";
                case MetalType.Tin: return "Sn";
                case MetalType.Copper: return "Cu";
                case MetalType.Bronze: return "Br";
                case MetalType.Zinc: return "Zn";
                case MetalType.Brass: return "Bs";
                case MetalType.Gold: return "Au";
                case MetalType.Electrum: return "El";
                case MetalType.Cadmium: return "Cd";
                case MetalType.Bendalloy: return "Bd";
                case MetalType.Aluminum: return "Al";
                case MetalType.Duralumin: return "Du";
                case MetalType.Chromium: return "Cr";
                case MetalType.Nicrosil: return "Ni";
            }
            return "?";
        }

        Text MakeText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 pos, Vector2 size, int fontSize, TextAnchor anchor)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            RectTransform rt = obj.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.sizeDelta = size; rt.anchoredPosition = pos;
            Text t = obj.AddComponent<Text>();
            t.font = font;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.raycastTarget = false;
            return t;
        }
    }
}