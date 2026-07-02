using System;
using System.Collections.Generic;

namespace BasicRPG.AI
{
    /// <summary>Status a behavior-tree node returns from <see cref="Node.Tick"/>. Mirrors the
    /// Success/Failure/Running convention used by NPBehave (`Operator`) and fluid-behavior-tree
    /// (`TaskStatus`). Written from scratch for BasicRPG — no foreign code pulled in.</summary>
    public enum NodeStatus { Success, Failure, Running }

    /// <summary>
    /// Base class for every behavior-tree node. A node does ONE tick of work and returns a
    /// status; the tree is re-ticked from the root each frame (stateless composites), which keeps
    /// the model simple and debuggable — there is no coroutine lifecycle, no async cancel, and no
    /// hidden running-child index to desync. This is deliberately the synchronous, "flat" BT shape
    /// rather than NPBehave's coroutine-driven one: it is easier to reason about and verify on
    /// URP/Linux where editor iteration is already constrained.
    /// </summary>
    public abstract class Node
    {
        /// <summary>One frame of work. Read/write the <paramref name="bb"/>; return a status.</summary>
        public abstract NodeStatus Tick(Blackboard bb);
    }

    // ── Composites ──────────────────────────────────────────────────────────────

    /// <summary>Runs children left→right. Stops at the first <see cref="NodeStatus.Failure"/>
    /// (returns Failure) or <see cref="NodeStatus.Running"/> (returns Running, blocking the rest).
    /// Succeeds only if every child succeeds. AND-semantics.</summary>
    public class Sequence : Node
    {
        private readonly List<Node> children;
        public Sequence(params Node[] children) { this.children = new List<Node>(children); }
        public Sequence(IEnumerable<Node> children) { this.children = new List<Node>(children); }
        public override NodeStatus Tick(Blackboard bb)
        {
            for (int i = 0; i < children.Count; i++)
            {
                NodeStatus s = children[i].Tick(bb);
                if (s != NodeStatus.Success) return s;
            }
            return NodeStatus.Success;
        }
    }

    /// <summary>Runs children left→right. Returns the first <see cref="NodeStatus.Success"/> or
    /// <see cref="NodeStatus.Running"/>; only fails if every child fails. OR-semantics — a
    /// prioritized fallback list (e.g. flee, else fight, else patrol).</summary>
    public class Selector : Node
    {
        private readonly List<Node> children;
        public Selector(params Node[] children) { this.children = new List<Node>(children); }
        public Selector(IEnumerable<Node> children) { this.children = new List<Node>(children); }
        public override NodeStatus Tick(Blackboard bb)
        {
            for (int i = 0; i < children.Count; i++)
            {
                NodeStatus s = children[i].Tick(bb);
                if (s != NodeStatus.Failure) return s;
            }
            return NodeStatus.Failure;
        }
    }

    /// <summary>Inverts Success↔Failure and passes Running through. "Not" a condition.</summary>
    public class Inverter : Node
    {
        private readonly Node child;
        public Inverter(Node child) { this.child = child; }
        public override NodeStatus Tick(Blackboard bb)
        {
            NodeStatus s = child.Tick(bb);
            if (s == NodeStatus.Success) return NodeStatus.Failure;
            if (s == NodeStatus.Failure) return NodeStatus.Success;
            return NodeStatus.Running;
        }
    }

    /// <summary>Repeats `child` up to `maxTimes` per tick (default 1 = run once). Returns the
    /// child's status, except a child Success that has not exhausted `maxTimes` is re-ticked. A
    /// child Failure propagates as Failure (use <see cref="RepeatForever"/> to swallow it).</summary>
    public class Repeat : Node
    {
        private readonly Node child;
        private readonly int maxTimes;
        public Repeat(Node child, int maxTimes = 1) { this.child = child; this.maxTimes = Math.Max(1, maxTimes); }
        public override NodeStatus Tick(Blackboard bb)
        {
            NodeStatus s = NodeStatus.Success;
            for (int i = 0; i < maxTimes; i++)
            {
                s = child.Tick(bb);
                if (s != NodeStatus.Success) return s;
            }
            return s;
        }
    }

    /// <summary>Repeats `child` every tick and always reports Success — swallows child failure.
    /// Use as a top-level wrapper to keep a subtree perpetually active.</summary>
    public class RepeatForever : Node
    {
        private readonly Node child;
        public RepeatForever(Node child) { this.child = child; }
        public override NodeStatus Tick(Blackboard bb) { child.Tick(bb); return NodeStatus.Success; }
    }

    // ── Leaves ───────────────────────────────────────────────────────────────────

    /// <summary>Leaf that evaluates a predicate and returns Success/Failure (never Running).
    /// The branch/decision primitive.</summary>
    public class Condition : Node
    {
        private readonly Func<Blackboard, bool> test;
        public Condition(Func<Blackboard, bool> test) { this.test = test; }
        public override NodeStatus Tick(Blackboard bb) => test(bb) ? NodeStatus.Success : NodeStatus.Failure;
    }

    /// <summary>Leaf that does work and returns the delegate's status. The "do something"
    /// primitive — drive movement, play an attack, set a flag. Return Running to keep the branch
    /// active across frames, Success when the action completed, Failure if it can't proceed.</summary>
    public class Action : Node
    {
        private readonly Func<Blackboard, NodeStatus> work;
        public Action(Func<Blackboard, NodeStatus> work) { this.work = work; }
        public override NodeStatus Tick(Blackboard bb) => work(bb);
    }
}