using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield nuclear shock/blast resist factors (0.0.9.67).
    /// NukeResist owns unit tier resolution + Harmony patches.
    /// </summary>
    internal static class NukeResistMathService
    {
        /// <summary>Damage multiplier vs Full baseline (higher = less resist).</summary>
        internal static float TierMul(NukeResist.Tier tier, bool buildingHalfResist, bool navalTripleResist)
        {
            if (tier == NukeResist.Tier.Building)
                return buildingHalfResist ? 2f : 1f;
            if (tier == NukeResist.Tier.Ship)
                return navalTripleResist ? (1f / 3f) : 2f;
            if (tier == NukeResist.Tier.Vehicle)
                return 2f;
            return 1f;
        }

        internal static float ShockFactor(
            NukeResist.Tier tier,
            float aircraftBaseline,
            float otherBaseline,
            bool buildingHalfResist,
            bool navalTripleResist)
        {
            float baseline = tier == NukeResist.Tier.Aircraft ? aircraftBaseline : otherBaseline;
            return Mathf.Clamp01(baseline * TierMul(tier, buildingHalfResist, navalTripleResist));
        }

        internal static float BlastFactor(
            NukeResist.Tier tier,
            float aircraftBaseline,
            float otherBaseline,
            bool buildingHalfResist,
            bool navalTripleResist)
        {
            float baseline = tier == NukeResist.Tier.Aircraft ? aircraftBaseline : otherBaseline;
            return Mathf.Clamp01(baseline * TierMul(tier, buildingHalfResist, navalTripleResist));
        }

        internal static bool BlastAboveThreshold(float blastDamage, float threshold)
        {
            return blastDamage >= threshold;
        }
    }
}
