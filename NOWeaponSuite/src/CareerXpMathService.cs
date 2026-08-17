using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield career XP / level / settle / prestige math (0.0.9.62→88).
    /// PlayerCareer owns persistence, session, and GUI.
    /// </summary>
    internal static class CareerXpMathService
    {
        internal const int MaxLevel = 1000;
        internal const int XpPerHit = 1;
        internal const int HitXpCapPerAircraft = 25;
        internal const int XpPerModule = 25;
        internal const int XpPerMissile = 10;
        internal const int XpPerGround = 4;
        internal const int XpPerNavy = 100;
        internal const int XpPerCarrier = 500;
        internal const int XpPerKill = 25;
        internal const int XpPerLevelMin = 10000;
        internal const int FlightXpCap = 1000;
        internal const float FlightXpCapSeconds = 3f * 3600f;
        internal const float HardXpMulCap = 3.8f;

        /// <summary>Match flight XP: linear 0→1000 over 3 hours, hard cap 1000.</summary>
        internal static int CalcFlightXp(float durationSec)
        {
            if (durationSec < 20f)
                return 0;
            float t = Mathf.Clamp01(durationSec / FlightXpCapSeconds);
            return Mathf.FloorToInt(t * FlightXpCap);
        }

        internal static float ClampMatchXpMul(float mul)
        {
            if (mul < 1f)
                return 1f;
            if (mul > HardXpMulCap)
                return HardXpMulCap;
            return mul;
        }

        internal static int MatchKillXp(int pvpKills, int aiKills)
        {
            return (pvpKills + aiKills) * XpPerKill;
        }

        internal static int XpCostToAdvance(int fromLevel)
        {
            int n = Mathf.Max(1, fromLevel);
            if (n > MaxLevel)
                n = MaxLevel;
            return XpPerLevelMin * n;
        }

        internal static int GetLevelFromXp(int xp)
        {
            if (xp <= 0)
                return 1;
            float disc = 1f + xp / 1250f;
            int advanced = Mathf.FloorToInt((-1f + Mathf.Sqrt(Mathf.Max(0f, disc))) / 2f);
            return Mathf.Clamp(advanced + 1, 1, MaxLevel);
        }

        internal static int XpForLevel(int level)
        {
            int L = Mathf.Clamp(level, 1, MaxLevel);
            long n = L - 1;
            long cum = (long)XpPerLevelMin * n * (n + 1) / 2L;
            if (cum > int.MaxValue)
                return int.MaxValue;
            return (int)cum;
        }

        internal static int XpToNext(int xp)
        {
            int lvl = Mathf.Clamp(GetLevelFromXp(xp), 1, MaxLevel);
            if (lvl >= MaxLevel)
                return 0;
            return XpForLevel(lvl + 1) - xp;
        }

        /// <summary>True when weapon label looks like gun/cannon (not missile).</summary>
        internal static bool IsGunWeaponLabel(string weapon)
        {
            if (string.IsNullOrEmpty(weapon))
                return false;
            return weapon.IndexOf("Gun", System.StringComparison.OrdinalIgnoreCase) >= 0
                || weapon.IndexOf("Cannon", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string CleanWeaponName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return "Missile";
            n = n.Replace("[IAL]", string.Empty)
                .Replace("[10kt]", string.Empty)
                .Replace("[1.5kt]", string.Empty)
                .Trim();
            if (n.Length > 42)
                n = n.Substring(0, 42);
            return n;
        }

        /// <summary>
        /// Match settle: target = round(baseXp * mul); grant = target − already-granted kill XP.
        /// </summary>
        internal static void SettleMatchGrant(int baseXp, float mul, int alreadyGrantedKillXp,
            out int targetXp, out int grantXp)
        {
            mul = ClampMatchXpMul(mul);
            if (baseXp < 0)
                baseXp = 0;
            targetXp = Mathf.Max(0, Mathf.RoundToInt(baseXp * mul));
            grantXp = targetXp - Mathf.Max(0, alreadyGrantedKillXp);
            if (grantXp < 0)
                grantXp = 0;
        }

        /// <summary>
        /// One prestige overflow pass when XP sits at/above max-level threshold.
        /// Returns updated xp/prestige; at most one loop (matches PlayerCareer.AddXp).
        /// </summary>
        internal static void ApplyPrestigeOverflow(int xp, int prestige, out int newXp, out int newPrestige)
        {
            newXp = xp;
            newPrestige = prestige;
            int maxXp = XpForLevel(MaxLevel);
            if (GetLevelFromXp(newXp) < MaxLevel || newXp < maxXp)
                return;
            newPrestige++;
            newXp = newXp - maxXp;
            if (newXp < 0)
                newXp = 0;
            if (newPrestige > 999)
                newPrestige = 999;
        }

        /// <summary>Level progress bar 0..1 within current level band.</summary>
        internal static float LevelBarPct(int xp, int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            if (level >= MaxLevel)
                return 1f;
            int curBase = XpForLevel(level);
            int next = XpForLevel(level + 1);
            if (next <= curBase)
                return 1f;
            return Mathf.Clamp01((xp - curBase) / (float)(next - curBase));
        }
    }
}
