using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield kill combat math (0.0.9.62): streak, skill-gap class, countable victims.
    /// KillAccolades / MatchScoreboard own UI and persistence.
    /// </summary>
    internal static class KillCombatMathService
    {
        internal const float StreakWindow = 14f;
        internal const float DedupSeconds = 2.5f;
        internal const float DefaultStrongGap = 0.15f;
        internal const float DefaultGodGap = 0.35f;
        internal const float EasyGap = -0.25f;

        internal enum SkillKillKind
        {
            Destroyed = 0,
            Kill = 1,
            EasyKill = 2,
            StrongFoe = 3,
            GodSlayer = 4
        }

        internal static bool IsCountableVictim(Unit u)
        {
            if (u == null || u is Missile)
                return false;
            return u is Aircraft || u is Ship || u is GroundVehicle || u is Building;
        }

        /// <summary>Advances streak if within window; returns new streak count and writes until.</summary>
        internal static int AdvanceStreak(int streak, float now, ref float streakUntil)
        {
            if (now <= streakUntil)
                streak++;
            else
                streak = 1;
            streakUntil = now + StreakWindow;
            return streak;
        }

        internal static SkillKillKind ClassifySkillGap(bool airVictim, float gap, float strongGap, float godGap)
        {
            if (!airVictim)
                return SkillKillKind.Destroyed;
            if (gap >= godGap)
                return SkillKillKind.GodSlayer;
            if (gap >= strongGap)
                return SkillKillKind.StrongFoe;
            if (gap <= EasyGap)
                return SkillKillKind.EasyKill;
            return SkillKillKind.Kill;
        }

        /// <summary>Scoreboard badge code for streak milestones (null if none).</summary>
        internal static string StreakBadgeCode(int streak)
        {
            if (streak == 2) return "DK";
            if (streak == 3) return "TK";
            if (streak == 4) return "QK";
            if (streak == 5) return "AC";
            return null;
        }

        /// <summary>Scoreboard badge code for mission kill totals (null if none).</summary>
        internal static string MissionKillBadgeCode(int missionKills)
        {
            if (missionKills == 5) return "AP";
            if (missionKills == 10) return "BD";
            return null;
        }

        internal static string SkillGapBadgeCode(SkillKillKind kind)
        {
            if (kind == SkillKillKind.GodSlayer) return "GS";
            if (kind == SkillKillKind.StrongFoe) return "SF";
            return null;
        }

        internal static Color FlashColor(SkillKillKind kind)
        {
            switch (kind)
            {
                case SkillKillKind.GodSlayer: return new Color(1f, 0.35f, 0.2f, 1f);
                case SkillKillKind.StrongFoe: return new Color(1f, 0.72f, 0.25f, 1f);
                case SkillKillKind.EasyKill: return new Color(0.7f, 0.85f, 1f, 1f);
                case SkillKillKind.Destroyed: return new Color(0.85f, 0.95f, 0.65f, 1f);
                default: return new Color(0.95f, 0.95f, 0.55f, 1f);
            }
        }
    }
}
