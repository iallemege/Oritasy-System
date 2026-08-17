using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield missile-camera lifecycle FSM (0.0.9.83): phase resolve, scan cadence, cycle index.
    /// MissileCameraHud owns cameras, tracking list, and Unity input/draw.
    /// </summary>
    internal static class MissileCameraLifecycleService
    {
        internal const float FallbackScanIntervalSec = 2.5f;

        internal enum Phase
        {
            Hidden = 0,
            PipChase = 1,
            ManualFullscreen = 2
        }

        /// <summary>
        /// Resolve display/control phase for this tick after prune + follow validity checks.
        /// </summary>
        internal static Phase ResolvePhase(
            bool manualPilot,
            bool followAlive,
            bool featureEnabled,
            bool overlayOn)
        {
            if (manualPilot)
                return followAlive ? Phase.ManualFullscreen : Phase.Hidden;
            if (!featureEnabled || !overlayOn)
                return Phase.Hidden;
            if (!followAlive)
                return Phase.Hidden;
            return Phase.PipChase;
        }

        internal static bool ShouldExitManual(bool manualPilot, bool followAlive)
        {
            return manualPilot && !followAlive;
        }

        internal static bool ShouldRunFallbackScan(float now, float nextScanAt)
        {
            return now >= nextScanAt;
        }

        internal static float ScheduleNextScan(float now)
        {
            return now + FallbackScanIntervalSec;
        }

        internal static bool CanEnterManual(bool featureAllowed, bool followAlive)
        {
            return featureAllowed && followAlive;
        }

        /// <summary>
        /// Next live missile index after currentIdx (-1 if none). scannedCount steps wrap around.
        /// </summary>
        internal static int NextLiveIndex(int currentIdx, int count, System.Func<int, bool> isAliveAt)
        {
            if (count <= 0 || isAliveAt == null)
                return -1;
            if (currentIdx < -1)
                currentIdx = -1;
            for (int n = 1; n <= count; n++)
            {
                int i = ((currentIdx + n) % count + count) % count;
                if (isAliveAt(i))
                    return i;
            }
            return -1;
        }

        /// <summary>Newest-first pick: last live index in list, or -1.</summary>
        internal static int PickNewestLiveIndex(int count, System.Func<int, bool> isAliveAt)
        {
            if (count <= 0 || isAliveAt == null)
                return -1;
            for (int i = count - 1; i >= 0; i--)
            {
                if (isAliveAt(i))
                    return i;
            }
            return -1;
        }

        internal static bool ShouldKeepTracked(bool alive, bool owned)
        {
            return alive && owned;
        }

        internal static bool FixedTickApplies(bool manualPilot, bool followAlive)
        {
            return manualPilot && followAlive;
        }
    }
}
