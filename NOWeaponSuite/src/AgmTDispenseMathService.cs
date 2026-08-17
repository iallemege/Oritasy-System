using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield AGM-T bus dispense / pack-pick / eject ring math (0.0.9.86).
    /// AgmTDispenser owns hunt queries, Spawner, and discard side effects.
    /// </summary>
    internal static class AgmTDispenseMathService
    {
        internal const float MinFlightFloorSec = 5f;
        internal const float DefaultMinFlightSec = 5f;
        internal const float DefaultDispenseDistM = 2500f;
        internal const float DefaultHuntIntervalSec = 0.4f;
        internal const float DefaultCheckIntervalSec = 0.25f;
        internal const float EjectKickForwardMps = 180f;
        internal const float EjectRadialOffsetM = 0.8f;
        internal const float EjectForwardOffsetM = 0.5f;
        internal const int SubCountMin = 1;
        internal const int SubCountMax = 24;
        internal const int DefaultSubCount = 6;

        internal static float ClampMinFlightDelay(float configured)
        {
            if (configured < MinFlightFloorSec)
                return MinFlightFloorSec;
            return configured;
        }

        internal static bool PastMinFlight(float ageSec, float minDelaySec)
        {
            return ageSec >= ClampMinFlightDelay(minDelaySec);
        }

        internal static bool NearDispenseDistance(float sqrDist, float dispenseDistM)
        {
            float d = dispenseDistM > 1f ? dispenseDistM : DefaultDispenseDistM;
            return sqrDist <= d * d;
        }

        /// <summary>True when bus should open cluster (near lock or timed).</summary>
        internal static bool ShouldDispense(float ageSec, float minDelaySec, bool nearTarget)
        {
            if (!PastMinFlight(ageSec, minDelaySec))
                return false;
            return nearTarget || PastMinFlight(ageSec, minDelaySec);
        }

        internal static bool AllowForceDispense(bool forceEarly, float ageSec, float minDelaySec)
        {
            if (forceEarly)
                return true;
            return PastMinFlight(ageSec, minDelaySec);
        }

        /// <summary>Prefer denser pack; air wins ties.</summary>
        internal static bool PreferAirPack(int airCount, int groundCount)
        {
            if (airCount == 0 && groundCount == 0)
                return false;
            return airCount >= groundCount && airCount > 0;
        }

        /// <summary>ACNM-118 is anti-surface: use ground pack whenever any ground hostiles exist.</summary>
        internal static bool PreferGroundPack(bool nukeVariant, int airCount, int groundCount)
        {
            if (!nukeVariant)
                return false;
            return groundCount > 0;
        }

        internal static float PackCandidateScore(float alignDot, float distM, float valueM)
        {
            float dist = distM < 1f ? 1f : distM;
            return alignDot * 2000f - dist + valueM * HuntSalvoGateService.ValueWeight;
        }

        internal static bool IsBetterPackScore(float score, float bestScore)
        {
            return score > bestScore;
        }

        internal static int ClampSubCount(int configured)
        {
            return Mathf.Clamp(configured, SubCountMin, SubCountMax);
        }

        /// <summary>
        /// How many GS25 follow the player's fire-time lock. Rest LOAL-hunt.
        /// Value is UnitDefinition.value (millions). No lock → caller passes 0 follow.
        /// 6-pack: &lt;12M→1, 12→2, 25→3, 45→4, 80→5, 120+→6.
        /// </summary>
        internal static int FollowLockCount(float valueM, int subCount)
        {
            int n = ClampSubCount(subCount);
            if (n < 1)
                return 0;
            int follow;
            if (valueM >= 120f)
                follow = n;
            else if (valueM >= 80f)
                follow = (n * 5) / 6;
            else if (valueM >= 45f)
                follow = (n * 4) / 6;
            else if (valueM >= 25f)
                follow = (n * 3) / 6;
            else if (valueM >= 12f)
                follow = (n * 2) / 6;
            else
                follow = 1;
            if (follow < 1)
                follow = 1;
            if (follow > n)
                follow = n;
            return follow;
        }

        internal static void EjectRingOffsets(
            int index,
            int count,
            Vector3 right,
            Vector3 up,
            Vector3 forward,
            float ejectSpeed,
            out Vector3 radial,
            out Vector3 posOffset,
            out Vector3 velKick)
        {
            float ang = (360f / Mathf.Max(1, count)) * index * Mathf.Deg2Rad;
            radial = right * Mathf.Cos(ang) + up * Mathf.Sin(ang);
            posOffset = radial * EjectRadialOffsetM + forward * EjectForwardOffsetM;
            velKick = radial * ejectSpeed + forward * EjectKickForwardMps;
        }

        internal static float ScheduleNextHunt(float now)
        {
            return now + DefaultHuntIntervalSec;
        }

        internal static float ScheduleNextCheck(float now)
        {
            return now + DefaultCheckIntervalSec;
        }
    }
}
