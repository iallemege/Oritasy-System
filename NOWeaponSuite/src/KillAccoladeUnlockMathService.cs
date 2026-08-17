namespace WeXon
{
    /// <summary>
    /// Greenfield kill-accolade arsenal unlock FSM (0.0.9.88).
    /// KillAccolades owns enqueue / PlayerPrefs / UI.
    /// </summary>
    internal static class KillAccoladeUnlockMathService
    {
        internal const string UnlockAdvanced = "advanced";
        internal const string UnlockCarrier = "carrier";
        internal const string UnlockStrategic = "strategic";

        [System.Flags]
        internal enum UnlockFlags
        {
            None = 0,
            Advanced = 1,
            Carrier = 2,
            Strategic = 4
        }

        internal static UnlockFlags FromSkillKind(KillCombatMathService.SkillKillKind kind)
        {
            if (kind == KillCombatMathService.SkillKillKind.GodSlayer)
                return UnlockFlags.Carrier | UnlockFlags.Advanced;
            if (kind == KillCombatMathService.SkillKillKind.StrongFoe)
                return UnlockFlags.Advanced;
            return UnlockFlags.None;
        }

        internal static UnlockFlags FromStreak(int streak)
        {
            if (streak == 3)
                return UnlockFlags.Advanced;
            if (streak >= 5)
                return UnlockFlags.Carrier | UnlockFlags.Advanced;
            return UnlockFlags.None;
        }

        internal static UnlockFlags FromMissionKills(int missionAirKills)
        {
            if (missionAirKills == 5)
                return UnlockFlags.Advanced;
            if (missionAirKills == 10)
                return UnlockFlags.Carrier | UnlockFlags.Strategic;
            return UnlockFlags.None;
        }

        /// <summary>Combo when both carrier+advanced already held and kills ≥ 7.</summary>
        internal static UnlockFlags FromCombo(bool hasCarrier, bool hasAdvanced, int missionAirKills)
        {
            if (hasCarrier && hasAdvanced && missionAirKills >= 7)
                return UnlockFlags.Strategic;
            return UnlockFlags.None;
        }

        internal static void AppendKeys(UnlockFlags flags, System.Collections.Generic.List<string> dest)
        {
            if (dest == null || flags == UnlockFlags.None)
                return;
            if ((flags & UnlockFlags.Advanced) != 0)
                dest.Add(UnlockAdvanced);
            if ((flags & UnlockFlags.Carrier) != 0)
                dest.Add(UnlockCarrier);
            if ((flags & UnlockFlags.Strategic) != 0)
                dest.Add(UnlockStrategic);
        }
    }
}
