namespace WeXon
{
    /// <summary>
    /// Greenfield MultiModeBrain hunt sticky / active-target gates (0.0.9.84).
    /// MultiModeBrain owns seeker warm, GuideTo, and claim tables.
    /// </summary>
    internal static class MultiModeHuntGateService
    {
        internal static bool IsHunting(
            bool allowFreeAttack,
            bool stickyOnly,
            bool playerDesignated,
            bool targetAlive)
        {
            return allowFreeAttack && !stickyOnly && !playerDesignated && !targetAlive;
        }

        /// <summary>
        /// Blocks seeker SetTarget(null) / LoseLock while guiding a live non-friendly target.
        /// </summary>
        internal static bool HasActiveHuntTarget(
            bool targetAlive,
            bool playerDesignated,
            bool stickyOnly,
            bool confirmedFriendly,
            bool canEngage)
        {
            if (!targetAlive)
                return false;
            if (playerDesignated || stickyOnly)
                return !confirmedFriendly;
            return canEngage;
        }

        internal static bool ShouldSetupTbmHunt(bool allowFreeAttack, bool isBallistic)
        {
            return allowFreeAttack && isBallistic;
        }
    }
}
