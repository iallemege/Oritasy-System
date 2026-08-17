using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield yield-proximity fuze math (0.0.9.67).
    /// YieldProximityFuze owns Harmony wiring + reflection.
    /// </summary>
    internal static class ProximityFuzeMathService
    {
        internal const float KinematicFloorSeconds = 0.05f;
        internal const float TightBubbleFraction = 0.35f;
        internal const float CpaLookaheadMul = 1.1f;

        /// <summary>
        /// Proximity trigger radius (m) from warhead blastYield.
        /// Cube-root scaling matches Shockwave: R = RefR * (Y/RefY)^(1/3) * Scale.
        /// </summary>
        internal static float RangeFromYield(
            float blastYield,
            float refYield,
            float refRangeM,
            float scale,
            float minM,
            float maxM)
        {
            float refY = refYield < 1f ? 1f : refYield;
            float refR = refRangeM < 1f ? 1f : refRangeM;
            float s = scale < 0.01f ? 0.01f : scale;
            float lo = minM < 1f ? 1f : minM;
            float hi = maxM < lo ? lo : maxM;
            float y = Mathf.Max(blastYield, 1f);
            float r = refR * Mathf.Pow(y / refY, 1f / 3f) * s;
            return Mathf.Clamp(r, lo, hi);
        }

        internal static float TriggerRange(float yieldRangeM, float missileSpeed)
        {
            float kinematic = Mathf.Max(0f, missileSpeed) * KinematicFloorSeconds;
            return Mathf.Max(yieldRangeM, kinematic);
        }

        /// <summary>True when relative motion has passed closest point of approach.</summary>
        internal static bool PassedCpa(Vector3 toMissile, Vector3 relVel, float fixedDeltaTime)
        {
            Vector3 next = toMissile + (fixedDeltaTime * CpaLookaheadMul) * relVel;
            return Vector3.Dot(relVel, next) > 0f;
        }

        internal static bool InsideTightBubble(float distanceSqr, float triggerRangeM)
        {
            float tight = triggerRangeM * TightBubbleFraction;
            return distanceSqr <= tight * tight;
        }

        internal static Vector3 CpaSnapPosition(Vector3 missilePos, Vector3 toMissile, Vector3 relVel)
        {
            return missilePos + Vector3.Project(-toMissile, relVel);
        }
    }
}
