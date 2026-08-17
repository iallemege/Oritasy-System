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
            Kh85Terrain = 1,
            Sticky = 2,
            OpenHunt = 3
        }

        /// <summary>Sticky / designated lock actions (live Unit or coast last aim).</summary>
        internal enum StickyAction
        {
            DropCoast = 0,
            GuideAlive = 1,
            SyncHotOnly = 2
        }

        /// <summary>Kh85C/E/S sticky path (Kh85MT owns aim — sync only, never GuideTo).</summary>
        internal enum Kh85StickyAction
        {
            DropCoast = 0,
            SyncAlive = 1
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
            bool isKh85CTerrain,
            bool stickyOrPlayerDesignated)
        {
            if (shipLaunchNeedsCoast)
                return ModeBranch.ShipCoast;
            if (isKh85CTerrain)
                return ModeBranch.Kh85Terrain;
            if (stickyOrPlayerDesignated)
                return ModeBranch.Sticky;
            return ModeBranch.OpenHunt;
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

        internal static Kh85StickyAction ResolveKh85Sticky(
            bool ejectedPilot,
            bool targetNonNull,
            bool alive,
            bool confirmedFriendly)
        {
            if (ejectedPilot)
                return Kh85StickyAction.DropCoast;
            if (targetNonNull && alive && !confirmedFriendly)
                return Kh85StickyAction.SyncAlive;
            return Kh85StickyAction.DropCoast;
        }

        /// <summary>Kh85 reapply sticky uses null/dead/friendly as DropCoast (same as SyncAlive inverse).</summary>
        internal static Kh85StickyAction ResolveKh85StickyReapply(
            bool ejectedPilot,
            bool targetNonNull,
            bool alive,
            bool confirmedFriendly)
        {
            if (ejectedPilot)
                return Kh85StickyAction.DropCoast;
            if (!targetNonNull || !alive || confirmedFriendly)
                return Kh85StickyAction.DropCoast;
            return Kh85StickyAction.SyncAlive;
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

        /// <summary>Kh85 terrain free path (non-sticky) on Before / Reapply.</summary>
        internal static OpenHuntAction ResolveKh85Free(
            bool allowFreeAttack,
            bool stickyOnly,
            bool playerDesignated,
            bool targetEngageableAlive,
            bool huntDue)
        {
            if (stickyOnly || playerDesignated || !allowFreeAttack)
                return OpenHuntAction.None;
            if (targetEngageableAlive)
                return OpenHuntAction.SyncHotExisting;
            if (huntDue)
                return OpenHuntAction.TryFreeHunt;
            return OpenHuntAction.None;
        }

        /// <summary>Setup() initial lock branch after soft-launch coast.</summary>
        internal enum SetupLockAction
        {
            ShipCoastHold = 0,
            StickyGuide = 1,
            StickyDeferKh85 = 2,
            FreeHuntLoal = 3,
            None = 4
        }

        internal static SetupLockAction ResolveSetupLock(
            bool shipLaunchNeedsCoast,
            bool targetEngageable,
            bool deferKh85GuideTo,
            bool allowFreeAttack,
            bool stickyOnly)
        {
            if (shipLaunchNeedsCoast)
                return SetupLockAction.ShipCoastHold;
            if (targetEngageable)
                return deferKh85GuideTo ? SetupLockAction.StickyDeferKh85 : SetupLockAction.StickyGuide;
            if (allowFreeAttack && !stickyOnly)
                return SetupLockAction.FreeHuntLoal;
            return SetupLockAction.None;
        }

        internal static bool ShouldClearGuidanceOverrideOnKh85Setup(bool deferKh85, bool isKh85CTerrain)
        {
            return deferKh85 || isKh85CTerrain;
        }
    }
}
