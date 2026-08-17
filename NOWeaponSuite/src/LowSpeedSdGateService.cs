namespace WeXon
{
    /// <summary>
    /// Greenfield low-speed self-destruct decision tree (0.0.9.89).
    /// Plugin.TickLowSpeedSelfDestruct owns dictionaries, Arm/Detonate, prune.
    /// </summary>
    internal static class LowSpeedSdGateService
    {
        internal enum Path
        {
            NoOp = 0,
            MarkDone = 1,
            Continue = 2,
            ClearBelow = 3,
            StartBelowTimer = 4,
            WaitHold = 5,
            Detonate = 6
        }

        internal static bool ShouldSkipSampleFrame(bool alreadyTrackingBelow, int frameCount, int missileId)
        {
            if (alreadyTrackingBelow)
                return false;
            return ((frameCount + (missileId & 3)) & 3) != 0;
        }

        internal static float ResolveMinAgeSec(float minAgeBase, bool isBallistic, float ballisticMinAge)
        {
            float minAge = minAgeBase;
            if (isBallistic)
            {
                float bMin = ballisticMinAge;
                if (minAge < bMin)
                    minAge = bMin;
            }
            return minAge;
        }

        internal static float ClampHoldSec(float hold)
        {
            return hold < 0.2f ? 0.2f : hold;
        }

        internal static float ResolveThresholdMps(float configuredKmh)
        {
            float kmh = configuredKmh < 1f ? 1f : configuredKmh;
            float thresh = kmh / 3.6f;
            return thresh < 1f ? 1f : thresh;
        }

        /// <summary>
        /// Early gates after enable/null checks. MarkDone means write sdFlag=1 and return.
        /// Continue means proceed to age/speed evaluation (optionally write sdFlag=2 first).
        /// </summary>
        internal static Path ResolveEligibility(
            bool isServer,
            byte sdFlag,
            bool gunShellOrMotorless)
        {
            if (!isServer)
                return Path.MarkDone;
            if (sdFlag != 2)
            {
                if (gunShellOrMotorless)
                    return Path.MarkDone;
            }
            return Path.Continue;
        }

        internal static Path ResolveAfterEligibility(
            bool safeDiscard,
            float ageSec,
            float minAgeSec,
            float remainingBurnSec,
            float speedMps,
            float thresholdMps,
            bool isBallistic,
            float velY,
            bool hasBelowSince,
            float now,
            float belowSince,
            float holdSec)
        {
            if (safeDiscard)
                return Path.NoOp;
            if (ageSec < minAgeSec)
                return Path.NoOp;
            if (remainingBurnSec > 0.15f)
                return Path.NoOp;

            float thresh = thresholdMps < 1f ? 1f : thresholdMps;
            if (speedMps >= thresh)
                return hasBelowSince ? Path.ClearBelow : Path.NoOp;

            if (isBallistic && velY > -8f)
                return hasBelowSince ? Path.ClearBelow : Path.NoOp;

            if (!hasBelowSince)
                return Path.StartBelowTimer;

            float hold = ClampHoldSec(holdSec);
            if (now - belowSince < hold)
                return Path.WaitHold;
            return Path.Detonate;
        }

        internal static bool ShouldPruneMaps(float unscaledNow, float nextPruneAt)
        {
            return unscaledNow >= nextPruneAt;
        }

        internal static float NextPruneAt(float unscaledNow)
        {
            return unscaledNow + 20f;
        }

        internal static bool ShouldClearBelowMap(int count)
        {
            return count > 64;
        }

        internal static bool ShouldClearFlagMap(int count)
        {
            return count > 128;
        }
    }
}
