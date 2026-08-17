using System;

namespace WeXon
{
    /// <summary>
    /// Auto-hunt must not lock camouflage nets / civilian decoy buildings.
    /// Plugin supplies BuildingType + names; this service only classifies.
    /// BuildingType.CIV = 0 in Nuclear Option.
    /// </summary>
    internal static class HuntJunkGateService
    {
        internal const int BuildingTypeCiv = 0;
        internal const float WorthlessBuildingValueM = 0.2f;

        internal static bool IsCivilianBuildingType(int buildingType)
        {
            return buildingType == BuildingTypeCiv;
        }

        internal static bool IsJunkHuntName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            if (s.IndexOf("camo", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("camouflage", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("camonet", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("netting", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("tarp", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("tent", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("dummy", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("gabion", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("\u4f2a\u88c5", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("camo net", StringComparison.Ordinal) >= 0)
                return true;
            if (LooksLikeBareNet(s))
                return true;
            return false;
        }

        /// <summary>"Camo Net" / "Net_01" — not "network" / "internet".</summary>
        internal static bool LooksLikeBareNet(string lower)
        {
            if (string.IsNullOrEmpty(lower))
                return false;
            if (lower.IndexOf("network", StringComparison.Ordinal) >= 0)
                return false;
            if (lower == "net")
                return true;
            if (lower.StartsWith("net ", StringComparison.Ordinal)
                || lower.StartsWith("net_", StringComparison.Ordinal)
                || lower.StartsWith("net-", StringComparison.Ordinal))
                return true;
            if (lower.EndsWith(" net", StringComparison.Ordinal)
                || lower.EndsWith("_net", StringComparison.Ordinal)
                || lower.EndsWith("-net", StringComparison.Ordinal))
                return true;
            if (lower.IndexOf(" net ", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// True = skip this building in free-hunt. Military types (FAC/RDR/HGR/DEF/AMMO/DEP)
        /// stay eligible even at low value.
        /// </summary>
        internal static bool IsJunkHuntBuilding(int buildingType, float valueM, string nameBlob)
        {
            if (IsCivilianBuildingType(buildingType))
                return true;
            if (IsJunkHuntName(nameBlob))
                return true;
            if (buildingType < 0 && valueM >= 0f && valueM < WorthlessBuildingValueM)
                return true;
            return false;
        }
    }
}
