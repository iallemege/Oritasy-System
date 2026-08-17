namespace WeXon
{
    /// <summary>
    /// Greenfield PlayerCareer kill attribution gates (0.0.9.92).
    /// PlayerCareer owns prefs, session records, and DrawGui.
    /// </summary>
    internal static class CareerRecordGateService
    {
        internal enum KillPath
        {
            Skip = 0,
            Friendly = 1,
            Enemy = 2
        }

        internal static KillPath ResolveRecordKill(
            bool enabled,
            bool victimNull,
            bool sameFactionFriendly)
        {
            if (!enabled || victimNull)
                return KillPath.Skip;
            if (sameFactionFriendly)
                return KillPath.Friendly;
            return KillPath.Enemy;
        }

        internal static bool IsPvpVictim(bool victimIsAircraft, bool playerPiloted)
        {
            return victimIsAircraft && playerPiloted;
        }

        internal static void ApplySkillCounters(
            bool airVictim,
            float skillGap,
            float strongGap,
            float godGap,
            ref int godSlayer,
            ref int strongKill)
        {
            if (!airVictim)
                return;
            if (skillGap >= godGap)
                godSlayer++;
            if (skillGap >= strongGap)
                strongKill++;
        }

        internal static int ApplyBestStreak(int currentBest, int streak)
        {
            return streak > currentBest ? streak : currentBest;
        }

        internal static float MainMenuCacheInterval(bool cachedIsMainMenu)
        {
            return cachedIsMainMenu ? 0.5f : 2f;
        }
    }
}
