using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield AI engagement gates (0.0.9.79): shoot cones/range, break-off,
    /// fire cadence, ACM pick chance, cruise edge throttle, hunt approach class.
    /// AiCombatBrain owns FSM state + Unity fire/ACM execution.
    /// </summary>
    internal static class AiCombatEngagementService
    {
        internal const float BreakOffAngleDeg = 50f;
        internal const float OtherWeaponConeSlackDeg = 8f;
        internal const float CruiseInwardFrac = 0.72f;
        internal const float CruiseEdgeThrottleFrac = 0.85f;
        internal const float CruiseEdgeThrottleCap = 0.55f;
        internal const float AcmMidScriptMinU = 0.4f;
        internal const float AcmMidScriptChanceScale = 0.65f;
        internal const float PreferAcmOverBreakChanceFloor = 0.5f;

        internal enum HuntApproachKind
        {
            LeadChase = 0,
            FarChase = 1,
            TooCloseSide = 2,
            LagLine = 3
        }

        internal static bool CanShoot(
            bool hasStation,
            bool isGun,
            bool isMissile,
            float dist,
            float angle,
            float gunMax,
            float gunCone,
            float missileMax,
            float missileMin,
            float missileCone)
        {
            if (!hasStation)
                return false;
            if (isGun)
                return dist < gunMax && angle < gunCone;
            if (isMissile)
                return dist < missileMax && dist > missileMin && angle < missileCone;
            return dist < missileMax && angle < missileCone + OtherWeaponConeSlackDeg;
        }

        internal static bool ShouldBreakOff(float dist, float angle, float breakDist)
        {
            return dist < breakDist && angle < BreakOffAngleDeg;
        }

        /// <summary>True when higher tiers should try J-turn/extend instead of flat break.</summary>
        internal static bool PreferAcmOverBreak(float acmChance, float roll01)
        {
            return acmChance > PreferAcmOverBreakChanceFloor && roll01 < acmChance;
        }

        internal static float BreakOffUntil(float now, float skill)
        {
            return now + Mathf.Lerp(2.8f, 0.9f, Mathf.Clamp01(skill));
        }

        internal static float ScheduleNextFireAt(float now, float weaponInterval, float fireDelay)
        {
            float interval = weaponInterval > 0.05f ? weaponInterval : 0.35f;
            return now + interval * Mathf.Max(0.15f, fireDelay);
        }

        internal static float ManeuverDuration(float baseDuration, float acmDurationMul)
        {
            float durMul = acmDurationMul > 0.05f ? acmDurationMul : 0.6f;
            return Mathf.Max(0.55f, baseDuration * durMul);
        }

        internal static float AcmRepickGap(float skill)
        {
            return Mathf.Lerp(0.07f, 0.012f, Mathf.Clamp01(skill));
        }

        internal static float NextAcmPickAfterManeuver(
            float maneuverUntil,
            float maneuverDuration,
            float skill)
        {
            float gap = AcmRepickGap(skill);
            return maneuverUntil - Mathf.Min(0.35f, maneuverDuration * 0.25f) + gap;
        }

        /// <summary>
        /// Mid-script ACM refresh gate. Returns false = keep current script this tick.
        /// </summary>
        internal static bool AllowAcmMidScriptRefresh(
            float progressU,
            float acmChance,
            float roll01)
        {
            if (progressU < AcmMidScriptMinU)
                return false;
            return roll01 <= acmChance * AcmMidScriptChanceScale;
        }

        internal static bool AllowAcmPick(float acmChance, float roll01)
        {
            return roll01 <= acmChance;
        }

        internal static float AcmRefreshOrDefault(float acmRefresh)
        {
            return acmRefresh > 0.05f ? acmRefresh : 0.45f;
        }

        /// <summary>Closer in, re-pick ACM sooner so intercept/geometry stays current.</summary>
        internal static float AcmRefreshForRange(float acmRefresh, float dist)
        {
            float r = AcmRefreshOrDefault(acmRefresh);
            if (dist < 2200f)
                return r * 0.45f;
            if (dist < 4500f)
                return r * 0.65f;
            return r;
        }

        internal static bool PreferInwardCruise(float radialMapFrac)
        {
            return radialMapFrac > CruiseInwardFrac;
        }

        internal static float CruiseThrottle(float throttleCruise, float radialMapFrac)
        {
            if (radialMapFrac > CruiseEdgeThrottleFrac)
                return Mathf.Min(throttleCruise, CruiseEdgeThrottleCap);
            return throttleCruise;
        }

        internal static HuntApproachKind ClassifyHuntApproach(
            float dist,
            float angle,
            bool isMissile,
            float missileMax,
            float missileMin,
            bool targetIsAircraft,
            float interceptFwdDot)
        {
            return ClassifyHuntApproach(dist, angle, isMissile, missileMax, missileMin,
                targetIsAircraft, interceptFwdDot, HuntApproachKind.LeadChase);
        }

        internal static HuntApproachKind ClassifyHuntApproach(
            float dist,
            float angle,
            bool isMissile,
            float missileMax,
            float missileMin,
            bool targetIsAircraft,
            float interceptFwdDot,
            HuntApproachKind prev)
        {
            HuntApproachKind next = HuntApproachKind.LeadChase;
            if (targetIsAircraft && interceptFwdDot < 0.08f && angle > 35f)
                next = HuntApproachKind.LagLine;
            else if (dist > missileMax * 0.85f)
                next = HuntApproachKind.FarChase;
            else if (dist < missileMin * 1.2f && isMissile)
                next = HuntApproachKind.TooCloseSide;
            else if (targetIsAircraft && angle > 25f)
                next = HuntApproachKind.LagLine;

            // Hold the last class across the boundary so AutoAim does not flip bank each tick.
            if (prev == HuntApproachKind.TooCloseSide && isMissile && dist < missileMin * 1.55f)
                return HuntApproachKind.TooCloseSide;
            if (prev == HuntApproachKind.LagLine && targetIsAircraft && angle > 18f)
                return HuntApproachKind.LagLine;
            if (prev == HuntApproachKind.FarChase && dist > missileMax * 0.72f)
                return HuntApproachKind.FarChase;
            return next;
        }

        internal static float HuntThrottle(
            float dist,
            float mySpeed,
            float cornerSpeed,
            float angle,
            float throttleAttack)
        {
            if (dist > 8000f)
                return 1f;
            if (mySpeed > cornerSpeed * 1.2f && angle > 30f)
                return 0.65f;
            return throttleAttack;
        }

        internal static float LeadTime(float dist, float mySpeed)
        {
            return dist / Mathf.Max(180f, mySpeed + 320f);
        }

        /// <summary>Break-off energy phase: true = dive-extend, false = zoom.</summary>
        internal static bool BreakOffDivePhase(float breakUntil, float now)
        {
            float bu = Mathf.Clamp01((breakUntil - now) / 3f);
            return bu > 0.45f;
        }
    }
}
