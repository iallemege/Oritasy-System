using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// Merged from former Kh85MTSurvival.dll (Kh85MT Advanced):
    /// A/B/C variants, survival guidance, maneuver scaling.
    /// Independent optional menu removed — configure via BepInEx cfg / Oritasy F1.
    /// </summary>
    internal static class Kh85Advanced
    {
        internal static ConfigEntry<bool> EnableC;
        internal static ConfigEntry<bool> EnableB;
        internal static ConfigEntry<bool> EnableA;
        internal static ConfigEntry<bool> EnableE;
        internal static ConfigEntry<bool> EnableD;
        internal static ConfigEntry<bool> EnableS;
        internal static ConfigEntry<bool> AutoSeek;
        internal static ConfigEntry<bool> MultiMode;
        internal static ConfigEntry<bool> InjectAll;

        internal const float LoalHuntDelaySec = 1.6f;

        internal static bool OritasyLoaded;

        internal static readonly Dictionary<string, float> VariantMul = new Dictionary<string, float>(StringComparer.Ordinal);
        internal static readonly Dictionary<string, string> VariantName = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static readonly Dictionary<string, string> VariantDesc = new Dictionary<string, string>(StringComparer.Ordinal);

        private static readonly List<WeaponMount> BMounts = new List<WeaponMount>();
        private static readonly List<WeaponMount> AMounts = new List<WeaponMount>();
        private static readonly List<WeaponMount> EMounts = new List<WeaponMount>();
        private static readonly List<WeaponMount> DMounts = new List<WeaponMount>();
        private static readonly List<WeaponMount> SMounts = new List<WeaponMount>();
        private static bool _variantsDone;

        internal static void InitStatic()
        {
            VariantMul["C"] = 2.8f;
            VariantMul["B"] = 2f;
            VariantMul["A"] = 1.8f;
            VariantMul["E"] = 2.2f;
            VariantMul["D"] = 2f;
            VariantMul["S"] = 2.4f;

            VariantName["C"] = "TGM-85C Shardfall";
            VariantName["B"] = "TGM-85B Torch";
            VariantName["A"] = "TGM-85A Coordinator";
            VariantName["E"] = "TGM-85E Torjan";
            VariantName["D"] = "TGM-85D Hegemony";
            VariantName["S"] = "TGM-85S Seaker";

            // Shared lore only — no per-variant capability blurbs (encyclopedia pass kept).
            VariantDesc["C"] = Kh85Weapon.EncyclopediaFor(VariantName["C"]);
            VariantDesc["B"] = Kh85Weapon.EncyclopediaFor(VariantName["B"]);
            VariantDesc["A"] = Kh85Weapon.EncyclopediaFor(VariantName["A"]);
            VariantDesc["E"] = Kh85Weapon.EncyclopediaFor(VariantName["E"]);
            VariantDesc["D"] = Kh85Weapon.EncyclopediaFor(VariantName["D"]);
            VariantDesc["S"] = Kh85Weapon.EncyclopediaFor(VariantName["S"]);
        }

        internal static void BindConfig(ConfigFile config)
        {
            EnableC = config.Bind("Variants", "EnableC", true, "TGM-85C Shardfall: x2 maneuver");
            EnableB = config.Bind("Variants", "EnableB", true, "TGM-85B Torch: 1–3 rack + aircraft ECM");
            EnableA = config.Bind("Variants", "EnableA", true, "TGM-85A Coordinator: 1–3 rack + defensive ECM");
            EnableE = config.Bind("Variants", "EnableE", true,
                "TGM-85E Torjan: dual-rack powered decoy — steal hostile locks within 6 km");
            EnableD = config.Bind("Variants", "EnableD", true, "TGM-85D Hegemony: single-rack radar-only");
            EnableS = config.Bind("Variants", "EnableS", true, "TGM-85S Seaker: single-rack hypersonic");
            AutoSeek = config.Bind("Guidance", "AutoSeek", true,
                "Force active-search seeker mode on TGM-85 (LOAL / 自锁). Also applied when Oritasy is present.");
            MultiMode = config.Bind("Guidance", "MultiMode", true,
                "Enable standalone self-lock hunt (Kh85SelfHunt) when fired without a target. Skipped if WeXon/Oritasy MM is present.");
            ConfigEntry<bool> loalRestored = config.Bind("Guidance", "LoalRestored142", false,
                "Internal: one-shot restore AutoSeek/MultiMode after they were saved false.");
            if (!loalRestored.Value)
            {
                AutoSeek.Value = true;
                MultiMode.Value = true;
                loalRestored.Value = true;
            }
            InjectAll = config.Bind("Mount", "InjectAll", false,
                "Add enabled TGM variants to every missile-capable hardpoint (not only AGM-68 pylons).");
            Kh85CFlight.BindConfig(config);
            Kh85AEcm.BindConfig(config);
            Kh85BEcm.BindConfig(config);
            Kh85EDecoy.BindConfig(config);
            Kh85DArm.BindConfig(config);
            Kh85SHyper.BindConfig(config);

            RefreshOritasyLoaded();
        }

        /// <summary>
        /// True when Oritasy / WeXon MultiMode can own LOAL hunt.
        /// Wired into EnableSelfLock / Kh85SelfHunt — skip standalone hunt when present.
        /// </summary>
        internal static bool WeXonGuidancePresent()
        {
            RefreshOritasyLoaded();
            if (OritasyLoaded)
                return true;
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos != null
                    && (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.iallemege.wexon")
                        || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.qiaochen.wexon")))
                    return true;
            }
            catch { }
            return SilentTypeExists("WeXon.Plugin");
        }

        /// <summary>
        /// Detect optional Oritasy without AccessTools.TypeByName (HarmonyX logs a warning on miss).
        /// </summary>
        internal static void RefreshOritasyLoaded()
        {
            if (OritasyLoaded)
                return;
            try
            {
                if (BepInEx.Bootstrap.Chainloader.PluginInfos != null
                    && (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.iallemege.oritasy")
                        || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.qiaochen.oritasy")
                        || BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("com.qiaochen.ci22xe")))
                {
                    OritasyLoaded = true;
                    return;
                }
            }
            catch { }

            OritasyLoaded = SilentTypeExists("Oritasy.Plugin")
                || SilentTypeExists("OritasyAir.Plugin");
        }

        /// <summary>AppDomain scan — no Harmony warning when the type is absent.</summary>
        internal static bool SilentTypeExists(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return false;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Assembly a = asms[i];
                    if (a == null)
                        continue;
                    Type t = a.GetType(fullName, false);
                    if (t != null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Soft type resolve — no AccessTools.TypeByName warning spam.</summary>
        internal static Type SilentFindType(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return null;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Assembly a = asms[i];
                    if (a == null)
                        continue;
                    Type t = a.GetType(fullName, false);
                    if (t != null)
                        return t;
                }
            }
            catch { }
            return null;
        }

        private static bool IsVariantSuffixKey(string key)
        {
            return Kh85Weapon.IsLetterVariantKey(key);
        }

        /// <summary>Add A/B/D/E mounts that match a C-family rack ammo count.</summary>
        internal static void CollectLetterMountsForAmmo(int ammo, HashSet<WeaponMount> want)
        {
            if (want == null)
                return;
            TryCreateVariants();
            AppendAmmoMatches(AMounts, ammo, want);
            AppendAmmoMatches(BMounts, ammo, want);
            AppendAmmoMatches(EMounts, ammo, want);
            AppendAmmoMatches(DMounts, ammo, want);
            AppendAmmoMatches(SMounts, ammo, want);
        }

        private static void AppendAmmoMatches(List<WeaponMount> src, int ammo, HashSet<WeaponMount> want)
        {
            if (src == null)
                return;
            for (int i = 0; i < src.Count; i++)
            {
                WeaponMount m = src[i];
                if (m == null)
                    continue;
                Kh85Weapon.EnsureMountPrefab(m);
                if (m.prefab == null)
                    continue;
                if (!Kh85Weapon.AllowVariantInHangar(m.jsonKey))
                    continue;
                int a = m.ammo > 0 ? m.ammo : 1;
                if (a == ammo || (ammo <= 1 && a <= 1))
                    want.Add(m);
            }
        }

        internal static void TryCreateVariants()
        {
            if (_variantsDone)
                return;
            CreateAllVariants();
        }

        internal static void InjectVariants(WeaponManager wm)
        {
            if (wm == null)
                return;
            HardpointSet[] sets;
            try { sets = wm.hardpointSets; }
            catch { return; }
            if (sets == null || sets.Length == 0)
                return;
            TryCreateVariants();
            bool injectAll = InjectAll != null && InjectAll.Value;

            for (int hi = 0; hi < sets.Length; hi++)
            {
                HardpointSet hs = sets[hi];
                if (hs == null || hs.weaponOptions == null)
                    continue;

                bool hasMissile = false;
                for (int i = 0; i < hs.weaponOptions.Count; i++)
                {
                    WeaponMount m = hs.weaponOptions[i];
                    if (m == null || m.prefab == null)
                        continue;
                    try
                    {
                        if (m.prefab.GetComponent<Missile>() != null
                            || (m.info != null && m.info.missile))
                        {
                            hasMissile = true;
                            break;
                        }
                    }
                    catch { continue; }
                }
                if (!hasMissile)
                    continue;

                // Always wire C-family clones onto missile pylons (hangar already lists them).
                Kh85Weapon.AppendClonesToHardpoint(hs);

                // Letter racks matching AGM / C-family ammo on this pylon.
                HashSet<WeaponMount> letterWant = new HashSet<WeaponMount>();
                for (int i = 0; i < hs.weaponOptions.Count; i++)
                {
                    WeaponMount m = hs.weaponOptions[i];
                    if (m == null)
                        continue;
                    try
                    {
                        if (Kh85Weapon.IsKh85Mount(m) && Kh85Weapon.IsCFamilyKey(m.jsonKey))
                            CollectLetterMountsForAmmo(m.ammo > 0 ? m.ammo : 1, letterWant);
                        else if (Kh85Weapon.IsAgm68Mount(m))
                            CollectLetterMountsForAmmo(m.ammo > 0 ? m.ammo : 1, letterWant);
                    }
                    catch { continue; }
                }
                if (injectAll)
                {
                    AppendListToSet(AMounts, letterWant);
                    AppendListToSet(BMounts, letterWant);
                    AppendListToSet(EMounts, letterWant);
                    AppendListToSet(DMounts, letterWant);
                    AppendListToSet(SMounts, letterWant);
                }
                foreach (WeaponMount m in letterWant)
                {
                    if (m == null)
                        continue;
                    Kh85Weapon.EnsureMountPrefab(m);
                    if (m.prefab != null && !hs.weaponOptions.Contains(m))
                        hs.weaponOptions.Add(m);
                }
            }
        }

        private static void AppendListToSet(List<WeaponMount> src, HashSet<WeaponMount> set)
        {
            if (src == null || set == null)
                return;
            for (int i = 0; i < src.Count; i++)
            {
                WeaponMount m = src[i];
                if (m == null)
                    continue;
                Kh85Weapon.EnsureMountPrefab(m);
                if (m.prefab != null && Kh85Weapon.AllowVariantInHangar(m.jsonKey))
                    set.Add(m);
            }
        }

        /// <summary>Hangar list helper — C + optional A/B/D/E mounts with live prefabs.</summary>
        internal static void AppendAllToList(List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null)
                return;
            TryCreateVariants();
            Kh85Weapon.AppendClonesToList(list, have);
            AppendList(BMounts, list, have);
            AppendList(AMounts, list, have);
            AppendList(EMounts, list, have);
            AppendList(DMounts, list, have);
            AppendList(SMounts, list, have);
        }

        private static void AppendList(List<WeaponMount> src, List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (src == null)
                return;
            for (int i = 0; i < src.Count; i++)
            {
                WeaponMount m = src[i];
                if (m == null)
                    continue;
                Kh85Weapon.EnsureMountPrefab(m);
                if (m.prefab == null)
                    continue;
                if (!Kh85Weapon.AllowVariantInHangar(m.jsonKey))
                    continue;
                if (have != null && !have.Add(m))
                    continue;
                if (have == null && list.Contains(m))
                    continue;
                list.Add(m);
            }
        }

        private static void CreateAllVariants()
        {
            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc == null || enc.missiles == null)
                return;

            MissileDefinition baseDef = null;
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition d = enc.missiles[i];
                if (d == null || string.IsNullOrEmpty(d.jsonKey))
                    continue;
                if (!d.jsonKey.StartsWith(Kh85Weapon.PackKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsVariantSuffixKey(d.jsonKey))
                    continue;
                baseDef = d;
                break;
            }
            if (baseDef == null)
                return;

            WeaponMount primary = Kh85Weapon.FindPrimaryClone();
            if (primary == null || primary.prefab == null)
                return;

            // Brand C encyclopedia / primary mount ([IAL] [10kt] — nuclear racks by ammo).
            ApplyVariantIdentity(primary, baseDef, "C");
            try
            {
                if (baseDef != null)
                {
                    string cKey = Kh85Weapon.StripIal(baseDef.jsonKey);
                    if (string.IsNullOrEmpty(cKey) || !cKey.StartsWith(Kh85Weapon.PackKey, StringComparison.OrdinalIgnoreCase))
                        cKey = Kh85Weapon.PackKey;
                    baseDef.jsonKey = cKey;
                    Kh85Weapon.RegisterDefinition(cKey, baseDef);
                }
            }
            catch { }

            BMounts.Clear();
            AMounts.Clear();
            EMounts.Clear();
            DMounts.Clear();
            SMounts.Clear();

            // External C racks by ammo (1 / 2 / 3) — skip internal bays.
            Dictionary<int, WeaponMount> cByAmmo = new Dictionary<int, WeaponMount>();
            List<WeaponMount> allC = Kh85Weapon.GetMountClonesSnapshot();
            for (int i = 0; i < allC.Count; i++)
            {
                WeaponMount m = allC[i];
                if (m == null || m.prefab == null || string.IsNullOrEmpty(m.jsonKey))
                    continue;
                if (!Kh85Weapon.IsCFamilyKey(m.jsonKey) || Kh85Weapon.IsInternalKey(m.jsonKey))
                    continue;
                int ammo = m.ammo > 0 ? m.ammo : 1;
                if (ammo < 1)
                    ammo = 1;
                if (ammo > 3)
                    continue;
                if (!cByAmmo.ContainsKey(ammo))
                    cByAmmo[ammo] = m;
            }
            if (!cByAmmo.ContainsKey(1))
                cByAmmo[1] = primary;

            // A/B: 1–3. E: dual. D/S: single.
            CreateLetterRacks(enc, baseDef, "A", EnableA, new int[] { 1, 2, 3 }, cByAmmo, AMounts);
            CreateLetterRacks(enc, baseDef, "B", EnableB, new int[] { 1, 2, 3 }, cByAmmo, BMounts);
            CreateLetterRacks(enc, baseDef, "E", EnableE, new int[] { 2 }, cByAmmo, EMounts);
            CreateLetterRacks(enc, baseDef, "D", EnableD, new int[] { 1 }, cByAmmo, DMounts);
            CreateLetterRacks(enc, baseDef, "S", EnableS, new int[] { 1 }, cByAmmo, SMounts);

            _variantsDone = true;
            RegisterLetterMounts(enc);
            Kh85Weapon.BindAllWeaponPrefabs();
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("TGM-85 variants ready [IAL]: B=" + BMounts.Count
                    + " A=" + AMounts.Count + " E=" + EMounts.Count
                    + " D=" + DMounts.Count + " S=" + SMounts.Count);
        }

        /// <summary>Re-bind letter racks into encyclopedia + network IndexLookup (post-AfterLoad safe).</summary>
        internal static void RegisterLetterMounts(Encyclopedia enc)
        {
            if (enc == null)
                return;
            RegisterMountList(enc, AMounts);
            RegisterMountList(enc, BMounts);
            RegisterMountList(enc, EMounts);
            RegisterMountList(enc, DMounts);
            RegisterMountList(enc, SMounts);
        }

        private static void RegisterMountList(Encyclopedia enc, List<WeaponMount> list)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null || string.IsNullOrEmpty(m.jsonKey))
                    continue;
                Kh85Weapon.UpsertMount(enc, m);
            }
        }

        private static void CreateLetterRacks(Encyclopedia enc, MissileDefinition baseDef, string letter,
            ConfigEntry<bool> enable, int[] ammos, Dictionary<int, WeaponMount> cByAmmo, List<WeaponMount> dest)
        {
            if (enable != null && !enable.Value)
                return;
            if (baseDef == null || ammos == null || cByAmmo == null || dest == null)
                return;

            string defKey = Kh85Weapon.PackKey + "_" + letter;
            MissileDefinition def = UnityEngine.Object.Instantiate(baseDef);
            def.hideFlags = HideFlags.DontUnloadUnusedAsset;
            def.jsonKey = defKey;
            def.unitName = Kh85Weapon.WithVariantDisplay(VariantName[letter], letter);
            def.description = VariantDesc[letter];
            def.code = "85" + letter;
            def.name = "TGM85" + letter + "Def";
            def.unitPrefab = baseDef.unitPrefab;
            Kh85Weapon.UpsertMissileDef(enc, def);
            Kh85Weapon.RegisterDefinition(defKey, def);

            HashSet<string> madeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int ai = 0; ai < ammos.Length; ai++)
            {
                int ammo = ammos[ai];
                WeaponMount donor = null;
                if (!cByAmmo.TryGetValue(ammo, out donor) || donor == null)
                {
                    // Fallback: pick closest available C rack.
                    if (ammo == 2 && cByAmmo.ContainsKey(1))
                        continue; // E dual requires a real x2 donor
                    if (!cByAmmo.TryGetValue(1, out donor) || donor == null)
                        continue;
                    if (ammo != 1)
                        continue;
                }

                string key = Kh85Weapon.MakeLetterRackKey(letter, donor.jsonKey, ammo);
                if (!madeKeys.Add(key))
                    continue;
                WeaponMount mount = UnityEngine.Object.Instantiate(donor);
                mount.hideFlags = HideFlags.DontUnloadUnusedAsset;
                mount.jsonKey = key;
                mount.prefab = donor.prefab;
                mount.ammo = donor.ammo > 0 ? donor.ammo : ammo;
                string mname = VariantName[letter];
                if (mount.ammo > 1)
                    mname = mname + " x" + mount.ammo;
                mount.mountName = Kh85Weapon.WithVariantDisplay(mname, letter);
                mount.name = "TGM85" + letter + "_x" + mount.ammo;

                if (donor.info != null)
                {
                    WeaponInfo info = UnityEngine.Object.Instantiate(donor.info);
                    info.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    info.weaponName = Kh85Weapon.WithVariantDisplay(VariantName[letter], letter);
                    info.shortName = info.weaponName;
                    info.description = VariantDesc[letter];
                    info.name = "TGM85" + letter + "Info_x" + mount.ammo;
                    info.nuclear = string.Equals(letter, "C", StringComparison.OrdinalIgnoreCase);
                    // Share vanilla AGM flight prefab (never Instantiated DDOL templates).
                    if (donor.info.weaponPrefab != null)
                        info.weaponPrefab = donor.info.weaponPrefab;
                    Kh85Weapon.EnsureSpawnPrefab(letter, info.weaponPrefab);
                    Kh85Weapon.ApplyIconToInfo(info);
                    mount.info = info;
                    Kh85Weapon.RegisterInfo(key, info);
                    // Also index under PackKey_A for fire-key fallback.
                    Kh85Weapon.RegisterInfo(Kh85Weapon.PackKey + "_" + letter, info);
                }

                Kh85Weapon.EnsureMountPrefab(mount);
                Kh85Weapon.UpsertMount(enc, mount);
                dest.Add(mount);
            }
        }

        private static void ApplyVariantIdentity(WeaponMount mount, MissileDefinition def, string letter)
        {
            string name = Kh85Weapon.WithVariantDisplay(VariantName[letter], letter);
            string desc = VariantDesc[letter];
            if (def != null)
            {
                def.unitName = name;
                def.code = "85" + letter;
                def.description = desc;
            }
            if (mount != null)
            {
                string mname = VariantName[letter];
                if (mount.ammo > 1)
                    mname = mname + " x" + mount.ammo;
                mount.mountName = Kh85Weapon.WithVariantDisplay(mname, letter);
                if (mount.info != null)
                {
                    mount.info.weaponName = name;
                    mount.info.shortName = name;
                    mount.info.description = desc;
                    if (string.Equals(letter, "C", StringComparison.OrdinalIgnoreCase))
                        mount.info.nuclear = true;
                    Kh85Weapon.ApplyIconToInfo(mount.info);
                }
            }
        }

        internal static void ResetVariants()
        {
            _variantsDone = false;
            AMounts.Clear();
            BMounts.Clear();
            EMounts.Clear();
            DMounts.Clear();
            SMounts.Clear();
        }

        internal static bool LetterPrefabsBroken()
        {
            return ListPrefabsBroken(AMounts)
                || ListPrefabsBroken(BMounts)
                || ListPrefabsBroken(EMounts)
                || ListPrefabsBroken(DMounts)
                || ListPrefabsBroken(SMounts);
        }

        private static bool ListPrefabsBroken(List<WeaponMount> list)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (m.prefab == null)
                    return true;
            }
            return false;
        }

        internal static int RepairLetterPrefabs(GameObject missilePrefab)
        {
            return RepairLetterList(AMounts, missilePrefab)
                + RepairLetterList(BMounts, missilePrefab)
                + RepairLetterList(EMounts, missilePrefab)
                + RepairLetterList(DMounts, missilePrefab)
                + RepairLetterList(SMounts, missilePrefab);
        }

        private static int RepairLetterList(List<WeaponMount> list, GameObject missilePrefab)
        {
            if (list == null)
                return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (m.prefab == null && Kh85Weapon.EnsureMountPrefab(m))
                    n++;
                if (m.info != null
                    && m.info.weaponPrefab == null
                    && missilePrefab != null)
                    m.info.weaponPrefab = missilePrefab;
            }
            return n;
        }

        /// <summary>After bind: force letter rack WeaponInfo onto shared AGM flight prefab.</summary>
        internal static void RebindLetterWeaponPrefabs(GameObject sharedFlight)
        {
            if (sharedFlight == null)
                return;
            RebindList(AMounts, sharedFlight);
            RebindList(BMounts, sharedFlight);
            RebindList(EMounts, sharedFlight);
            RebindList(DMounts, sharedFlight);
            RebindList(SMounts, sharedFlight);
        }

        private static void RebindList(List<WeaponMount> list, GameObject sharedFlight)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null || m.info == null)
                    continue;
                m.info.weaponPrefab = sharedFlight;
            }
        }
    }

    /// <summary>Stamped at spawn from mount/pending key — authoritative letter for ability brains.</summary>
    internal sealed class Kh85VariantTag : MonoBehaviour
    {
        internal string Letter = "C";
        internal string SourceKey;

        private void OnDestroy()
        {
            try
            {
                Missile m = GetComponent<Missile>();
                Kh85Live.Unregister(m);
            }
            catch { }
        }
    }

    /// <summary>Live Kh85 missiles for E sibling checks (distance, no OverlapSphere).</summary>
    internal static class Kh85Live
    {
        internal static readonly List<Missile> All = new List<Missile>(32);

        internal static void Register(Missile missile)
        {
            if (missile == null)
                return;
            if (!All.Contains(missile))
                All.Add(missile);
        }

        internal static void Unregister(Missile missile)
        {
            if (missile == null)
                return;
            All.Remove(missile);
        }

        internal static void Prune()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Missile m = All[i];
                try
                {
                    if (m == null || m.disabled)
                        All.RemoveAt(i);
                }
                catch { All.RemoveAt(i); }
            }
        }
    }

    /// <summary>
    /// Soft-detect CI22XE / Missile Camera ManualActive without AccessTools.TypeByName.
    /// When true, Kh85 Sticky/C/E/S must not SetAimpoint (SafeSetAimpoint + Steering early-outs).
    /// </summary>
    internal static class Kh85MclosGate
    {
        private static bool _resolved;
        private static PropertyInfo _manualActive;
        private static float _nextRetry;

        internal static bool ManualActive
        {
            get
            {
                int frame = Time.frameCount;
                if (frame != _manualFrame)
                {
                    _manualFrame = frame;
                    _cachedManual = ReadManualActive();
                }
                return _cachedManual;
            }
        }

        private static bool _cachedManual;
        private static int _manualFrame = -1;

        private static bool ReadManualActive()
        {
            EnsureResolved();
            if (_manualActive == null)
                return false;
            try
            {
                object v = _manualActive.GetValue(null, null);
                return v is bool && (bool)v;
            }
            catch { return false; }
        }

        private static void EnsureResolved()
        {
            if (_manualActive != null)
                return;
            if (_resolved && Time.unscaledTime < _nextRetry)
                return;
            _resolved = true;
            _nextRetry = Time.unscaledTime + 5f;

            string[] typeNames = new string[]
            {
                "CI22XE.MissileCameraHud",
                "WeXon.MissileCameraHud",
                "VeyrnAcm.MissileCameraHud",
            };
            for (int i = 0; i < typeNames.Length; i++)
            {
                Type t = Kh85Advanced.SilentFindType(typeNames[i]);
                if (t == null)
                    continue;
                PropertyInfo p = t.GetProperty("ManualActive",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p != null && p.PropertyType == typeof(bool))
                {
                    _manualActive = p;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Standalone LOAL hunt when AutoSeek/MultiMode on and WeXon/Oritasy MM absent.
    /// Variant-aware: C ships/ground, D radar units, else nearest hostile Unit.
    /// </summary>
    public class Kh85SelfHunt : MonoBehaviour
    {
        private static readonly List<Unit> HuntBuf = new List<Unit>(64);
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");

        private Missile _missile;
        private float _nextHunt;
        private float _nextPrune;

        internal static void Attach(Missile missile)
        {
            if (missile == null)
                return;
            if (missile.GetComponent<Kh85SelfHunt>() != null)
                return;
            if (missile.GetComponent<Kh85LockHold>() != null)
                return;
            try { missile.gameObject.AddComponent<Kh85SelfHunt>(); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Kh85SelfHunt add: " + ex.Message);
            }
        }

        private void Awake()
        {
            _missile = GetComponent<Missile>();
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null)
            {
                enabled = false;
                return;
            }
            try
            {
                if (_missile.disabled)
                {
                    enabled = false;
                    return;
                }
            }
            catch { }

            // Optional MM owns hunt when present on THIS missile.
            try
            {
#if ORITASY_COMBINED
                if (GetComponent<WeXon.MultiModeBrain>() != null)
                {
                    enabled = false;
                    return;
                }
#else
                if (Kh85Advanced.WeXonGuidancePresent())
                {
                    enabled = false;
                    return;
                }
#endif
            }
            catch { }

            bool wantLoal = (Kh85Advanced.AutoSeek != null && Kh85Advanced.AutoSeek.Value)
                || (Kh85Advanced.MultiMode != null && Kh85Advanced.MultiMode.Value);
            if (!wantLoal)
            {
                enabled = false;
                return;
            }

            try
            {
                if (_missile.timeSinceSpawn < Kh85Advanced.LoalHuntDelaySec)
                    return;
            }
            catch { return; }

            if (Time.time >= _nextPrune)
            {
                _nextPrune = Time.time + 2f;
                Kh85Live.Prune();
            }

            Unit current = null;
            try
            {
                if (TargetField != null)
                    current = TargetField.GetValue(_missile) as Unit;
            }
            catch { }
            if (current != null)
            {
                Unit owner = null;
                try { owner = _missile.owner; }
                catch { }
                Unit safe = Kh85Weapon.SanitizeLockTarget(owner, current);
                // SelfHunt only runs on LOAL. A vanilla hangar stuffed onto missile.target
                // must not freeze the hunt — require both HQs and a different HQ.
                bool junk = false;
#if ORITASY_COMBINED
                try { junk = WeXon.Plugin.IsJunkHuntTarget(safe); }
                catch { }
#endif
                if (safe is Container)
                    safe = null;
                if (safe != null && !junk && Kh85Util.IsStrictHostile(_missile, safe))
                {
                    Kh85LockHold.Attach(_missile, safe);
                    enabled = false;
                    return;
                }
            }

            if (Time.time < _nextHunt)
                return;
            _nextHunt = Time.time + 0.4f;

            Unit found = HuntNearest(_missile);
            if (found == null)
                return;
            Kh85Weapon.ApplyTargetLock(_missile, found);
            Kh85LockHold.Attach(_missile, found);
            enabled = false;
        }

        private static Unit HuntNearest(Missile self)
        {
            if (self == null)
                return null;
            float range = 35000f;
#if ORITASY_COMBINED
            try
            {
                float mm = WeXon.Plugin.EffectiveHuntRadius(self);
                if (mm > range)
                    range = mm;
            }
            catch { }
#endif
            string letter = Kh85Util.GetVariant(self);
            if (letter == "D")
            {
                float dRange = Kh85DArm.HuntRange != null ? Kh85DArm.HuntRange.Value : 14000f;
                if (dRange > range)
                    range = dRange;
            }

            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(self.GlobalPosition(), range, HuntBuf);
            }
            catch
            {
                return HuntOverlapFallback(self, range, letter);
            }

            return PickBestHunt(self, HuntBuf, HuntBuf.Count, letter, self.transform.position, range);
        }

        private static readonly Collider[] OverlapBuf = new Collider[48];

        private static Unit HuntOverlapFallback(Missile self, float range, string letter)
        {
            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(self.transform.position, range, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return null; }

            HuntBuf.Clear();
            HashSet<int> seen = null;
            for (int i = 0; i < hits; i++)
            {
                Collider c = OverlapBuf[i];
                if (c == null)
                    continue;
                Unit u = null;
                try { u = c.GetComponentInParent<Unit>(); }
                catch { }
                if (u == null)
                    continue;
                if (seen == null)
                    seen = new HashSet<int>();
                if (!seen.Add(u.GetInstanceID()))
                    continue;
                HuntBuf.Add(u);
            }
            return PickBestHunt(self, HuntBuf, HuntBuf.Count, letter, self.transform.position, range);
        }

        private static Unit PickBestHunt(Missile self, List<Unit> list, int count, string letter,
            Vector3 pos, float range)
        {
            if (self == null || list == null || count <= 0)
                return null;
            if (count > list.Count)
                count = list.Count;
            float rangeSq = range * range;
            Unit best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < count; i++)
            {
                Unit u = list[i];
                if (!IsValidHuntTarget(self, u, letter))
                    continue;
                float sq = (u.transform.position - pos).sqrMagnitude;
                if (sq > rangeSq)
                    continue;
                float dist = Mathf.Sqrt(sq);
                float valueM = 0f;
                try
                {
                    if (u.definition != null)
                        valueM = u.definition.value;
                }
                catch { }
                int locks = CountSiblingLocks(self, u);
                int uid = 0;
                try { uid = u.GetInstanceID(); }
                catch { }
#if ORITASY_COMBINED
                try { locks += WeXon.HuntSalvoGateService.PendingLocks(uid); }
                catch { }
                float score = WeXon.HuntSalvoGateService.HuntScore(
                    valueM, dist, locks, WeXon.HuntSalvoGateService.MaxLoalLocks(valueM));
#else
                float score = valueM * 6000f - dist - locks * 20000f;
#endif
                if (score > bestScore)
                {
                    bestScore = score;
                    best = u;
                }
            }
#if ORITASY_COMBINED
            if (best != null)
            {
                try { WeXon.HuntSalvoGateService.NotePick(best.GetInstanceID()); }
                catch { }
            }
#endif
            return best;
        }

        private static int CountSiblingLocks(Missile self, Unit u)
        {
            if (u == null)
                return 0;
            int n = 0;
            for (int i = 0; i < Kh85Live.All.Count; i++)
            {
                Missile m = Kh85Live.All[i];
                if (m == null || object.ReferenceEquals(m, self))
                    continue;
                Unit t = null;
                try
                {
                    if (TargetField != null)
                        t = TargetField.GetValue(m) as Unit;
                }
                catch { }
                if (t != null && object.ReferenceEquals(t, u))
                    n++;
            }
            return n;
        }

        private static bool IsValidHuntTarget(Missile self, Unit u, string letter)
        {
            if (self == null || u == null)
                return false;
            if (object.ReferenceEquals(u, self))
                return false;
            if (u is Missile)
                return false;
            if (u is Scenery)
                return false;
            try
            {
                if (u.disabled)
                    return false;
            }
            catch { }
            if (u is Container)
                return false;
            Unit owner = null;
            try { owner = self.owner; }
            catch { }
            // LOAL: opposing combat HQ only. Null HQ and Neutral are not hostiles.
            if (!Kh85Util.IsStrictHostile(self, u))
                return false;

#if ORITASY_COMBINED
            try
            {
                if (WeXon.Plugin.IsJunkHuntTarget(u))
                    return false;
            }
            catch { }
#endif

            if (Kh85Weapon.SanitizeLockTarget(owner, u) == null)
                return false;

            if (letter == "D")
                return Kh85DArm.IsValidRadarTarget(self, u);

            if (letter == "C")
            {
                if (u is Ship)
                    return true;
                try
                {
                    if (u.GetComponentInParent<Ship>() != null)
                        return true;
                }
                catch { }
                // Ground: not aircraft.
                if (u is Aircraft)
                    return false;
                try
                {
                    if (u.GetComponentInParent<Aircraft>() != null)
                        return false;
                }
                catch { }
                return true;
            }

            return true;
        }
    }

    internal static class Kh85Util
    {
        internal static bool IsKh85(Missile missile)
        {
            if (missile == null)
                return false;
            // Hot path: tag is authoritative after OnSpawned.
            if (missile.GetComponent<Kh85VariantTag>() != null)
                return true;
            try
            {
                if (missile.definition != null
                    && !string.IsNullOrEmpty(missile.definition.jsonKey))
                {
                    // Definitive reject for vanilla AGM/AAM/etc — never GetMissileInfo on hot path.
                    if (missile.definition.jsonKey.StartsWith(Kh85Weapon.PackKey, StringComparison.OrdinalIgnoreCase))
                        return true;
                    return false;
                }
            }
            catch { }
            return Kh85Weapon.IsKh85Info(Kh85Weapon.GetMissileInfo(missile));
        }

        internal static string GetVariant(Missile missile)
        {
            if (missile == null)
                return "C";
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag != null && !string.IsNullOrEmpty(tag.Letter))
                return tag.Letter;
            try
            {
                if (missile.definition != null && !string.IsNullOrEmpty(missile.definition.jsonKey))
                    return Kh85Weapon.VariantLetterFromKey(missile.definition.jsonKey);
            }
            catch { }
            string un = null;
            try { un = missile.NetworkunitName; }
            catch { }
            if (!string.IsNullOrEmpty(un))
            {
                if (un.IndexOf("TGM-85A", StringComparison.OrdinalIgnoreCase) >= 0
                    || un.IndexOf("Coordinator", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "A";
                if (un.IndexOf("TGM-85B", StringComparison.OrdinalIgnoreCase) >= 0
                    || un.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "B";
                if (un.IndexOf("TGM-85D", StringComparison.OrdinalIgnoreCase) >= 0
                    || un.IndexOf("Hegemony", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "D";
                if (un.IndexOf("TGM-85E", StringComparison.OrdinalIgnoreCase) >= 0
                    || un.IndexOf("Torjan", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "E";
                if (un.IndexOf("TGM-85S", StringComparison.OrdinalIgnoreCase) >= 0
                    || un.IndexOf("Seaker", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "S";
            }
            return "C";
        }

        internal static void StampVariant(Missile missile, string letter, string sourceKey)
        {
            if (missile == null)
                return;
            if (string.IsNullOrEmpty(letter))
                letter = "C";
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag == null)
                tag = missile.gameObject.AddComponent<Kh85VariantTag>();
            tag.Letter = letter;
            tag.SourceKey = sourceKey;
            Kh85Live.Register(missile);
        }

        /// <summary>NetworkHQ first, then MapHQ (buildings often only have MapHQ).</summary>
        internal static FactionHQ ResolveHq(Unit unit)
        {
            if (unit == null)
                return null;
            FactionHQ hq = null;
            try { hq = unit.NetworkHQ; }
            catch { }
            if (hq != null)
                return hq;
            try { hq = unit.MapHQ; }
            catch { }
            return hq;
        }

        /// <summary>Both HQs present and the same object. Null HQ is not friendly.</summary>
        internal static bool IsConfirmedFriendly(Unit shooterSide, Unit candidate)
        {
            if (shooterSide == null || candidate == null)
                return false;
            FactionHQ a = ResolveHq(shooterSide);
            FactionHQ b = ResolveHq(candidate);
            return a != null && b != null && object.ReferenceEquals(a, b);
        }

        /// <summary>
        /// Free-hunt / vanilla auto-pick: both HQs required and different.
        /// Incomplete HQ (hangars) is not hostile — that is the 175C hangar-LOAL gate.
        /// </summary>
        internal static bool IsStrictHostile(Missile self, Unit candidate)
        {
            if (self == null || candidate == null)
                return false;
            if (object.ReferenceEquals(candidate, self))
                return false;
            Unit owner = null;
            try { owner = self.owner; }
            catch { }
            if (owner != null && object.ReferenceEquals(candidate, owner))
                return false;
            FactionHQ a = ResolveHq(owner);
            if (a == null)
                a = ResolveHq(self);
            FactionHQ b = ResolveHq(candidate);
            if (a == null || b == null)
                return false;
            if (IsNeutralHq(a) || IsNeutralHq(b))
                return false;
            return !object.ReferenceEquals(a, b);
        }

        internal static bool IsNeutralHq(FactionHQ hq)
        {
            if (hq == null)
                return true;
            string objName = null;
            string facName = null;
            try { objName = hq.name; }
            catch { }
            try
            {
                if (hq.faction != null)
                    facName = hq.faction.factionName;
            }
            catch { }
            if (LooksNeutralLabel(objName) || LooksNeutralLabel(facName))
                return true;
            return false;
        }

        private static bool LooksNeutralLabel(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            string n = s.ToLowerInvariant();
            if (n.IndexOf("neutral", StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("\u4e2d\u7acb", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }
    }

    /// <summary>
    /// Standalone survival (works with Oritasy OFF). Vanilla Optical/Cruise SlowChecks
    /// detonates on MissedTarget / LosingGround / null targetUnit — early bad aim or LOAL
    /// without MultiModeBrain looks like vanish-on-fire.
    /// </summary>
    internal static class Kh85Survival
    {
        internal static bool ShouldSuppressGeometrySd(Missile missile)
        {
            if (missile == null || !Kh85Util.IsKh85(missile))
                return false;
            try
            {
                // Longer window — MissedTarget after sticky look-ahead aim flips was SD'ing mid-course.
                if (missile.timeSinceSpawn < 12f)
                    return true;
            }
            catch { return true; }
            try
            {
                if (missile.GetThrust() > 0.5f || missile.EngineOn())
                    return true;
            }
            catch
            {
                try
                {
                    if (missile.GetThrust() > 0.5f)
                        return true;
                }
                catch { }
            }
            return false;
        }

        /// <summary>
        /// Skip SlowChecks Detonate (null target / miss / burnout) for the whole Kh85 flight.
        /// Combined build also skips for every other guided missile (WeXon SD suppress).
        /// </summary>
        internal static bool ShouldSkipSlowChecks(Missile missile)
        {
            if (missile == null)
                return false;
            if (Kh85Util.IsKh85(missile) || Kh85Weapon.IsPendingKh85Donor(missile))
                return true;
            if (ShouldSuppressGeometrySd(missile))
                return true;
#if ORITASY_COMBINED
            try
            {
                if (WeXon.Plugin.ShouldSuppressSeekerSelfDestruct(missile))
                    return true;
            }
            catch { }
#endif
            return false;
        }

        /// <summary>
        /// 175C: vanilla cruise Seek always runs (A/B/D terminal + C TerrainWaypoint).
        /// Skip only PreTerminalMode for C/E/S — that path Detonates at ~6s on null target.
        /// </summary>
        internal static bool ShouldSkipCruisePreTerminal(Missile missile)
        {
            if (missile == null)
                return false;
            if (Kh85Util.IsKh85(missile))
            {
                string letter = Kh85Util.GetVariant(missile);
                return letter == "C" || letter == "E" || letter == "S";
            }
            return Kh85Weapon.IsPendingKh85Donor(missile);
        }
    }

    /// <summary>Keep LosingGround from self-destroying thrusting / launch-frame TGM missiles.</summary>
    [HarmonyPatch(typeof(Missile), "LosingGround")]
    internal static class Patch_Survival_LosingGround
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || !Kh85Survival.ShouldSuppressGeometrySd(__instance))
                return;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(Missile), "MissedTarget")]
    internal static class Patch_Survival_MissedTarget
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || !Kh85Survival.ShouldSuppressGeometrySd(__instance))
                return;
            __result = false;
        }
    }

    /// <summary>
    /// Skip seeker SlowChecks that Detonate TGM during launch / LOAL. Own class for TargetMethods.
    /// OpticalSeekerHighDrag (0.34.x) has no SlowChecks — bind only declared methods via
    /// reflection GetMethod (AccessTools.Method logs a HarmonyX warning on miss).
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_Survival_SlowChecks
    {
        private static readonly Type[] SeekerTypes = new Type[]
        {
            typeof(ARHSeeker),
            typeof(SARHSeeker),
            typeof(IRSeeker),
            typeof(ARMSeeker),
            typeof(LaserSeeker),
            typeof(OpticalSeeker),
            typeof(OpticalSeekerBomb),
            typeof(OpticalSeekerCruiseMissile),
            typeof(OpticalSeekerHighDrag),
            typeof(OpticalSeekerShell),
            typeof(InertialSeekerShell),
        };

        private const BindingFlags SlowChecksFlags =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            for (int i = 0; i < SeekerTypes.Length; i++)
            {
                Type t = SeekerTypes[i];
                if (t == null)
                    continue;
                MethodInfo m = null;
                try { m = t.GetMethod("SlowChecks", SlowChecksFlags); }
                catch { m = null; }
                if (m != null)
                    yield return m;
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(object __instance)
        {
            MonoBehaviour mb = __instance as MonoBehaviour;
            if (mb == null)
                return true;
            Missile missile = mb.GetComponentInParent<Missile>();
            if (missile == null)
                return true;
            // false = skip original SlowChecks (no Detonate from null target / miss).
            if (Kh85Survival.ShouldSkipSlowChecks(missile))
                return false;
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance)
        {
            MonoBehaviour mb = __instance as MonoBehaviour;
            if (mb == null)
                return;
            Missile missile = mb.GetComponentInParent<Missile>();
            if (missile == null || !Kh85Util.IsKh85(missile))
                return;
            try
            {
                FieldInfo tooFast = AccessTools.Field(__instance.GetType(), "tooFast");
                if (tooFast != null && tooFast.FieldType == typeof(bool))
                    tooFast.SetValue(__instance, false);
            }
            catch { }
        }
    }

    /// <summary>
    /// C/E/S: skip PreTerminalMode Detonate (null target + knownPos in range ~6s).
    /// A/B/D keep vanilla terminal. Seek is never skipped (175C).
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "PreTerminalMode")]
    internal static class Patch_Survival_CruisePreTerminal
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(OpticalSeekerCruiseMissile __instance)
        {
            if (__instance == null)
                return true;
            Missile missile = __instance.GetComponentInParent<Missile>();
            if (Kh85Survival.ShouldSkipCruisePreTerminal(missile))
                return false;
            if (missile != null && (Kh85Util.IsKh85(missile) || Kh85Weapon.IsPendingKh85Donor(missile)))
            {
                try
                {
                    FieldInfo tu = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
                    Unit t = tu != null ? tu.GetValue(__instance) as Unit : null;
                    if (t == null)
                        return false;
                }
                catch { return false; }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "FixedUpdate")]
    internal static class Patch_Maneuver
    {
        private static readonly HashSet<int> Done = new HashSet<int>();
        private static FieldInfo _torque;
        private static FieldInfo _gLimit;
        private static float _nextPrune;

        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            if (__instance == null)
                return;
            if (Time.unscaledTime >= _nextPrune)
            {
                _nextPrune = Time.unscaledTime + 90f;
                if (Done.Count > 256)
                    Done.Clear();
            }
            int id = __instance.GetInstanceID();
            if (Done.Contains(id))
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(__instance))
                return;
            // Tag only — never IsKh85→GetMissileInfo for every in-flight AGM.
            if (__instance.GetComponent<Kh85VariantTag>() == null)
            {
                Kh85Weapon.NoteNonKh85Missile(__instance);
                return;
            }
            Done.Add(id);

            string variant = Kh85Util.GetVariant(__instance);
            float mul = 2f;
            Kh85Advanced.VariantMul.TryGetValue(variant, out mul);

            try
            {
                if (_torque == null)
                    _torque = AccessTools.Field(typeof(Missile), "torque");
                if (_gLimit == null)
                    _gLimit = AccessTools.Field(typeof(Missile), "gLimit");
                if (_torque != null)
                {
                    float v = Convert.ToSingle(_torque.GetValue(__instance));
                    _torque.SetValue(__instance, v * mul);
                }
                if (_gLimit != null)
                {
                    float v = Convert.ToSingle(_gLimit.GetValue(__instance));
                    if (v > 0.01f)
                    {
                        v *= mul;
                        float floor = 22f;
                        if (variant == "C")
                            floor = 32f;
                        else if (variant == "S")
                            floor = 28f;
                        else if (variant == "E")
                            floor = 24f;
                        if (v < floor)
                            v = floor;
                        _gLimit.SetValue(__instance, v);
                    }
                }
                if (_torque != null)
                {
                    float tq = Convert.ToSingle(_torque.GetValue(__instance));
                    if (tq < 12f)
                        _torque.SetValue(__instance, 16f);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Keep TGM-85 in active-search for the whole flight (per-missile throttle).
    /// Standalone LOAL hunt is Kh85SelfHunt; freeHunt writes removed (field absent in DebugRef).
    /// </summary>
    [HarmonyPatch(typeof(Missile), "FixedUpdate")]
    internal static class Patch_Guidance
    {
        private static readonly Dictionary<int, float> NextApplyById = new Dictionary<int, float>(64);
        private static float _nextPrune;

        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            if (__instance == null)
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(__instance))
                return;
            // Hot path: tagged only — skip InfoByKey / name scans for every missile.
            if (__instance.GetComponent<Kh85VariantTag>() == null)
            {
                Kh85Weapon.NoteNonKh85Missile(__instance);
                return;
            }

            int id = __instance.GetInstanceID();
            float next;
            if (NextApplyById.TryGetValue(id, out next) && Time.time < next)
                return;
            NextApplyById[id] = Time.time + 0.75f;

            if (Time.unscaledTime >= _nextPrune)
            {
                _nextPrune = Time.unscaledTime + 60f;
                if (NextApplyById.Count > 128)
                    NextApplyById.Clear();
            }

            bool auto = Kh85Advanced.AutoSeek == null || Kh85Advanced.AutoSeek.Value;
            bool multi = Kh85Advanced.MultiMode == null || Kh85Advanced.MultiMode.Value;
            if (!auto && !multi)
                return;

            try
            {
                if (auto)
                    __instance.seekerMode = Missile.SeekerMode.activeSearch;
            }
            catch { }

            // Ensure standalone hunt is attached if LOAL and still unlocked.
            bool hasMmLoal = false;
#if ORITASY_COMBINED
            try { hasMmLoal = __instance.GetComponent<WeXon.MultiModeBrain>() != null; }
            catch { hasMmLoal = false; }
#endif
            if (multi && !hasMmLoal
                && __instance.GetComponent<Kh85LockHold>() == null
                && __instance.GetComponent<Kh85SelfHunt>() == null)
            {
                Kh85SelfHunt.Attach(__instance);
            }
        }
    }
}
