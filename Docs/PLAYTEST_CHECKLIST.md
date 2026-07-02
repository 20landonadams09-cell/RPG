# BasicRPG Playtest Checklist

For the playtest partner. This is what to hammer on while we keep coding. **Report anything that
doesn't behave as the "Expected" line says** — that's a bug, even if it seems minor. The freshest,
least-verified areas are flagged 🆕; those are the highest-value finds.

## 0. Setup (do this once, in order)

1. `git pull` on `main` (latest commit should be the AI behavior-tree add; Unity version stays
   **6000.5.1f1** — do NOT upgrade).
2. Open the project in Unity **6000.5.1f1** (the installed editor). Let it reimport/compile. There
   should be **no compile errors** in the Console — if there are, stop and report the first one
   verbatim.
3. Build the two scenes we test from the RPG menu (scenes are generated, not hand-placed):
   - `RPG → Build All-Metals Sandbox` → creates `Assets/Scenes/AllMetalsSandbox.unity`. 🆕
     **If you skip this, opening AllMetalsSandbox fails with "scene not in build profile" — that's
     expected until you run this.**
   - `RPG → Build Title Sequence Scene` (rebuilds the title with the latest fixes).
4. Controls cheat-sheet (keyboard; gamepad mappings exist too — see `Keybinds.cs`):

   | Key | Action |
   |---|---|
   | WASD / Space | move / jump |
   | Shift | sprint |
   | C | dodge |
   | Tab | **metal-selection wheel** (freeze-time, multi-select burn set) |
   | B | **global burn pause/resume** (stops all effects + drain, keeps selection) |
   | 1–8 | select focused metal (drink target / HUD focus — NOT burn) |
   | X | drink a metal vial (refills focused metal's reserve from inventory) |
   | R (hold) | flare to max |
   | mouse wheel | flare intensity 1→10 |
   | LMB | while Iron/Steel burns: push/pull the aimed metal · else: attack |
   | F / Q | Steelpush / Ironpull (alternate push/pull keys) |
   | RMB | while Iron/Steel burns: flare · else: block |
   | MMB (hold) | freeze-time aim at a specific anchor |
   | F5 / F9 | save / load |

## 1. Title sequence 🆕 (recently remade)

Open `TitleSequence.unity`, hit Play.

- [ ] At ~63 s the rock drops and **"MISTBORN" draws itself as blue steel lines**, letter by letter
      left→right over ~3 s. **Expected:** the word is **centered** on screen and **fits comfortably**
      (not too big, not clipped). 🆕 This was just retuned — please confirm it actually looks centered
      and sized right.
- [ ] Leading edge is bright white-blue, settles to translucent blue, then a single flare pulse.
- [ ] After the title, subtitle **"THE FINAL EMPIRE"** fades in below (legacy text — should be a
      clean off-white, **NOT pink/magenta**).
- [ ] `Esc` or `Space` skips. After the drop (or skip) it loads **AllMetalsSandbox**.
- [ ] No pink/magenta anywhere, no missing-script warnings in Console.

## 2. All-Metals Sandbox — per metal 🆕

Open `AllMetalsSandbox.unity`, Play. The HUD redesign is new here: there is **no all-16-metals
display** anymore. Instead you get a **flare ring** (arcs = metals currently burning, with reserve
fills) and the **Tab wheel** to pick what to burn.

### 2a. Multi-burn + HUD (the core new behavior) 🆕
- [ ] Press **Tab** → game freezes, radial wheel opens. Hover + LMB **toggles** metals on/off
      (multi-select — turn on more than one). Press **Tab** again (or it closes) to apply;
      **Esc** discards the pending changes.
- [ ] Turn on **Iron + Pewter** (two metals) and close. **Expected:** the flare ring shows **2 arcs**
      (one per burning metal), each with its own reserve fill. Both effects active at once.
- [ ] Press **B**. **Expected:** all effects stop and drain stops (you stop pulling/pushing, pewter
      strength gone), but the 2 arcs stay "selected" in the wheel. Press **B** again → everything
      resumes. The selection survived the pause.
- [ ] Turn on **Iron + Pewter + Tin**. **Expected:** 3 arcs; Tin flips the camera to **first-person**;
      Iron/Steel pull/push still works in 1st person.
- [ ] Burn one metal until its reserve hits 0. **Expected:** only that metal auto-stops; the others
      keep burning (one running dry doesn't kill the rest).
- [ ] Scroll wheel while burning → flare intensity 1→10 (the 10 flare ticks fill up); hold **R** →
      jumps to max flare. Flare makes effects stronger **and drains reserve faster**.

### 2b. Iron / Steel
- [ ] Aim at a **wall anchor** (heavy/fixed), hold **F** (Steelpush). **Expected:** you get launched
      **away** from it (Newton's 3rd law — the heavy anchor shoves *you*).
- [ ] Aim at a **wall anchor**, hold **Q** (Ironpull). **Expected:** you yank **toward** it.
- [ ] Aim at a **loose object** (coin/crate), hold **Q**. **Expected:** the object flies to you (the
      light object moves, not you).
- [ ] Aim at a **loose object resting on the ground below/at chest height**, hold **Q**. **Expected:**
      the object slides to you; **you do NOT get lifted/floated off the ground.** (This is a specific
      canon fix — flag it hard if the player floats.)
- [ ] Hold **MMB** to freeze-time and aim at a specific anchor among several — only the aimed one is
      targeted.

### 2c. Tin 🆕
- [ ] Turn Tin on in the wheel. **Expected:** camera switches to **first-person**, the dark alcove
      becomes visible (night-vision), and you can sense nearby enemies (vibration/scent cue).
- [ ] Walk into the **bright light** hazard while Tin burns. **Expected:** overload feedback (it should
      hurt / blind — not silently nothing).
- [ ] Turn Tin off. **Expected:** camera returns to **third-person**.

### 2d. Pewter
- [ ] Pewter on → cross the **platform gap** that's too far for a normal jump. **Expected:** you clear
      it with the enhanced jump.
- [ ] Melee the **damage dummy** with vs without Pewter. **Expected:** noticeably more damage while
      burning. Also test taking a hit — Pewter should **reduce** incoming damage (and defer wounds
      that hit hard; releasing Pewter later applies them — verify the deferred-wound flow doesn't
      instantly kill you on release in a weird way).

### 2e. Copper / Bronze (the thugs)
- [ ] There are 2 Pewter-thug enemies. Burn **Bronze**. **Expected:** you can sense their allomantic
      pulse (they're burning) — even through a wall, ideally.
- [ ] Burn **Copper** (Coppercloud) and stand near a thug. **Expected:** the thug's burning is
      **suppressed** — it loses its Pewter buff (slower, weaker) and tints grey-blue; a
      "Coppercloud suppresses a Thug" notification shows. Walk away → it resumes burning.
- [ ] While your Coppercloud is up, a Bronze-burning enemy shouldn't be able to pulse-sense you
      (player is hidden). Worth a quick check.

### 2f. Zinc / Brass (emotion aura)
- [ ] There's a ring of ~4 enemies just outside detect range. Burn **Zinc** near them (riot).
      **Expected:** they detect you from farther, move faster, attack more often (hyper-aggressive).
- [ ] Burn **Brass** (soothe). **Expected:** they barely notice you, move sluggishly, attack slowly.
- [ ] Walk out of aura range. **Expected:** they reset to normal.

### 2g. Drinking metals 🆕
- [ ] All 5 of Tin/Copper/Bronze/Zinc/Brass should be **drinkable** (X refills the focused metal).
      🆕 These were just wired up — confirm none gives "No metal to drink". (Iron/Steel/Pewter already
      worked.)
- [ ] **RightAlt** debug-refills all reserves to full.

### 2h. Save / load
- [ ] Burn a specific set of metals, drain a couple reserves partway, press **F5**. Press **F9**.
      **Expected:** reserves, the burn set (which metals were on), and the B-pause state all restore.

## 3. Enemy AI

- [ ] A basic thug **patrols** waypoints, **detects** you in range, **chases**, then does a brief
      yellow **telegraph flash** before its melee hit (so the hit is dodgeable). On death it drops a
      loot cube you can pick up.
- [ ] No enemy slides as a T-pose blob — it should walk/idle/fall with the humanoid animator. If a
      shoved enemy lands and freezes in a fall pose, report it.

### 3a. Inquisitor (new, optional) 🆕
This is **not** placed in any scene by a builder yet. To test it, the dev needs to drop the
`InquisitorEnemy` component on a GameObject that has a `CharacterController` + a capsule/child model
+ a `Health`. If one is in a scene:
- [ ] It **flees** when wounded (below ~30% HP) and the player is close; **closes** when it detects
      you; **strikes** in melee (red telegraph); **returns home** when it loses you.
- [ ] If you don't have one placed, just note "Inquisitor not in any scene" — that's expected, not a
      bug. (It's also not yet steelpush-shoveable / tin-sensed — known TODO, not a bug to report.)

## 4. General / stability catches (report any of these immediately)

- [ ] Any **pink/magenta** material or text (the recurring URP-port bug — screenshot it).
- [ ] Any **ArgumentOutOfRangeException** or **NullReferenceException** spam in the Console — copy
      the first lines + which scene/what you were doing.
- [ ] Any **"scene not added to build profile"** error → means that scene wasn't built via the RPG
      menu (see Setup step 3).
- [ ] Frame-rate hitches or the player falling through the floor / getting stuck on geometry.
- [ ] Animations looking wrong (T-pose, perpetual fall, slide-walk).

## 5. How to report

For each issue, note: **scene** + **what you did** (keys pressed) + **what you expected** +
**what happened** + a **screenshot or the Console error text**. Drop them in our shared channel /
issue list and we'll triage. Prioritize 🆕 items — those are the least-tested.