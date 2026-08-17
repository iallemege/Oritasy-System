namespace WeXon
{
    /// <summary>
    /// Greenfield pending IAL nuke consume FSM (0.0.9.94).
    /// Plugin.ConsumePendingNuke owns ApplyNukeForced and pending field clears.
    /// </summary>
    internal static class PendingNukeGateService
    {
        internal const float StaleTimeoutSec = 12f;
        internal const float FreshFireSec = 2.5f;

        internal enum Path
        {
            Skip = 0,
            ClearStale = 1,
            Consume = 2
        }

        internal static Path Resolve(
            bool missileNull,
            int pendingCount,
            bool gunShell,
            bool agmTBlocks,
            float ageSincePending,
            bool cloneInfo,
            bool ownerMatch,
            bool freshFire)
        {
            if (missileNull || pendingCount <= 0)
                return Path.Skip;
            if (gunShell || agmTBlocks)
                return Path.Skip;
            if (ageSincePending > StaleTimeoutSec)
                return Path.ClearStale;
            if (!cloneInfo || !(ownerMatch || freshFire))
                return Path.Skip;
            return Path.Consume;
        }

        internal static bool IsFreshFire(float ageSincePending)
        {
            return ageSincePending <= FreshFireSec;
        }
    }
}
