using UnityEngine;

namespace BasicRPG.Allomancy
{
    /// <summary>
    /// Real-world ↔ Unity unit conversion for the allomancy force model. The force equation
    /// <c>F = k·m_metal·B/(r+δ)²</c> is real-life physics (SI: F in Newtons, r &amp; δ in metres,
    /// m in kg, a in m/s²), but Unity's transform units are arbitrary. This is the single source
    /// of truth that maps between them so the equation runs in SI and the resulting velocity impulse
    /// is converted back to Unity units/s for the CharacterController/Rigidbody.
    ///
    /// Canonical scale (the user's locked-in conversion rate): <b>Vin is 2 Unity units tall in-game;
    /// her lore height is 153 cm (1.53 m).</b> → 1 Unity unit = 0.765 m.
    ///
    /// How it is used (see <see cref="IronSteel.ApplyForce"/> / <see cref="IronSteel.ApplyForceBubble"/>):
    ///   r_m       = r_units · <see cref="MetersPerUnit"/>              — distance in metres
    ///   δ_m       = rangeDelta_units · <see cref="MetersPerUnit"/>     — near-field clamp in metres
    ///   F (N)     = allomanticK · m_metal · B / (r_m + δ_m)²             — force in Newtons
    ///   N (N)     = m · <see cref="RealGravity"/> (grounded) / 0 (airborne) — contact normal force in Newtons
    ///   a (m/s²)  = F / [ m · (1 + α · N) ]                              — acceleration in m/s²
    ///   Δv (u/s)  = a · dt · <see cref="UnitsPerMeter"/>                — velocity impulse back to Unity units/s
    ///
    /// <b>Retune that preserves the prior feel EXACTLY</b> (so the grounded-coin-~18×-Mistborn test and
    /// all launch magnitudes are unchanged): switching the distance from units to metres scales the force
    /// denominator by <see cref="MetersPerUnit"/>² and the impulse numerator by <see cref="UnitsPerMeter"/>
    /// (net 1/MPU³), so <c>k_SI = k_old · MPU³ = 300 · 0.765³ ≈ 134.3</c>; switching the contact force N
    /// from the gameplay gravity to real g scales N by g_real/g_game, so the mobility term is preserved
    /// by <c>α_SI = α_old · g_game / g_real = 0.5 · 19.62 / 9.81 = 1.0</c>. With
    /// <c>allomanticK=134.3</c>, <c>mobilityAlpha=1.0</c>, <c>bracingGravity=9.81</c> the SI model produces
    /// the identical per-frame impulse (units/s) as the old game-unit model. Masses are NOT rebased here
    /// (kept as the existing game values — coin 0.2 / crate 1 / block 20 / bodyMass 1); making them real
    /// kg (coin ~0.005, Vin ~50) is a separate tuning pass the user can opt into, with k re-derived the
    /// same way.
    ///
    /// <see cref="RealGravity"/> is the SI gravity used ONLY for the contact-force estimate (mobility).
    /// It is deliberately separate from <see cref="Player.PlayerController"/>'s gameplay gravity (19.62
    /// units/s², a 2× feel value for snappy jumps) — the force model uses real g; the movement controller
    /// keeps its gameplay gravity.
    /// </summary>
    public static class AllomancyUnits
    {
        /// <summary>Vin is 2 Unity units tall; lore height 153 cm (1.53 m). → 1 unit = 0.765 m.</summary>
        public const float MetersPerUnit = 0.765f;       // 1.53 m / 2 units

        /// <summary>1 / <see cref="MetersPerUnit"/> ≈ 1.307 — converts a metres/s velocity to Unity units/s.</summary>
        public const float UnitsPerMeter = 1f / MetersPerUnit;

        /// <summary>Real-world gravitational acceleration (m/s²). Used for the SI contact-force estimate
        /// N = m·g in the mobility model. Distinct from the gameplay <c>PlayerController.gravity</c>.</summary>
        public const float RealGravity = 9.81f;
    }
}