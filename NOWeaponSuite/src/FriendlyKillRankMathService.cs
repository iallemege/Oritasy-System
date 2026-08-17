namespace WeXon
{
    /// <summary>
    /// Greenfield friendly-kill HUD best/worst ranking (0.0.9.88).
    /// FriendlyKillHud owns aircraft enumeration and draw.
    /// </summary>
    internal static class FriendlyKillRankMathService
    {
        internal const float RefreshHzMin = 0.25f;
        internal const float RefreshHzMax = 10f;

        internal static float ClampRefreshHz(float hz)
        {
            if (hz < RefreshHzMin)
                return RefreshHzMin;
            if (hz > RefreshHzMax)
                return RefreshHzMax;
            return hz;
        }

        /// <summary>
        /// Pick best/worst indices into parallel score arrays. Returns false if no markers.
        /// </summary>
        internal static bool TryRank(
            int count,
            System.Func<int, int> scoreAt,
            out int bestIndex,
            out int worstIndex,
            out int bestScore,
            out int worstScore)
        {
            bestIndex = -1;
            worstIndex = -1;
            bestScore = int.MinValue;
            worstScore = int.MaxValue;
            if (count <= 0 || scoreAt == null)
                return false;

            if (count == 1)
            {
                bestIndex = 0;
                bestScore = scoreAt(0);
                return true;
            }

            for (int i = 0; i < count; i++)
            {
                int s = scoreAt(i);
                if (s > bestScore)
                {
                    bestScore = s;
                    bestIndex = i;
                }
                if (s < worstScore)
                {
                    worstScore = s;
                    worstIndex = i;
                }
            }

            // Everyone tied — no useful ranking
            if (bestScore == worstScore)
            {
                bestIndex = -1;
                worstIndex = -1;
                return false;
            }
            return bestIndex >= 0 && worstIndex >= 0;
        }
    }
}
