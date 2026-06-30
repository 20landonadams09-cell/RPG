using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Marks a scene object as an allomantic anchor — a piece of metal an Iron/Steel Misting
    /// can push against (Steel) or pull toward (Iron). Any collider-bearing object with this
    /// component is a valid anchor; enemies carry it too (metal armor), so you can shove them.
    ///
    /// There are two kinds of anchor (Newton's-third-law demonstration):
    /// • ANCHORED (default, <see cref="anchored"/> = true) — "nailed to the ground": a static
    ///   piece of metal (a wall, a bolted cube) that CANNOT move. Pushing/pulling it transfers
    ///   the FULL reaction to the Mistborn — you launch off it. The metal stays put.
    /// • LOOSE (<see cref="anchored"/> = false) — a free, movable body (a coin, a crate, a block)
    ///   carrying a Rigidbody. Pushing/pulling it is a MASS-SPLIT: the same impulse goes to both
    ///   bodies, so the lighter one moves more. A light coin flies off and you barely move; a
    ///   heavy block barely budges and YOU get the recoil instead. Equal momentum on both bodies
    ///   (m_obj * v_obj == m_player * v_player) — the velocity ratio is the inverse of the mass
    ///   ratio. Armored enemies are loose too (shoved via their CharacterController).
    ///
    /// `mass` is the body's real mass for that split: a heavy anchor barely budges (most of the
    /// reaction launches you), a light anchor flies away (you barely move). For an anchored
    /// (immovable) anchor, mass only scales the shove the player feels + the allomantic range
    /// bonus (heavier metal pushes from farther — canon).
    /// </summary>
    public class MetalAnchor : MonoBehaviour
    {
        [Tooltip("Heavier anchors resist being shoved and give the player a stronger push off them; heavier metal also reaches farther (canon).")]
        public float mass = 1f;

        [Tooltip("True = ANCHORED (nailed down / immovable): the metal never moves, the full push/pull reaction launches the Mistborn (hover/launch off a wall). False = LOOSE (a free Rigidbody body): pushed/pulled as a mass-split — the lighter body moves more (a coin flies off, a heavy block shoves you back). Armored enemies are loose via their Enemy component.")]
        public bool anchored = true;
    }
}