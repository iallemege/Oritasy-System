using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// ACM-119 [IAL] / ACNM-118 [IAL] cluster bus (AAM-29 airframe): self-hunts dense packs,
    /// then dispenses 6× GS25. Veyrn Aeronautics (northern country); sold to BDF and PALA.
    /// Conventional mount is branded [IAL] but never nuclear.
    /// </summary>
    internal static class AgmTWeapon
    {
        internal const string PackKey = "ACM_119";
        internal const string NukePackKey = "ACNM_118";
        internal const string LegacyPackKey = "AGM_T";
        internal const string LegacyPackKey119 = "AGM_119";
        internal const string NukeKeySuffix = "_15kt";
        /// <summary>Conventional: [IAL] cosmetic only — nuclear=false, no nuke warhead.</summary>
        internal const string DisplayName = "ACM-119 [IAL]";
        /// <summary>Nuclear fit: [IAL] + [1.5kt] yield tag (never [10kt]).</summary>
        internal const string DisplayNameNuke = "ACNM-118 [IAL] [1.5kt]";
        internal const string NukeYieldTag = "[1.5kt]";
        /// <summary>Encyclopedia / mount description — conventional only (no nuke cross-ref).</summary>
        internal const string EncyclopediaText =
            "ACM-119 [IAL] is a neutral-export cluster munition from Veyrn Aeronautics, "
            + "an aircraft manufacturer in a northern country that publishes no catalogs "
            + "and answers no diplomatic inquiries. Its workshops retooled AAM-29-class "
            + "airframes into a high-speed dispenser: after a short powered run the bus "
            + "discards itself and releases six GS25 optically guided submunitions that "
            + "continue to hunt air and surface contacts. Identical unmarked lots reached "
            + "both the Boscali Defense Force and the Primeva Armed Liberation Alliance "
            + "through brokers, without end-user clauses or national insignia—theater-neutral "
            + "stock that neither side can easily attribute after impact.";
        /// <summary>Encyclopedia / mount description — nuclear only (no conventional cross-ref).</summary>
        internal const string EncyclopediaTextNuke =
            "ACNM-118 [IAL] [1.5kt] is a neutral-export nuclear cluster munition from Veyrn Aeronautics, "
            + "an aircraft manufacturer in a northern country that publishes no catalogs "
            + "and answers no diplomatic inquiries. Built on an AAM-29-class airframe, the bus "
            + "runs under power before discarding itself and releasing six GS25 optically "
            + "guided submunitions fitted with 1.5kt-class warheads, which continue to hunt "
            + "air and surface contacts. Identical unmarked lots reached both the Boscali "
            + "Defense Force and the Primeva Armed Liberation Alliance through brokers, "
            + "without end-user clauses or national insignia—theater-neutral stock that "
            + "neither side can easily attribute after impact.";
        /// <summary>Game scale: 20kt ≈ 20000000 → 1.5kt = 1500000.</summary>
        internal const float NukeYield15kt = 1500000f;

        private static bool _injected;
        internal static bool IsInjected { get { return _injected; } }
        private static readonly HashSet<int> InfoIds = new HashSet<int>();
        private static readonly HashSet<int> NukeInfoIds = new HashSet<int>();
        private static readonly HashSet<string> CreatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WeaponMount> MountClones = new List<WeaponMount>();
        /// <summary>Survives WeaponMount.Initialize() wiping mount.info back to AAM2 prefab info.</summary>
        private static readonly Dictionary<string, WeaponInfo> InfoByKey =
            new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, bool> NukeByKey =
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        private static WeaponInfo _gs25Info;
        private static GameObject _gs25Prefab;
        private static MissileDefinition _encyclopediaDef;
        private static MissileDefinition _encyclopediaDefNuke;
        private const float ConventionalBusBlastYield = 19f;
        private const float ConventionalGs25BlastYield = 15f;
        private const string BusPrefabConvName = "ACM_119_busPrefab";
        private const string BusPrefabNukeName = "ACNM_118_busPrefab";

        private static GameObject _busPrefabConv;
        private static GameObject _busPrefabNuke;

        private sealed class PendingFire
        {
            public Unit owner;
            public bool nuke;
            public float time;
            public WeaponInfo info;
        }

        private static readonly List<PendingFire> PendingFires = new List<PendingFire>();

        internal static bool IsAgmTKey(string key)
        {
            return AgmTGateMathService.IsAgmTKey(key, PackKey, NukePackKey, LegacyPackKey, LegacyPackKey119);
        }

        internal static bool IsAgmTNukeKey(string key)
        {
            return AgmTGateMathService.IsAgmTNukeKey(
                key, NukePackKey, NukeKeySuffix, PackKey, LegacyPackKey, LegacyPackKey119);
        }

        internal static bool IsAgmTInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (InfoIds.Contains(info.GetInstanceID()) || NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            string n = info.shortName != null ? info.shortName : string.Empty;
            string w = info.weaponName != null ? info.weaponName : string.Empty;
            return n.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAgmTNukeInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            if (!IsAgmTInfo(info))
                return false;
            string n = ((info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.weaponName != null ? info.weaponName : string.Empty));
            return info.nuclear
                || n.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("1.5kt", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("[1.5", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAgmTMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsAgmTKey(mount.jsonKey))
                return true;
            return IsAgmTInfo(mount.info);
        }

        internal static bool IsAgmTNukeMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsAgmTNukeKey(mount.jsonKey))
                return true;
            return IsAgmTNukeInfo(mount.info);
        }

        internal static bool IsAgmTMissile(Missile missile)
        {
            if (missile == null)
                return false;
            if (missile.GetComponent<AgmTDispenser>() != null)
                return true;
            try
            {
                if (missile.definition != null)
                {
                    if (IsAgmTKey(missile.definition.jsonKey))
                        return true;
                    string un = missile.definition.unitName != null ? missile.definition.unitName : string.Empty;
                    if (un.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                        || un.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                        || un.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                        || un.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Inject once; always re-register with a populated encyclopedia / mount cache.</summary>
        private static float _nextMissingLog;
        private static bool _legacyStampsCleared;

        internal static bool HasUsableClones()
        {
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && m.prefab != null)
                    return true;
            }
            return false;
        }

        private static int CountUsableClones()
        {
            int n = 0;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && m.prefab != null)
                    n++;
            }
            return n;
        }

        /// <summary>
        /// Scene unload can destroy Instantiated WeaponMount clones. If none remain usable,
        /// drop _injected so FirstInject recreates ACM-119 / ACNM-118.
        /// </summary>
        private static void ResetDeadInjection()
        {
            for (int i = MountClones.Count - 1; i >= 0; i--)
            {
                WeaponMount m = MountClones[i];
                if (m == null)
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
                Plugin.Log.LogWarning("ACM-119 / ACNM-118: mount clones gone — will re-inject");
        }

        internal static void Ensure()
        {
            DestroyLegacyStampedBusPrefabs();
            ResetDeadInjection();

            WeaponInfo gs25Probe = _injected ? _gs25Info : FindGs25Info();
            bool gs25Ready = gs25Probe != null && gs25Probe.weaponPrefab != null;
            WeaponMount[] allProbe = null;
            bool mountsAvailable = _injected;
            if (!_injected)
            {
                allProbe = Resources.FindObjectsOfTypeAll<WeaponMount>();
                mountsAvailable = allProbe != null && allProbe.Length > 0;
            }

            AgmTLifecycleGateService.EnsurePath path = AgmTLifecycleGateService.ResolveEnsure(
                Plugin.EnableAgmT.Value, _injected, gs25Ready, mountsAvailable);

            if (path == AgmTLifecycleGateService.EnsurePath.Disabled)
            {
                if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 30f;
                    Plugin.Log.LogWarning("ACM-119 / ACNM-118: EnableAgmT=false (set Features.EnableAgmT=true)");
                }
                return;
            }

            if (path == AgmTLifecycleGateService.EnsurePath.WaitGs25)
            {
                if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 8f;
                    Plugin.Log.LogWarning("ACM-119 / ACNM-118: waiting for GS25 weapon prefab...");
                }
                return;
            }

            if (path == AgmTLifecycleGateService.EnsurePath.WaitMounts)
            {
                if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 8f;
                    Plugin.Log.LogWarning("ACM-119 / ACNM-118: waiting for WeaponMount assets...");
                }
                return;
            }

            if (path == AgmTLifecycleGateService.EnsurePath.FirstInject)
            {
                _gs25Info = gs25Probe;
                _gs25Prefab = gs25Probe.weaponPrefab;
                WeaponMount[] all = allProbe;
                Encyclopedia enc = Plugin.GetEncyclopedia();
                EnsureEncyclopediaDef(enc, false);
                EnsureEncyclopediaDef(enc, true);
                int added = 0;

                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount src = all[i];
                    if (src == null || src.info == null || !src.info.missile)
                        continue;
                    if (IsAgmTKey(src.jsonKey) || Plugin.IsIalKey(src.jsonKey))
                        continue;
                    if (!IsAam29Mount(src))
                        continue;

                    string baseKey = !string.IsNullOrEmpty(src.jsonKey) ? src.jsonKey
                        : (!string.IsNullOrEmpty(src.name) ? src.name : "AAM2");
                    string agmKey = RemapAamKey(baseKey, false);
                    string nukeKey = RemapAamKey(baseKey, true);

                    if (!CreatedKeys.Contains(agmKey))
                    {
                        if (CreateMountVariant(src, enc, agmKey, false, ref added))
                            CreatedKeys.Add(agmKey);
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
                    Plugin.Log.LogInfo("ACM-119 / ACNM-118: injected " + added + " mounts");
                }
                else if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 12f;
                    Plugin.Log.LogWarning("ACM-119 / ACNM-118: GS25 ok but no AAM-29 donor mounts matched yet");
                }
            }

            if (_injected)
            {
                // Cheap no-op when healthy. Required immediately after tearing down
                // stamped NetworkIdentity templates (Mirage "already been spawned").
                RepairBrokenPrefabs();
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
        }

        private static float _nextHardpointInject;
        private static float _nextMaintAt;
        private static int _hardpointIdlePasses;
        private static int _lastHardpointLogAdded = -1;
        private static WeaponMount _cachedAam29Donor;
        private static float _nextDonorProbe;
        private static readonly HashSet<int> ShipWmIds = new HashSet<int>();
        private static readonly HashSet<int> AirWmIds = new HashSet<int>();

        /// <summary>
        /// Periodic full pass: wire ACM-119 / ACNM-118 onto all aircraft hardpoints.
        /// Throttled — do not call from WeaponManager.Awake (use InjectIntoWeaponManager).
        /// </summary>
        internal static void InjectIntoAircraftHardpoints()
        {
            if (!_injected || MountClones.Count == 0)
                return;

            float backoff = AgmTLifecycleGateService.HardpointBackoffSec(_hardpointIdlePasses);
            _nextHardpointInject = Time.unscaledTime + backoff;

            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers == null || managers.Length == 0)
                return;

            int added = 0;
            for (int i = 0; i < managers.Length; i++)
                added += InjectIntoWeaponManager(managers[i]);

            _hardpointIdlePasses = AgmTLifecycleGateService.NextIdlePasses(_hardpointIdlePasses, added);
            if (AgmTLifecycleGateService.ShouldLogHardpointWire(added, _lastHardpointLogAdded)
                && Plugin.Log != null)
            {
                _lastHardpointLogAdded = added;
                Plugin.Log.LogInfo("ACM-119 / ACNM-118: wired " + added + " options onto aircraft hardpoints");
            }
        }

        /// <summary>
        /// Wire ACM-119 / ACNM-118 onto one aircraft WeaponManager only (spawn-safe, no global scan).
        /// </summary>
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
                if (AgmTLifecycleGateService.ShouldSkipHardpoint(false, false, HardpointAcceptsMissiles(hs)))
                    continue;
                added += AddAgmTToHardpoint(hs);
            }
            return added;
        }

        /// <summary>
        /// ACM-119 is aircraft-pylon only. Skip ships, SAM trucks, and buildings.
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
                return AgmTLifecycleGateService.HardpointAcceptsMissiles(false);
            bool any = false;
            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null || m.info == null)
                    continue;
                if (m.info.missile)
                {
                    any = true;
                    break;
                }
            }
            return AgmTLifecycleGateService.HardpointAcceptsMissiles(any);
        }

        private static int AddAgmTToHardpoint(HardpointSet hs)
        {
            HashSet<WeaponMount> want = new HashSet<WeaponMount>();
            bool matchedAam = false;

            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null || IsAgmTMount(m))
                    continue;
                if (!IsAam29Mount(m))
                    continue;
                matchedAam = true;
                string baseKey = !string.IsNullOrEmpty(m.jsonKey) ? m.jsonKey
                    : (!string.IsNullOrEmpty(m.name) ? m.name : "AAM2");
                AddCloneKey(want, RemapAamKey(baseKey, false));
                AddCloneKey(want, RemapAamKey(baseKey, true));
            }

            if (AgmTLifecycleGateService.ResolveHardpointAdd(matchedAam) == AgmTLifecycleGateService.HardpointAddPath.PreferredFallback)
            {
                WeaponMount conv = FindPreferredClone(false);
                WeaponMount nuke = FindPreferredClone(true);
                if (conv != null)
                    want.Add(conv);
                if (nuke != null)
                    want.Add(nuke);
            }

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

        /// <summary>Prefer single-rail ACM/ACNM; fall back to any clone of that family.</summary>
        private static WeaponMount FindPreferredClone(bool nuke)
        {
            string primary;
            string secondary;
            AgmTAssetGateService.PreferredCloneKeys(nuke, PackKey, NukePackKey, out primary, out secondary);
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
                if (nuke ? IsAgmTNukeMount(m) : (IsAgmTMount(m) && !IsAgmTNukeMount(m)))
                    return m;
            }
            return null;
        }

        /// <summary>Expose clones for hangar merge without FindObjectsOfTypeAll.</summary>
        internal static void AppendMountsToList(List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null || have == null || MountClones.Count == 0)
                return;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.prefab == null)
                    continue;
                if (have.Add(m))
                    list.Add(m);
            }
        }

        /// <summary>
        /// Older builds cloned rack GameObjects that Unity later destroyed → null prefab → SpawnMount NRE.
        /// Re-point every AGM-T mount at a live AAM-29 rack / shared vanilla flight prefab.
        /// Never restore Instantiated NetworkIdentity templates (Mirage "already been spawned").
        /// </summary>
        internal static void RepairBrokenPrefabs()
        {
            if (MountClones.Count == 0)
                return;

            bool anyBroken = false;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null)
                    continue;
                if (m.prefab == null
                    || m.info == null
                    || m.info.weaponPrefab == null
                    || IsLegacyStampedBusPrefab(m.info.weaponPrefab))
                {
                    anyBroken = true;
                    break;
                }
            }
            if (!anyBroken)
                return;

            WeaponMount src = GetCachedAam29Donor();
            if (src == null || src.prefab == null)
                return;

            GameObject missilePrefab = ResolveSharedFlightPrefab(
                src.info != null ? src.info.weaponPrefab : null);

            int fixedN = 0;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null)
                    continue;

                bool broken = m.prefab == null;
                if (broken)
                {
                    m.prefab = src.prefab;
                    fixedN++;
                }

                if (m.info != null)
                {
                    GameObject wp = m.info.weaponPrefab;
                    if (wp == null || IsLegacyStampedBusPrefab(wp))
                    {
                        if (missilePrefab != null)
                            m.info.weaponPrefab = missilePrefab;
                    }
                }

                RestoreMountIdentity(m);
            }

            if (fixedN > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("AGM-T: repaired " + fixedN + " mounts with null rack prefab");
        }

        private static WeaponMount GetCachedAam29Donor()
        {
            if (_cachedAam29Donor != null && _cachedAam29Donor && _cachedAam29Donor.prefab != null)
                return _cachedAam29Donor;
            if (Time.unscaledTime < _nextDonorProbe)
                return null;
            _nextDonorProbe = Time.unscaledTime + 12f;
            _cachedAam29Donor = FindAnyAam29Mount();
            return _cachedAam29Donor;
        }

        private static WeaponMount FindAnyAam29Mount()
        {
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all == null)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (m == null || m.prefab == null || m.info == null)
                    continue;
                if (IsAgmTKey(m.jsonKey) || Plugin.IsIalKey(m.jsonKey))
                    continue;
                if (IsAam29Mount(m))
                    return m;
            }
            return null;
        }

        /// <summary>Re-add clone mounts to hangar cache (RefreshMountCache can drop Instantiated assets).</summary>
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
            if (!AgmTAssetGateService.ShouldRegisterWithEncyclopedia(
                enc == null, enc != null && Plugin.IsEncyclopediaPopulated(enc)))
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

        /// <summary>Re-apply AGM-T WeaponInfo / mountName after WeaponMount.Initialize().</summary>
        internal static void RestoreMountIdentity(WeaponMount mount)
        {
            WeaponInfo info = null;
            bool haveInfo = mount != null && !string.IsNullOrEmpty(mount.jsonKey)
                && InfoByKey.TryGetValue(mount.jsonKey, out info) && info != null;
            if (AgmTAssetGateService.ResolveRestoreIdentity(
                    mount == null, mount == null || string.IsNullOrEmpty(mount.jsonKey), haveInfo)
                == AgmTAssetGateService.IdentityPath.Skip)
                return;

            try
            {
                if (mount.prefab != null)
                {
                    Weapon[] rails = mount.prefab.GetComponentsInChildren<Weapon>(true);
                    int guns = 0;
                    int total = 0;
                    for (int i = 0; i < rails.Length; i++)
                    {
                        if (rails[i] == null) continue;
                        total++;
                        if (rails[i] is Gun) guns++;
                    }
                    int n = AgmTAssetGateService.CountNonGunRails(total, guns);
                    if (n > 0)
                        mount.ammo = n;
                }
            }
            catch { }

            bool nuke = false;
            NukeByKey.TryGetValue(mount.jsonKey, out nuke);
            string wantName = nuke ? DisplayNameNuke : DisplayName;
            // ACNM-118: [IAL] [1.5kt] — never [10kt]
            if (info != null)
            {
                string wn = nuke ? DisplayNameNuke : DisplayName;
                string sn = wn;
                Plugin.StripTag(ref wn, "[10kt]");
                Plugin.StripTag(ref sn, "[10kt]");
                info.weaponName = wn;
                info.shortName = sn;
                VeyrnMissileIcon.ApplyTo(info);
            }
            mount.info = info;
            mount.mountName = AgmTAssetGateService.FormatMountDisplayName(wantName, mount.ammo);
        }

        /// <summary>Only known clones — never FindObjectsOfTypeAll (hangar hot path).</summary>
        internal static void RestoreAllMountIdentities()
        {
            for (int i = 0; i < MountClones.Count; i++)
                RestoreMountIdentity(MountClones[i]);
        }

        private static bool CreateMountVariant(WeaponMount src, Encyclopedia enc, string agmKey, bool nuke, ref int added)
        {
            WeaponMount clone = UnityEngine.Object.Instantiate(src);
            clone.name = AgmTAssetGateService.CloneMountObjectName(nuke, src.name);
            clone.jsonKey = agmKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

            WeaponInfo infoClone = UnityEngine.Object.Instantiate(src.info);
            infoClone.name = AgmTAssetGateService.CloneInfoObjectName(nuke);
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
            if (nuke)
                infoClone.blastDamage = GetAgmTNukeYield();
            else
                infoClone.blastDamage = 0f;

            RoleIdentity eff = infoClone.effectiveness;
            eff.antiSurface = AgmTAssetGateService.DualRoleAntiSurface;
            eff.antiAir = AgmTAssetGateService.DualRoleAntiAir;
            eff.antiMissile = 0f;
            eff.antiRadar = 0f;
            infoClone.effectiveness = eff;

            TargetRequirements tr = infoClone.targetRequirements;
            tr.minRange = AgmTAssetGateService.TargetMinRangeM;
            tr.maxRange = AgmTAssetGateService.TargetMaxRangeM;
            tr.maxSpeed = AgmTAssetGateService.TargetMaxSpeed;
            tr.minAltitude = AgmTAssetGateService.TargetMinAltM;
            tr.maxAltitude = AgmTAssetGateService.TargetMaxAltM;
            infoClone.targetRequirements = tr;
            VeyrnMissileIcon.ApplyTo(infoClone);

            // Hangar rack + flight body MUST share vanilla AAM-29 prefabs.
            // Instantiating a NetworkIdentity into DontDestroyOnLoad makes Mirage treat
            // the template as a scene object ("already been spawned") — missiles vanish on fire.
            // Bus identity is applied in OnSpawned / SetupBus (same pattern as Kh-85MT).
            clone.prefab = src.prefab;
            if (src.info != null && src.info.weaponPrefab != null)
            {
                GameObject flight = ResolveSharedFlightPrefab(src.info.weaponPrefab);
                if (flight != null)
                    infoClone.weaponPrefab = flight;
            }

            clone.info = infoClone;
            // Keep multi-rail ammo from source (double/triple/…). Initialize may under-count.
            if (src.ammo > 0)
                clone.ammo = src.ammo;
            clone.mountName = AgmTAssetGateService.FormatMountDisplayName(
                nuke ? DisplayNameNuke : DisplayName, clone.ammo);

            InfoIds.Add(infoClone.GetInstanceID());
            if (nuke)
                NukeInfoIds.Add(infoClone.GetInstanceID());
            InfoByKey[agmKey] = infoClone;
            NukeByKey[agmKey] = nuke;
            MountClones.Add(clone);
            if (Plugin.CachedMountSet.Add(clone))
                Plugin.CachedMounts.Add(clone);

            if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(agmKey))
                Encyclopedia.WeaponLookup[agmKey] = clone;

            try
            {
                if (enc != null && enc.IndexLookup != null && !enc.IndexLookup.Contains(clone))
                {
                    enc.IndexLookup.Add(clone);
                    INetworkDefinition nd = clone;
                    nd.LookupIndex = enc.IndexLookup.Count - 1;
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("AGM-T IndexLookup: " + ex.Message);
            }

            added++;
            return true;
        }

        private static void EnsureEncyclopediaDef(Encyclopedia enc, bool nuke)
        {
            if (enc == null || enc.missiles == null)
                return;
            if (!AgmTAssetGateService.ShouldEnsureEncyclopediaDef(
                nuke, _encyclopediaDef != null, _encyclopediaDefNuke != null))
                return;

            MissileDefinition src = FindAam2Definition(enc);
            if (src == null)
                return;

            string key = nuke ? NukePackKey : PackKey;
            MissileDefinition clone = UnityEngine.Object.Instantiate(src);
            clone.name = AgmTAssetGateService.CloneEncyclopediaDefName(nuke);
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
            RoleIdentity role = clone.roleIdentity;
            role.antiSurface = AgmTAssetGateService.EncRoleAntiSurface;
            role.antiAir = AgmTAssetGateService.EncRoleAntiAir;
            role.antiMissile = 0f;
            role.antiRadar = 0f;
            clone.roleIdentity = role;

            if (!enc.missiles.Contains(clone))
                enc.missiles.Add(clone);
            if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(key))
                Encyclopedia.Lookup[key] = clone;

            if (nuke)
                _encyclopediaDefNuke = clone;
            else
                _encyclopediaDef = clone;
        }

        private static MissileDefinition FindAam2Definition(Encyclopedia enc)
        {
            if (enc != null && enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d != null && AgmTAssetGateService.IsAam2DefinitionKey(d.jsonKey))
                        return d;
                }
            }
            MissileDefinition[] all = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && AgmTAssetGateService.IsAam2DefinitionKey(all[i].jsonKey))
                    return all[i];
            }
            return null;
        }

        private static bool IsAam29Mount(WeaponMount m)
        {
            if (m == null)
                return false;
            string sn = m.info != null && m.info.shortName != null ? m.info.shortName : string.Empty;
            string wn = m.info != null && m.info.weaponName != null ? m.info.weaponName : string.Empty;
            return AgmTAssetGateService.IsAam29Donor(m.jsonKey, sn, wn);
        }

        private static string RemapAamKey(string aamKey, bool nuke)
        {
            return AgmTGateMathService.RemapAamKey(aamKey, nuke, PackKey, NukePackKey);
        }

        private static WeaponInfo FindGs25Info()
        {
            if (_gs25Info != null)
                return _gs25Info;
            WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            for (int i = 0; i < infos.Length; i++)
            {
                WeaponInfo info = infos[i];
                if (info == null)
                    continue;
                string sn = info.shortName != null ? info.shortName : string.Empty;
                string wn = info.weaponName != null ? info.weaponName : string.Empty;
                string nm = info.name != null ? info.name : string.Empty;
                if (AgmTAssetGateService.IsGs25InfoName(sn, wn, nm))
                {
                    if (info.weaponPrefab != null)
                        return info;
                }
            }
            return null;
        }

        internal static float GetAgmTNukeYield()
        {
            // Mount encyclopedia blastDamage uses same scaled yield as in-flight warhead
            return Plugin.GetAgmTNukeBlastYield();
        }

        /// <summary>UnitDefinition.value is in millions (UnitConverter.ValueReading / StrategicArsenal Allocation).</summary>
        internal static float GetUnitValueMillions(Unit u)
        {
            if (u == null)
                return 0f;
            try
            {
                UnitDefinition def = u.definition;
                if (def != null)
                    return def.value;
            }
            catch { }
            return 0f;
        }

        internal static float NukeMinTargetValueM()
        {
            return Plugin.AgmTNukeMinTargetValueM != null ? Plugin.AgmTNukeMinTargetValueM.Value : 25f;
        }

        internal static float NukeProximityM()
        {
            return Plugin.AgmTNukeProximityM != null ? Plugin.AgmTNukeProximityM.Value : 500f;
        }

        internal static bool MeetsValueGate(Unit u, bool nukeVariant)
        {
            if (!nukeVariant || u == null)
                return true;
            return AgmTGateMathService.MeetsValueGate(
                GetUnitValueMillions(u), NukeMinTargetValueM(), nukeVariant);
        }

        internal static bool IsAcnmNuclearMissile(Missile missile)
        {
            if (missile == null)
                return false;
            AgmTSubBrain brain = missile.GetComponent<AgmTSubBrain>();
            if (brain != null && brain.IsNukeVariant)
                return true;
            AgmTDispenser bus = missile.GetComponent<AgmTDispenser>();
            if (bus != null && bus.IsNukeVariant)
                return true;
            return IsAgmTNukeInfo(GetMissileInfo(missile));
        }

        internal static bool MeetsNuclearArmConditions(Missile missile)
        {
            if (missile == null)
                return false;
            bool isSub = false;
            try { isSub = missile.GetComponent<AgmTSubBrain>() != null; }
            catch { }
            float minT;
            float minD;
            if (isSub)
            {
                // Time-only: close-in hits after coast still arm (80m AND blocked pack intercepts).
                minT = AgmTGateMathService.SubNukeArmMinFlightSec;
                minD = 0f;
            }
            else
            {
                minT = Plugin.AgmTNukeArmMinFlightTime != null ? Plugin.AgmTNukeArmMinFlightTime.Value : 6f;
                minD = Plugin.AgmTNukeArmMinDistance != null ? Plugin.AgmTNukeArmMinDistance.Value : 2000f;
            }
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch
            {
                AgmTSubBrain b = missile.GetComponent<AgmTSubBrain>();
                if (b != null)
                    age = b.AgeSeconds;
                else
                {
                    AgmTDispenser d = missile.GetComponent<AgmTDispenser>();
                    if (d != null)
                        age = d.AgeSeconds;
                }
            }
            Vector3 spawn = Vector3.zero;
            bool haveSpawn = false;
            AgmTSubBrain brain = missile.GetComponent<AgmTSubBrain>();
            if (brain != null)
            {
                spawn = brain.SpawnPosition;
                haveSpawn = true;
            }
            else
            {
                AgmTDispenser bus = missile.GetComponent<AgmTDispenser>();
                if (bus != null)
                {
                    spawn = bus.SpawnPosition;
                    haveSpawn = true;
                }
            }
            float dist = haveSpawn ? (missile.transform.position - spawn).magnitude : 0f;
            return AgmTGateMathService.MeetsArmConditions(age, minT, dist, minD, haveSpawn);
        }

        internal static Unit ResolveIntendedTarget(Missile missile)
        {
            if (missile == null)
                return null;
            AgmTSubBrain brain = missile.GetComponent<AgmTSubBrain>();
            if (brain != null)
            {
                Unit intended = brain.IntendedTarget;
                if (intended != null && Plugin.IsUnitAlive(intended))
                    return intended;
            }
            try
            {
                Unit t = Plugin.ResolveDesignatedTarget(missile);
                if (t != null && Plugin.IsUnitAlive(t))
                    return t;
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                Unit u;
                if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u) && Plugin.IsUnitAlive(u))
                    return u;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Bus: nuke only when armed and within NukeProximityM of intended target.
        /// GS25: once the short sub arm delay is met, the bomblet itself is the warhead.
        /// </summary>
        internal static bool ShouldAllowNuclearDetonation(Missile missile)
        {
            if (!IsAcnmNuclearMissile(missile))
                return false;
            if (!MeetsNuclearArmConditions(missile))
                return false;
            try
            {
                if (missile.GetComponent<AgmTSubBrain>() != null)
                    return true;
            }
            catch { }
            Unit tgt = ResolveIntendedTarget(missile);
            if (tgt == null)
                return false;
            try
            {
                float sqr = (missile.transform.position - tgt.transform.position).sqrMagnitude;
                return AgmTGateMathService.WithinNukeProximity(sqr, NukeProximityM());
            }
            catch { return false; }
        }

        internal static void SyncFromMount(Weapon weapon, WeaponMount mount)
        {
            if (AgmTAssetGateService.ResolveSyncFromMount(
                    weapon == null, mount == null, weapon is Gun, IsAgmTMount(mount))
                == AgmTAssetGateService.SyncPath.Skip)
                return;
            RestoreMountIdentity(mount);
            if (mount.info == null)
                return;
            weapon.info = mount.info;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("AGM-T: synced WeaponInfo onto " + weapon.name
                    + " -> " + mount.info.weaponName);
        }

        internal static void NoteFire(Weapon weapon)
        {
            WeaponMount mount = weapon != null ? Plugin.GetWeaponMount(weapon) : null;
            WeaponInfo stationInfo = null;
            try
            {
                if (weapon != null && WeaponStationField != null)
                {
                    WeaponStation st = WeaponStationField.GetValue(weapon) as WeaponStation;
                    if (st != null)
                        stationInfo = st.WeaponInfo;
                }
            }
            catch { }

            bool isAgm = IsAgmTMount(mount)
                || (weapon != null && IsAgmTInfo(weapon.info))
                || IsAgmTInfo(stationInfo);
            if (!isAgm && mount != null)
                isAgm = AgmTAssetGateService.LooksLikeAgmTMountName(mount.mountName, mount.jsonKey);

            if (AgmTAssetGateService.ResolveNoteFire(
                    Plugin.EnableAgmT.Value, weapon == null, isAgm)
                == AgmTAssetGateService.NoteFirePath.Skip)
                return;

            RestoreMountIdentity(mount);
            if (mount != null && mount.info != null)
                weapon.info = mount.info;

            PendingFire pf = new PendingFire();
            pf.owner = weapon.attachedUnit;
            if (pf.owner == null)
            {
                try { pf.owner = weapon.attachedUnit; }
                catch { }
            }
            pf.time = Time.time;
            pf.info = weapon.info;
            pf.nuke = AgmTAssetGateService.ResolvePendingNuke(
                mount != null && IsAgmTNukeKey(mount.jsonKey),
                IsAgmTNukeMount(mount),
                IsAgmTNukeInfo(weapon.info),
                IsAgmTNukeInfo(stationInfo));
            PendingFires.Add(pf);

            Plugin.Log.LogInfo("AGM-T: NoteFire pending#" + PendingFires.Count
                + " nuke=" + pf.nuke
                + " mount=" + (mount != null ? mount.jsonKey : "?"));
            ResyncOwnerRails(pf.owner);
        }

        private static readonly FieldInfo WeaponStationField = AccessTools.Field(typeof(Weapon), "weaponStation");

        /// <summary>True for AGM-T bus / GS25 children — never consume IAL pending nukes.</summary>
        internal static bool ShouldBlockIalPendingNuke(Missile missile)
        {
            if (missile == null)
                return false;
            return AgmTLifecycleGateService.ShouldBlockIalPending(
                missile.GetComponent<AgmTDispenser>() != null,
                missile.GetComponent<AgmTSubBrain>() != null,
                PendingFires.Count > 0 && LooksLikeAam29Bus(missile),
                IsAgmTMissile(missile) || IsGs25Child(missile));
        }

        internal static void OnSpawned(Missile missile)
        {
            OnSpawned(missile, null);
        }

        internal static void OnSpawned(Missile missile, Unit spawnOwner)
        {
            if (missile == null || !Plugin.EnableAgmT.Value)
                return;
            // ConsumePending only after confirming this is not an existing bus/sub/GS25.
            if (missile.GetComponent<AgmTDispenser>() != null
                || missile.GetComponent<AgmTSubBrain>() != null
                || IsGs25Child(missile))
                return;

            bool pendingNuke = false;
            WeaponInfo pendingInfo = null;
            bool pending = ConsumePending(missile, spawnOwner, out pendingNuke, out pendingInfo);
            bool ours = pending
                || IsLegacyStampedBusPrefab(missile.gameObject)
                || IsAgmTMissile(missile)
                || IsAgmTInfo(GetMissileInfo(missile));
            AgmTLifecycleGateService.SpawnPath path = AgmTLifecycleGateService.ResolveOnSpawn(
                true,
                false,
                false,
                false,
                false,
                ours,
                ours);
            if (path != AgmTLifecycleGateService.SpawnPath.SetupBus)
                return;

            if (!pendingNuke)
                pendingNuke = IsAgmTNukeInfo(GetMissileInfo(missile))
                    || IsAgmTNukeMissileName(missile);
            SetupBus(missile, pendingNuke, pendingInfo);
        }

        /// <summary>GS25 cluster children — skip MultiMode GuideTo (optical Seek + SubBrain).</summary>
        internal static int AcmGs25SpawnDepth;

        internal static bool IsPoweredGs25Sub(Missile missile)
        {
            if (missile == null)
                return false;
            try { return missile.GetComponent<AgmTSubBrain>() != null; }
            catch { return false; }
        }

        internal static bool IsGs25Submunition(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.GetComponent<AgmTSubBrain>() != null)
                    return true;
            }
            catch { }
            return IsGs25Child(missile);
        }

        /// <summary>True once bus dispenser is on the missile — skip MultiMode GuideTo fights.</summary>
        internal static bool HasBusDispenser(Missile missile)
        {
            return missile != null && missile.GetComponent<AgmTDispenser>() != null;
        }

        /// <summary>GS25 submunitions ignore nuclear shock impulse / blast kill.</summary>
        internal static bool IsShockImmuneSubmunition(Missile missile)
        {
            return missile != null
                && (Plugin.AgmTSubNukeImmune == null || Plugin.AgmTSubNukeImmune.Value)
                && missile.GetComponent<AgmTSubBrain>() != null;
        }

        internal static bool IsShockImmuneUnit(Unit unit)
        {
            Missile m = unit as Missile;
            return IsShockImmuneSubmunition(m);
        }

        internal static bool IsGs25Child(Missile missile)
        {
            if (missile == null)
                return false;
            string n = missile.name != null ? missile.name : string.Empty;
            string k = string.Empty;
            string u = string.Empty;
            try
            {
                if (missile.definition != null)
                {
                    k = missile.definition.jsonKey != null ? missile.definition.jsonKey : string.Empty;
                    u = missile.definition.unitName != null ? missile.definition.unitName : string.Empty;
                }
            }
            catch { }
            return AgmTLifecycleGateService.IsGs25ChildName(n, k, u);
        }

        private static readonly FieldInfo MissileInfoField = AccessTools.Field(typeof(Missile), "info");

        private static WeaponInfo GetMissileInfo(Missile missile)
        {
            try
            {
                return MissileInfoField != null
                    ? MissileInfoField.GetValue(missile) as WeaponInfo
                    : null;
            }
            catch { return null; }
        }

        private static bool ConsumePending(Missile missile, Unit spawnOwner, out bool wasNuke, out WeaponInfo info)
        {
            wasNuke = false;
            info = null;
            PrunePending();
            if (PendingFires.Count <= 0)
                return false;
            if (Plugin.IsGunShellMissile(missile) || Plugin.IsBallisticMissile(missile))
                return false;

            bool oursPrefab = IsLegacyStampedBusPrefab(missile.gameObject);
            bool identifiable = oursPrefab
                || IsAgmTMissile(missile)
                || IsAgmTInfo(GetMissileInfo(missile));
            // Shared vanilla AAM-29 airframe: consume by owner match when it looks like the bus.
            if (!identifiable && !LooksLikeAam29Bus(missile))
                return false;

            Unit owner = spawnOwner != null ? spawnOwner : (missile != null ? missile.owner : null);
            int pick = -1;
            for (int i = 0; i < PendingFires.Count; i++)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null)
                    continue;
                if (OwnerMatchesPending(pf.owner, owner))
                {
                    pick = i;
                    break;
                }
            }
            // Stamped bus prefab does not need an owner match.
            if (pick < 0 && oursPrefab)
                pick = 0;
            if (pick < 0)
                return false;

            PendingFire taken = PendingFires[pick];
            PendingFires.RemoveAt(pick);
            wasNuke = taken != null && taken.nuke;
            info = taken != null ? taken.info : null;
            if (oursPrefab && IsAgmTNukeMissileName(missile))
                wasNuke = true;
            return true;
        }

        private static void PrunePending()
        {
            float now = Time.time;
            for (int i = PendingFires.Count - 1; i >= 0; i--)
            {
                PendingFire pf = PendingFires[i];
                if (pf == null
                    || now - pf.time > AgmTLifecycleGateService.PendingTimeoutSec)
                    PendingFires.RemoveAt(i);
            }
        }

        private static bool OwnerMatchesPending(Unit pendingOwner, Unit spawnOwner)
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

        private static bool LooksLikeAam29Bus(Missile missile)
        {
            if (missile == null)
                return false;
            string n = missile.name != null ? missile.name : string.Empty;
            string k = string.Empty;
            string u = string.Empty;
            try
            {
                if (missile.definition != null)
                {
                    k = missile.definition.jsonKey != null ? missile.definition.jsonKey : string.Empty;
                    u = missile.definition.unitName != null ? missile.definition.unitName : string.Empty;
                }
            }
            catch { }
            return AgmTLifecycleGateService.LooksLikeAam29BusName(n, k, u);
        }

        private static void SetupBus(Missile missile, bool nuke)
        {
            SetupBus(missile, nuke, null);
        }

        private static void SetupBus(Missile missile, bool nuke, WeaponInfo sourceInfo)
        {
            if (missile.GetComponent<AgmTDispenser>() != null)
                return;

            try
            {
                MissileDefinition def = nuke ? _encyclopediaDefNuke : _encyclopediaDef;
                if (def != null)
                    missile.definition = def;
                string want = nuke ? DisplayNameNuke : DisplayName;
                missile.NetworkunitName = want;
                try { missile.name = want; }
                catch { }
            }
            catch { }

            WeaponInfo info = sourceInfo;
            if (info == null || !IsAgmTInfo(info))
                info = GetMissileInfo(missile);
            if (info == null || !IsAgmTInfo(info))
                info = FindInfoForNuke(nuke);
            if (info != null && MissileInfoField != null)
            {
                try { MissileInfoField.SetValue(missile, info); }
                catch { }
            }

            if (nuke)
                Plugin.ApplyAgmTNukeWarhead(missile);
            else
            {
                Plugin.EnsureConventionalWarhead(missile, ConventionalBusBlastYield);
                Plugin.ApplyAgmTDragReduction(missile);
            }

            GameObject sub = _gs25Prefab;
            if (sub == null && _gs25Info != null)
                sub = _gs25Info.weaponPrefab;
            if (sub == null)
                sub = Gs25Prefab;

            AgmTDispenser d = Plugin.TryAddBehaviour<AgmTDispenser>(missile.gameObject);
            if (d == null)
                return;
            d.Configure(sub, nuke);
            // Visual-only AAM-IV body (GS25 children never call this)
            if (Plugin.AgmTBusCustomVisual == null || Plugin.AgmTBusCustomVisual.Value)
                AgmTBusVisual.ApplyToBus(missile);
            Plugin.Log.LogInfo("AGM-T: dispenser attached nuke=" + nuke
                + " gs25=" + (sub != null ? sub.name : "NULL")
                + " on " + missile.name);
        }

        /// <summary>
        /// Old Instantiated DDOL templates named ACM_119_busPrefab / ACNM_118_busPrefab.
        /// Mirage treats those NetworkIdentities as already-spawned scene objects.
        /// </summary>
        private static bool IsLegacyStampedBusPrefab(GameObject go)
        {
            if (go == null)
                return false;
            string n = go.name != null ? go.name : string.Empty;
            return n.IndexOf(BusPrefabConvName, StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf(BusPrefabNukeName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static GameObject ResolveSharedFlightPrefab(GameObject donor)
        {
            DestroyLegacyStampedBusPrefabs();
            if (donor == null || IsLegacyStampedBusPrefab(donor))
                return null;
            return donor;
        }

        /// <summary>
        /// Tear down Instantiated NetworkIdentity bus templates. Same Mirage bug Kh-85MT hit:
        /// "already been spawned" then StartMissile NRE and the missile vanishes.
        /// </summary>
        private static void DestroyLegacyStampedBusPrefabs()
        {
            if (_legacyStampsCleared)
                return;
            _legacyStampsCleared = true;
            int n = 0;
            try
            {
                if (_busPrefabConv != null)
                {
                    UnityEngine.Object.Destroy(_busPrefabConv);
                    _busPrefabConv = null;
                    n++;
                }
                if (_busPrefabNuke != null)
                {
                    UnityEngine.Object.Destroy(_busPrefabNuke);
                    _busPrefabNuke = null;
                    n++;
                }
                GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i];
                    if (!IsLegacyStampedBusPrefab(go))
                        continue;
                    try
                    {
                        UnityEngine.Object.Destroy(go);
                        n++;
                    }
                    catch { }
                }
            }
            catch { }
            if (n > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("ACM-119 / ACNM-118: cleared " + n
                    + " stamped bus prefabs (network-safe shared AAM-29 body)");
        }

        private static bool IsAgmTNukeMissileName(Missile missile)
        {
            if (missile == null)
                return false;
            string n = missile.name != null ? missile.name : string.Empty;
            if (n.IndexOf(BusPrefabNukeName, StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            try
            {
                if (missile.definition != null)
                    return IsAgmTNukeKey(missile.definition.jsonKey);
            }
            catch { }
            return false;
        }

        private static WeaponInfo FindInfoForNuke(bool nuke)
        {
            foreach (KeyValuePair<string, WeaponInfo> kv in InfoByKey)
            {
                string key = kv.Key;
                WeaponInfo info = kv.Value;
                if (info == null)
                    continue;
                if (nuke)
                {
                    if (IsAgmTNukeKey(key) || IsAgmTNukeInfo(info))
                        return info;
                }
                else if (IsAgmTKey(key) && !IsAgmTNukeKey(key))
                    return info;
            }
            return null;
        }

        private static void ResyncOwnerRails(Unit owner)
        {
            if (owner == null)
                return;
            Weapon[] weapons = null;
            try { weapons = owner.GetComponentsInChildren<Weapon>(true); }
            catch { return; }
            if (weapons == null)
                return;
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun)
                    continue;
                WeaponMount mount = Plugin.GetWeaponMount(w);
                if (mount == null || !IsAgmTMount(mount))
                    continue;
                SyncFromMount(w, mount);
            }
        }

        internal static GameObject Gs25Prefab
        {
            get
            {
                if (_gs25Prefab != null)
                    return _gs25Prefab;
                WeaponInfo info = FindGs25Info();
                return info != null ? info.weaponPrefab : null;
            }
        }

        internal static void PrepareSubmunition(Missile child, bool nuke, Unit target, bool inheritedLock)
        {
            if (child == null)
                return;
            try { child.NetworkunitName = nuke ? "GS25 [IAL] [1.5kt]" : "GS25 [IAL]"; }
            catch { }
            if (nuke)
                Plugin.ApplyAgmTNukeWarhead(child);
            else
            {
                Plugin.EnsureConventionalWarhead(child, ConventionalGs25BlastYield);
                Plugin.ApplyAgmTDragReduction(child);
            }
            StripSubMultiMode(child);
            AgmTSubSteerMathService.ApplyLimits(child);
            try { child.DeployFins(); }
            catch { }
            // Do not SetTarget here — coast then SubBrain (immediate lock = 90° loop).
            if (child.GetComponent<AgmTSubBrain>() == null)
            {
                AgmTSubBrain brain = Plugin.TryAddBehaviour<AgmTSubBrain>(child.gameObject);
                if (brain == null)
                    return;
                brain.Configure(nuke);
                brain.RememberTarget(target, inheritedLock);
            }
            else
            {
                AgmTSubBrain brain = child.GetComponent<AgmTSubBrain>();
                if (brain != null)
                    brain.RememberTarget(target, inheritedLock);
            }
        }

        private static void StripSubMultiMode(Missile child)
        {
            if (child == null)
                return;
            try
            {
                MultiModeBrain brain = child.GetComponent<MultiModeBrain>();
                if (brain != null)
                    UnityEngine.Object.Destroy(brain);
            }
            catch { }
        }
    }

    /// <summary>Bus: self-search densest air/ground pack, then dispense after ≥5s.</summary>
    public class AgmTDispenser : MonoBehaviour
    {
        private Missile _missile;
        private GameObject _subPrefab;
        private bool _nuke;
        private bool _dispensed;
        private bool _safeDiscard;
        private float _spawnTime;
        private Vector3 _spawnPos;
        private float _nextCheck;
        private float _nextHunt;
        private readonly List<Unit> _scratch = new List<Unit>(64);
        private readonly List<Unit> _air = new List<Unit>(32);
        private readonly List<Unit> _ground = new List<Unit>(32);
        private Unit _launchLock;
        private int _lastPumpFrame = -1;
        private static readonly List<AgmTDispenser> Live = new List<AgmTDispenser>(16);
        private static readonly List<AgmTDispenser> PumpScratch = new List<AgmTDispenser>(16);

        public bool IsNukeVariant { get { return _nuke; } }
        public float AgeSeconds { get { return Time.time - _spawnTime; } }
        public Vector3 SpawnPosition { get { return _spawnPos; } }

        public static bool IsSafeDiscard(Missile missile)
        {
            if (missile == null)
                return false;
            AgmTDispenser d = missile.GetComponent<AgmTDispenser>();
            return d != null && d._safeDiscard;
        }

        public void Configure(GameObject subPrefab, bool nuke)
        {
            _subPrefab = subPrefab;
            _nuke = nuke;
            CaptureLaunchLock();
        }

        private void CaptureLaunchLock()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null)
                return;
            Unit t = null;
            try { t = Plugin.ResolveHardLockTarget(_missile); }
            catch { }
            if (t == null)
                t = GetCurrentTarget();
            if (t == null || !Plugin.IsUnitAlive(t))
                return;
            // Player TGP / fire lock: soft IFF. Strict hunt dropped incomplete-HQ
            // ships and buildings, so ACNM inherited nothing and GS25 flew straight.
            if (!Plugin.IsAgmTEngageTarget(_missile, t))
                return;
            _launchLock = t;
        }

        private void Awake()
        {
            _missile = GetComponent<Missile>();
            _spawnTime = Time.time;
            _spawnPos = _missile != null ? _missile.transform.position : Vector3.zero;
            if (_subPrefab == null)
                _subPrefab = AgmTWeapon.Gs25Prefab;
            Live.Add(this);
        }

        private void OnDestroy()
        {
            Live.Remove(this);
        }

        private void FixedUpdate()
        {
            TickBus();
        }

        /// <summary>Packed payload may skip Unity FixedUpdate; HostedTick pumps this.</summary>
        internal static void PumpAll()
        {
            if (Live.Count == 0)
                return;
            PumpScratch.Clear();
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                AgmTDispenser d = Live[i];
                if (d == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                PumpScratch.Add(d);
            }
            for (int i = 0; i < PumpScratch.Count; i++)
                PumpScratch[i].PumpOnce();
            PumpScratch.Clear();
        }

        internal void PumpOnce()
        {
            if (_lastPumpFrame == Time.frameCount)
                return;
            _lastPumpFrame = Time.frameCount;
            TickBus();
        }

        private void TickBus()
        {
            if (_dispensed || _missile == null || _missile.disabled)
                return;

            if (_launchLock == null && AgeSeconds < 0.6f)
                CaptureLaunchLock();

            bool probed = false;
            bool nmNull = true;
            bool serverNonNull = false;
            bool serverActive = false;
            bool isServer = false;
            bool localSim = false;
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                nmNull = nm == null;
                if (nm != null && nm.Server != null)
                {
                    serverNonNull = true;
                    serverActive = nm.Server.Active;
                }
                isServer = _missile.IsServer;
                localSim = _missile.LocalSim;
                probed = true;
            }
            catch { }

            // Probe failure: still try (original). Successful probe: respect server gate.
            if (probed
                && AgmTLifecycleGateService.ResolveBusServerSim(
                    nmNull, serverNonNull, serverActive, isServer, localSim)
                == AgmTLifecycleGateService.ServerSimPath.Skip)
                return;

            if (_subPrefab == null)
                _subPrefab = AgmTWeapon.Gs25Prefab;

            float now = Time.time;
            if (now >= _nextHunt)
            {
                _nextHunt = AgmTDispenseMathService.ScheduleNextHunt(now);
                // Wait out the launch-lock capture window so HuntDensePack
                // cannot stamp a pack onto the missile before we snapshot the player's lock.
                if (_launchLock != null || AgeSeconds >= 0.6f)
                    HuntDensePack();
            }

            if (now < _nextCheck)
                return;
            _nextCheck = AgmTDispenseMathService.ScheduleNextCheck(now);

            float age = now - _spawnTime;
            float minDelay = Plugin.AgmTMinFlightTime != null
                ? Plugin.AgmTMinFlightTime.Value
                : AgmTDispenseMathService.DefaultMinFlightSec;
            float dispenseDist = Plugin.AgmTDispenseDistance != null
                ? Plugin.AgmTDispenseDistance.Value
                : AgmTDispenseMathService.DefaultDispenseDistM;

            if (!AgmTDispenseMathService.PastMinFlight(age, minDelay))
                return;

            // Need GS25 prefab only for dispense (hunt already ran above)
            if (_subPrefab == null)
            {
                _subPrefab = AgmTWeapon.Gs25Prefab;
                if (_subPrefab == null)
                {
                    Plugin.Log.LogWarning("AGM-T: GS25 prefab missing, cannot dispense");
                    return;
                }
            }

            // Early dispense when near a lock; otherwise always open at minDelay
            // (waiting until maxAge caused vanilla seeker self-destruct with zero GS25)
            bool near = false;
            try
            {
                Unit tgt = _launchLock;
                if (tgt == null || !Plugin.IsUnitAlive(tgt))
                    tgt = Plugin.ResolveDesignatedTarget(_missile);
                if (tgt == null)
                    tgt = GetCurrentTarget();
                if (tgt != null)
                {
                    Vector3 a = _missile.transform.position;
                    Vector3 b = tgt.transform.position;
                    near = AgmTDispenseMathService.NearDispenseDistance((a - b).sqrMagnitude, dispenseDist);
                }
            }
            catch { }

            if (AgmTDispenseMathService.ShouldDispense(age, minDelay, near))
                Dispense();
        }

        /// <summary>Force cluster open (Detonate / miss path). Honors 5s floor unless forceEarly.</summary>
        public static bool TryForceDispense(Missile missile, bool forceEarly)
        {
            if (missile == null)
                return false;
            AgmTDispenser d = missile.GetComponent<AgmTDispenser>();
            if (d == null || d._dispensed)
                return false;
            float minDelay = Plugin.AgmTMinFlightTime != null
                ? Plugin.AgmTMinFlightTime.Value
                : AgmTDispenseMathService.DefaultMinFlightSec;
            if (!AgmTDispenseMathService.AllowForceDispense(
                    forceEarly, Time.time - d._spawnTime, minDelay))
                return false;
            if (d._subPrefab == null)
                d._subPrefab = AgmTWeapon.Gs25Prefab;
            d.Dispense();
            return true;
        }

        private Unit GetCurrentTarget()
        {
            try
            {
                PersistentID tid = _missile.targetID;
                Unit u;
                if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u))
                    return u;
            }
            catch { }
            return null;
        }

        /// <summary>Steer bus toward the denser nearby hostile group (air vs ground).</summary>
        private void HuntDensePack()
        {
            if (_launchLock != null && Plugin.IsUnitAlive(_launchLock)
                && Plugin.IsAgmTEngageTarget(_missile, _launchLock))
            {
                try { _missile.SetTarget(_launchLock); }
                catch { }
                return;
            }

            float huntR = Plugin.AgmTBusHuntRadius != null ? Plugin.AgmTBusHuntRadius.Value : 35000f;
            float mmR = Plugin.EffectiveHuntRadius(_missile);
            if (huntR < mmR)
                huntR = mmR;
            _air.Clear();
            _ground.Clear();

            _scratch.Clear();
            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(_missile.GlobalPosition(), huntR, _scratch);
            }
            catch { return; }

            for (int i = 0; i < _scratch.Count; i++)
            {
                Unit u = _scratch[i];
                if (u == null || u is Scenery || u is Missile)
                    continue;
                if (!IsBusHuntTarget(u))
                    continue;
                if (IsAirUnit(u))
                    _air.Add(u);
                else
                    _ground.Add(u);
            }

            List<Unit> pack;
            if (_air.Count == 0 && _ground.Count == 0)
                return;
            if (AgmTDispenseMathService.PreferGroundPack(_nuke, _air.Count, _ground.Count))
                pack = _ground;
            else if (AgmTDispenseMathService.PreferAirPack(_air.Count, _ground.Count))
                pack = _air;
            else
                pack = _ground.Count > 0 ? _ground : _air;

            Unit best = PickBestInPack(pack);
            if (best == null)
                return;

            try
            {
                Unit cur = GetCurrentTarget();
                if (cur != null && Plugin.IsUnitAlive(cur) && IsBusHuntTarget(cur))
                {
                    // Keep player lock if still valid; otherwise retarget densest pack
                    if (object.ReferenceEquals(cur, best) || IsInList(pack, cur))
                        return;
                }
                _missile.SetTarget(best);
            }
            catch { }
        }

        /// <summary>Strict HQ hostility only — soft IsAllowedTarget was seeking friendlies/neutrals.</summary>
        private bool IsBusHuntTarget(Unit u)
        {
            if (u == null || u is Scenery || u is Missile)
                return false;
            if (object.ReferenceEquals(u, _missile) || object.ReferenceEquals(u, _missile.owner))
                return false;
            if (!Plugin.IsUnitAlive(u))
                return false;
            if (Plugin.IsJunkHuntTarget(u))
                return false;
            if (!Plugin.IsAgmTEngageTarget(_missile, u))
                return false;
            // Hangars at the launch airbase: still require both HQs.
            // Ships / vehicles with late HQ must be huntable or the bus never turns.
            if (u is Container)
                return false;
            return Plugin.IsHostileHuntTarget(_missile, u);
        }

        private static bool IsInList(List<Unit> list, Unit u)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (object.ReferenceEquals(list[i], u))
                    return true;
            }
            return false;
        }

        private Unit PickBestInPack(List<Unit> pack)
        {
            Unit best = null;
            float bestScore = float.MinValue;
            Vector3 pos = _missile.transform.position;
            Vector3 fwd = _missile.transform.forward;
            for (int i = 0; i < pack.Count; i++)
            {
                Unit u = pack[i];
                if (u == null)
                    continue;
                Vector3 d = u.transform.position - pos;
                float dist = d.magnitude;
                if (dist < 1f)
                    dist = 1f;
                float align = Vector3.Dot(fwd, d / dist);
                float score = AgmTDispenseMathService.PackCandidateScore(
                    align, dist, AgmTWeapon.GetUnitValueMillions(u));
                if (AgmTDispenseMathService.IsBetterPackScore(score, bestScore))
                {
                    bestScore = score;
                    best = u;
                }
            }
            return best;
        }

        internal static bool IsAirUnit(Unit u)
        {
            if (u == null)
                return false;
            if (u is Aircraft)
                return true;
            try
            {
                if (u.speed > 60f)
                    return true;
            }
            catch { }
            try
            {
                if (u.definition != null)
                {
                    TypeIdentity t = u.definition.typeIdentity;
                    if (t.air > 0.5f)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void Dispense()
        {
            if (_dispensed)
                return;
            _dispensed = true;

            // Disarm + intangibility BEFORE hiding/destroying so the AAM-29 body never detonates
            BeginSafeDiscard();

            int count = AgmTDispenseMathService.ClampSubCount(
                Plugin.AgmTSubCount != null ? Plugin.AgmTSubCount.Value : AgmTDispenseMathService.DefaultSubCount);
            float eject = Plugin.AgmTEjectSpeed != null ? Plugin.AgmTEjectSpeed.Value : 45f;

            Unit inherit = null;
            if (_launchLock != null && Plugin.IsUnitAlive(_launchLock)
                && Plugin.IsAgmTEngageTarget(_missile, _launchLock))
                inherit = _launchLock;
            int follow = 0;
            float lockValueM = 0f;
            if (inherit != null)
            {
                lockValueM = AgmTWeapon.GetUnitValueMillions(inherit);
                follow = AgmTDispenseMathService.FollowLockCount(lockValueM, count);
            }

            Spawner spawner = null;
            try { spawner = NetworkSceneSingleton<Spawner>.i; }
            catch { }
            if (spawner == null)
            {
                Plugin.Log.LogWarning("AGM-T: Spawner missing, cannot dispense GS25");
                FinishBus();
                return;
            }

            Vector3 basePos = _missile.transform.position;
            Quaternion rot = _missile.transform.rotation;
            Vector3 baseVel = _missile.rb != null ? _missile.rb.velocity : Vector3.zero;
            Unit owner = ResolveLaunchOwner(_missile);
            PersistentID ownerPid = ResolveLaunchOwnerId(_missile, owner);

            Missile[] children = new Missile[count];
            int spawned = 0;
            AgmTWeapon.AcmGs25SpawnDepth++;
            try
            {
            for (int i = 0; i < count; i++)
            {
                Vector3 radial, posOff, velKick;
                AgmTDispenseMathService.EjectRingOffsets(
                    i, count,
                    _missile.transform.right, _missile.transform.up, _missile.transform.forward,
                    eject, out radial, out posOff, out velKick);
                Vector3 pos = basePos + posOff;
                Vector3 vel = baseVel + velKick;
                Quaternion spawnRot = rot;
                if (vel.sqrMagnitude > 1f)
                {
                    Vector3 up = _missile.transform.up;
                    spawnRot = Quaternion.LookRotation(vel.normalized, up);
                }

                Unit subTgt = null;
                bool inherited = false;
                if (i < follow && inherit != null)
                {
                    subTgt = inherit;
                    inherited = true;
                }

                try
                {
                    Missile child = spawner.SpawnMissile(_subPrefab, pos, spawnRot, vel, null, owner);
                    if (child != null)
                    {
                        // Pin PersistentID so missile cams only follow this player's ACM feed.
                        if (ownerPid.Id != 0u)
                        {
                            try { child.NetworkownerID = ownerPid; }
                            catch { }
                            try { child.ownerID = ownerPid; }
                            catch { }
                        }
                        children[spawned] = child;
                        spawned++;
                        AgmTWeapon.PrepareSubmunition(child, _nuke, subTgt, inherited);
                        MissileCameraBridge.TryNotifySpawn(child);
                    }
                }
                catch (Exception ex)
                {
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogWarning("AGM-T spawn GS25: " + ex.Message);
                }
            }
            }
            finally
            {
                AgmTWeapon.AcmGs25SpawnDepth--;
            }

            if (spawned > 0 && spawned < children.Length)
            {
                Missile[] trim = new Missile[spawned];
                for (int i = 0; i < spawned; i++)
                    trim[i] = children[i];
                children = trim;
            }

            Plugin.Log.LogInfo("AGM-T: dispensed " + spawned + " GS25 nuke=" + _nuke
                + " followLock=" + follow
                + " valueM=" + lockValueM.ToString("0.0"));

            // Switch missile PiP to GS25 before bus body is destroyed
            if (spawned > 0)
                MissileCameraBridge.TryHandoffCluster(children);

            FinishBus();
        }

        private void BeginSafeDiscard()
        {
            _safeDiscard = true;
            Plugin.DisarmMissileForDiscard(_missile);
            HideBusModel();
            try
            {
                Collider[] cols = _missile.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                {
                    if (cols[i] != null)
                        cols[i].enabled = false;
                }
            }
            catch { }
        }

        private void HideBusModel()
        {
            try
            {
                Renderer[] renderers = _missile.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null)
                        renderers[i].enabled = false;
                }
            }
            catch { }
            try
            {
                MeshFilter[] filters = _missile.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length; i++)
                {
                    if (filters[i] != null)
                        filters[i].mesh = null;
                }
            }
            catch { }
        }

        private void FinishBus()
        {
            _safeDiscard = true;
            try
            {
                Plugin.DisarmMissileForDiscard(_missile);
            }
            catch { }
            try
            {
                // disabled without Detonate — Prefix blocks any stray Detonate calls
                _missile.Networkdisabled = true;
            }
            catch { }
            try
            {
                UnityEngine.Object.Destroy(_missile.gameObject, 0.05f);
            }
            catch
            {
                try { UnityEngine.Object.Destroy(_missile.gameObject); }
                catch { }
            }
        }

        private static Unit ResolveLaunchOwner(Missile bus)
        {
            if (bus == null)
                return null;
            try
            {
                Unit o = bus.owner;
                if (o != null)
                {
                    Aircraft ac = o as Aircraft;
                    if (ac == null)
                        ac = o.GetComponentInParent<Aircraft>();
                    if (ac != null)
                        return ac;
                    return o;
                }
            }
            catch { }
            return bus;
        }

        private static PersistentID ResolveLaunchOwnerId(Missile bus, Unit owner)
        {
            PersistentID id = PersistentID.None;
            if (bus != null)
            {
                try
                {
                    id = bus.ownerID;
                    if (id.Id == 0u)
                        id = bus.NetworkownerID;
                }
                catch { }
            }
            if (id.Id == 0u && owner != null)
            {
                try
                {
                    id = owner.persistentID;
                    if (id.Id == 0u)
                        id = owner.NetworkpersistentID;
                }
                catch { }
            }
            return id;
        }

        private void CollectTargets(float radius)
        {
            _scratch.Clear();
            GlobalPosition center = _missile.GlobalPosition();
            try
            {
                Unit seed = _launchLock;
                if (seed == null || !Plugin.IsUnitAlive(seed))
                    seed = Plugin.ResolveDesignatedTarget(_missile);
                if (seed == null)
                    seed = GetCurrentTarget();
                if (seed != null)
                    center = seed.GlobalPosition();
            }
            catch { }

            List<Unit> found = new List<Unit>(64);
            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(center, radius, found);
            }
            catch
            {
                try { BattlefieldGrid.GetUnitsInRangeNonAlloc(_missile.GlobalPosition(), radius, found); }
                catch { return; }
            }

            AddValidTargets(found, center, radius);

            if (_scratch.Count == 0)
            {
                try
                {
                    found.Clear();
                    BattlefieldGrid.GetUnitsInRangeNonAlloc(_missile.GlobalPosition(), radius * 1.5f, found);
                    AddValidTargets(found, _missile.GlobalPosition(), radius * 1.5f);
                }
                catch { }
            }
        }

        private void PreferLaunchLock()
        {
            if (_launchLock == null || !Plugin.IsUnitAlive(_launchLock)
                || !Plugin.IsAgmTEngageTarget(_missile, _launchLock))
                return;
            for (int i = _scratch.Count - 1; i >= 0; i--)
            {
                if (object.ReferenceEquals(_scratch[i], _launchLock))
                    _scratch.RemoveAt(i);
            }
            _scratch.Insert(0, _launchLock);
        }

        private void AddValidTargets(List<Unit> found, GlobalPosition center, float radius)
        {
            for (int i = 0; i < found.Count; i++)
            {
                Unit u = found[i];
                if (u == null || u is Scenery || u is Missile)
                    continue;
                // Air + ground both allowed
                if (!IsBusHuntTarget(u))
                    continue;
                try
                {
                    if (FastMath.OutOfRange(u.GlobalPosition(), center, radius))
                        continue;
                }
                catch { }
                _scratch.Add(u);
            }
        }
    }

    /// <summary>GS25 from AGM-T: high-thrust cruise (~35km) + air/ground hunt.</summary>
    public class AgmTSubBrain : MonoBehaviour
    {
        private Missile _missile;
        private bool _nuke;
        private float _nextHunt;
        private float _spawnTime;
        private Vector3 _spawnPos;
        private float _fuelLeft = 1f;
        private Unit _intendedTarget;
        private bool _inheritedLock;
        private bool _stuckBoomTried;
        private float _lastTickFixed = -1f;
        private readonly List<Unit> _huntBuf = new List<Unit>(64);
        private static readonly List<AgmTSubBrain> Live = new List<AgmTSubBrain>(24);
        private static readonly List<AgmTSubBrain> PumpScratch = new List<AgmTSubBrain>(24);

        public bool IsNukeVariant { get { return _nuke; } }
        public float AgeSeconds { get { return Time.time - _spawnTime; } }
        public Vector3 SpawnPosition { get { return _spawnPos; } }
        public Unit IntendedTarget { get { return _intendedTarget; } }

        public void RememberTarget(Unit u)
        {
            RememberTarget(u, false);
        }

        public void RememberTarget(Unit u, bool inheritedLock)
        {
            if (u != null && Plugin.IsUnitAlive(u))
            {
                _intendedTarget = u;
                if (inheritedLock)
                    _inheritedLock = true;
            }
        }

        public void Configure(bool nuke)
        {
            _nuke = nuke;
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_nuke)
                Plugin.ApplyAgmTNukeWarhead(_missile);
            else
            {
                Plugin.EnsureConventionalWarhead(_missile, 15f);
                Plugin.ApplyAgmTDragReduction(_missile);
            }
        }

        private void Awake()
        {
            _missile = GetComponent<Missile>();
            _spawnTime = Time.time;
            _spawnPos = _missile != null ? _missile.transform.position : Vector3.zero;
            _fuelLeft = 1f;
            Live.Add(this);
            // Warhead applied in Configure() — Awake runs before Configure when AddComponent
        }

        private void OnDestroy()
        {
            Live.Remove(this);
        }

        /// <summary>Packed payload may skip Unity FixedUpdate; HostedTick pumps this.</summary>
        internal static void PumpAll()
        {
            if (Live.Count == 0)
                return;
            PumpScratch.Clear();
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                AgmTSubBrain b = Live[i];
                if (b == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                PumpScratch.Add(b);
            }
            for (int i = 0; i < PumpScratch.Count; i++)
                PumpScratch[i].TickSub();
            PumpScratch.Clear();
        }

        private void FixedUpdate()
        {
            TickSub();
        }

        private void TickSub()
        {
            if (_missile == null || _missile.disabled)
                return;
            if (Mathf.Approximately(_lastTickFixed, Time.fixedTime))
                return;
            _lastTickFixed = Time.fixedTime;

            bool serverActive = false;
            bool isServer = false;
            bool localSim = false;
            bool probed = false;
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm != null && nm.Server != null)
                    serverActive = nm.Server.Active;
                isServer = _missile.IsServer;
                localSim = _missile.LocalSim;
                probed = true;
            }
            catch { }
            if (probed
                && AgmTLifecycleGateService.ResolveSubServerSim(serverActive, isServer, localSim)
                == AgmTLifecycleGateService.ServerSimPath.Skip)
                return;

            ApplyHighThrust();
            TryArmNuclear();
            TryDetonateIfStoppedArmed();
            AgmTSubSteerMathService.ApplyLimits(_missile);

            if (AgmTSubSteerMathService.IsCoasting(_missile))
                return;

            if (Time.time >= _nextHunt)
            {
                _nextHunt = AgmTLifecycleGateService.ScheduleSubHunt(Time.time);
                if (!TryKeepAssignedTarget())
                    HuntNewTarget();
            }

            if (_intendedTarget != null && Plugin.IsUnitAlive(_intendedTarget)
                && Plugin.IsAgmTEngageTarget(_missile, _intendedTarget))
                SteerToward(_intendedTarget);
        }

        private void HuntNewTarget()
        {
            Unit cur = null;
            try
            {
                PersistentID tid = _missile.targetID;
                Unit u;
                if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u) && Plugin.IsUnitAlive(u)
                    && Plugin.IsAgmTEngageTarget(_missile, u))
                {
                    cur = u;
                    _intendedTarget = u;
                }
            }
            catch { }

            AgmTLifecycleGateService.SubHuntPath hunt = AgmTLifecycleGateService.ResolveSubHunt(
                cur != null,
                _intendedTarget != null,
                _intendedTarget != null && Plugin.IsUnitAlive(_intendedTarget));
            if (hunt == AgmTLifecycleGateService.SubHuntPath.KeepCurrent)
                return;
            if (hunt == AgmTLifecycleGateService.SubHuntPath.ClearIntended)
                _intendedTarget = null;

            float r = Plugin.AgmTSearchRadius != null ? Plugin.AgmTSearchRadius.Value : 35000f;
            float mmR = Plugin.EffectiveHuntRadius(_missile);
            if (mmR > r)
                r = mmR;
            Unit best = FindNearestHostile(r);
            if (best != null)
            {
                _intendedTarget = best;
                try { _missile.SetTarget(best); }
                catch { }
            }
        }

        /// <summary>
        /// Vanilla optical Seek will not turn off-boresight. Command aim every physics tick;
        /// SetAimpoint prefix still clamps to avoid 90° loops.
        /// </summary>
        private void SteerToward(Unit target)
        {
            if (target == null || _missile == null)
                return;
            try { _missile.SetTarget(target); }
            catch { }

            Vector3 mpos = _missile.transform.position;
            Vector3 tpos = target.transform.position;
            Vector3 tvel = Vector3.zero;
            try
            {
                if (target.rb != null)
                    tvel = target.rb.velocity;
            }
            catch { }
            Vector3 mvel = Vector3.zero;
            try
            {
                if (_missile.rb != null)
                    mvel = _missile.rb.velocity;
            }
            catch { }
            if (mvel.sqrMagnitude < 1f)
                mvel = _missile.transform.forward * 80f;

            Vector3 lead = AgmTSubSteerMathService.LeadAimPoint(mpos, mvel, tpos, tvel);
            try { _missile.SetAimpoint(lead.ToGlobalPosition(), tvel); }
            catch { }
        }

        private void TryArmNuclear()
        {
            if (!_nuke || _missile == null)
                return;
            if (Plugin.IsMissileWarheadArmed(_missile))
                return;
            if (!AgmTWeapon.MeetsNuclearArmConditions(_missile))
                return;
            Plugin.ArmAcnmNuclearWarhead(_missile);
        }

        /// <summary>
        /// Terrain impact before arm zeros velocity and skips Detonate. After the
        /// short GS25 arm delay, force the boom so they do not sit as duds.
        /// </summary>
        private void TryDetonateIfStoppedArmed()
        {
            if (!_nuke || _missile == null || _stuckBoomTried)
                return;
            if (AgeSeconds < AgmTGateMathService.SubNukeArmMinFlightSec)
                return;
            if (!Plugin.IsMissileWarheadArmed(_missile))
                return;
            try
            {
                if (_missile.rb == null || _missile.rb.velocity.sqrMagnitude > 16f)
                    return;
                _stuckBoomTried = true;
                _missile.Detonate(_missile.transform.up, false, true);
            }
            catch { }
        }

        /// <summary>
        /// Vanilla GS25 has motors:[]. Add AAM-class thrust for long powered flight (~35km).
        /// </summary>
        private void ApplyHighThrust()
        {
            if (_fuelLeft <= 0f || _missile.rb == null)
                return;

            float burn = Plugin.AgmTGs25BurnTime != null
                ? Plugin.AgmTGs25BurnTime.Value
                : AgmTSubFuelMathService.DefaultBurnSec;
            float maxRange = Plugin.AgmTGs25MaxRange != null
                ? Plugin.AgmTGs25MaxRange.Value
                : AgmTSubFuelMathService.DefaultMaxRangeM;
            float thrust = Plugin.AgmTGs25Thrust != null
                ? Plugin.AgmTGs25Thrust.Value
                : AgmTSubFuelMathService.DefaultThrust;

            float age = Time.time - _spawnTime;
            float traveled = (_missile.transform.position - _spawnPos).magnitude;
            float fuel;
            if (!AgmTSubFuelMathService.TryUpdateFuel(age, traveled, burn, maxRange, out fuel))
            {
                _fuelLeft = 0f;
                return;
            }
            _fuelLeft = fuel;

            if (!AgmTSubFuelMathService.ShouldApplyThrust(_fuelLeft, _missile.speed))
                return;

            try
            {
                Vector3 forceDir = AgmTSubSteerMathService.ThrustDir(
                    _missile.transform.forward, _missile.rb.velocity);
                _missile.rb.AddForce(thrust * forceDir);
            }
            catch
            {
                try
                {
                    _missile.rb.AddForce(thrust * AgmTSubSteerMathService.ThrustDir(
                        _missile.transform.forward, _missile.rb.velocity));
                }
                catch { }
            }
        }

        private Unit FindNearestHostile(float radius)
        {
            _huntBuf.Clear();
            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(_missile.GlobalPosition(), radius, _huntBuf);
            }
            catch { return null; }

            Unit best = null;
            int bestRank = -1;
            float bestDist = float.MaxValue;
            Vector3 pos = _missile.transform.position;
            for (int i = 0; i < _huntBuf.Count; i++)
            {
                Unit u = _huntBuf[i];
                if (u == null || u is Scenery || u is Missile)
                    continue;
                if (Plugin.IsJunkHuntTarget(u))
                    continue;
                if (!Plugin.IsAgmTEngageTarget(_missile, u))
                    continue;
                float d = (u.transform.position - pos).sqrMagnitude;
                // Prefer high-value + unclaimed + in-cone, but always take a hostile
                // if that is all that is left (6 GS25 used to skip 5 after ClaimedByOther).
                int rank = 0;
                if (AgmTWeapon.MeetsValueGate(u, _nuke))
                    rank += 8;
                if (!ClaimedByOther(u))
                    rank += 4;
                if (HuntTargetInCone(u))
                    rank += 2;
                if (rank > bestRank || (rank == bestRank && d < bestDist))
                {
                    bestRank = rank;
                    bestDist = d;
                    best = u;
                }
            }
            return best;
        }

        private bool HuntTargetInCone(Unit u)
        {
            if (u == null || _missile == null)
                return false;
            Vector3 to = u.transform.position - _missile.transform.position;
            float dist = to.magnitude;
            Vector3 vel = Vector3.zero;
            try
            {
                if (_missile.rb != null)
                    vel = _missile.rb.velocity;
            }
            catch { }
            return AgmTSubSteerMathService.AcceptHuntTarget(
                _missile.transform.forward, vel, to, dist, AgeSeconds);
        }

        private bool TryKeepAssignedTarget()
        {
            if (_intendedTarget == null || !Plugin.IsUnitAlive(_intendedTarget)
                || !Plugin.IsAgmTEngageTarget(_missile, _intendedTarget))
            {
                _inheritedLock = false;
                return false;
            }
            try { _missile.SetTarget(_intendedTarget); }
            catch { }
            return true;
        }

        private bool ClaimedByOther(Unit u)
        {
            if (u == null)
                return false;
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                AgmTSubBrain b = Live[i];
                if (b == null)
                {
                    Live.RemoveAt(i);
                    continue;
                }
                if (object.ReferenceEquals(b, this))
                    continue;
                if (object.ReferenceEquals(b._intendedTarget, u))
                    return true;
            }
            return false;
        }
    }
}
