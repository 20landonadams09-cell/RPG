using UnityEngine;

namespace BasicRPG.AI
{
    /// <summary>
    /// Drives a behavior tree each frame. Assign a root <see cref="Node"/> (set in Awake/Start,
    /// or via <see cref="SetRoot"/>) and this ticks it against its <see cref="Blackboard"/> every
    /// Update. The blackboard is exposed so callers can prime it with world state (player ref, own
    /// health, home position) before/after ticks.
    ///
    /// This is the BasicRPG-flavoured equivalent of NPBehave's `Root`/`UnityContext` or
    /// fluid-behavior-tree's `BehaviorTree` host — a thin MonoBehaviour adapter around the
    /// framework in <see cref="BehaviorTreeNodes"/>. It owns NO behavior itself; all behavior lives
    /// in the node tree, so this stays pipeline/scene-agnostic (no TMP, no HDRP, no allomancy).
    /// </summary>
    public class BehaviorTree : MonoBehaviour
    {
        /// <summary>Shared AI memory. Prime this with sensed world state before ticks.</summary>
        public Blackboard Blackboard { get; } = new Blackboard();

        /// <summary>Root of the tree. Null is a no-op (the runner idles).</summary>
        public Node Root { get; private set; }

        /// <summary>If false, ticking is paused without clearing the tree (mirrors the B-key
        /// allomancy pause pattern — retained, not destroyed).</summary>
        public bool IsRunning { get; set; } = true;

        /// <summary>Install the tree root. Call once after building the node graph.</summary>
        public void SetRoot(Node root) => Root = root;

        void Update()
        {
            if (!IsRunning || Root == null) return;
            Root.Tick(Blackboard);
        }
    }
}