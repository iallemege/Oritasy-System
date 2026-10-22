using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Hangar replicas of missiles / rockets as pure kinetic [KE] AP.
    /// Skips nuclear warheads and the ACM series (ACM-119 / ACNM / AGM-T / GS25).
    /// </summary>
    internal static class KeAmmoCloneService
    {
        internal const string Tag = "[KE]";
        internal const string KeySuffix = "_KE";

        private static readonly FieldInfo BlastYieldField =
            AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo MissileInfoField =
            AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo WeaponMountField =
            AccessTools.Field(typeof(Weapon), "mount");
        private static readonly HashSet<int> InfoIds = new HashSet<int>();
        private static readonly HashSet<string> CreatedKeys =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WeaponMount> Clones = new List<WeaponMount>(128);
        private static readonly Dictionary<string, WeaponMount> KeByDonorKey =
            new Dictionary<string, WeaponMount>(StringComparer.OrdinalIgnoreCase);
        private static float _nextRetry;
        private static float _nextInject;
        private static int _emptyPasses;
        private static int _pending;
        private static WeaponInfo _pendingInfo;
        private static string _pendingKey;
        private static float _pendingUntil;
        private static bool _injectedPrefabs;

        internal static void Ensure()
        {
            // After the first successful clone pass, never scan every WeaponMount
            // again. Combat near enemies spawns mounts/missiles — FindObjectsOfTypeAll
            // then freezes for seconds. Hangar inject is WeaponSelector.Initialize
            // plus a 30s UnitRegistry walk below.
            if (Clones.Count == 0 && Time.unscaledTime >= _nextRetry)
            {
                WeaponMount[] all = null;
                try { all = Resources.FindObjectsOfTypeAll<WeaponMount>(); }
                catch { all = null; }
                if (all == null || all.Length < 8)
                    _nextRetry = Time.unscaledTime + 8f;
                else
                {
                    Encyclopedia enc = Plugin.GetEncyclopedia();
                    int added = 0;
                    for (int i = 0; i < all.Length; i++)
                    {
                        WeaponMount src = all[i];
                        if (!CanClone(src))
                            continue;
                        if (TryClone(src, enc))
                            added++;
                    }
                    if (added > 0)
                    {
                        _emptyPasses = 0;
                        _nextRetry = Time.unscaledTime + 90f;
                        _nextInject = 0f;
                        if (Plugin.Log != null)
                            Plugin.Log.LogInfo("KE ammo replicas: +" + added + " total=" + Clones.Count);
                    }
                    else
                    {
                        _emptyPasses++;
                        _nextRetry = Time.unscaledTime + (_emptyPasses >= 2 ? 90f : 8f);
                    }
                }
            }
            InjectIntoAircraftHardpoints();
        }

        internal static bool IsKeKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.EndsWith(KeySuffix, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsKeInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            try
            {
                if (InfoIds.Contains(info.GetInstanceID()))
                    return true;
            }
            catch { }
            return ModularArmorMathService.IsKeNamed(info);
        }

        internal static bool IsKeMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsKeKey(mount.jsonKey))
                return true;
            return IsKeInfo(mount.info);
        }

        internal static bool IsKeMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null && IsKeKey(missile.definition.jsonKey))
                    return true;
            }
            catch { }
            try
            {
                if (IsKeInfo(ReadMissileInfo(missile)))
                    return true;
            }
            catch { }
            return false;
        }

        private static WeaponInfo ReadMissileInfo(Missile missile)
        {
            if (missile == null || MissileInfoField == null)
                return null;
            try { return MissileInfoField.GetValue(missile) as WeaponInfo; }
            catch { return null; }
        }

        internal static void Sync(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || mount == null || mount.info == null)
                return;
            if (!IsKeMount(mount))
                return;
            if (weapon is Gun)
                return;
            try
            {
                if (weapon.info != null && weapon.info.gun)
                    return;
            }
            catch { }
            weapon.info = mount.info;
        }

        internal static void NoteFire(Weapon weapon)
        {
            if (weapon == null)
                return;
            WeaponMount mount = null;
            if (WeaponMountField != null)
            {
                try { mount = WeaponMountField.GetValue(weapon) as WeaponMount; }
                catch { }
            }
            WeaponInfo inf = null;
            if (IsKeMount(mount))
                inf = mount.info;
            else
            {
                try { inf = weapon.info; }
                catch { }
            }
            if (!IsKeInfo(inf) && !IsKeMount(mount))
                return;
            _pending++;
            _pendingInfo = inf;
            _pendingKey = mount != null ? mount.jsonKey : null;
            _pendingUntil = Time.time + 2.5f;
        }

        internal static void ConsumeSpawned(Missile missile)
        {
            if (missile == null)
                return;
            if (_pending > 0 && Time.time > _pendingUntil)
            {
                _pending = 0;
                _pendingInfo = null;
                _pendingKey = null;
            }
            if (_pending > 0 && _pendingInfo != null)
            {
                if (MissileInfoField != null)
                {
                    try { MissileInfoField.SetValue(missile, _pendingInfo); }
                    catch { }
                }
                StampDefinition(missile, _pendingKey);
                _pending--;
                if (_pending <= 0)
                {
                    _pending = 0;
                    _pendingInfo = null;
                    _pendingKey = null;
                }
            }
            ApplyToMissile(missile);
        }

        private static void StampDefinition(Missile missile, string keKey)
        {
            if (missile == null || string.IsNullOrEmpty(keKey))
                return;
            if (Encyclopedia.Lookup == null || !Encyclopedia.Lookup.ContainsKey(keKey))
                return;
            try
            {
                MissileDefinition def = Encyclopedia.Lookup[keKey] as MissileDefinition;
                if (def != null)
                    missile.definition = def;
            }
            catch { }
        }

        internal static void ApplyToMissile(Missile missile)
        {
            if (missile == null || !IsKeMissile(missile))
                return;
            try
            {
                WeaponInfo inf = ReadMissileInfo(missile);
                if (inf != null)
                    inf.blastDamage = 0f;
            }
            catch { }
            if (BlastYieldField != null)
            {
                try { BlastYieldField.SetValue(missile, 0f); }
                catch { }
            }
        }

        private static bool CanClone(WeaponMount src)
        {
            if (src == null || src.info == null || src.prefab == null)
                return false;
            if (IsKeMount(src))
                return false;
            if (!string.IsNullOrEmpty(src.jsonKey)
                && src.jsonKey.EndsWith("_IAL", StringComparison.OrdinalIgnoreCase))
                return false;
            WeaponInfo w = src.info;
            try
            {
                if (w.nuclear || w.bomb || w.glideBomb)
                    return false;
                if (w.gun && !w.missile)
                    return false;
            }
            catch { return false; }

            string blob = Blob(src);
            if (ModularArmorMathService.IsNukeNamed(blob)
                || ModularArmorMathService.IsNuclearWeapon(w))
                return false;
            if (ModularArmorMathService.IsAcmSeries(blob))
                return false;
            if (ModularArmorMathService.IsForeignPack(blob))
                return false;
            if (ModularArmorMathService.NameHas(blob, "dump"))
                return false;

            bool missile = false;
            bool rocket = false;
            try
            {
                missile = w.missile || w.laserGuided;
            }
            catch { }
            int cls = ModularArmorMathService.ClassifyKinetic(w);
            rocket = cls == ModularArmorMathService.WepRocket;
            if (!missile && !rocket)
                return false;
            return true;
        }

        private static bool TryClone(WeaponMount src, Encyclopedia enc)
        {
            string baseKey = !string.IsNullOrEmpty(src.jsonKey) ? src.jsonKey
                : (!string.IsNullOrEmpty(src.name) ? src.name : src.info.name);
            if (string.IsNullOrEmpty(baseKey))
                return false;
            string keKey = baseKey + KeySuffix;
            if (CreatedKeys.Contains(keKey))
                return false;

            WeaponMount clone;
            try
            {
                clone = UnityEngine.Object.Instantiate(src);
            }
            catch
            {
                return false;
            }
            if (clone == null)
                return false;
            clone.name = (src.name != null ? src.name : "Mount") + KeySuffix;
            clone.jsonKey = keKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

            WeaponInfo infoClone;
            try
            {
                infoClone = UnityEngine.Object.Instantiate(src.info);
            }
            catch
            {
                return false;
            }
            if (infoClone == null)
                return false;
            infoClone.name = (src.info.name != null ? src.info.name : "WeaponInfo") + KeySuffix;
            infoClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            infoClone.nuclear = false;
            try { infoClone.strategic = false; }
            catch { }
            infoClone.blastDamage = 0f;
            try
            {
                if (infoClone.pierceDamage < 8f)
                    infoClone.pierceDamage = src.info.pierceDamage * 1.2f;
                else
                    infoClone.pierceDamage = src.info.pierceDamage * 1.12f;
            }
            catch { }
            LabelKe(infoClone);

            clone.info = infoClone;
            string mn = src.mountName != null && src.mountName.Length > 0 ? src.mountName : clone.name;
            AppendKeTag(ref mn);
            clone.mountName = mn;

            InfoIds.Add(infoClone.GetInstanceID());
            CreatedKeys.Add(keKey);
            Clones.Add(clone);
            IndexDonor(src, clone);
#if ORITASY_COMBINED
            try { WeXon.Plugin.OfferExtraMount(clone); }
            catch { }
#endif

            if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                enc.weaponMounts.Add(clone);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(keKey))
                Encyclopedia.WeaponLookup[keKey] = clone;
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

            CloneMissileDef(enc, src.jsonKey, keKey, DisplayName(src));
            return true;
        }

        private static void CloneMissileDef(Encyclopedia enc, string srcKey, string keKey, string baseName)
        {
            if (enc == null || enc.missiles == null || string.IsNullOrEmpty(srcKey))
                return;
            MissileDefinition src = null;
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition d = enc.missiles[i];
                if (d == null || string.IsNullOrEmpty(d.jsonKey))
                    continue;
                if (string.Equals(d.jsonKey, srcKey, StringComparison.OrdinalIgnoreCase))
                {
                    src = d;
                    break;
                }
            }
            if (src == null)
                return;
            MissileDefinition clone;
            try { clone = UnityEngine.Object.Instantiate(src); }
            catch { return; }
            if (clone == null)
                return;
            clone.name = (src.name != null ? src.name : "Missile") + KeySuffix;
            clone.jsonKey = keKey;
            clone.code = (src.code != null && src.code.Length > 0 ? src.code : srcKey) + "-KE";
            string un = src.unitName != null && src.unitName.Length > 0 ? src.unitName : baseName;
            AppendKeTag(ref un);
            clone.unitName = un;
            string desc = src.description != null ? src.description : string.Empty;
            string line = "Pure kinetic AP replica. Blast 0. Overpenetrates modules.";
            if (desc.IndexOf("[KE]", StringComparison.OrdinalIgnoreCase) < 0)
                desc = string.IsNullOrEmpty(desc) ? line : desc + "\n" + line;
            clone.description = desc;
            clone.dontAutomaticallyAddToEncyclopedia = false;
            enc.missiles.Add(clone);
            if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(keKey))
                Encyclopedia.Lookup[keKey] = clone;
        }

        private static void LabelKe(WeaponInfo info)
        {
            if (info == null)
                return;
            string n = info.weaponName;
            string s = info.shortName;
            string d = info.description;
            AppendKeTag(ref n);
            if (!string.IsNullOrEmpty(s) && s.IndexOf("KE", StringComparison.OrdinalIgnoreCase) < 0)
                s = s + " KE";
            string line = "Pure kinetic. No blast.";
            if (string.IsNullOrEmpty(d))
                d = line;
            else if (d.IndexOf("[KE]", StringComparison.OrdinalIgnoreCase) < 0)
                d = d + "\n" + line;
            info.weaponName = n;
            info.shortName = s;
            info.description = d;
        }

        private static void AppendKeTag(ref string name)
        {
            if (string.IsNullOrEmpty(name))
                name = Tag;
            else if (name.IndexOf(Tag, StringComparison.OrdinalIgnoreCase) < 0)
                name = name + " " + Tag;
        }

        private static string DisplayName(WeaponMount src)
        {
            if (src == null)
                return string.Empty;
            if (src.info != null && !string.IsNullOrEmpty(src.info.weaponName))
                return src.info.weaponName;
            if (!string.IsNullOrEmpty(src.mountName))
                return src.mountName;
            return src.name;
        }

        private static string Blob(WeaponMount src)
        {
            string a = src.jsonKey != null ? src.jsonKey : string.Empty;
            string b = src.mountName != null ? src.mountName : string.Empty;
            string c = src.name != null ? src.name : string.Empty;
            string d = string.Empty;
            if (src.info != null)
                d = ModularArmorMathService.WeaponBlob(src.info);
            return a + " " + b + " " + c + " " + d;
        }

        internal static void InjectIntoAircraftHardpoints()
        {
            if (Clones.Count == 0)
                return;
            if (Time.unscaledTime < _nextInject)
                return;
            if (!_injectedPrefabs)
            {
                _nextInject = Time.unscaledTime + 8f;
                WeaponManager[] managers = null;
                try { managers = Resources.FindObjectsOfTypeAll<WeaponManager>(); }
                catch { return; }
                if (managers == null)
                    return;
                for (int i = 0; i < managers.Length; i++)
                {
                    WeaponManager wm = managers[i];
                    if (wm == null || wm.hardpointSets == null)
                        continue;
                    for (int h = 0; h < wm.hardpointSets.Length; h++)
                        AddKeToHardpoint(wm.hardpointSets[h]);
                }
                _injectedPrefabs = true;
                _nextInject = Time.unscaledTime + 30f;
                return;
            }
            _nextInject = Time.unscaledTime + 30f;
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
                InjectForAircraft(all[i]);
        }

        internal static void InjectForAircraft(Aircraft ac)
        {
            if (ac == null || Clones.Count == 0)
                return;
            WeaponManager wm = null;
            try { wm = ac.weaponManager; }
            catch { return; }
            if (wm == null || wm.hardpointSets == null)
                return;
            for (int h = 0; h < wm.hardpointSets.Length; h++)
                AddKeToHardpoint(wm.hardpointSets[h]);
        }

        internal static void AppendToAvailable(HardpointSet hs, List<WeaponMount> available)
        {
            if (available == null || KeByDonorKey.Count == 0)
                return;
            HashSet<WeaponMount> have = new HashSet<WeaponMount>();
            int i;
            for (i = 0; i < available.Count; i++)
            {
                if (available[i] != null)
                    have.Add(available[i]);
            }
            int n = available.Count;
            for (i = 0; i < n; i++)
                TryAppendKe(available[i], available, have);
            if (hs == null || hs.weaponOptions == null)
                return;
            for (i = 0; i < hs.weaponOptions.Count; i++)
                TryAppendKe(hs.weaponOptions[i], available, have);
        }

        private static void IndexDonor(WeaponMount src, WeaponMount ke)
        {
            if (src == null || ke == null)
                return;
            if (!string.IsNullOrEmpty(src.jsonKey))
                KeByDonorKey[src.jsonKey] = ke;
            if (!string.IsNullOrEmpty(src.name) && !KeByDonorKey.ContainsKey(src.name))
                KeByDonorKey[src.name] = ke;
            if (src.info != null && !string.IsNullOrEmpty(src.info.name)
                && !KeByDonorKey.ContainsKey(src.info.name))
                KeByDonorKey[src.info.name] = ke;
        }

        private static WeaponMount FindKeForDonor(WeaponMount donor)
        {
            if (donor == null)
                return null;
            WeaponMount ke;
            if (!string.IsNullOrEmpty(donor.jsonKey)
                && KeByDonorKey.TryGetValue(donor.jsonKey, out ke) && ke != null)
                return ke;
            if (!string.IsNullOrEmpty(donor.name)
                && KeByDonorKey.TryGetValue(donor.name, out ke) && ke != null)
                return ke;
            if (donor.info != null && !string.IsNullOrEmpty(donor.info.name)
                && KeByDonorKey.TryGetValue(donor.info.name, out ke) && ke != null)
                return ke;
            return null;
        }

        private static void TryAppendKe(WeaponMount donor, List<WeaponMount> dest, HashSet<WeaponMount> have)
        {
            if (donor == null || dest == null || IsKeMount(donor))
                return;
            WeaponMount ke = FindKeForDonor(donor);
            if (ke == null)
                return;
            if (have.Add(ke))
                dest.Add(ke);
        }

        private static void AddKeToHardpoint(HardpointSet hs)
        {
            if (hs == null)
                return;
            if (hs.weaponOptions == null)
                hs.weaponOptions = new List<WeaponMount>();
            HashSet<WeaponMount> have = new HashSet<WeaponMount>();
            int i;
            for (i = 0; i < hs.weaponOptions.Count; i++)
            {
                if (hs.weaponOptions[i] != null)
                    have.Add(hs.weaponOptions[i]);
            }
            int n = hs.weaponOptions.Count;
            for (i = 0; i < n; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null || IsKeMount(m))
                    continue;
                WeaponMount ke = FindKeForDonor(m);
                if (ke == null)
                    continue;
                if (have.Add(ke))
                    hs.weaponOptions.Add(ke);
            }
        }
    }
}
