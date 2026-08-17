using System;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield loadout mount / naval / IAL key gates (0.0.9.89).
    /// Plugin + WeaponChecker Harmony prefixes own Unity mutations.
    /// </summary>
    internal static class LoadoutMountGateService
    {
        internal enum HardpointPath
        {
            RunVanilla = 0,
            ForceDeny = 1,
            ForceAllow = 2
        }

        internal enum NuclearPath
        {
            RunVanilla = 0,
            ForceAllow = 1
        }

        internal enum UnrestrictedBypassPath
        {
            RunVanilla = 0,
            ForceAllow = 1
        }

        internal static bool IsIalKey(string key)
        {
            return !string.IsNullOrEmpty(key) && key.EndsWith("_IAL", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Name-based naval cell / ship launcher hardpoint (not aircraft pylons).</summary>
        internal static bool IsNavalHardpointName(string hardpointName)
        {
            if (string.IsNullOrEmpty(hardpointName))
                return false;
            string n = hardpointName;
            if (n.IndexOf("VLS", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Cell", StringComparison.OrdinalIgnoreCase) >= 0
                && (n.IndexOf("Launch", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("VLS", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Mk", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }

        internal static bool RollAiNukePreference(float chance01, float random01)
        {
            float chance = Mathf.Clamp01(chance01);
            return random01 < chance;
        }

        internal static bool AllowPlayerUnrestricted(bool enableUnrestricted, int playerUnrestrictedDepth)
        {
            return enableUnrestricted && playerUnrestrictedDepth > 0;
        }

        internal static HardpointPath ResolveHardpointPrefix(
            bool blockIalOnShips,
            bool isIalMount,
            bool isNavalHardpoint,
            bool isAgmTMount,
            bool allowPlayerUnrestricted)
        {
            return ResolveHardpointPrefix(
                blockIalOnShips, isIalMount, isNavalHardpoint, isAgmTMount,
                false, false, allowPlayerUnrestricted);
        }

        internal static HardpointPath ResolveHardpointPrefix(
            bool blockIalOnShips,
            bool isIalMount,
            bool isNavalHardpoint,
            bool isAgmTMount,
            bool isAam2CvMount,
            bool allowPlayerUnrestricted)
        {
            return ResolveHardpointPrefix(
                blockIalOnShips, isIalMount, isNavalHardpoint, isAgmTMount,
                isAam2CvMount, false, allowPlayerUnrestricted);
        }

        internal static HardpointPath ResolveHardpointPrefix(
            bool blockIalOnShips,
            bool isIalMount,
            bool isNavalHardpoint,
            bool isAgmTMount,
            bool isAam2CvMount,
            bool isKh85Mount,
            bool allowPlayerUnrestricted)
        {
            if (isAam2CvMount && isNavalHardpoint)
                return HardpointPath.ForceDeny;
            if (blockIalOnShips && isIalMount && isNavalHardpoint)
                return HardpointPath.ForceDeny;
            if ((isAgmTMount || isAam2CvMount || isKh85Mount) && !isNavalHardpoint)
                return HardpointPath.ForceAllow;
            if (!allowPlayerUnrestricted)
                return HardpointPath.RunVanilla;
            return HardpointPath.ForceAllow;
        }

        internal static NuclearPath ResolveNuclearPrefix(
            bool ialExemptFromWarheadQuota,
            bool playerNonNull,
            bool enableUnrestricted,
            bool allowPlayerUnrestricted,
            bool isLocalHumanPlayer)
        {
            if (ialExemptFromWarheadQuota)
                return NuclearPath.ForceAllow;
            if (playerNonNull && enableUnrestricted
                && (allowPlayerUnrestricted || isLocalHumanPlayer))
                return NuclearPath.ForceAllow;
            return NuclearPath.RunVanilla;
        }

        internal static UnrestrictedBypassPath ResolveUnrestrictedBypass(bool allowPlayerUnrestricted)
        {
            return allowPlayerUnrestricted
                ? UnrestrictedBypassPath.ForceAllow
                : UnrestrictedBypassPath.RunVanilla;
        }
    }
}
