using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield TBM free-hunt radius / cadence / pick gates (0.0.9.84).
    /// TbmHuntAssist owns grid scan and guidance writes.
    /// </summary>
    internal static class TbmHuntMathService
    {
        internal const float DefaultRadiusM = 60000f;
        internal const float MinRadiusM = 1000f;
        internal const float MinCandidateDistM = 20f;
        internal const float DefaultHuntIntervalSec = 0.5f;

        internal static float ClampHuntRadius(float configured)
        {
            if (configured < MinRadiusM)
                return DefaultRadiusM;
            return configured;
        }

        internal static float ScheduleNextHunt(float now, float searchInterval)
        {
            return now + Mathf.Max(DefaultHuntIntervalSec, searchInterval);
        }

        internal static bool HuntDue(float now, float nextHuntAt, bool force)
        {
            return force || now >= nextHuntAt;
        }

        /// <summary>True if candidate distance beats current best within radius band.</summary>
        internal static bool IsBetterCandidate(float dist, float radius, float bestDist)
        {
            if (dist > radius || dist < MinCandidateDistM)
                return false;
            return dist < bestDist;
        }

        internal static bool IsSurfaceHuntUnit(bool isMissile, bool isAircraft, bool isSurfaceUnit)
        {
            if (isMissile || isAircraft)
                return false;
            return isSurfaceUnit;
        }
    }
}
