/* MetallurgicTitleRenderer.cs
 *
 * Draws "MISTBORN" at the rock drop as REAL blue steel lines — world-space LineRenderer
 * strokes that trace each letter into existence, left to right, as if an allomancer's steel
 * lines are drawing the title. The leading edge is bright white-blue; the trail settles to
 * translucent blue. After all letters are drawn, a single flare pulse ripples across the
 * title, then it settles into a gentle pulsing glow.
 *
 * DESIGN INTENT (canon lock-in, see basicrpg-allomancy-canon-lockin memory): the blue lines
 * are an allomancer's mental cue for nearby metal — force felt from the chest. Here, as a
 * TITLE-SCREEN STYLISTIC MOTIF (not a gameplay canon claim), those lines literally draw the
 * word MISTBORN. Revealed over ~3 s, settle, pulse. This is a lore echo, not a mechanic.
 *
 * WHY LineRenderer (not TextMeshPro): the prior version traced TMP glyphs via per-vertex
 * colors, but that fought TMP's mesh/cull lifecycle — zeroing vertex alpha against a
 * CanvasRenderer with CullTransparentMesh left the mesh culled to invisible, and TMP's own
 * mesh rebuilds snapped the colors back to a static blue word. Drawing the letters as actual
 * line geometry has no font atlas, no colors32, no CullTransparentMesh — it is robust on URP
 * by construction (a Sprites/Default line material), which is why this remake exists.
 *
 * The stroke root is a world-space child of the title camera (wired by the scene builder via
 * `cameraTransform`) so the title stays framed through the camera dolly. The subtitle stays a
 * TMP text under the canvas (faded by this renderer's coroutine + the controller's titleGroup
 * CanvasGroup fade). The controller still calls StartDrawing(duration) at the 63 s drop.
 */

using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

namespace BasicRPG.TitleScreen
{
    public class MetallurgicTitleRenderer : MonoBehaviour
    {
        [Header("Camera (line strokes parent to this so they stay framed)")]
        [Tooltip("Title camera transform. The line-stroke root parents to it at `depth` in front.")]
        public Transform cameraTransform;
        [Tooltip("Distance in front of the camera the title sits (world units).")]
        public float depth = 6f;

        [Header("Title")]
        public string titleString = "MISTBORN";
        [Tooltip("Cap height of the letters in world units (at `depth`). Sized so the full word fits comfortably inside the title camera's view with margin.")]
        public float letterHeight = 1.5f;
        [Tooltip("Horizontal gap between letters, as a fraction of cap height. Tighter than before so MISTBORN reads as one word and fits the screen.")]
        public float letterSpacing = 0.18f;
        [Tooltip("Line stroke width in world units.")]
        public float lineWidth = 0.08f;
        [Tooltip("World-space Y offset of the title above screen center, so it sits above the subtitle rather than overlapping it.")]
        public float verticalOffset = 0.6f;

        [Header("Metallurgic Line Colors")]
        [Tooltip("Settled color — semi-transparent blue, like steel lines.")]
        public Color blueLineColor = new Color(0.27f, 0.53f, 1f, 0.6f);
        [Tooltip("Leading-edge color — bright white-blue as the line is being drawn.")]
        public Color traceColor = new Color(0.7f, 0.85f, 1f, 0.95f);
        [Tooltip("Flare color — bright pulse after all letters are drawn.")]
        public Color flareColor = new Color(0.6f, 0.82f, 1f, 0.95f);

        [Header("Draw Animation")]
        public float drawDuration = 3f;
        [Tooltip("How long the leading-edge glow lingers before settling to blue.")]
        public float traceSettleTime = 0.4f;

        [Header("Post-Draw Glow")]
        [Tooltip("Subtle energy pulse frequency after the title is fully drawn.")]
        public float pulseHz = 1.2f;
        [Range(0f, 0.3f)]
        public float pulseStrength = 0.1f;
        [Tooltip("Duration of the single flare that fires right after drawing completes.")]
        public float flareDuration = 0.6f;

        [Header("Subtitle")]
        [Tooltip("Legacy ugui Text (NOT TMP) — pipeline-agnostic, never pink on URP. The renderer fades it in after the title draws.")]
        public Text subtitleText;
        public string subtitleString = "";
        public float subtitleDelay = 1.5f;
        public float subtitleFadeDuration = 1f;

        // ── Runtime state ───────────────────────────────────────────────────────
        private bool isDrawing;
        private bool drawComplete;
        private Transform strokeRoot;                 // parents all line strokes; child of cameraTransform
        private Material lineMat;                      // shared Sprites/Default material
        private readonly List<Stroke> strokes = new List<Stroke>();

        private class Stroke
        {
            public LineRenderer lr;
            public Vector3[] pts;     // full polyline in strokeRoot-local space
            public float startX;      // leftmost x (for left-to-right ordering)
        }

        void Awake()
        {
            // Build the strokes once. The root parents to the camera so the title stays framed.
            if (cameraTransform == null)
            {
                // Fall back to Camera.main if the builder didn't wire it.
                Camera c = Camera.main;
                if (c != null) cameraTransform = c.transform;
            }
            BuildStrokes();
        }

        /// <summary>Called by TitleSequenceController at the rock drop.</summary>
        public void StartDrawing(float duration)
        {
            if (isDrawing) return;
            drawDuration = duration;
            StartCoroutine(DrawSequence());
        }

        IEnumerator DrawSequence()
        {
            isDrawing = true;
            if (strokes.Count == 0) { isDrawing = false; yield break; }

            // Order strokes left-to-right so the trace reads across the word.
            strokes.Sort((a, b) => a.startX.CompareTo(b.startX));
            int n = strokes.Count;

            // All strokes start invisible (no geometry), then trace in. Starts are staggered across
            // the first ~40% of the duration so letters draw left→right; each completes within the
            // duration.
            float staggerWindow = drawDuration * 0.4f;
            float perStrokeDur  = drawDuration * 0.6f;
            float startTime = Time.time;
            float[] strokeStart = new float[n];
            for (int i = 0; i < n; i++)
                strokeStart[i] = startTime + (n > 1 ? (i / (float)(n - 1)) * staggerWindow : 0f);

            // ── Trace phase: reveal each stroke by arc length ───────────────────
            float traceEnd = startTime + drawDuration;
            while (Time.time < traceEnd)
            {
                for (int i = 0; i < n; i++)
                {
                    float p = Mathf.Clamp01((Time.time - strokeStart[i]) / perStrokeDur);
                    SetStrokeProgress(strokes[i], p);
                    // Leading edge bright (end), trail dim-blue (start) — the gradient IS the trace.
                    strokes[i].lr.startColor = blueLineColor;
                    strokes[i].lr.endColor   = traceColor;
                }
                yield return null;
            }
            // Fully drawn.
            foreach (var s in strokes) SetStrokeProgress(s, 1f);

            // ── Settle: leading edge fades from trace white-blue → settled blue ──
            float settleEnd = Time.time + traceSettleTime;
            while (Time.time < settleEnd)
            {
                float t = Mathf.Clamp01((Time.time - (settleEnd - traceSettleTime)) / traceSettleTime);
                Color end = Color.Lerp(traceColor, blueLineColor, t);
                foreach (var s in strokes) { s.lr.startColor = blueLineColor; s.lr.endColor = end; }
                yield return null;
            }
            foreach (var s in strokes) { s.lr.startColor = blueLineColor; s.lr.endColor = blueLineColor; }

            // ── Flare pulse ─────────────────────────────────────────────────────
            yield return FlareAllStrokes();

            drawComplete = true;

            // ── Subtitle fade-in ─────────────────────────────────────────────────
            if (subtitleText != null && !string.IsNullOrEmpty(subtitleString))
            {
                yield return new WaitForSeconds(subtitleDelay);
                subtitleText.text = subtitleString;
                float elapsed = 0f;
                Color baseSub = subtitleText.color;
                while (elapsed < subtitleFadeDuration)
                {
                    elapsed += Time.deltaTime;
                    subtitleText.color = new Color(baseSub.r, baseSub.g, baseSub.b,
                        Mathf.Lerp(0f, 1f, elapsed / subtitleFadeDuration));
                    yield return null;
                }
            }

            isDrawing = false;
        }

        /// <summary>Single bright pulse across all strokes, then settle back to blue.</summary>
        IEnumerator FlareAllStrokes()
        {
            float elapsed = 0f;
            while (elapsed < flareDuration)
            {
                elapsed += Time.deltaTime;
                float t = elapsed / flareDuration;
                // Fast rise (30%), slow decay (70%).
                float intensity = t < 0.3f ? t / 0.3f : 1f - ((t - 0.3f) / 0.7f);
                Color c = Color.Lerp(blueLineColor, flareColor, intensity);
                foreach (var s in strokes) { s.lr.startColor = c; s.lr.endColor = c; }
                yield return null;
            }
            foreach (var s in strokes) { s.lr.startColor = blueLineColor; s.lr.endColor = blueLineColor; }
        }

        void Update()
        {
            // Gentle Metallurgic energy pulse after drawing is done.
            if (!drawComplete) return;
            float pulse = 1f + Mathf.Sin(Time.time * pulseHz * Mathf.PI * 2f) * pulseStrength;
            Color pulsed = new Color(blueLineColor.r * pulse, blueLineColor.g * pulse, blueLineColor.b * pulse, blueLineColor.a);
            foreach (var s in strokes) { s.lr.startColor = pulsed; s.lr.endColor = pulsed; }
        }

        // ── Stroke construction ────────────────────────────────────────────────────

        void BuildStrokes()
        {
            if (cameraTransform == null) return;

            strokeRoot = new GameObject("MistbornTitleLines").transform;
            strokeRoot.SetParent(cameraTransform, false);
            strokeRoot.localPosition = new Vector3(0f, verticalOffset, depth);
            strokeRoot.localRotation = Quaternion.identity;
            strokeRoot.localScale = Vector3.one;

            // URP-safe line material. Sprites/Default respects LineRenderer vertex colors
            // (start/end color) and is shipped with Unity even on URP. Fallback chain guards the
            // pink-shader bug: a null Shader.Find → pink fallback material. (See project memory:
            // missing/absent shaders render magenta on URP — never silently accept a null shader.)
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            lineMat = new Material(sh);
            lineMat.color = Color.white; // line tint comes from start/end color, not the material

            // First pass: measure total width (in WORLD units) so the row is centred at x=0.
            // NOTE: letter widths from GetLetterStrokes are NORMALIZED (0..1 of cap height), so
            // they must be scaled by `letterHeight` before summing — mixing normalized widths with
            // the world-unit gap term below was the off-center bug (the row sat shifted right).
            List<Vector2[][]> letterStrokes = new List<Vector2[][]>();
            List<float> letterWidths = new List<float>();
            float totalWidth = 0f;
            for (int ci = 0; ci < titleString.Length; ci++)
            {
                Vector2[][] ls = GetLetterStrokes(titleString[ci], out float w);
                letterStrokes.Add(ls);
                letterWidths.Add(w);
                totalWidth += w * letterHeight;
            }
            totalWidth += letterSpacing * letterHeight * Mathf.Max(0, titleString.Length - 1);

            float cursorX = -totalWidth * 0.5f;   // centre the row at x=0
            float baselineY = -letterHeight * 0.5f;
            float s = letterHeight;                // normalized 0..1 → world 0..letterHeight

            for (int ci = 0; ci < titleString.Length; ci++)
            {
                Vector2[][] ls = letterStrokes[ci];
                float w = letterWidths[ci];
                for (int si = 0; si < ls.Length; si++)
                {
                    Vector2[] poly = ls[si];
                    Vector3[] pts = new Vector3[poly.Length];
                    float minX = float.MaxValue;
                    for (int pi = 0; pi < poly.Length; pi++)
                    {
                        float x = cursorX + poly[pi].x * s;
                        float y = baselineY + poly[pi].y * s;
                        pts[pi] = new Vector3(x, y, 0f);
                        if (x < minX) minX = x;
                    }

                    GameObject strokeGO = new GameObject($"Stroke_{ci}_{si}");
                    strokeGO.transform.SetParent(strokeRoot, false);
                    strokeGO.transform.localPosition = Vector3.zero;
                    strokeGO.transform.localRotation = Quaternion.identity;
                    strokeGO.transform.localScale = Vector3.one;

                    LineRenderer lr = strokeGO.AddComponent<LineRenderer>();
                    lr.useWorldSpace = false;
                    lr.material = lineMat;
                    lr.widthCurve = AnimationCurve.Constant(0f, 1f, lineWidth);
                    lr.startWidth = lineWidth;
                    lr.endWidth = lineWidth;
                    lr.numCornerVertices = 4;   // smooth the joins on curved letters (S/O/B/R bowls)
                    lr.numCapVertices = 2;
                    lr.positionCount = 0;   // nothing drawn until revealed
                    // Park the full polyline on the renderer (SetStrokeProgress grows positionCount).
                    Stroke st = new Stroke { lr = lr, pts = pts, startX = minX };
                    strokes.Add(st);
                    SetStrokeProgress(st, 0f);
                }
                cursorX += w * s + letterSpacing * letterHeight;
            }
        }

        /// <summary>Reveal a stroke to fraction `p` (0..1) of its arc length: positions[0..k] are the
        /// real polyline points, positions[k] is the interpolated tip, positionCount = k+1. p=0 → no
        /// geometry. p=1 → the full polyline.</summary>
        static void SetStrokeProgress(Stroke st, float p)
        {
            if (st.lr == null || st.pts == null || st.pts.Length == 0) return;
            if (p <= 0f) { st.lr.positionCount = 0; return; }
            if (p >= 1f) { st.lr.positionCount = st.pts.Length; st.lr.SetPositions(st.pts); return; }

            // Cut by segment count (uniform across segments — close enough to arc length for short
            // polylines; avoids a per-frame arc-length table).
            int segs = st.pts.Length - 1;
            float f = p * segs;
            int k = Mathf.FloorToInt(f);
            float frac = f - k;
            int count = k + 1;
            Vector3 tip = Vector3.Lerp(st.pts[k], st.pts[k + 1], frac);
            st.lr.positionCount = count + 1;
            for (int i = 0; i < count; i++) st.lr.SetPosition(i, st.pts[i]);
            st.lr.SetPosition(count, tip);
        }

        // ── Letter geometry (clean block-capitals as straight-stroke polylines) ──────
        // Each letter lives in a normalized box: x ∈ [0, w] (its advance width), y ∈ [0, 1]
        // (0 = baseline, 1 = cap). Bows/bowls are rounded with several short segments; the
        // LineRenderer's corner vertices smooth the joins. Straight strokes keep the
        // allomancy "steel line" feel while reading as proper capitals.

        static Vector2[][] GetLetterStrokes(char c, out float width)
        {
            switch (c)
            {
                case 'M': width = 0.7f; return new[] {
                    new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.35f, 0.35f), new Vector2(0.7f, 1f), new Vector2(0.7f, 0f) } };
                case 'I': width = 0.2f; return new[] {
                    new[] { new Vector2(0.1f, 0f), new Vector2(0.1f, 1f) } };
                case 'S': width = 0.55f; return new[] {
                    new[] { new Vector2(0.55f, 0.88f), new Vector2(0.45f, 1f), new Vector2(0.15f, 1f), new Vector2(0f, 0.78f), new Vector2(0f, 0.6f),
                            new Vector2(0.2f, 0.5f), new Vector2(0.45f, 0.48f), new Vector2(0.5f, 0.42f), new Vector2(0.45f, 0.2f), new Vector2(0.15f, 0f), new Vector2(0f, 0.12f) } };
                case 'T': width = 0.55f; return new[] {
                    new[] { new Vector2(0f, 1f), new Vector2(0.55f, 1f) },
                    new[] { new Vector2(0.275f, 1f), new Vector2(0.275f, 0f) } };
                case 'B': width = 0.6f; return new[] {
                    new[] { new Vector2(0f, 0f), new Vector2(0f, 1f) },                                                                                         // spine
                    new[] { new Vector2(0f, 1f), new Vector2(0.42f, 1f), new Vector2(0.58f, 0.92f), new Vector2(0.6f, 0.75f), new Vector2(0.55f, 0.58f), new Vector2(0.3f, 0.5f), new Vector2(0f, 0.5f) }, // top bowl
                    new[] { new Vector2(0f, 0.5f), new Vector2(0.35f, 0.5f), new Vector2(0.58f, 0.42f), new Vector2(0.6f, 0.25f), new Vector2(0.55f, 0.08f), new Vector2(0.3f, 0f), new Vector2(0f, 0f) } }; // bottom bowl
                case 'O': width = 0.6f; return new[] {   // ellipse centred (0.3, 0.5), rx 0.3, ry 0.5
                    new[] { new Vector2(0.6f, 0.5f), new Vector2(0.56f, 0.75f), new Vector2(0.45f, 0.93f), new Vector2(0.3f, 1f), new Vector2(0.15f, 0.93f),
                            new Vector2(0.04f, 0.75f), new Vector2(0f, 0.5f), new Vector2(0.04f, 0.25f), new Vector2(0.15f, 0.07f), new Vector2(0.3f, 0f),
                            new Vector2(0.45f, 0.07f), new Vector2(0.56f, 0.25f), new Vector2(0.6f, 0.5f) } };
                case 'R': width = 0.6f; return new[] {
                    new[] { new Vector2(0f, 0f), new Vector2(0f, 1f) },                                                                                         // spine
                    new[] { new Vector2(0f, 1f), new Vector2(0.42f, 1f), new Vector2(0.58f, 0.92f), new Vector2(0.6f, 0.75f), new Vector2(0.55f, 0.58f), new Vector2(0.3f, 0.5f), new Vector2(0f, 0.5f) }, // top bowl (matches B)
                    new[] { new Vector2(0.25f, 0.5f), new Vector2(0.45f, 0f), new Vector2(0.6f, 0f) } };                                                          // leg
                case 'N': width = 0.7f; return new[] {
                    new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0.7f, 0f), new Vector2(0.7f, 1f) } };
                default: width = 0.3f; return new Vector2[0][]; // unknown → blank slot
            }
        }
    }
}