using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Marks a scene object as an allomantic anchor — a piece of metal an Iron/Steel Misting
    /// can push against (Steel) or pull toward (Iron). Any collider-bearing object with this
    /// component is a valid anchor; enemies carry it too (metal armor), so you can shove them.
    /// `mass` is a flavour dial: a heavy anchor (wall) barely budges when you push it, so most
    /// of the reaction goes into launching *you*; a light anchor (a coin) flies away and you
    /// barely move. (BasicRPG keeps anchors static for now — only the player + enemy Character
    /// Controllers move — so mass currently scales the shove felt by the player.)
    /// </summary>
    public class MetalAnchor : MonoBehaviour
    {
        [Tooltip("Heavier anchors resist being shoved and give the player a stronger push off them.")]
        public float mass = 1f;
    }
}