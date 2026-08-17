using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Hardpoint / mount cache for unrestricted loadout</summary>
    public partial class Plugin
    {
        internal static void RegisterHardpointSets(WeaponManager wm)
        {
            if (wm == null || wm.hardpointSets == null)
                return;
            for (int i = 0; i < wm.hardpointSets.Length; i++)
            {
                if (wm.hardpointSets[i] != null)
                    Ci22HardpointSetIds.Add(wm.hardpointSets[i].GetHashCode());
            }
        }

        internal static void RefreshMountCache()
        {
            AllMountCache.Clear();
            AllMountSet.Clear();
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (m == null || m.prefab == null || IsMountDisabled(m) || IsMountBlacklisted(m))
                    continue;
                if (AllMountSet.Add(m))
                    AllMountCache.Add(m);
            }
        }

        private static bool IsMountDisabled(WeaponMount m)
        {
            if (m == null || MountDisabledField == null)
                return false;
            try { return (bool)MountDisabledField.GetValue(m); }
            catch { return false; }
        }

        private static bool IsMountBlacklisted(WeaponMount m)
        {
            if (m == null)
                return true;
            string n = ((m.mountName != null ? m.mountName : string.Empty) + " " + m.name).ToLowerInvariant();
            return n.IndexOf("afv", StringComparison.Ordinal) >= 0
                || n.IndexOf("lcv", StringComparison.Ordinal) >= 0
                || n.IndexOf("container", StringComparison.Ordinal) >= 0
                || n.IndexOf("ugv", StringComparison.Ordinal) >= 0
                || n.IndexOf("pallet", StringComparison.Ordinal) >= 0
                || n.IndexOf("troops", StringComparison.Ordinal) >= 0;
        }
    }
}
