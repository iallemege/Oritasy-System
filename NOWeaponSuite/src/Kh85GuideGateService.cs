namespace WeXon
{
    /// <summary>
    /// Greenfield Kh85 GuideTo defer gate (0.0.9.89).
    /// Plugin resolves Kh85Kind / unstamped launch; MultiModeBrain applies GuidePath.
    /// Kind values match Plugin.Kh85Kind: Unresolved=0, NotKh85=1, MultiMode=2, TerrainAim=3.
    /// </summary>
    internal static class Kh85GuideGateService
    {
        internal const byte KindUnresolved = 0;
        internal const byte KindNotKh85 = 1;
        internal const byte KindMultiMode = 2;
        internal const byte KindTerrainAim = 3;

        internal const float MultiModeLaunchDeferSec = 0.35f;
        internal const float LoalHuntDelaySec = 1.6f;
        internal const float SoftEnergySpeedFactor = 0.85f;

        /// <summary>TGM-85 LOAL: do not grid-hunt until this age. Player/sticky locks skip.</summary>
        internal static bool LoalHuntDelayActive(bool isKh85Family, bool playerOrSticky, float ageSec)
        {
            if (!isKh85Family || playerOrSticky)
                return false;
            return ageSec < LoalHuntDelaySec;
        }

        /// <summary>True when MultiMode must not GuideTo yet (Kh85MT owns aim / stamp pending).</summary>
        internal static bool ShouldDeferGuideTo(
            bool missileNull,
            byte kind,
            bool likelyUnstampedKh85,
            float timeSinceSpawn,
            float speedMps,
            float minGuideSpeedMps)
        {
            if (missileNull)
                return false;
            if (kind == KindTerrainAim || kind == KindUnresolved || kind == KindMultiMode)
                return true;
            if (kind == KindNotKh85)
                return likelyUnstampedKh85;
            return false;
        }

        /// <summary>Ship soft-launch: leave vanilla guidance until the motor builds speed.</summary>
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

        /// <summary>Match "_A", "_A_…", "_Ax2" — not "_single" as S.</summary>
        internal static bool IsLetterToken(string rest, string letter)
        {
            if (string.IsNullOrEmpty(rest) || string.IsNullOrEmpty(letter))
                return false;
            string p = "_" + letter;
            if (!rest.StartsWith(p, System.StringComparison.OrdinalIgnoreCase))
                return false;
            if (rest.Length == p.Length)
                return true;
            char next = rest[p.Length];
            return next == '_' || next == 'x' || next == 'X' || (next >= '0' && next <= '9');
        }

        /// <summary>A/B/D → MultiMode; C/E/S → TerrainAim; else Unresolved (unknown letter).</summary>
        internal static byte KindFromLetter(string letter)
        {
            if (string.IsNullOrEmpty(letter))
                return KindUnresolved;
            if (string.Equals(letter, "A", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(letter, "B", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(letter, "D", System.StringComparison.OrdinalIgnoreCase))
                return KindMultiMode;
            if (string.Equals(letter, "C", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(letter, "E", System.StringComparison.OrdinalIgnoreCase)
                || string.Equals(letter, "S", System.StringComparison.OrdinalIgnoreCase))
                return KindTerrainAim;
            return KindUnresolved;
        }

        /// <summary>
        /// Kh85MT jsonKey after stripping _IAL. Returns NotKh85 if not Kh85MT.
        /// Unlettered / C-family keys → TerrainAim.
        /// </summary>
        internal static byte KindFromJsonKey(string jsonKey, out bool sawKh85)
        {
            sawKh85 = false;
            if (string.IsNullOrEmpty(jsonKey)
                || !jsonKey.StartsWith("Kh85MT", System.StringComparison.OrdinalIgnoreCase))
                return KindNotKh85;
            sawKh85 = true;
            string core = jsonKey;
            if (core.EndsWith("_IAL", System.StringComparison.OrdinalIgnoreCase))
                core = core.Substring(0, core.Length - 4);
            string rest = core.Length > 6 ? core.Substring(6) : "";
            if (IsLetterToken(rest, "E") || IsLetterToken(rest, "S"))
                return KindTerrainAim;
            if (IsLetterToken(rest, "A") || IsLetterToken(rest, "B") || IsLetterToken(rest, "D"))
                return KindMultiMode;
            return KindTerrainAim;
        }

        /// <summary>TGM-85 / brand display names. Returns NotKh85 if no match; may set sawKh85 on bare TGM-85.</summary>
        internal static byte KindFromDisplayName(string name, out bool sawKh85)
        {
            sawKh85 = false;
            if (string.IsNullOrEmpty(name))
                return KindNotKh85;
            if (name.IndexOf("TGM-85C", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("TGM-85E", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("TGM-85S", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Shardfall", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Torjan", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Seaker", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return KindTerrainAim;
            if (name.IndexOf("TGM-85A", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("TGM-85B", System.StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("TGM-85D", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return KindMultiMode;
            if (name.IndexOf("TGM-85", System.StringComparison.OrdinalIgnoreCase) >= 0)
                sawKh85 = true;
            return KindNotKh85;
        }

        internal static bool WeaponBlobLooksKh85(string weaponNameBlob)
        {
            if (string.IsNullOrEmpty(weaponNameBlob))
                return false;
            return weaponNameBlob.IndexOf("TGM-85", System.StringComparison.OrdinalIgnoreCase) >= 0
                || weaponNameBlob.IndexOf("Kh85MT", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Known Kh85 but letter not stamped yet. Do not cache MultiMode — that made
        /// default C (sea-skim) take MM GuideTo and dive into terrain.
        /// </summary>
        internal static byte FinalizeKind(bool sawKh85)
        {
            return sawKh85 ? KindUnresolved : KindNotKh85;
        }

        /// <summary>
        /// 175C: never skip cruise Seek. C/E/S skip PreTerminalMode Detonate only.
        /// </summary>
        internal static bool ShouldSkipCruisePreTerminal(byte kind, bool likelyUnstamped)
        {
            return kind == KindTerrainAim
                || kind == KindUnresolved
                || likelyUnstamped;
        }
    }
}
