# Reference Archives Index

Nine third-party Unity open-source archives were dropped into the project root as **reference
material**. They are NOT part of BasicRPG: they live outside `Assets/` (so Unity never compiles or
packages them), they are git-ignored (see `.gitignore`), and no BasicRPG script depends on them.
This file is the index so collaborators know what each one is, what it's good for, and where it
conflicts with BasicRPG's own canon-built systems — so the right call is made when a real need
arises.

## Why they're reference, not dependencies

BasicRPG's core systems are built from scratch and **canon-locked**:

- **Allomancy** (`Assets/Scripts/Allomancy/`) is the game's ability system. It matches Mistborn
  canon (Iron/Steel as one component, Tin → first-person, Coppercloud suppression, etc.). Two of the
  archives below are *foreign gameplay-ability-system frameworks* that would **replace** it — they
  are explicitly NOT integrated. See memory `basicrpg-ironsteel-no-layer-split` and
  `basicrpg-allomancy-canon-lockin`.
- **Inventory / items** (`Assets/Scripts/Items/`) is hand-built and the scene builders depend on it.
  Two archives are foreign inventory systems that would replace it — NOT integrated.

So the constructive use of these archives is: **read them for patterns, then port the idea in
BasicRPG's own hand-built style** when a genuine gap exists — do not drop the foreign codebase in.
The first such port is the enemy-AI behavior tree in `Assets/Scripts/AI/` (see below).

## Per-archive summary

| Archive | License | What it is | BasicRPG disposition |
|---|---|---|---|
| `unity-gameplay-ability-system-main.zip` | MIT | Gameplay ability-system framework (sjai013) | **Reference only — do NOT integrate.** Conflicts with canon Allomancer. Read for ability-tagging / effect-composition patterns. |
| `Flexi-main.zip` | MIT | "Flexi" ability-system framework (Physalia, ~90% WIP) | **Reference only — do NOT integrate.** Same conflict as above. |
| `Inventory-Pro-master.zip` | MIT (Devdog) | Asset-store-grade inventory + UI + save + input stack | **Reference only.** Conflicts with hand-built `Inventory`/`ItemSO`/`InventoryUI`. Consult for slot/drag-and-drop UI patterns only. |
| `Inventory-master.zip` | MIT | Lighter inventory | **Reference only.** Same conflict. |
| `Quest-System-Pro-master.zip` | MIT (Devdog) | Quest system (pairs with InventoryPro's stack) | **Reference only** for now. BasicRPG has no quest system yet — if one is added, port a lightweight BasicRPG-style quest graph from this, do not pull in the Devdog stack. |
| `Unity3d-Finite-State-Machine-master.zip` | MIT | Generic FSM lib | **Reference.** BasicRPG's `Enemy` already uses an inline enum+switch FSM; this informed the `Assets/Scripts/AI/StateMachine`-style thinking. |
| `fluid-behavior-tree-develop.zip` | MIT | Behavior-tree framework (com.fluid.behavior-tree) | **Reference — pattern source.** Informed `Assets/Scripts/AI/` (Sequence/Selector/NodeStatus/Blackboard). |
| `NPBehave-master.zip` | MIT | Behavior-tree framework (NPBehave) | **Reference — pattern source.** Informed `Assets/Scripts/AI/` (Blackboard, composites, synchronous-tick design). |
| `awesome-opensource-unity-master.zip` | n/a (link list) | Curated README of Unity OSS — **0 code** | Nothing to integrate. Browse the linked repos for ideas. |

## First selective port: `Assets/Scripts/AI/` (behavior tree)

The one genuinely-additive, low-conflict capability drawn from these references is a **behavior
tree for complex enemy AI** — something the inline switch-FSM in `Enemy.cs` can't cleanly express
(e.g. a multi-phase Inquisitor that flees when wounded, closes, strikes, and holds ground).

Ported from scratch (no foreign files copied), inspired by NPBehave + fluid-behavior-tree:

- `Blackboard.cs` — typed AI short-term memory.
- `BehaviorTreeNodes.cs` — `NodeStatus`, `Node`, `Sequence`, `Selector`, `Inverter`, `Repeat`,
  `RepeatForever`, `Condition`, `Action`.
- `BehaviorTree.cs` — MonoBehaviour driver (ticks the root each frame).
- `InquisitorEnemy.cs` — example/reference consumer: a self-contained complex enemy whose decisions
  are a BT, with locomotion lifted from the verified `Enemy.cs` pattern.

**Not yet wired into a scene builder** — drop `InquisitorEnemy` on a GameObject with a
`CharacterController` to try it. **Not yet integrated with the allomancy systems** (not
Steelpush-shoveable / Tin-sensed / Zinc-Brass-affected) by design, to avoid touching canon
allomancy; see the doc-comment in `InquisitorEnemy.cs` for the integration TODO.