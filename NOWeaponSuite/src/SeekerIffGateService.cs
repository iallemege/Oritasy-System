using System;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield seeker IFF / SetTarget / SD-suppress gates (0.0.9.84).
    /// Plugin / MultiModeBrain / Harmony own Unit resolution and side effects.
    /// </summary>
    internal static class SeekerIffGateService
    {
        /// <summary>Block SetTarget(null) while free-hunt still owns a live target.</summary>
        internal static bool ShouldBlockNullTargetClear(bool multiModeHasHunt, bool tbmHasTarget)
        {
            return multiModeHasHunt || tbmHasTarget;
        }

        /// <summary>
        /// Friendly SetTarget gate after ejected-pilot check.
        /// True = allow vanilla SetTarget; false = block.
        /// </summary>
        internal static bool AllowFriendlyAwareSetTarget(
            bool enableIff,
            bool blockFriendlySetTarget,
            bool targetIsNull,
            bool isAllowedTarget)
        {
            if (!enableIff || !blockFriendlySetTarget)
                return true;
            if (targetIsNull)
                return true;
            return isAllowedTarget;
        }

        /// <summary>Soft IFF after structural rejects (self/missile/dead/scenery/ejected).</summary>
        internal static bool SoftIffAllows(bool enableIff, bool sameFaction)
        {
            if (!enableIff)
                return true;
            return !sameFaction;
        }

        /// <summary>
        /// Strict free-hunt / LOAL hostility when both HQs resolve.
        /// Incomplete HQ is denied (175C) — never treat unknown hangars as hostile.
        /// </summary>
        internal static bool StrictHuntAllows(
            bool enableIff,
            bool softAllowedWhenIffOff,
            bool shooterHqPresent,
            bool targetHqPresent,
            bool sameHq,
            bool aircraftSide,
            bool softAllowedWhenIncomplete)
        {
            if (!enableIff)
                return softAllowedWhenIffOff;
            if (shooterHqPresent && targetHqPresent)
                return !sameHq;
            return false;
        }

        internal static bool StrictHuntAllows(
            bool enableIff,
            bool softAllowedWhenIffOff,
            bool shooterHqPresent,
            bool targetHqPresent,
            bool sameHq)
        {
            return StrictHuntAllows(
                enableIff, softAllowedWhenIffOff,
                shooterHqPresent, targetHqPresent, sameHq,
                false, false);
        }

        /// <summary>FactionHQ object/faction name: Neutral / 中立. Null HQ is unaligned.</summary>
        internal static bool IsNeutralFactionLabel(string hqObjectName, string factionName)
        {
            return LooksNeutralLabel(hqObjectName) || LooksNeutralLabel(factionName);
        }

        private static bool LooksNeutralLabel(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            string n = s.ToLowerInvariant();
            if (n.IndexOf("neutral", StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("\u4e2d\u7acb", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// VLS SARH (RAM-45 dummy 0.01s + 1.1s delay, R9 similar). Vanilla SlowChecks
        /// run while EngineOn is false (inter-stage) and airburst on MissedTarget /
        /// speed below SD / null target. Keep vanilla SD after this window.
        /// </summary>
        internal static bool IsShipPitchOverWindow(bool vlsSoftLaunch, float ageSec, float windowSec)
        {
            if (!vlsSoftLaunch)
                return false;
            float w = windowSec > 0.5f ? windowSec : 10f;
            return ageSec < w;
        }

        /// <summary>
        /// Guided missiles: skip seeker SlowChecks / MissedTarget / LosingGround / low-speed
        /// airburst for the whole flight. Collision and proximity hits still detonate.
        /// Gun shells and unguided rockets keep vanilla SD.
        /// </summary>
        internal static bool ShouldSuppressSeekerSelfDestruct(
            bool isGunShell,
            bool isUnguidedRocket)
        {
            if (isGunShell || isUnguidedRocket)
                return false;
            return true;
        }

        internal static int ApplyWarheadQuotaSubtract(int currentWarheads, int ialExemptAmmoSum)
        {
            if (currentWarheads <= 0 || ialExemptAmmoSum <= 0)
                return currentWarheads;
            return Mathf.Max(0, currentWarheads - ialExemptAmmoSum);
        }
    }
}
