using UnityEngine;
using UnityEngine.UIElements;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Drives the always-on UI-Toolkit metal ring (Ashwalker's <see cref="MetalRingVisual"/>) for
    /// BasicRPG. Creates the bottom-left <c>MetalRingContainer</c> + a <see cref="MetalRingVisual"/>
    /// child in <see cref="OnEnable"/> (the UIDocument root is guaranteed available by then — not
    /// reliably in Awake), then pushes one arc per BURNING metal to the ring at ~10 fps.
    /// <see cref="FlareIntensityHUD"/> finds the same container (in Start, which runs after every
    /// OnEnable) and injects its flare segments around it — so both pieces share one UIDocument.
    ///
    /// The ring splits into N equal sectors, one per burning metal
    /// (<see cref="Allomancer.BurningMetals"/>): each sector fills by that metal's reserve % and is
    /// coloured by its <see cref="MetalDefinition.hudColor"/>, with a dim separator notch at each
    /// sector boundary (the "something in between them"). BasicRPG is single-active-metal, so while
    /// burning this is N = 1 → one full coloured arc (fixing the old "only coloured on one side"
    /// half-empty look); when not burning N = 0 → only the dim background ring draws. The ring spins
    /// slowly via its own internal ticker; this driver only refreshes the reserve data at ~10 fps.
    ///
    /// Coexistence with the ugui HUD: every VisualElement here (and the UIDocument root) sets
    /// <c>pickingMode = Ignore</c> so the UI-Toolkit panel never swallows pointer events that should
    /// reach ugui (inventory, dialogue, the metal wheel). The container is positioned absolutely in
    /// the bottom-left corner (matching where the old AllomancyHUD sat) with <c>overflow = Visible</c>
    /// so the orbiting flare segments can extend outside the 90×90 box.
    /// </summary>
    public class MetalRingDriver : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private Allomancer allomancer;

        private MetalRingVisual _ring;
        private VisualElement _container;
        private float _acc;

        // Reused each refresh to avoid per-frame allocation (the ring copies it internally).
        private readonly System.Collections.Generic.List<(float pct, Color color)> _segments =
            new System.Collections.Generic.List<(float, Color)>();

        void OnEnable()
        {
            if (uiDocument == null)
            {
                Debug.LogWarning("[MetalRingDriver] No UIDocument wired.");
                return;
            }

            var root = uiDocument.rootVisualElement;
            if (root == null) return;

            // The full-screen root must not swallow clicks meant for ugui underneath.
            root.pickingMode = PickingMode.Ignore;

            _container = new VisualElement { name = "MetalRingContainer" };
            _container.style.position = Position.Absolute;
            _container.style.left   = 16f;      // px from left edge (matches old AllomancyHUD x=16)
            _container.style.bottom = 16f;      // px from bottom edge (matches old y=16)
            _container.style.width  = 90f;       // Ashwalker ring size
            _container.style.height = 90f;
            _container.style.overflow = Overflow.Visible; // flare segments orbit outside the box
            _container.pickingMode = PickingMode.Ignore;
            root.Add(_container);

            _ring = new MetalRingVisual();
            _container.Add(_ring);
        }

        void Update()
        {
            if (_ring == null || allomancer == null) return;
            _acc += Time.unscaledDeltaTime;
            if (_acc < 0.1f) return;   // ~10 fps — the ring's own spin is handled by its internal ticker
            _acc = 0f;

            // Build one segment per burning metal (N=1 today → one full coloured arc). The pct is
            // each metal's reserve / Max, so an empty metal draws no arc (pct 0) and a full reserve
            // fills its whole sector. Not burning → empty list → only the dim background ring draws.
            _segments.Clear();
            foreach (MetalType m in allomancer.BurningMetals)
            {
                float reserve = allomancer.GetReserve(m);
                float pct = Mathf.Clamp01(reserve / MetallurgyConstants.DefaultMaxReserve);
                MetalDefinition def = MetalDatabase.Get(m);
                Color c = def != null ? def.hudColor : Color.white;
                _segments.Add((pct, c));
            }
            _ring.SetValues(_segments);
        }
    }
}