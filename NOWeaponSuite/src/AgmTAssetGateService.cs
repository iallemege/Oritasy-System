using System;

namespace WeXon
{
    /// <summary>
    /// Greenfield ACM-119 / ACNM-118 donor classify + mount identity + NoteFire gates (0.0.9.93).
    /// AgmTWeapon owns Instantiate / encyclopedia list mutation.
    /// </summary>
    internal static class AgmTAssetGateService
    {
        internal const float DualRoleAntiSurface = 0.75f;
        internal const float DualRoleAntiAir = 0.75f;
        internal const float EncRoleAntiSurface = 1f;
        internal const float EncRoleAntiAir = 1f;
        internal const float TargetMinRangeM = 500f;
        internal const float TargetMaxRangeM = 35000f;
        internal const float TargetMaxSpeed = 100000f;
        internal const float TargetMinAltM = 0f;
        internal const float TargetMaxAltM = 100000f;

        internal enum IdentityPath
        {
            Skip = 0,
            Apply = 1
        }

        internal enum SyncPath
        {
            Skip = 0,
            SyncInfo = 1
        }

        internal enum NoteFirePath
        {
            Skip = 0,
            ArmPending = 1
        }

        internal static bool IsAam29Donor(string jsonKey, string shortName, string weaponName)
        {
            string key = jsonKey != null ? jsonKey : string.Empty;
            if (key.StartsWith("AAM2", StringComparison.OrdinalIgnoreCase))
                return true;
            string sn = shortName != null ? shortName : string.Empty;
            string wn = weaponName != null ? weaponName : string.Empty;
            return sn.IndexOf("AAM-29", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("AAM-29", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("Scythe", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsGs25InfoName(string shortName, string weaponName, string assetName)
        {
            string sn = shortName != null ? shortName : string.Empty;
            string wn = weaponName != null ? weaponName : string.Empty;
            string nm = assetName != null ? assetName : string.Empty;
            return string.Equals(sn, "GS25", StringComparison.OrdinalIgnoreCase)
                || string.Equals(wn, "GS25", StringComparison.OrdinalIgnoreCase)
                || nm.IndexOf("Submunition1", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAam2DefinitionKey(string jsonKey)
        {
            return string.Equals(jsonKey, "AAM2", StringComparison.OrdinalIgnoreCase);
        }

        internal static string FormatMountDisplayName(string baseDisplay, int ammo)
        {
            string name = baseDisplay != null ? baseDisplay : string.Empty;
            if (ammo > 1)
                return name + " x" + ammo;
            return name;
        }

        internal static string CloneMountObjectName(bool nuke, string srcName)
        {
            string src = srcName != null ? srcName : "mount";
            return (nuke ? "ACNM_118_" : "ACM_119_") + src;
        }

        internal static string CloneInfoObjectName(bool nuke)
        {
            return nuke ? "ACNM_118_info" : "ACM_119_info";
        }

        internal static string CloneEncyclopediaDefName(bool nuke)
        {
            return nuke ? "ACNM_118" : "ACM_119";
        }

        internal static IdentityPath ResolveRestoreIdentity(bool mountNull, bool keyEmpty, bool infoFound)
        {
            if (mountNull || keyEmpty || !infoFound)
                return IdentityPath.Skip;
            return IdentityPath.Apply;
        }

        internal static int CountNonGunRails(int totalWeaponsIncludingInactive, int gunCount)
        {
            int n = totalWeaponsIncludingInactive - gunCount;
            return n > 0 ? n : 0;
        }

        internal static SyncPath ResolveSyncFromMount(bool weaponNull, bool mountNull, bool isGun, bool isAgmTMount)
        {
            if (weaponNull || mountNull || isGun || !isAgmTMount)
                return SyncPath.Skip;
            return SyncPath.SyncInfo;
        }

        /// <summary>Mount-name fallback when info/key classify missed (hangar display strings).</summary>
        internal static bool LooksLikeAgmTMountName(string mountName, string jsonKey)
        {
            string mn = mountName != null ? mountName : string.Empty;
            if (mn.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || mn.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || mn.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || mn.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return AgmTGateMathService.IsAgmTKey(
                jsonKey,
                AgmTWeapon.PackKey,
                AgmTWeapon.NukePackKey,
                AgmTWeapon.LegacyPackKey,
                AgmTWeapon.LegacyPackKey119);
        }

        internal static NoteFirePath ResolveNoteFire(
            bool enableAgmT,
            bool weaponNull,
            bool classifiedAgmT)
        {
            if (!enableAgmT || weaponNull || !classifiedAgmT)
                return NoteFirePath.Skip;
            return NoteFirePath.ArmPending;
        }

        internal static bool ResolvePendingNuke(
            bool nukeKey,
            bool nukeMount,
            bool nukeWeaponInfo,
            bool nukeStationInfo)
        {
            return nukeKey || nukeMount || nukeWeaponInfo || nukeStationInfo;
        }

        internal static bool ShouldEnsureEncyclopediaDef(bool nuke, bool convReady, bool nukeReady)
        {
            if (!nuke && convReady)
                return false;
            if (nuke && nukeReady)
                return false;
            return true;
        }

        internal static bool ShouldRegisterWithEncyclopedia(bool encNull, bool populated)
        {
            return !encNull && populated;
        }

        /// <summary>Preferred clone lookup keys: pack_single then pack.</summary>
        internal static void PreferredCloneKeys(bool nuke, string packKey, string nukePackKey, out string primary, out string secondary)
        {
            string pack = nuke ? nukePackKey : packKey;
            primary = pack + "_single";
            secondary = pack;
        }
    }
}
