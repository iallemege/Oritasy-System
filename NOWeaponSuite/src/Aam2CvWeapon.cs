using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// AAM-2CV [IAL] / AAM-2CV [IAL] [5kt]: encyclopedia presents a 2064 Veyrn AAM.
    /// Mechanically an AAM-36 clone with ACM-119 bus mesh, Mach 8 / 150 km,
    /// double maneuver, all aircraft pylons.
    /// </summary>
    internal static class Aam2CvWeapon
    {
        internal const string DisplayName = "AAM-2CV [IAL]";
        internal const string DisplayNameNuke = "AAM-2CV [IAL] [5kt]";
        internal const string EncyclopediaText =
            "AAM-2CV [IAL] is a neutral-export air-defense missile from Veyrn Aeronautics, "
            + "an aircraft manufacturer in a northern country that publishes no catalogs "
            + "and answers no diplomatic inquiries. Fielded in 2064 as an all-new ramjet "
            + "beyond-visual-range interceptor, it is built for a Mach 8 dash, a 150 km "
            + "engagement envelope, and high off-boresight authority, and continues to hunt "
            + "aerial contacts after launch without a continuous lock. "
            + "Identical unmarked lots reached both the Boscali Defense Force and the Primeva "
            + "Armed Liberation Alliance through brokers, without end-user clauses or national "
            + "insignia—theater-neutral stock that neither side can easily attribute after impact.";
        internal const string EncyclopediaTextNuke =
            "AAM-2CV [IAL] [5kt] is a neutral-export nuclear air-defense missile from Veyrn Aeronautics, "
            + "an aircraft manufacturer in a northern country that publishes no catalogs "
            + "and answers no diplomatic inquiries. Fielded in 2064 as an all-new ramjet "
            + "beyond-visual-range interceptor, it pairs a Mach 8 dash and 150 km envelope "
            + "with a 5kt-class warhead and continues to hunt aerial contacts after launch "
            + "without a continuous lock. Identical unmarked lots reached both the Boscali Defense Force "
            + "and the Primeva Armed Liberation Alliance through brokers, without end-user "
            + "clauses or national insignia—theater-neutral stock that neither side can easily "
            + "attribute after impact.";
        /// <summary>Game scale: 20kt ≈ 20000000 → 5kt = 5000000.</summary>
        internal const float NukeYield5kt = 5000000f;
        internal const float SpeedMach = Aam2CvGateService.SpeedMach;
        internal const float RangeM = Aam2CvGateService.RangeM;

        private static bool _injected;
        internal static bool IsInjected { get { return _injected; } }

        private static readonly HashSet<int> InfoIds = new HashSet<int>();
        private static readonly HashSet<int> NukeInfoIds = new HashSet<int>();
        private static readonly HashSet<string> CreatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WeaponMount> MountClones = new List<WeaponMount>();
        private static readonly Dictionary<string, WeaponInfo> InfoByKey =
            new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> NukeByKey =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static MissileDefinition _encyclopediaDef;
        private static MissileDefinition _encyclopediaDefNuke;
        private static float _nextHardpointInject;
        private static float _nextMaintAt;
        private static float _nextMissingLog;
        private static int _hardpointIdlePasses;
        private static int _lastHardpointLogAdded = -1;
        private static readonly HashSet<int> ShipWmIds = new HashSet<int>();
        private static readonly HashSet<int> AirWmIds = new HashSet<int>();

        private static readonly FieldInfo MissileInfoField = AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo WeaponStationField = AccessTools.Field(typeof(Weapon), "weaponStation");
        private static readonly FieldInfo GLimitField = AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo TorqueField = AccessTools.Field(typeof(Missile), "torque");
        private static readonly FieldInfo MaxTurnRateField = AccessTools.Field(typeof(Missile), "maxTurnRate");
        private static readonly FieldInfo FinAreaField = AccessTools.Field(typeof(Missile), "finArea");
        private static readonly FieldInfo MotorsField = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo ArhRadarField = AccessTools.Field(typeof(ARHSeeker), "radarParameters");
        private static readonly FieldInfo SarhRadarField = AccessTools.Field(typeof(SARHSeeker), "radarParams");

        private sealed class PendingFire
        {
            public Unit owner;
            public bool nuke;
            public float time;
            public WeaponInfo info;
        }

        private static readonly List<PendingFire> PendingFires = new List<PendingFire>();

        internal static bool HasUsableClones()
        {
            return CountUsableClones() > 0;
        }

        private static int CountUsableClones()
        {
            int n = 0;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && m.prefab != null && m.info != null)
                    n++;
            }
            return n;
        }

        internal static bool IsAam2CvKey(string key)
        {
            return Aam2CvGateService.IsAam2CvKey(key);
        }

        internal static bool IsAam2CvNukeKey(string key)
        {
            return Aam2CvGateService.IsAam2CvNukeKey(key);
        }

        internal static bool IsAam2CvInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (InfoIds.Contains(info.GetInstanceID()) || NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            string n = ((info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.weaponName != null ? info.weaponName : string.Empty));
            return n.IndexOf("AAM-2CV", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAam2CvNukeInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            if (!IsAam2CvInfo(info))
                return false;
            string n = ((info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.weaponName != null ? info.weaponName : string.Empty));
            return info.nuclear
                || n.IndexOf("5kt", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAam2CvMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsAam2CvKey(mount.jsonKey))
                return true;
            return IsAam2CvInfo(mount.info);
        }

        internal static bool IsAam2CvNukeMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsAam2CvNukeKey(mount.jsonKey))
                return true;
            return IsAam2CvNukeInfo(mount.info);
        }

        internal static bool IsAam2CvMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.GetComponent<Aam2CvMark>() != null)
                    return true;
            }
            catch { }
            return IsAam2CvInfo(GetMissileInfo(missile));
        }

        private static WeaponInfo GetMissileInfo(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                if (MissileInfoField != null)
                    return MissileInfoField.GetValue(missile) as WeaponInfo;
            }
            catch { }
            return null;
        }

        internal static void Ensure()
        {
            ResetDeadInjection();
            if (!_injected)
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null || all.Length == 0)
                {
                    if (Time.unscaledTime >= _nextMissingLog)
                    {
                        _nextMissingLog = Time.unscaledTime + 8f;
                        if (Plugin.Log != null)
                            Plugin.Log.LogWarning("AAM-2CV: waiting for WeaponMount assets...");
                    }
                    return;
                }

                Encyclopedia enc = Plugin.GetEncyclopedia();
                EnsureEncyclopediaDef(enc, false);
                EnsureEncyclopediaDef(enc, true);
                int added = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount src = all[i];
                    if (src == null || src.info == null || !src.info.missile)
                        continue;
                    if (IsAam2CvKey(src.jsonKey) || Plugin.IsIalKey(src.jsonKey))
                        continue;
                    if (AgmTWeapon.IsAgmTKey(src.jsonKey))
                        continue;
                    if (!IsAam36Mount(src))
                        continue;
                    if (Aam2CvGateService.IsEightRoundMount(src.jsonKey, src.ammo))
                        continue;

                    string baseKey = !string.IsNullOrEmpty(src.jsonKey) ? src.jsonKey : "AAM4";
                    string convKey = Aam2CvGateService.RemapAam4Key(baseKey, false);
                    string nukeKey = Aam2CvGateService.RemapAam4Key(baseKey, true);
                    if (!CreatedKeys.Contains(convKey))
                    {
                        if (CreateMountVariant(src, enc, convKey, false, ref added))
                            CreatedKeys.Add(convKey);
                    }
                    if (!CreatedKeys.Contains(nukeKey))
                    {
                        if (CreateMountVariant(src, enc, nukeKey, true, ref added))
                            CreatedKeys.Add(nukeKey);
                    }
                }

                if (added > 0 || MountClones.Count > 0)
                {
                    _injected = true;
                    RestoreAllMountIdentities();
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("AAM-2CV: injected " + added + " mounts");
                }
                else if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 12f;
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AAM-2CV: no AAM-36 (AAM4) donor mounts yet");
                }
            }

            if (!_injected)
                return;

            AgmTLifecycleGateService.MaintPath maint = AgmTLifecycleGateService.ResolveMaint(
                Time.unscaledTime, _nextMaintAt, _nextHardpointInject);
            if (maint == AgmTLifecycleGateService.MaintPath.RepairAndRegister
                || maint == AgmTLifecycleGateService.MaintPath.Both)
            {
                _nextMaintAt = AgmTLifecycleGateService.ScheduleNextMaint(Time.unscaledTime);
                RegisterWithEncyclopedia(Plugin.GetEncyclopedia());
                EnsureMountsInCache();
            }
            if (maint == AgmTLifecycleGateService.MaintPath.HardpointScan
                || maint == AgmTLifecycleGateService.MaintPath.Both)
                InjectIntoAircraftHardpoints();
        }

        private static void ResetDeadInjection()
        {
            for (int i = MountClones.Count - 1; i >= 0; i--)
            {
                if (MountClones[i] == null)
                    MountClones.RemoveAt(i);
            }
            if (!AgmTLifecycleGateService.ShouldResetInjection(_injected, CountUsableClones()))
                return;
            _injected = false;
            CreatedKeys.Clear();
            InfoByKey.Clear();
            NukeByKey.Clear();
            InfoIds.Clear();
            NukeInfoIds.Clear();
            MountClones.Clear();
            _encyclopediaDef = null;
            _encyclopediaDefNuke = null;
            _hardpointIdlePasses = 0;
            _nextHardpointInject = 0f;
            _nextMaintAt = 0f;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("AAM-2CV: mount clones gone — will re-inject");
        }

        internal static void InjectIntoAircraftHardpoints()
        {
            if (!_injected || MountClones.Count == 0)
                return;
            float backoff = AgmTLifecycleGateService.HardpointBackoffSec(_hardpointIdlePasses);
            _nextHardpointInject = Time.unscaledTime + backoff;
            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers == null)
                return;
            int added = 0;
            for (int i = 0; i < managers.Length; i++)
                added += InjectIntoWeaponManager(managers[i]);
            _hardpointIdlePasses = AgmTLifecycleGateService.NextIdlePasses(_hardpointIdlePasses, added);
            if (AgmTLifecycleGateService.ShouldLogHardpointWire(added, _lastHardpointLogAdded)
                && Plugin.Log != null)
            {
                _lastHardpointLogAdded = added;
                Plugin.Log.LogInfo("AAM-2CV: wired " + added + " options onto aircraft hardpoints");
            }
        }

        internal static int InjectIntoWeaponManager(WeaponManager wm)
        {
            if (AgmTLifecycleGateService.ResolveWmInject(
                    _injected, MountClones.Count, wm == null,
                    wm == null || wm.hardpointSets == null, IsShipWeaponManager(wm))
                == AgmTLifecycleGateService.WmInjectPath.Skip)
                return 0;

            int added = 0;
            for (int h = 0; h < wm.hardpointSets.Length; h++)
            {
                HardpointSet hs = wm.hardpointSets[h];
                if (hs == null || Plugin.IsNavalHardpoint(hs))
                    continue;
                if (hs.weaponOptions == null)
                    hs.weaponOptions = new List<WeaponMount>();
                if (!HardpointAcceptsMissiles(hs))
                    continue;
                added += AddToHardpoint(hs);
            }
            return added;
        }

        /// <summary>
        /// AAM-2CV is aircraft-pylon only. Skip ships, SAM trucks, and buildings.
        /// </summary>
        private static bool IsShipWeaponManager(WeaponManager wm)
        {
            if (wm == null)
                return false;
            int id = wm.GetInstanceID();
            if (ShipWmIds.Contains(id))
                return true;
            if (AirWmIds.Contains(id))
                return false;
            try
            {
                if (wm.GetComponentInParent<Aircraft>() != null)
                {
                    AirWmIds.Add(id);
                    return false;
                }
            }
            catch { }
            ShipWmIds.Add(id);
            return true;
        }

        private static bool HardpointAcceptsMissiles(HardpointSet hs)
        {
            if (hs == null || hs.weaponOptions == null || hs.weaponOptions.Count == 0)
                return false;
            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m != null && m.info != null && m.info.missile)
                    return true;
            }
            return false;
        }

        private static int AddToHardpoint(HardpointSet hs)
        {
            for (int i = hs.weaponOptions.Count - 1; i >= 0; i--)
            {
                WeaponMount drop = hs.weaponOptions[i];
                if (drop == null || !IsAam2CvMount(drop))
                    continue;
                if (!Aam2CvGateService.IsEightRoundMount(drop.jsonKey, drop.ammo))
                    continue;
                hs.weaponOptions.RemoveAt(i);
            }

            HashSet<WeaponMount> want = new HashSet<WeaponMount>();
            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null || IsAam2CvMount(m))
                    continue;
                if (!IsAam36Mount(m))
                    continue;
                if (Aam2CvGateService.IsEightRoundMount(m.jsonKey, m.ammo))
                    continue;
                string baseKey = !string.IsNullOrEmpty(m.jsonKey) ? m.jsonKey : "AAM4";
                AddCloneKey(want, Aam2CvGateService.RemapAam4Key(baseKey, false));
                AddCloneKey(want, Aam2CvGateService.RemapAam4Key(baseKey, true));
            }

            WeaponMount conv = FindPreferredClone(false);
            WeaponMount nuke = FindPreferredClone(true);
            if (conv != null)
                want.Add(conv);
            if (nuke != null)
                want.Add(nuke);

            int added = 0;
            foreach (WeaponMount m in want)
            {
                if (m == null || m.prefab == null)
                    continue;
                if (hs.weaponOptions.Contains(m))
                    continue;
                hs.weaponOptions.Add(m);
                added++;
            }
            return added;
        }

        private static void AddCloneKey(HashSet<WeaponMount> want, string key)
        {
            WeaponMount m = FindCloneByKey(key);
            if (m != null)
                want.Add(m);
        }

        private static WeaponMount FindCloneByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && string.Equals(m.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        private static WeaponMount FindPreferredClone(bool nuke)
        {
            string primary;
            string secondary;
            Aam2CvGateService.PreferredCloneKeys(nuke, out primary, out secondary);
            WeaponMount preferred = FindCloneByKey(primary);
            if (preferred != null)
                return preferred;
            preferred = FindCloneByKey(secondary);
            if (preferred != null)
                return preferred;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.prefab == null)
                    continue;
                if (nuke ? IsAam2CvNukeMount(m) : (IsAam2CvMount(m) && !IsAam2CvNukeMount(m)))
                    return m;
            }
            return null;
        }

        internal static void AppendMountsToList(List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null || have == null || MountClones.Count == 0)
                return;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.prefab == null)
                    continue;
                if (Aam2CvGateService.IsEightRoundMount(m.jsonKey, m.ammo))
                    continue;
                if (have.Add(m))
                    list.Add(m);
            }
        }

        internal static void EnsureMountsInCache()
        {
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.prefab == null)
                    continue;
                if (Plugin.CachedMountSet.Add(m))
                    Plugin.CachedMounts.Add(m);
            }
        }

        private static void RegisterWithEncyclopedia(Encyclopedia enc)
        {
            if (enc == null || !Plugin.IsEncyclopediaPopulated(enc))
                return;
            EnsureEncyclopediaDef(enc, false);
            EnsureEncyclopediaDef(enc, true);
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount clone = MountClones[i];
                if (clone == null || string.IsNullOrEmpty(clone.jsonKey))
                    continue;
                if (enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                    enc.weaponMounts.Add(clone);
                if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(clone.jsonKey))
                    Encyclopedia.WeaponLookup[clone.jsonKey] = clone;
                try
                {
                    if (enc.IndexLookup != null && !enc.IndexLookup.Contains(clone))
                    {
                        enc.IndexLookup.Add(clone);
                        INetworkDefinition nd = clone;
                        nd.LookupIndex = enc.IndexLookup.Count - 1;
                    }
                }
                catch { }
                RestoreMountIdentity(clone);
            }
        }

        internal static void RestoreMountIdentity(WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey))
                return;
            WeaponInfo info = null;
            if (!InfoByKey.TryGetValue(mount.jsonKey, out info) || info == null)
                return;
            bool nuke = false;
            NukeByKey.TryGetValue(mount.jsonKey, out nuke);
            string want = nuke ? DisplayNameNuke : DisplayName;
            info.weaponName = want;
            info.shortName = want;
            Plugin.StripTag(ref info.weaponName, "[10kt]");
            Plugin.StripTag(ref info.shortName, "[10kt]");
            VeyrnMissileIcon.ApplyTo(info);
            ApplyFireEnvelope(info);
            mount.info = info;
            mount.mountName = Aam2CvGateService.FormatMountDisplayName(want, mount.ammo);
        }

        internal static void RestoreAllMountIdentities()
        {
            for (int i = 0; i < MountClones.Count; i++)
                RestoreMountIdentity(MountClones[i]);
        }

        private static bool CreateMountVariant(WeaponMount src, Encyclopedia enc, string key, bool nuke, ref int added)
        {
            WeaponMount clone = UnityEngine.Object.Instantiate(src);
            clone.name = (nuke ? "AAM_2CV_5kt_" : "AAM_2CV_") + (src.name != null ? src.name : "Mount");
            clone.jsonKey = key;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

            WeaponInfo infoClone = UnityEngine.Object.Instantiate(src.info);
            infoClone.name = nuke ? "WeaponInfo_AAM_2CV_5kt" : "WeaponInfo_AAM_2CV";
            infoClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            infoClone.weaponName = nuke ? DisplayNameNuke : DisplayName;
            infoClone.shortName = nuke ? DisplayNameNuke : DisplayName;
            string desc = nuke ? EncyclopediaTextNuke : EncyclopediaText;
            if (nuke)
                Plugin.EnsureNukeDescriptionLine(ref desc);
            else
                Plugin.EnsureWexonDescriptionTag(ref desc);
            infoClone.description = desc;
            infoClone.nuclear = nuke;
            infoClone.strategic = false;
            infoClone.rearmShip = false;
            infoClone.missile = true;
            infoClone.blastDamage = nuke ? NukeYield5kt : src.info.blastDamage;
            ApplyFireEnvelope(infoClone);
            VeyrnMissileIcon.ApplyTo(infoClone);

            clone.prefab = src.prefab;
            if (src.info != null && src.info.weaponPrefab != null)
                infoClone.weaponPrefab = src.info.weaponPrefab;

            clone.info = infoClone;
            if (src.ammo > 0)
                clone.ammo = src.ammo;
            clone.mountName = Aam2CvGateService.FormatMountDisplayName(
                nuke ? DisplayNameNuke : DisplayName, clone.ammo);

            InfoIds.Add(infoClone.GetInstanceID());
            if (nuke)
                NukeInfoIds.Add(infoClone.GetInstanceID());
            InfoByKey[key] = infoClone;
            NukeByKey[key] = nuke;
            MountClones.Add(clone);
            if (Plugin.CachedMountSet.Add(clone))
                Plugin.CachedMounts.Add(clone);

            if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(key))
                Encyclopedia.WeaponLookup[key] = clone;
            try
            {
                if (enc != null && enc.IndexLookup != null && !enc.IndexLookup.Contains(clone))
                {
                    enc.IndexLookup.Add(clone);
                    INetworkDefinition nd = clone;
                    nd.LookupIndex = enc.IndexLookup.Count - 1;
                }
            }
            catch { }

            added++;
            return true;
        }

        private static void EnsureEncyclopediaDef(Encyclopedia enc, bool nuke)
        {
            if (enc == null || enc.missiles == null)
                return;
            if (nuke && _encyclopediaDefNuke != null)
                return;
            if (!nuke && _encyclopediaDef != null)
                return;

            MissileDefinition src = FindAam36Definition(enc);
            if (src == null)
                return;
            string key = nuke ? Aam2CvGateService.NukePackKey : Aam2CvGateService.PackKey;
            MissileDefinition clone = UnityEngine.Object.Instantiate(src);
            clone.name = nuke ? "MissileDef_AAM_2CV_5kt" : "MissileDef_AAM_2CV";
            clone.jsonKey = key;
            clone.code = nuke ? DisplayNameNuke : DisplayName;
            clone.unitName = nuke ? DisplayNameNuke : DisplayName;
            string encDesc = nuke ? EncyclopediaTextNuke : EncyclopediaText;
            if (nuke)
                Plugin.EnsureNukeDescriptionLine(ref encDesc);
            else
                Plugin.EnsureWexonDescriptionTag(ref encDesc);
            clone.description = encDesc;
            clone.dontAutomaticallyAddToEncyclopedia = false;
            if (!enc.missiles.Contains(clone))
                enc.missiles.Add(clone);
            if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(key))
                Encyclopedia.Lookup[key] = clone;
            if (nuke)
                _encyclopediaDefNuke = clone;
            else
                _encyclopediaDef = clone;
        }

        private static MissileDefinition FindAam36Definition(Encyclopedia enc)
        {
            if (enc != null && enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null)
                        continue;
                    if (Aam2CvGateService.IsAam36Definition(d.jsonKey, d.unitName, d.name))
                        return d;
                }
            }
            MissileDefinition[] all = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                MissileDefinition d = all[i];
                if (d != null && Aam2CvGateService.IsAam36Definition(d.jsonKey, d.unitName, d.name))
                    return d;
            }
            return null;
        }

        private static bool IsAam36Mount(WeaponMount m)
        {
            if (m == null)
                return false;
            string sn = m.info != null && m.info.shortName != null ? m.info.shortName : string.Empty;
            string wn = m.info != null && m.info.weaponName != null ? m.info.weaponName : string.Empty;
            return Aam2CvGateService.IsAam36Donor(m.jsonKey, sn, wn);
        }

        internal static void SyncFromMount(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || mount == null || weapon is Gun || !IsAam2CvMount(mount))
                return;
            RestoreMountIdentity(mount);
            if (mount.info == null)
                return;
            weapon.info = mount.info;
        }

        internal static void NoteFire(Weapon weapon)
        {
            if (weapon == null)
                return;
            WeaponMount mount = Plugin.GetWeaponMount(weapon);
            WeaponInfo stationInfo = null;
            try
            {
                if (WeaponStationField != null)
                {
                    WeaponStation st = WeaponStationField.GetValue(weapon) as WeaponStation;
                    if (st != null)
                        stationInfo = st.WeaponInfo;
                }
            }
            catch { }

            bool ours = IsAam2CvMount(mount)
                || IsAam2CvInfo(weapon.info)
                || IsAam2CvInfo(stationInfo);
            if (!ours)
                return;

            PendingFire pf = new PendingFire();
            pf.owner = weapon.attachedUnit;
            pf.time = Time.time;
            pf.info = weapon.info;
            pf.nuke = (mount != null && IsAam2CvNukeMount(mount))
                || IsAam2CvNukeInfo(weapon.info)
                || IsAam2CvNukeInfo(stationInfo);
            PendingFires.Add(pf);
        }

        internal static void OnSpawned(Missile missile, Unit spawnOwner)
        {
            if (missile == null)
                return;
            if (AgmTWeapon.HasBusDispenser(missile) || AgmTWeapon.IsPoweredGs25Sub(missile))
                return;

            bool pendingNuke = false;
            WeaponInfo pendingInfo = null;
            bool pending = ConsumePending(missile, spawnOwner, out pendingNuke, out pendingInfo);
            bool ours = pending
                || IsAam2CvMissile(missile)
                || IsAam2CvInfo(GetMissileInfo(missile));
            if (!ours)
                return;

            if (!pendingNuke)
                pendingNuke = IsAam2CvNukeInfo(GetMissileInfo(missile))
                    || IsAam2CvNukeInfo(pendingInfo);

            ApplySpawnIdentity(missile, pendingNuke, pendingInfo);
        }

        private static bool ConsumePending(Missile missile, Unit spawnOwner, out bool wasNuke, out WeaponInfo info)
        {
            wasNuke = false;
            info = null;
            PrunePending();
            if (PendingFires.Count <= 0)
                return false;

            Unit owner = spawnOwner != null ? spawnOwner : (missile != null ? missile.owner : null);
            int pick = -1;
            for (int i = 0; i < PendingFires.Count; i++)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null)
                    continue;
                if (OwnerMatches(pf.owner, owner))
                {
                    pick = i;
                    break;
                }
            }
            if (pick < 0)
                return false;

            PendingFire taken = PendingFires[pick];
            PendingFires.RemoveAt(pick);
            wasNuke = taken != null && taken.nuke;
            info = taken != null ? taken.info : null;
            return true;
        }

        private static void PrunePending()
        {
            float now = Time.time;
            for (int i = PendingFires.Count - 1; i >= 0; i--)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null || now - pf.time > 8f)
                    PendingFires.RemoveAt(i);
            }
        }

        private static bool OwnerMatches(Unit pendingOwner, Unit spawnOwner)
        {
            if (pendingOwner == null || spawnOwner == null)
                return false;
            if (object.ReferenceEquals(spawnOwner, pendingOwner))
                return true;
            try
            {
                if (spawnOwner.transform != null && pendingOwner.transform != null
                    && spawnOwner.transform.root == pendingOwner.transform.root)
                    return true;
            }
            catch { }
            Aircraft a = pendingOwner as Aircraft;
            Aircraft b = spawnOwner as Aircraft;
            try
            {
                if (a == null)
                    a = pendingOwner.GetComponentInParent<Aircraft>();
                if (b == null)
                    b = spawnOwner.GetComponentInParent<Aircraft>();
            }
            catch { }
            return a != null && b != null && object.ReferenceEquals(a, b);
        }

        private static void ApplySpawnIdentity(Missile missile, bool nuke, WeaponInfo sourceInfo)
        {
            WeaponInfo info = sourceInfo;
            if (info == null || !IsAam2CvInfo(info))
                info = GetMissileInfo(missile);
            string want = nuke ? DisplayNameNuke : DisplayName;
            try
            {
                missile.NetworkunitName = want;
                missile.name = want;
            }
            catch { }
            if (info != null && MissileInfoField != null)
            {
                try { MissileInfoField.SetValue(missile, info); }
                catch { }
            }
            if (nuke)
                Plugin.ApplyAam2CvNukeWarhead(missile);
            if (info != null && IsAam2CvInfo(info))
                ApplyFireEnvelope(info);

            Aam2CvMark mark = missile.GetComponent<Aam2CvMark>();
            if (mark == null)
                mark = Plugin.TryAddBehaviour<Aam2CvMark>(missile.gameObject);
            if (mark != null)
            {
                mark.Nuke = nuke;
                if (!mark.Boosted)
                {
                    DoubleManeuver(missile);
                    ApplyKinematics(missile);
                    ApplySeekerRange(missile);
                    mark.Boosted = true;
                }
            }
            else
            {
                DoubleManeuver(missile);
                ApplyKinematics(missile);
                ApplySeekerRange(missile);
            }

            if (Plugin.AgmTBusCustomVisual == null || Plugin.AgmTBusCustomVisual.Value)
                AgmTBusVisual.ApplyAcmBusMesh(missile.gameObject);
            WireTbmExhaust(missile);
        }

        private static void WireTbmExhaust(Missile missile)
        {
            if (missile == null)
                return;
            AgmTBusVisual.TbmExhaustBind bind = AgmTBusVisual.ApplyTbmExhaust(missile.gameObject);
            if (bind == null || MotorsField == null)
                return;
            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                if (motors == null || motors.Length == 0)
                    return;
                object last = motors.GetValue(motors.Length - 1);
                if (last == null)
                    return;
                Type mt = last.GetType();
                FieldInfo psField = mt.GetField("particleSystems",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo trailField = mt.GetField("trailEmitters",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                FieldInfo lightField = mt.GetField("lights",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (psField != null && bind.ParticleSystems != null && bind.ParticleSystems.Length > 0)
                {
                    Type elem = psField.FieldType.GetElementType();
                    if (elem != null)
                    {
                        Array arr = Array.CreateInstance(elem, bind.ParticleSystems.Length);
                        int n = 0;
                        for (int i = 0; i < bind.ParticleSystems.Length; i++)
                        {
                            Component c = bind.ParticleSystems[i];
                            if (c == null || !elem.IsInstanceOfType(c))
                                continue;
                            arr.SetValue(c, n);
                            n++;
                        }
                        if (n < arr.Length)
                        {
                            Array trimmed = Array.CreateInstance(elem, n);
                            Array.Copy(arr, trimmed, n);
                            arr = trimmed;
                        }
                        if (n > 0)
                            psField.SetValue(last, arr);
                    }
                }
                if (trailField != null && bind.Trails != null)
                    trailField.SetValue(last, bind.Trails);
                if (lightField != null && bind.Lights != null)
                    lightField.SetValue(last, bind.Lights);

                // Earlier motors must not Stop() the shared TBM plume on booster burnout.
                for (int i = 0; i < motors.Length - 1; i++)
                {
                    object motor = motors.GetValue(i);
                    if (motor == null)
                        continue;
                    Type et = motor.GetType();
                    FieldInfo earlyPs = et.GetField("particleSystems",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo earlyTr = et.GetField("trailEmitters",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo earlyLt = et.GetField("lights",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (earlyPs != null && earlyPs.FieldType.GetElementType() != null)
                        earlyPs.SetValue(motor, Array.CreateInstance(earlyPs.FieldType.GetElementType(), 0));
                    if (earlyTr != null && earlyTr.FieldType.GetElementType() != null)
                        earlyTr.SetValue(motor, Array.CreateInstance(earlyTr.FieldType.GetElementType(), 0));
                    if (earlyLt != null && earlyLt.FieldType.GetElementType() != null)
                        earlyLt.SetValue(motor, Array.CreateInstance(earlyLt.FieldType.GetElementType(), 0));
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("AAM-2CV TBM exhaust wire: " + ex.Message);
            }
        }

        private static void ApplyFireEnvelope(WeaponInfo info)
        {
            if (info == null)
                return;
            try
            {
                TargetRequirements tr = info.targetRequirements;
                tr.maxRange = RangeM;
                info.targetRequirements = tr;
            }
            catch { }
        }

        private static void ApplyKinematics(Missile missile)
        {
            if (missile == null || MotorsField == null)
                return;
            float wantSpeed = Aam2CvGateService.SpeedMs();
            float minBurn = Aam2CvGateService.MinBurnSec();
            float currentTop = 0f;
            try { currentTop = missile.GetTopSpeed(8000f, 8000f); }
            catch { }
            float thrustMul = Aam2CvGateService.ThrustMulFromCurrentTop(currentTop);
            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                if (motors == null || motors.Length == 0)
                    return;
                for (int i = 0; i < motors.Length; i++)
                {
                    object motor = motors.GetValue(i);
                    if (motor == null)
                        continue;
                    Type mt = motor.GetType();
                    FieldInfo fTop = mt.GetField("topSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo fThrust = mt.GetField("thrust", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo fBurn = mt.GetField("burnTime", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fTop != null)
                        fTop.SetValue(motor, wantSpeed);
                    if (fThrust != null)
                    {
                        float thrust = Convert.ToSingle(fThrust.GetValue(motor));
                        if (thrust > 0.01f)
                            fThrust.SetValue(motor, thrust * thrustMul);
                    }
                    if (fBurn != null)
                    {
                        float burn = Convert.ToSingle(fBurn.GetValue(motor));
                        if (burn < minBurn)
                            fBurn.SetValue(motor, minBurn);
                    }
                }
            }
            catch { }
        }

        private static void ApplySeekerRange(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                ARHSeeker arh = missile.GetComponent<ARHSeeker>();
                if (arh != null)
                    StampRadarMaxRange(ArhRadarField, arh);
            }
            catch { }
            try
            {
                SARHSeeker sarh = missile.GetComponent<SARHSeeker>();
                if (sarh != null)
                    StampRadarMaxRange(SarhRadarField, sarh);
            }
            catch { }
        }

        private static void StampRadarMaxRange(FieldInfo field, object seeker)
        {
            if (field == null || seeker == null)
                return;
            object raw = field.GetValue(seeker);
            if (raw == null || !(raw is RadarParams))
                return;
            RadarParams rp = (RadarParams)raw;
            if (rp.maxRange >= RangeM)
                return;
            rp.maxRange = RangeM;
            field.SetValue(seeker, rp);
        }

        private static void DoubleManeuver(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (GLimitField != null)
                    GLimitField.SetValue(missile, Aam2CvGateService.GLimit);
            }
            catch { }
            try
            {
                float torque = 8f;
                float turn = 0f;
                if (TorqueField != null)
                {
                    try { torque = (float)TorqueField.GetValue(missile); }
                    catch { }
                }
                if (MaxTurnRateField != null)
                {
                    try { turn = (float)MaxTurnRateField.GetValue(missile); }
                    catch { }
                }
                else
                {
                    try { turn = missile.GetMaxTurnRate(); }
                    catch { }
                }
                if (torque < 0.05f)
                    torque = 8f;
                else
                    torque = torque * 2f;
                if (turn < 0.05f)
                    turn = 24f;
                else
                    turn = turn * 2f;
                missile.SetTorque(torque, turn);
            }
            catch { }
            try
            {
                if (FinAreaField != null)
                {
                    float fin = (float)FinAreaField.GetValue(missile);
                    if (fin > 0.0001f)
                        FinAreaField.SetValue(missile, fin * 2f);
                }
            }
            catch { }
        }
    }

    /// <summary>Spawned AAM-2CV identity + one-shot maneuver / kinematics flag.</summary>
    public class Aam2CvMark : MonoBehaviour
    {
        public bool Nuke;
        public bool Boosted;
    }

    [HarmonyPatch(typeof(HUDMissileState), "CalcWeaponRange")]
    internal static class Patch_HUDMissileState_Aam2CvRange
    {
        private static readonly FieldInfo MaxRangeField = AccessTools.Field(typeof(HUDMissileState), "maxRange");
        private static readonly FieldInfo WeaponInfoField = AccessTools.Field(typeof(HUDWeaponState), "weaponInfo");

        [HarmonyPostfix]
        private static void Postfix(HUDMissileState __instance)
        {
            if (__instance == null || MaxRangeField == null || WeaponInfoField == null)
                return;
            WeaponInfo info = null;
            try { info = WeaponInfoField.GetValue(__instance) as WeaponInfo; }
            catch { }
            if (!Aam2CvWeapon.IsAam2CvInfo(info))
                return;
            try
            {
                float cur = (float)MaxRangeField.GetValue(__instance);
                if (cur < Aam2CvWeapon.RangeM)
                    MaxRangeField.SetValue(__instance, Aam2CvWeapon.RangeM);
            }
            catch { }
        }
    }
}
