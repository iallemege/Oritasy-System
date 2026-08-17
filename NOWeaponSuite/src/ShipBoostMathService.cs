using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield ship soft-launch velocity kick math (0.0.9.94).
    /// Plugin.BoostShipMissileLaunchVelocity owns rb writes.
    /// </summary>
    internal static class ShipBoostMathService
    {
        internal const float DefaultMinLaunchMps = 140f;
        internal const float MinLaunchFloorMps = 40f;
        internal const float LaunchWindowSec = 1.25f;
        internal const float AlongOkFactor = 0.92f;
        internal const float SpeedOkFactor = 0.85f;
        /// <summary>Nose mostly world-up → VLS cell; vanilla pitches then lights the delayed motor.</summary>
        internal const float VlsUpDotSkip = 0.65f;

        internal static float ClampMinLaunchMps(float configured)
        {
            float min = configured > 0f ? configured : DefaultMinLaunchMps;
            return min < MinLaunchFloorMps ? MinLaunchFloorMps : min;
        }

        /// <summary>Returns delta-v along nose to apply, or 0 when no kick needed.</summary>
        internal static float ResolveKickDeltaV(
            bool shipLaunched,
            bool missileOk,
            float ageSec,
            float minLaunchMps,
            float alongNose,
            float speedMps,
            float noseUpDot,
            bool sarhSeeker)
        {
            if (!missileOk || !shipLaunched)
                return 0f;
            // RAM-45 VLS / SARH: dummy 0.01s motor, 1.1s delay, then 7s burn.
            // Vanilla pitches at ~0 m/s. A 140 m/s nose kick while vertical makes
            // SlowChecks (speed < 200 / MissedTarget) airburst after pitch-over.
            if (sarhSeeker || noseUpDot >= VlsUpDotSkip)
                return 0f;
            float min = ClampMinLaunchMps(minLaunchMps);
            if (ageSec > LaunchWindowSec)
                return 0f;
            if (alongNose >= min * AlongOkFactor && speedMps >= min * SpeedOkFactor)
                return 0f;
            float need = min - Mathf.Max(alongNose, 0f);
            if (need < 1f)
                need = min - speedMps;
            return need < 1f ? 0f : need;
        }
    }
}
