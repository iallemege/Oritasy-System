namespace WeXon
{
    /// <summary>
    /// Greenfield MultiModeBrain BeforeMissileUpdate / ReapplyGuidanceAfterSeek
    /// orchestration gates (0.0.9.90). MultiModeBrain owns seeker writes / GuideTo / hunt.
    /// </summary>
    internal static class MultiModeTickGateService
    {
        internal const float DesignatedCheckIntervalSec = 0.25f;

        internal enum EarlyOut
        {
            Continue = 0,
            SkipDuplicateFrame = 1,
            NotReady = 2,
            ClearOverride = 3,
            StripIncompatible = 4
        }

        internal enum ModeBranch
        {
            ShipCoast = 0,
            Sticky = 1,
            OpenHunt = 2
        }

        /// <summary>Sticky / designated lock actions (live Unit or coast last aim).</summary>
        internal enum StickyAction
        {
            DropCoast = 0,
            GuideAlive = 1,
            SyncHotOnly = 2
        }

        internal enum OpenHuntAction
        {
            None = 0,
            GuideExisting = 1,
            SyncHotExisting = 2,
            TryFreeHunt = 3,
            ReleaseVanilla = 4,
            CoastSticky = 5,
            AcceptDesignated = 6
        }

        /// <summary>
        /// BeforeMissileUpdate: NotReady skips without touching lastBeforeFrame.
        /// Caller must stamp lastBeforeFrame after SkipDuplicate check, before ClearOverride.
        /// </summary>
        internal static EarlyOut ResolveBeforeEarly(
            bool ready,
            bool missileNull,
            int lastBeforeFrame,
            int frameCount,
            bool enableMultiMode,
            bool gunOrMotorless)
        {
            if (!ready || missileNull)
                return EarlyOut.NotReady;
            if (lastBeforeFrame == frameCount)
                return EarlyOut.SkipDuplicateFrame;
            if (!enableMultiMode || gunOrMotorless)
                return EarlyOut.ClearOverride;
            return EarlyOut.Continue;
        }

        internal static EarlyOut ResolveReapplyEarly(
            bool enableMultiMode,
            bool gunOrMotorless,
            bool isCruise)
        {
            if (!enableMultiMode)
                return EarlyOut.ClearOverride;
            if (gunOrMotorless)
                return EarlyOut.ClearOverride;
            if (isCruise)
                return EarlyOut.StripIncompatible;
            return EarlyOut.Continue;
        }

        internal static ModeBranch ResolveModeBranch(
            bool shipLaunchNeedsCoast,
            bool stickyOrPlayerDesignated)
        {
            if (shipLaunchNeedsCoast)
                return ModeBranch.ShipCoast;
            if (stickyOrPlayerDesignated)
                return ModeBranch.Sticky;
            return ModeBranch.OpenHunt;
        }

        internal static bool ShipLaunchNeedsCoast(
            bool isShipLaunched,
            float ageSec,
            float speedMps,
            float coastAgeSec,
            float coastMinSpeedMps)
        {
            if (!isShipLaunched)
                return false;
            float ageGate = coastAgeSec > 0f ? coastAgeSec : 1.0f;
            float spdGate = coastMinSpeedMps > 0f ? coastMinSpeedMps : 80f;
            return ageSec < ageGate || speedMps < spdGate;
        }

        internal static StickyAction ResolveStickyBefore(
            bool ejectedPilot,
            bool targetNonNull,
            bool alive,
            bool confirmedFriendly)
        {
            if (ejectedPilot)
                return StickyAction.DropCoast;
            if (targetNonNull && alive && !confirmedFriendly)
                return StickyAction.GuideAlive;
            return StickyAction.DropCoast;
        }

        /// <summary>Reapply sticky: skip full GuideTo when BeforeMissileUpdate already synced this frame.</summary>
        internal static StickyAction ResolveStickyReapply(
            bool ejectedPilot,
            bool targetNonNull,
            bool alive,
            bool confirmedFriendly,
            bool aimAlreadySyncedThisFrame)
        {
            if (ejectedPilot)
                return StickyAction.DropCoast;
            if (!(targetNonNull && alive) || confirmedFriendly)
                return StickyAction.DropCoast;
            if (aimAlreadySyncedThisFrame)
                return StickyAction.SyncHotOnly;
            return StickyAction.GuideAlive;
        }

        internal static bool DesignatedCheckDue(float now, float nextDesignatedCheckAt)
        {
            return now >= nextDesignatedCheckAt;
        }

        internal static float ScheduleNextDesignatedCheck(float now)
        {
            return now + DesignatedCheckIntervalSec;
        }

        /// <summary>Open-hunt BeforeMissileUpdate after sticky/designated checks.</summary>
        internal static OpenHuntAction ResolveOpenBefore(
            bool designatedDue,
            bool designatedEngageable,
            bool targetBad,
            bool targetNull,
            bool allowFreeAttack,
            bool stickyOnly)
        {
            if (designatedDue && designatedEngageable)
                return OpenHuntAction.AcceptDesignated;
            // targetBad handled by facade DropTarget before this when needed
            if (targetNull && allowFreeAttack && !stickyOnly)
                return OpenHuntAction.TryFreeHunt;
            return OpenHuntAction.None;
        }

        /// <summary>Open-hunt ReapplyGuidanceAfterSeek after sticky branch.</summary>
        internal static OpenHuntAction ResolveOpenReapply(
            bool targetEngageableAlive,
            bool aimAlreadySyncedThisFrame,
            bool allowFreeAttack,
            bool stickyOnly,
            bool huntDue)
        {
            if (targetEngageableAlive)
            {
                return aimAlreadySyncedThisFrame
                    ? OpenHuntAction.SyncHotExisting
                    : OpenHuntAction.GuideExisting;
            }
            if (allowFreeAttack && !stickyOnly)
                return huntDue ? OpenHuntAction.TryFreeHunt : OpenHuntAction.ReleaseVanilla;
            if (stickyOnly)
                return OpenHuntAction.CoastSticky;
            return OpenHuntAction.ReleaseVanilla;
        }

        /// <summary>Setup() initial lock branch after soft-launch coast.</summary>
        internal enum SetupLockAction
        {
            ShipCoastHold = 0,
            StickyGuide = 1,
            FreeHuntLoal = 2,
            None = 3
        }

        internal static SetupLockAction ResolveSetupLock(
            bool shipLaunchNeedsCoast,
            bool targetEngageable,
            bool allowFreeAttack,
            bool stickyOnly)
        {
            if (shipLaunchNeedsCoast)
                return SetupLockAction.ShipCoastHold;
            if (targetEngageable)
                return SetupLockAction.StickyGuide;
            if (allowFreeAttack && !stickyOnly)
                return SetupLockAction.FreeHuntLoal;
            return SetupLockAction.None;
        }
    }
}
