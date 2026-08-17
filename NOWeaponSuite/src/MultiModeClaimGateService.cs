namespace WeXon
{
    /// <summary>
    /// Greenfield MultiMode same-owner target claim gates (0.0.9.85).
    /// MultiModeBrain owns ClaimByTargetId dictionary mutation.
    /// </summary>
    internal static class MultiModeClaimGateService
    {
        /// <summary>
        /// True = do not overwrite claim (sticky sibling from same owner already holds target).
        /// </summary>
        internal static bool ShouldSkipClaimSteal(
            bool holderExists,
            bool holderIsSelf,
            bool holderStickyOnly,
            bool sameOwner)
        {
            if (!holderExists || holderIsSelf)
                return false;
            return holderStickyOnly && sameOwner;
        }

        internal static bool IsClaimedBySibling(bool otherExists, bool otherIsSelf)
        {
            return otherExists && !otherIsSelf;
        }

        internal static bool IsClaimedByStickySibling(bool otherExists, bool otherIsSelf, bool otherStickyOnly)
        {
            if (!otherExists || otherIsSelf)
                return false;
            return otherStickyOnly;
        }
    }
}
