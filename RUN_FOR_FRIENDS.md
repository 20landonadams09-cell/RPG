# How to run BasicRPG (for testers / friends)

A quick walkthrough to clone and play the project.

## 1. Get the code

```
git clone https://github.com/20landonadams09-cell/RPG.git
```

Or download the ZIP from the repo page if you don't have git.

## 2. Install Unity (if you don't already have it)

- Get **Unity Hub**: https://unity.com/download
- In Hub → **Installs → Install Editor**, pick version **6000.5.1f1** (the exact version this
  project uses). If it's not in the default list, find it under **Unity Archive** versions.
- Default modules are fine.

## 3. Open the project

- Unity Hub → **Add → Add project from disk** → select the cloned `RPG/` folder.
- It imports / compiles for a minute or two (spinner bottom-right). Let it finish — the
  **Console** should show no red errors.

## 4. Build the scene

This project has **no prebuilt scene** — it's generated from code:

- Top menu: **RPG → Build Starter Scene**.
- Watch the **Console** for `[RPGBuilder] ...` lines. When it says **"Build complete"**, it's done.
  (This creates `Assets/Scenes/Starter.unity`.)

## 5. Play

Press the **▶ Play** button at the top.

---

## Controls

### Keyboard
| Key | Action |
|-----|--------|
| W A S D | Move (camera-relative) |
| Left Shift | Sprint (drains stamina) |
| Space | Jump |
| Mouse | Orbit camera |
| Tab | Open metal wheel (time freezes; click a metal to select, Esc to cancel) |
| E | Interact (talk / open / take) |
| I | Toggle inventory |
| Left Mouse | Melee attack **— OR —** click+hold to push/pull the nearest metal (while Iron/Steel burning). **Double-click then hold** = "bubble" (push/pull ALL in-range metals) |
| Right Mouse (hold) | Block (all metals, including while burning Iron/Steel) |
| Middle Mouse (hold) | Freeze time and pick a metal target (while Iron/Steel is burning) |
| Scroll Wheel | Flare intensity 1–10 (the "flare wheel") — scroll up to flare harder, down to ease off (while burning) |
| C | Dodge |
| B | Toggle burning the active metal |
| 1 – 8 | Select active metal |
| F (hold) | Steelpush — alternate to Left Mouse (Steel burning) |
| Q (hold) | Ironpull — alternate to Left Mouse (Iron burning) |
| R (hold) | Flare to max (jump to the top flare step while held) — universal, for any metal |

> **Iron/Steel contextual mouse:** while you're **burning Iron or Steel**, **LMB hold = push (Steel)
> / pull (Iron)** and **MMB hold = freeze time and pick a target**; LMB melee is paused. **RMB
> still blocks** (block works for every metal now), and **flaring is done with the scroll wheel** —
> scroll up/down to set flare intensity 1–10 (the "flare wheel"), or **hold R / Options** to jump
> straight to max flare. When you stop burning (toggle **B**), LMB goes back to attack. **F/Q** stay
> as keyboard alternates to LMB. Burning ≠ flaring — **B** is the efficient standard burn (intensity
> 1); the scroll wheel flares — the accelerated, faster-draining boost (up to intensity 10).
>
> **Targeting:** a single **click + hold LMB** pushes/pulls the **NEAREST** metal (the closest one
> to you — the one with the thickest blue line). To affect **every** in-range metal at once, do a
> quick **double-click then hold LMB** — a "bubble" that pushes/pulls them all simultaneously (each
> gets the equal force; you take the combined recoil, which partially cancels across metals in
> different directions). Hold **MMB** to freeze time and lock a specific metal when several are
> close together (the lock is the bright target line even if it isn't the nearest). The thickest
> blue line always points to the **nearest** metal (a nearness cue); the **colour** tells you which
> one is your target — and in a bubble, every line turns the target colour. (A look-to-target
> aiming system is planned for later; for now it's nearest + bubble.)
| X | Drink a metal (refill reserve) |
| F5 / F9 | Save / Load |

### Tutorial (the metal test scenes)
Each test scene opens with a step-by-step tutorial that **freezes the world** while it teaches,
then turns the arena live for you to try things. The instruction sits in a bar at the **bottom**
of the screen (out of the way of the action).

| Key / Button | Action |
|-----|--------|
| Follow the on-screen prompt | Advance each step (the prompt names the exact input) |
| Enter (keyboard) / Dpad↓ (gamepad) | Finish the final "explore freely" step |
| Backspace (keyboard) / Dpad↓ (gamepad) | Skip the whole tutorial at any time |

> Tip: the Pewter scene has a dedicated step explaining how to **deal damage** (Left Mouse / R1).

### Gamepad (DualSense / PS5)
| Pad button | Action |
|------------|--------|
| Left stick | Move |
| Right stick | Orbit camera |
| Cross ✕ | Jump |
| Circle ○ | Dodge |
| Square □ | Interact |
| Triangle △ | Toggle burning |
| L1 (hold) | Block — OR — freeze-aim a metal target (while Iron/Steel burning) |
| R1 | Attack |
| L2 (hold) | Ironpull |
| R2 (hold) | Steelpush |
| Share | Open/close metal wheel |
| Options (hold) | Flare to max (gamepad has no scroll wheel — hold Options to jump to max flare) |
| L3 (hold) | Sprint |
| R3 (click) | Toggle inventory |
| Dpad Up | Drink a metal |
| Dpad Left | Save |
| Dpad Right | Load |

> **Heads-up:** the gamepad button numbers are tuned for **Linux**. On Windows/Mac some
> buttons may be swapped (e.g. L1/R1, or a trigger reading as a button vs. an axis). If
> something's off, report which button does what wrong and the maintainer can tweak the
> `Pad*` constants in `Assets/Scripts/Allomancy/Keybinds.cs` (and the `RightStickX`/
> `RightStickY` axes in **Project Settings → Input Manager** for the right stick).

---

## Allomantic metals — what each does

Open the wheel (**Tab / Share**) and hover a metal: its name + a one-line effect appears in the
center. The 8 **basic** metals are burnable now (select, then **B / △** to burn); the 8 **higher**
metals are shown greyed/locked — they're part of the framework but their effects aren't in yet.

### Basic metals (burnable)
| Metal | Allomantic effect (in-game) | Keys |
|-------|------------------------------|------|
| **Iron** | **Ironpull** — yank yourself toward a nearby metal anchor | hold **LMB / L2** (Iron burning; Q alt) |
| **Steel** | **Steelpush** — launch yourself off a nearby metal anchor | hold **LMB / R2** (Steel burning; F alt) |
| **Pewter** | **Physical enhancement** — hits harder, runs/jumps farther, tanks hits, mends. Burn too long then stop → **drag crash** | burn **B / △** |
| **Tin** | **Enhanced senses** — night vision, hearing, scent pings, tremorsense. Bright light/loud noise → **sensory overload** | burn **B / △** |
| **Copper** | **Coppercloud** — hide your allomancy and suppress nearby enemy allomancers | burn **B / △** |
| **Bronze** | **Seeking** — hear allomantic pulses from enemy allomancers, through walls | burn **B / △** |
| **Zinc** | **Riot** — inflame nearby enemies' emotions (hyper-aggressive, they swarm) | burn **B / △** |
| **Brass** | **Soothe** — dampen nearby enemies' emotions (calm, slow, barely notice you) | burn **B / △** |

> **Flare (burn harder):** scroll the **mouse wheel** while burning to set flare intensity 1–10
> (the "flare wheel" — a radial HUD lights up orange→red around the metal ring) — stronger
> effect, faster drain. **Hold R / Options** to jump straight to max flare. **RMB** always blocks
> (all metals). **X / Dpad↑** drinks a metal to refill its reserve. **1–8** selects the active
> basic metal fast. **Burning ≠ flaring**: B is the efficient standard burn (intensity 1); flare
> (scroll wheel) is the accelerated, faster-draining boost (up to intensity 10).

### Higher metals (locked — framework only for now)
| Metal | Allomantic effect (lore, not yet implemented) |
|-------|-----------------------------------------------|
| **Gold** | See a vision of your alternate past self |
| **Electrum** | See shadows of your own near future |
| **Cadmium** | Slow time in a bubble around you |
| **Bendalloy** | Speed up time in a bubble around you |
| **Aluminum** | Instantly wipe your own metal reserves |
| **Duralumin** | Flash-consume every burning metal in one huge burst |
| **Chromium** | By touch, wipe another allomancer's reserves |
| **Nicrosil** | By touch, surge another allomancer's burning metals |

> Feruchemy (store) and Hemalurgy (steal) aren't in the game — this is an Allomancy-only build.

---

## If something's broken

- **Red errors in the Console** during build or play → copy them and send them over.
- **Anything renders pink/magenta** → tell us; that's usually a render-pipeline issue and easy to fix.

Have fun — and let us know what breaks.