namespace WeXon
{
    /// <summary>
    /// Greenfield KillAccolades ProcessKill early + flash milestone gates (0.0.9.92).
    /// KillAccolades owns Enqueue UI and GrantUnlock persistence.
    /// </summary>
    internal static class KillAccoladeProcessGateService
    {
        internal enum Path
        {
            Skip = 0,
            FriendlyOnly = 1,
            ProcessEnemy = 2
        }

        internal static Path Resolve(
            bool enabled,
            bool victimNull,
            bool countableVictim,
            bool killerLocalHuman,
            bool dedupBlocked,
            bool friendly)
        {
            if (!enabled || victimNull || !countableVictim || !killerLocalHuman || dedupBlocked)
                return Path.Skip;
            if (friendly)
                return Path.FriendlyOnly;
            return Path.ProcessEnemy;
        }

        /// <summary>True when streak should flash a milestone toast (2/3/4/5+).</summary>
        internal static bool ShouldFlashStreak(int streak)
        {
            return KillCombatMathService.StreakBadgeCode(streak) != null
                || streak > 5;
        }

        /// <summary>True when mission total should flash AP/BD toast.</summary>
        internal static bool ShouldFlashMissionTotal(int missionKills)
        {
            return KillCombatMathService.MissionKillBadgeCode(missionKills) != null;
        }
    }
}
