using System;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield AGM-T / ACM-119 key + nuclear arm/prox gates (0.0.9.67).
    /// AgmTWeapon owns injection, Harmony, and spawn orchestration.
    /// </summary>
    internal static class AgmTGateMathService
    {
        internal const float NukeProximityFloorM = 50f;
        /// <summary>GS25: short delay so impact fuse can fire (bus keeps 6s / 2km).</summary>
        internal const float SubNukeArmMinFlightSec = 0.35f;
        internal const float SubNukeArmMinDistanceM = 0f;

        internal static bool IsAgmTKey(
            string key,
            string packKey,
            string nukePackKey,
            string legacyPackKey,
            string legacyPackKey119)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.StartsWith(packKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(nukePackKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(legacyPackKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(legacyPackKey119, StringComparison.OrdinalIgnoreCase)
                || key.IndexOf("ACM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("AGM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("AGM_T", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAgmTNukeKey(
            string key,
            string nukePackKey,
            string nukeKeySuffix,
            string packKey,
            string legacyPackKey,
            string legacyPackKey119)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (key.StartsWith(nukePackKey, StringComparison.OrdinalIgnoreCase)
                || key.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0
                || key.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return IsAgmTKey(key, packKey, nukePackKey, legacyPackKey, legacyPackKey119)
                && key.IndexOf(nukeKeySuffix, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string RemapAamKey(string aamKey, bool nuke, string packKey, string nukePackKey)
        {
            string pack = nuke ? nukePackKey : packKey;
            if (string.IsNullOrEmpty(aamKey))
                return pack + "_single";
            if (aamKey.StartsWith("AAM2", StringComparison.OrdinalIgnoreCase))
                return pack + aamKey.Substring(4);
            return pack + "_" + aamKey;
        }

        internal static bool MeetsValueGate(float unitValueMillions, float minValueM, bool nukeVariant)
        {
            if (!nukeVariant)
                return true;
            return unitValueMillions + 0.001f >= minValueM;
        }

        internal static bool MeetsArmConditions(
            float ageSeconds,
            float minFlightTime,
            float distanceFromSpawn,
            float minDistance,
            bool haveSpawn)
        {
            if (ageSeconds < minFlightTime)
                return false;
            if (haveSpawn && distanceFromSpawn < minDistance)
                return false;
            return true;
        }

        internal static float ClampNukeProximityM(float configured)
        {
            return configured < NukeProximityFloorM ? NukeProximityFloorM : configured;
        }

        internal static bool WithinNukeProximity(float distanceSqr, float proximityM)
        {
            float prox = ClampNukeProximityM(proximityM);
            return distanceSqr <= prox * prox;
        }
    }
}
