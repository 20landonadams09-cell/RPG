// MetalRingVisual.cs
//
// Custom UI-Toolkit element that draws the allomancy reserve ring as N proportional arcs — one per
// BURNING metal — plus dim separator notches at each sector boundary. Ported from Ashwalker's
// MetalRingVisual.cs (pure VisualElement + Painter2D, zero Ashwalker-type deps) and generalized from
// Ashwalker's fixed two-arc (primary/secondary) layout to an N-arc layout.
//
// The ring is divided into N equal sectors (360°/N); sector i belongs to burning metal i and fills from
// its start angle by (its reserve %) × sectorSpan, in that metal's HUD colour. A small dim radial
// separator notch is drawn at each sector boundary — the "something in between them" so the gaps
// between arcs are never empty. A dim full-circle background ring is always drawn behind it.
//
//   N = 1 (BasicRPG single-active-metal today) → one 360° sector = a full ring coloured by the active
//        metal, with a single separator notch at 12 o'clock. Fixes the old "only coloured on one side"
//        half-empty look. When nothing is burning (N = 0) only the dim background ring draws.
//   N = 2 (Ashwalker-style dual-metal) → two 180° sectors, each its own colour — the OG two-colour look.
//   N > 2 → generalizes for future simultaneous multi-metal burning (one arc per burning metal).
//
// The whole ring spins slowly (one full turn per metal per ~16 s) — Ashwalker's spin, preserved.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace BasicRPG.Allomancy
{
    public class MetalRingVisual : VisualElement
    {
        // ── Segment data (one per burning metal) ─────────────────────────────────

        private readonly List<(float pct, Color color)> _segments = new List<(float, Color)>();

        // ── Drawing constants ──────────────────────────────────────────────────────

        private const float LINE_W            = 7f;    // arc stroke width
        private const float SEPARATOR_DEG     = 8f;    // gap + notch between sectors
        private const float SPIN_DEG_PER_TICK = 0.375f; // degrees per 16 ms tick (16 s/rotation — one per metal)

        // ── Spin state ─────────────────────────────────────────────────────────────

        private float _spin = 0f;
        private IVisualElementScheduledItem _ticker;

        // ── Constructor ────────────────────────────────────────────────────────────

        public MetalRingVisual()
        {
            generateVisualContent += Draw;

            // Default size — can be overridden from UXML/USS
            style.width  = 90;
            style.height = 90;
            style.position = Position.Absolute;
            style.left = 0;
            style.top  = 0;

            // Do not block pointer events from reaching ugui underneath.
            pickingMode = PickingMode.Ignore;

            // Start spinning once attached to a panel, pause when detached
            RegisterCallback<AttachToPanelEvent>(_ =>
                _ticker = schedule.Execute(Tick).Every(16));
            RegisterCallback<DetachFromPanelEvent>(_ =>
                _ticker?.Pause());
        }

        private void Tick()
        {
            _spin = (_spin + SPIN_DEG_PER_TICK) % 360f;
            MarkDirtyRepaint();
        }

        // ── Public API ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Update the ring from the burning-metals list. Each entry is (reservePct 0..1, hudColour);
        /// one entry per burning metal → one arc (the ring splits into N equal sectors, one per
        /// entry, each filled by its pct and coloured by its colour). Pass an empty list when nothing
        /// is burning (only the dim background ring draws). Triggers a repaint.</summary>
        public void SetValues(IList<(float pct, Color color)> segments)
        {
            _segments.Clear();
            if (segments != null)
                for (int i = 0; i < segments.Count; i++)
                    _segments.Add((Mathf.Clamp01(segments[i].pct), segments[i].color));
            MarkDirtyRepaint();
        }

        // ── Drawing ────────────────────────────────────────────────────────────────

        private void Draw(MeshGenerationContext ctx)
        {
            var p      = ctx.painter2D;
            var rect   = contentRect;
            var center = rect.center;
            float r    = Mathf.Min(rect.width, rect.height) * 0.42f;

            // ── Background ring (full dim circle) — always drawn ──────────────────
            p.lineWidth   = LINE_W;
            p.strokeColor = new Color(0.25f, 0.25f, 0.25f, 0.55f);
            p.BeginPath();
            p.Arc(center, r, 0f, 360f, ArcDirection.Clockwise);
            p.Stroke();

            int n = _segments.Count;
            if (n <= 0) return;

            // 0° = right; 12 o'clock = -90°. Each sector = 360/N. _spin rotates the whole ring.
            float sectorSpan = 360f / n;
            // The separator gap is capped so a many-segment ring still has room to fill.
            float gap     = Mathf.Min(SEPARATOR_DEG, sectorSpan * 0.25f);
            float halfGap = gap * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float pct = _segments[i].pct;
                Color col = _segments[i].color;
                float start = -90f + i * sectorSpan + _spin;     // this sector's start boundary
                float fillSpan = (sectorSpan - gap) * pct;       // fill grows from the start by reserve %

                if (fillSpan > 0.5f)
                {
                    p.lineWidth   = LINE_W;
                    p.strokeColor = col;
                    p.BeginPath();
                    p.Arc(center, r,
                          start + halfGap,
                          start + halfGap + fillSpan,
                          ArcDirection.Clockwise);
                    p.Stroke();
                }

                // Separator notch at this sector's start boundary — a short dim radial stroke
                // crossing the ring, so the gap between arcs is "something", not empty.
                DrawSeparator(p, center, r, start);
            }
        }

        private void DrawSeparator(Painter2D p, Vector2 center, float r, float angleDeg)
        {
            float rad = angleDeg * Mathf.Deg2Rad;
            float cosA = Mathf.Cos(rad);
            float sinA = Mathf.Sin(angleDeg * Mathf.Deg2Rad);
            float inner = r - LINE_W * 0.8f;
            float outer = r + LINE_W * 0.8f;
            var a = new Vector2(center.x + cosA * inner, center.y + sinA * inner);
            var b = new Vector2(center.x + cosA * outer, center.y + sinA * outer);
            p.lineWidth   = 2f;
            p.strokeColor = new Color(0.18f, 0.18f, 0.18f, 0.85f);
            p.BeginPath();
            p.MoveTo(a);
            p.LineTo(b);
            p.Stroke();
        }
    }
}