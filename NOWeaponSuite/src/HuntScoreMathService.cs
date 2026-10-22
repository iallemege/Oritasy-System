using System;

namespace WeXon
{
    /// <summary>
    /// Pure hunt geometry. LOAL must not pick targets that demand stall-inducing yanks.
    /// </summary>
    internal static class HuntScoreMathService
    {
        internal const float SoftClaimPenaltyM = 2500f;
        internal const float MinEngageM = 5f;

        /// <summary>
        /// Score = distance weighted by off-boresight. Infinity = reject (FOV / energy cone).
        /// minDot is the stricter of MadMode FOV and energy-aware MaxOffBoresight.
        /// </summary>
        internal static float ScoreCandidate(
            float mx, float my, float mz,
            float cx, float cy, float cz,
            float fwdX, float fwdY, float fwdZ,
            float minDot,
            float radius,
            float softPenaltyM)
        {
            float dx = cx - mx;
            float dy = cy - my;
            float dz = cz - mz;
            float distSq = dx * dx + dy * dy + dz * dz;
            float minM = MinEngageM;
            if (distSq < minM * minM)
                return float.PositiveInfinity;
            float radiusSq = radius * radius;
            if (distSq > radiusSq)
                return float.PositiveInfinity;
            float dist = (float)Math.Sqrt(distSq);
            float inv = 1f / dist;
            float dot = fwdX * dx * inv + fwdY * dy * inv + fwdZ * dz * inv;
            if (dot < minDot)
                return float.PositiveInfinity;
            // Prefer nose-on: same range, 60° off scores worse than 10° off.
            float aspectCost = (1f - dot) * dist * 0.85f;
            return dist + aspectCost + softPenaltyM;
        }
    }
}
