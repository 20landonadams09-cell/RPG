using UnityEngine;
using UnityEngine.UI;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Compact ugui allomancy HUD: the active metal's name + reserve bar + percent. The radial
    /// MetalWheel (Tab) handles selection across all 16 metals, so this HUD no longer carries the
    /// old flat 8-metal strip — it just shows the currently active metal's reserve at a glance.
    /// Throttled to ~10 fps. Builds nothing in Awake; the scene builder wires the Text + fill refs.
    /// </summary>
    public class AllomancyHUD : MonoBehaviour
    {
        [SerializeField] private RectTransform reserveFill;  // anchorMax.x driven by reserve %
        [SerializeField] private Text nameText;
        [SerializeField] private Text pctText;

        private float acc;

        void Update()
        {
            // Throttle to ~10 fps — the reserve bar doesn't need per-frame updates.
            acc += Time.unscaledDeltaTime;
            if (acc < 0.1f) return;
            acc = 0f;
            if (pending != null) Apply(pending.Value);
        }

        private struct Snapshot { public float[] reserves; public MetalType active; public bool burning; public float flare; }
        private Snapshot? pending;

        /// <summary>Called by Allomancer each frame; stashes a snapshot for the throttled refresh.</summary>
        public void UpdateDisplay(float[] reserves, MetalType active, bool burning, float flare = 1f)
        {
            pending = new Snapshot { reserves = reserves, active = active, burning = burning, flare = flare };
        }

        void Apply(Snapshot s)
        {
            float max = MetallurgyConstants.DefaultMaxReserve;
            MetalDefinition def = MetalDatabase.Get(s.active);
            float norm = Mathf.Clamp01(s.reserves[(int)s.active] / max);
            bool flaring = s.flare > 1.01f;

            if (reserveFill != null)
            {
                Vector2 a = reserveFill.anchorMax; a.x = norm; reserveFill.anchorMax = a;
                if (def != null)
                {
                    // Flaring brightens the bar toward white so the "burn harder" state reads.
                    Color c = def.hudColor;
                    if (flaring) c = Color.Lerp(c, Color.white, Mathf.Clamp01((s.flare - 1f) * 0.8f));
                    reserveFill.GetComponent<Image>().color = c;
                }
            }
            if (nameText != null)
            {
                string label = def != null ? def.displayName : s.active.ToString();
                if (s.burning) label += flaring ? "  (flaring)" : "  (burning)";
                nameText.text = label;
            }
            if (pctText != null)
                pctText.text = Mathf.RoundToInt(norm * 100f) + "%";
        }
    }
}