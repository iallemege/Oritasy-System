using System;

namespace WeXon
{
    /// <summary>
    /// AAM-2CV key / AAM-36 donor classify + Mach 8 / 150 km kinematics (pure).
    /// Aam2CvWeapon owns Instantiates.
    /// </summary>
    internal static class Aam2CvGateService
    {
        internal const string PackKey = "AAM_2CV";
        internal const string NukePackKey = "AAM_2CV_5kt";
        internal const float SpeedMach = 8f;
        internal const float RangeM = 150000f;
        internal const float MachToMs = 340.3f;
        internal const float GLimit = 75f;

        internal static float SpeedMs()
        {
            return SpeedMach * MachToMs;
        }

        internal static float MinBurnSec()
        {
            float spd = SpeedMs();
            if (spd < 1f)
                return 60f;
            return RangeM / spd;
        }

        /// <summary>
        /// Thrust scale so drag-limited speed can hold Mach 8 after fin-area doubling.
        /// currentTopMs is Missile.GetTopSpeed after maneuver stamp.
        /// </summary>
        internal static float ThrustMulFromCurrentTop(float currentTopMs)
        {
            float want = SpeedMs();
            if (currentTopMs < 50f)
                return 8f;
            if (currentTopMs >= want)
                return 1.25f;
            float ratio = want / currentTopMs;
            float mul = ratio * ratio * 1.35f;
            if (mul < 1.25f)
                mul = 1.25f;
            if (mul > 24f)
                mul = 24f;
            return mul;
        }

        internal static bool IsEightRoundMount(string jsonKey, int ammo)
        {
            if (ammo == 8)
                return true;
            if (string.IsNullOrEmpty(jsonKey))
                return false;
            string s = jsonKey.ToLowerInvariant();
            return s.IndexOf("x8", StringComparison.Ordinal) >= 0;
        }

        internal static bool IsAam2CvKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.StartsWith(PackKey, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(NukePackKey, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsAam2CvNukeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.StartsWith(NukePackKey, StringComparison.OrdinalIgnoreCase)
                || (IsAam2CvKey(key) && key.IndexOf("5kt", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        internal static bool IsAam36Donor(string jsonKey, string shortName, string weaponName)
        {
            if (!string.IsNullOrEmpty(jsonKey)
                && jsonKey.StartsWith("AAM4", StringComparison.OrdinalIgnoreCase)
                && !IsAam2CvKey(jsonKey))
                return true;
            string blob = ((shortName != null ? shortName : string.Empty) + " "
                + (weaponName != null ? weaponName : string.Empty));
            if (blob.IndexOf("AAM-36", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (blob.IndexOf("Scimitar", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool IsAam36Definition(string jsonKey, string unitName, string name)
        {
            if (!string.IsNullOrEmpty(jsonKey)
                && jsonKey.StartsWith("AAM4", StringComparison.OrdinalIgnoreCase)
                && !IsAam2CvKey(jsonKey))
                return true;
            string blob = ((unitName != null ? unitName : string.Empty) + " "
                + (name != null ? name : string.Empty) + " "
                + (jsonKey != null ? jsonKey : string.Empty));
            return blob.IndexOf("AAM-36", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("Scimitar", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static string RemapAam4Key(string aamKey, bool nuke)
        {
            string pack = nuke ? NukePackKey : PackKey;
            if (string.IsNullOrEmpty(aamKey))
                return pack + "_single";
            if (aamKey.StartsWith("AAM4", StringComparison.OrdinalIgnoreCase))
                return pack + aamKey.Substring(4);
            return pack + "_single";
        }

        internal static string FormatMountDisplayName(string weaponName, int ammo)
        {
            if (ammo > 1)
                return weaponName + " x" + ammo;
            return weaponName;
        }

        internal static void PreferredCloneKeys(bool nuke, out string primary, out string secondary)
        {
            string pack = nuke ? NukePackKey : PackKey;
            primary = pack + "_single";
            secondary = pack + "_double";
        }
    }
}
