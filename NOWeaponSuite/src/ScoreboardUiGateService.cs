namespace WeXon
{
    /// <summary>
    /// Greenfield Tab-scoreboard visibility + kill attribution gates (0.0.9.92).
    /// MatchScoreboard owns rows, badges UI, and kill-feed.
    /// </summary>
    internal static class ScoreboardUiGateService
    {
        internal enum Visibility
        {
            Hidden = 0,
            HoldKey = 1,
            ToggleOpen = 2
        }

        internal enum KillPath
        {
            Skip = 0,
            Friendly = 1,
            Enemy = 2
        }

        internal enum TickToggle
        {
            None = 0,
            FlipToggle = 1,
            CloseEscape = 2
        }

        internal static bool IsDedupBlocked(float now, float until)
        {
            return now < until;
        }

        internal static float ScheduleDedupUntil(float now, float dedupSeconds)
        {
            float d = dedupSeconds > 0f ? dedupSeconds : KillCombatMathService.DedupSeconds;
            return now + d;
        }

        internal static Visibility ResolveVisibility(bool enabled, bool inMission, bool holdToShow, bool keyHeld, bool toggleOpen)
        {
            if (!enabled || !inMission)
                return Visibility.Hidden;
            if (holdToShow)
                return keyHeld ? Visibility.HoldKey : Visibility.Hidden;
            return toggleOpen ? Visibility.ToggleOpen : Visibility.Hidden;
        }

        internal static bool IsVisible(Visibility v)
        {
            return v == Visibility.HoldKey || v == Visibility.ToggleOpen;
        }

        internal static TickToggle ResolveTickToggle(
            bool enabled,
            bool inMission,
            bool holdToShow,
            bool keyDown,
            bool escapeDown,
            bool toggleOpen)
        {
            if (!enabled || !inMission)
                return TickToggle.None;
            if (!holdToShow && keyDown)
                return TickToggle.FlipToggle;
            if (escapeDown && toggleOpen)
                return TickToggle.CloseEscape;
            return TickToggle.None;
        }

        internal static KillPath ResolveKillPath(
            bool enabled,
            bool killerNull,
            bool victimNull,
            bool countableVictim,
            bool dedupBlocked,
            bool friendly)
        {
            if (!enabled || killerNull || victimNull || !countableVictim || dedupBlocked)
                return KillPath.Skip;
            return friendly ? KillPath.Friendly : KillPath.Enemy;
        }

        internal static bool ShouldCountDeathOnly(
            bool killerPlayerNull,
            bool victimAircraftNonNull,
            bool victimPlayerNonNull,
            bool dedupBlocked)
        {
            return killerPlayerNull && victimAircraftNonNull && victimPlayerNonNull && !dedupBlocked;
        }
    }
}
