using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeXon
{
    /// <summary>
    /// G Factor Bomb: 5 kg store, UFO bomb model, 5000kt detonation, hangar cost 1M.
    /// Hangar icon is the vanilla ammo-dump sprite.
    /// </summary>
    internal static class AmmoDump50TWeapon
    {
        internal const string JsonKey = "ammoDump_50T";
        internal const string DisplayName = "G Factor Bomb";
        internal const string ShortName = "G Factor Bomb";
        internal const string NameTag = "G Factor Bomb";
        internal const string DescriptionEn =
            "High-density concentrated G-element (G Factor) bomb.";
        internal const string DescriptionZh =
            "高密度浓缩G元素（G Factor）炸弹";
        internal const float MassKg = 5f;
        /// <summary>Hangar units are millions; 1 = $1M.</summary>
        internal const float CostPerRound = 1f;
        /// <summary>Game scale: 1kt = 1000000 → 5000kt.</summary>
        internal const float NukeYield5000kt = 5000000000f;
        internal const float VisualScaleUfo = 1f;
        internal const float VisualScaleDump = 0.1f;
        internal const float VisualYieldKt = 5000f;
        internal const float HullHitPoints = 1f;

        private const string MarkerName = "WeXon_AmmoDump50T";
        private const string VisualChildName = "WeXon_AmmoDump50T_Vis";

        private static readonly FieldInfo BlastYieldField = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo MissileInfoField = AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo ShockYieldField = AccessTools.Field(typeof(Shockwave), "yieldKilotons");
        private static readonly FieldInfo ShockRadiusField = AccessTools.Field(typeof(Shockwave), "blastRadius");
        private static readonly FieldInfo ShockVaporGoField = AccessTools.Field(typeof(Shockwave), "vaporCloud");
        private static readonly FieldInfo ShockVaporMatField = AccessTools.Field(typeof(Shockwave), "vaporCloudMat");
        private static readonly FieldInfo ArmorField = AccessTools.Field(typeof(Missile), "armorProperties");
        private static readonly FieldInfo WarheadField = AccessTools.Field(typeof(Missile), "warhead");
        private static readonly ArmorProperties GlassArmor = MakeGlassArmor();
        private static readonly Color ShockAlbedo = new Color(0.82f, 0.66f, 1f, 1f);
        private static readonly Color ShockEmit = new Color(7.2f, 3.8f, 14.5f, 1f);
        private static readonly MaterialPropertyBlock ShockMpb = new MaterialPropertyBlock();
        private static readonly HashSet<int> PurpleShockIds = new HashSet<int>();
        private static readonly HashSet<int> TintedRendererIds = new HashSet<int>();

        private static bool _injected;
        private static WeaponMount _clone;
        private static WeaponInfo _info;
        private static GameObject _visualDonor;
        private static bool _visualTried;
        private static bool _visualIsUfo;
        private static float _nextVisualRetry;
        private static float _nextHardpointInject;
        private static float _nextMaintAt;
        private static float _nextMissingLog;
        private static int _hardpointIdlePasses;
        private static int _lastHardpointLogAdded = -1;
        private static int _pendingFxFrames;
        private static Unit _pendingOwner;
        private static float _pendingFireAt;
        private static bool _shattering;
        private static Vector3 _shatterOrigin;
        private static float _shatterRadiusSq;
        private static float _shatterUntil;
        private static readonly HashSet<int> ShipWmIds = new HashSet<int>();
        private static readonly HashSet<int> AirWmIds = new HashSet<int>();

        internal static bool IgnoresResist(Unit u)
        {
            if (u == null || u.transform == null)
                return false;
            if (Time.unscaledTime > _shatterUntil)
                return false;
            return (u.transform.position - _shatterOrigin).sqrMagnitude <= _shatterRadiusSq;
        }

        internal static void NoteShatter(Vector3 pos, float radiusM)
        {
            if (_shattering)
                return;
            if (Time.unscaledTime < _shatterUntil
                && (_shatterOrigin - pos).sqrMagnitude < 400f)
                return;
            float r = radiusM;
            if (r < 200f)
                r = 900f * Mathf.Pow(VisualYieldKt, 1f / 3f);
            _shatterOrigin = pos;
            _shatterRadiusSq = r * r;
            _shatterUntil = Time.unscaledTime + 3f;
            ShatterRadius(pos, r);
        }

        private static float ReadShockRadius(GameObject fx)
        {
            if (fx == null || ShockRadiusField == null)
                return 0f;
            try
            {
                Shockwave[] waves = fx.GetComponentsInChildren<Shockwave>(true);
                if (waves == null)
                    return 0f;
                float best = 0f;
                for (int i = 0; i < waves.Length; i++)
                {
                    if (waves[i] == null)
                        continue;
                    float r = (float)ShockRadiusField.GetValue(waves[i]);
                    if (r > best)
                        best = r;
                }
                return best;
            }
            catch { return 0f; }
        }

        private static void ShatterRadius(Vector3 pos, float radiusM)
        {
            if (_shattering)
                return;
            _shattering = true;
            try
            {
                float rSq = radiusM * radiusM;
                List<Unit> all = null;
                try { all = UnitRegistry.allUnits; }
                catch { all = null; }
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.transform == null)
                            continue;
                        if ((u.transform.position - pos).sqrMagnitude > rSq)
                            continue;
                        ShatterUnit(u, pos);
                    }
                }
                Missile[] missiles = null;
                try { missiles = UnityEngine.Object.FindObjectsOfType<Missile>(); }
                catch { missiles = null; }
                if (missiles != null)
                {
                    for (int i = 0; i < missiles.Length; i++)
                    {
                        Missile m = missiles[i];
                        if (m == null || m.transform == null)
                            continue;
                        if ((m.transform.position - pos).sqrMagnitude > rSq)
                            continue;
                        ShatterUnit(m, pos);
                    }
                }
            }
            finally
            {
                _shattering = false;
            }
        }

        private static void ShatterUnit(Unit u, Vector3 origin)
        {
            if (u == null)
                return;
            if (GFactorRutherfordField.IsShielded(u))
                return;
            if (GFactorRutherfordField.IsInsideField(u))
                return;
            Vector3 away = Vector3.up;
            try
            {
                away = u.transform.position - origin;
                if (away.sqrMagnitude < 0.25f)
                    away = Vector3.up;
                else
                    away.Normalize();
            }
            catch { }
            Vector3 vel = away * 120f;
            UnitPart[] parts = null;
            try { parts = u.GetComponentsInChildren<UnitPart>(true); }
            catch { parts = null; }
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    UnitPart p = parts[i];
                    if (p == null)
                        continue;
                    try { p.hitPoints = 0f; }
                    catch { }
                    try { p.ApplyDamage(0f, 1e9f, 0f, 2e6f); }
                    catch { }
                    try { p.SpawnFragments(); }
                    catch { }
                    try { p.Detach(vel, origin); }
                    catch { }
                }
            }
            Wreckage[] wrecks = null;
            try { wrecks = u.GetComponentsInChildren<Wreckage>(true); }
            catch { wrecks = null; }
            if (wrecks != null)
            {
                for (int i = 0; i < wrecks.Length; i++)
                {
                    if (wrecks[i] == null)
                        continue;
                    try { wrecks[i].Disintegrate(); }
                    catch { }
                }
            }
            Aircraft ac = u as Aircraft;
            if (ac != null && ac.pilots != null)
            {
                for (int i = 0; i < ac.pilots.Length; i++)
                {
                    Pilot pl = ac.pilots[i];
                    if (pl == null)
                        continue;
                    try { pl.ApplyDamage(0f, 1e9f, 0f, 1e9f); }
                    catch { }
                }
            }
            try { u.DisableUnit(); }
            catch { }
        }

        internal static bool IsInjected { get { return _injected; } }

        internal static bool IsKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            if (string.Equals(key, JsonKey, StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(key, "gFactorContainer", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsKey(mount.jsonKey))
                return true;
            return IsInfo(mount.info);
        }

        internal static bool IsInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (_info != null && object.ReferenceEquals(info, _info))
                return true;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            if (n.IndexOf("G Factor Bomb", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("G Factor Container", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("[50T]", StringComparison.OrdinalIgnoreCase) >= 0
                && n.IndexOf("Ammo Dump", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (info.name != null
                && (info.name.IndexOf("AmmoDump50T", StringComparison.OrdinalIgnoreCase) >= 0
                    || info.name.IndexOf("GFactor", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }

        internal static bool IsMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.GetComponent<AmmoDump50TMark>() != null)
                    return true;
            }
            catch { }
            return IsInfo(GetMissileInfo(missile));
        }

        private static WeaponInfo GetMissileInfo(Missile missile)
        {
            if (missile == null || MissileInfoField == null)
                return null;
            try { return MissileInfoField.GetValue(missile) as WeaponInfo; }
            catch { return null; }
        }

        internal static void Ensure()
        {
            if (_clone == null)
                _injected = false;
            if (!_injected)
            {
                WeaponMount donor = FindBombDonor();
                if (donor == null || donor.info == null || donor.prefab == null)
                {
                    if (Time.unscaledTime >= _nextMissingLog)
                    {
                        _nextMissingLog = Time.unscaledTime + 8f;
                        if (Plugin.Log != null)
                            Plugin.Log.LogWarning("G Factor Container: waiting for bomb donor / ammo dump assets...");
                    }
                    return;
                }
                if (CreateClone(donor))
                {
                    _injected = true;
                    _nextHardpointInject = 0f;
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("G Factor Container: mount ready (5kg / 1M / 5000kt / UFO bomb model)");
                    InjectIntoAircraftHardpoints();
                }
            }
            if (!_injected)
                return;

            BindUfoPrefabIfReady();

            if (Time.unscaledTime >= _nextMaintAt)
            {
                _nextMaintAt = Time.unscaledTime + 12f;
                RegisterWithEncyclopedia(Plugin.GetEncyclopedia());
                EnsureMountsInCache();
                RestoreIdentity();
            }
            if (Time.unscaledTime >= _nextHardpointInject)
                InjectIntoAircraftHardpoints();
        }

        internal static void InjectIntoAircraftHardpoints()
        {
            if (!_injected || _clone == null)
                return;
            _nextHardpointInject = Time.unscaledTime + 8f;
            WeaponManager[] managers = Resources.FindObjectsOfTypeAll<WeaponManager>();
            if (managers == null)
                return;
            int added = 0;
            for (int i = 0; i < managers.Length; i++)
                added += InjectIntoWeaponManager(managers[i]);
            if (added > 0 && added != _lastHardpointLogAdded && Plugin.Log != null)
            {
                _lastHardpointLogAdded = added;
                Plugin.Log.LogInfo("G Factor Container: wired onto " + added + " aircraft hardpoints");
            }
            if (added == 0)
                _hardpointIdlePasses++;
            else
                _hardpointIdlePasses = 0;
        }

        internal static int InjectIntoWeaponManager(WeaponManager wm)
        {
            if (!_injected || _clone == null || wm == null || wm.hardpointSets == null)
                return 0;
            if (IsShipWeaponManager(wm))
                return 0;
            if (IsJ35Draken(wm))
                return 0;
            int added = 0;
            for (int h = 0; h < wm.hardpointSets.Length; h++)
            {
                HardpointSet hs = wm.hardpointSets[h];
                if (hs == null || Plugin.IsNavalHardpoint(hs))
                    continue;
                if (hs.weaponOptions == null)
                    hs.weaponOptions = new List<WeaponMount>();
                if (!HardpointCanTakeStore(hs))
                    continue;
                if (hs.weaponOptions.Contains(_clone))
                    continue;
                hs.weaponOptions.Add(_clone);
                added++;
            }
            return added;
        }

        internal static void EnsureMountsInCache()
        {
            if (_clone == null || _clone.prefab == null)
                return;
            if (Plugin.CachedMountSet.Add(_clone))
                Plugin.CachedMounts.Add(_clone);
        }

        internal static void AppendMountsToList(List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null || have == null)
                return;
            if (_clone == null || _clone.prefab == null)
                return;
            if (have.Add(_clone))
                list.Add(_clone);
        }

        internal static bool IsHangingOn(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.disabled)
                    return false;
            }
            catch { return false; }

            AmmoDump50THull[] hulls = null;
            try { hulls = ac.GetComponentsInChildren<AmmoDump50THull>(true); }
            catch { hulls = null; }
            if (hulls != null)
            {
                for (int i = 0; i < hulls.Length; i++)
                {
                    AmmoDump50THull h = hulls[i];
                    if (h == null || !h.Hung)
                        continue;
                    if (h.HitPoints > 0f)
                        return true;
                }
            }

            if (ac.weaponStations != null)
            {
                for (int s = 0; s < ac.weaponStations.Count; s++)
                {
                    WeaponStation st = ac.weaponStations[s];
                    if (!StationIsOurs(st))
                        continue;
                    if (st.Ammo > 0)
                        return true;
                    if (st.Weapons != null && st.Weapons.Count > 0)
                        return true;
                }
            }

            WeaponManager wm = ac.weaponManager;
            if (wm != null && wm.hardpointSets != null)
            {
                for (int h = 0; h < wm.hardpointSets.Length; h++)
                {
                    HardpointSet hs = wm.hardpointSets[h];
                    if (hs == null || !IsMount(hs.weaponMount))
                        continue;
                    if (LiveWeaponsOnSet(hs))
                        return true;
                }
            }

            Weapon[] weapons = null;
            try { weapons = ac.GetComponentsInChildren<Weapon>(true); }
            catch { weapons = null; }
            if (weapons != null)
            {
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null || w is Gun)
                        continue;
                    try
                    {
                        AmmoDump50THull hung = w.GetComponent<AmmoDump50THull>();
                        if (hung != null && !hung.Hung)
                            continue;
                    }
                    catch { }
                    if (IsInfo(w.info))
                        return true;
                    WeaponMount mount = null;
                    try { mount = Plugin.GetWeaponMount(w); }
                    catch { mount = null; }
                    if (IsMount(mount))
                        return true;
                }
            }
            return false;
        }

        private static bool LiveWeaponsOnSet(HardpointSet hs)
        {
            if (hs == null)
                return false;
            if (hs.hardpoints == null)
                return IsMount(hs.weaponMount);
            for (int i = 0; i < hs.hardpoints.Count; i++)
            {
                Hardpoint hp = hs.hardpoints[i];
                if (hp == null || hp.transform == null)
                    continue;
                Weapon[] ws = null;
                try { ws = hp.transform.GetComponentsInChildren<Weapon>(true); }
                catch { continue; }
                if (ws == null)
                    continue;
                for (int k = 0; k < ws.Length; k++)
                {
                    Weapon w = ws[k];
                    if (w == null || w is Gun)
                        continue;
                    try
                    {
                        AmmoDump50THull hung = w.GetComponent<AmmoDump50THull>();
                        if (hung != null && !hung.Hung)
                            continue;
                    }
                    catch { }
                    if (IsInfo(w.info) || IsMount(Plugin.GetWeaponMount(w)))
                        return true;
                }
            }
            return false;
        }

        internal static bool StationIsOurs(WeaponStation station)
        {
            if (station == null)
                return false;
            if (IsInfo(station.WeaponInfo))
                return true;
            if (station.Weapons == null)
                return false;
            for (int i = 0; i < station.Weapons.Count; i++)
            {
                Weapon w = station.Weapons[i];
                if (w == null)
                    continue;
                if (IsInfo(w.info))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True when vanilla warhead math would still charge this station
        /// (donor GPO-N info, or an older nuclear=true clone).
        /// </summary>
        internal static bool VanillaCountsStationAsWarhead(WeaponStation station)
        {
            if (station == null)
                return false;
            if (station.WeaponInfo != null && station.WeaponInfo.nuclear)
                return true;
            if (station.Weapons == null)
                return false;
            for (int i = 0; i < station.Weapons.Count; i++)
            {
                Weapon w = station.Weapons[i];
                if (w != null && w.info != null && w.info.nuclear)
                    return true;
            }
            return false;
        }

        internal static int CountSelectedWarheadsVanillaWouldCharge(WeaponManager wm)
        {
            if (wm == null || wm.hardpointSets == null)
                return 0;
            int n = 0;
            for (int h = 0; h < wm.hardpointSets.Length; h++)
            {
                HardpointSet hs = wm.hardpointSets[h];
                if (hs == null || !IsMount(hs.weaponMount))
                    continue;
                if (hs.weaponMount.info == null || !hs.weaponMount.info.nuclear)
                    continue;
                int pylons = 1;
                if (hs.hardpoints != null && hs.hardpoints.Count > 0)
                    pylons = hs.hardpoints.Count;
                int ammo = hs.weaponMount.ammo > 0 ? hs.weaponMount.ammo : 1;
                n += pylons * ammo;
            }
            return n;
        }

        internal static void SyncFromMount(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || mount == null || weapon is Gun || !IsMount(mount))
                return;
            RestoreIdentity();
            if (mount.info == null)
                return;
            weapon.info = mount.info;
            ApplyVisual(weapon.gameObject, true);
            AttachHangHull(weapon);
        }

        internal static void NoteFire(Weapon weapon)
        {
            if (weapon == null)
                return;
            WeaponMount mount = Plugin.GetWeaponMount(weapon);
            if (!IsMount(mount) && !IsInfo(weapon.info))
                return;
            _pendingOwner = weapon.attachedUnit;
            _pendingFireAt = Time.time;
            try
            {
                AmmoDump50THull hull = weapon.GetComponent<AmmoDump50THull>();
                if (hull != null)
                    hull.Hung = false;
            }
            catch { }
            GFactorRutherfordField.NotifyDrop(weapon);
        }

        internal static void ApplyToMissile(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (missile.gameObject == null || !missile.gameObject.scene.IsValid())
                    return;
            }
            catch { return; }
            bool pending = _pendingOwner != null && (Time.time - _pendingFireAt) < 2.5f;
            bool ours = IsMissile(missile) || IsInfo(GetMissileInfo(missile));
            if (!ours && pending)
            {
                WeaponInfo info = GetMissileInfo(missile);
                bool looksBomb = false;
                try
                {
                    looksBomb = info != null && (info.bomb || info.nuclear);
                }
                catch { }
                if (looksBomb)
                    ours = true;
            }
            if (!ours)
                return;

            try
            {
                AmmoDump50TMark mark = missile.GetComponent<AmmoDump50TMark>();
                if (mark == null)
                    mark = missile.gameObject.AddComponent<AmmoDump50TMark>();
                mark.HitPoints = HullHitPoints;
            }
            catch { }

            if (_info != null && MissileInfoField != null)
            {
                try { MissileInfoField.SetValue(missile, _info); }
                catch { }
            }
            if (BlastYieldField != null)
            {
                try { BlastYieldField.SetValue(missile, NukeYield5000kt); }
                catch { }
            }
            try
            {
                if (missile.rb != null)
                    missile.rb.mass = MassKg;
            }
            catch { }
            MakeMissileGlass(missile);
            SetChildPartHitPoints(missile.gameObject, HullHitPoints);
            ApplyVisual(missile.gameObject, false);
            _pendingOwner = null;
        }

        internal static bool AbsorbHit(Missile missile, float pierce, float blast, float fire, float impact)
        {
            if (!IsMissile(missile))
                return false;
            float dmg = pierce + blast + fire + impact;
            if (dmg <= 0f)
                return true;
            AmmoDump50TMark mark = null;
            try { mark = missile.GetComponent<AmmoDump50TMark>(); }
            catch { }
            if (mark == null)
            {
                KillNow(missile);
                return true;
            }
            mark.HitPoints -= dmg;
            if (mark.HitPoints > 0f)
                return true;
            KillNow(missile);
            return true;
        }

        internal static void KillNow(Missile missile)
        {
            if (missile == null)
                return;
            PrepareDetonate(missile);
            try
            {
                if (WarheadField != null)
                {
                    Missile.Warhead wh = WarheadField.GetValue(missile) as Missile.Warhead;
                    if (wh != null)
                        wh.Armed = true;
                }
            }
            catch { }
            try { missile.Arm(); }
            catch { }
            try { missile.Detonate(Vector3.up, false, false); }
            catch { }
        }

        internal static void DetonateAt(Vector3 pos)
        {
            _pendingFxFrames = 12;
            GameObject prefab = null;
            GameObject fxGo = null;
            try { prefab = Plugin.ResolveNukeFx(false); }
            catch { prefab = null; }
            if (prefab != null)
            {
                try { fxGo = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity); }
                catch { fxGo = null; }
                if (fxGo != null)
                {
                    try
                    {
                        MushroomCloud mc = fxGo.GetComponentInChildren<MushroomCloud>(true);
                        if (mc != null)
                            mc.yield = VisualYieldKt;
                    }
                    catch { }
                    if (ShockYieldField != null)
                    {
                        try
                        {
                            Shockwave[] waves = fxGo.GetComponentsInChildren<Shockwave>(true);
                            if (waves != null)
                            {
                                for (int i = 0; i < waves.Length; i++)
                                {
                                    if (waves[i] == null)
                                        continue;
                                    waves[i].enabled = true;
                                    ShockYieldField.SetValue(waves[i], VisualYieldKt);
                                    MarkPurpleShock(waves[i]);
                                }
                            }
                        }
                        catch { }
                    }
                }
            }
            ScaleSpawnedNukeFx(pos);
            NoteShatter(pos, ReadShockRadius(fxGo));
        }

        private static ArmorProperties MakeGlassArmor()
        {
            ArmorProperties ap = new ArmorProperties();
            ap.pierceArmor = 0f;
            ap.blastArmor = 0f;
            ap.fireArmor = 0f;
            ap.pierceTolerance = 0f;
            ap.blastTolerance = 0f;
            ap.fireTolerance = 0f;
            ap.overpressureLimit = 0f;
            return ap;
        }

        internal static ArmorProperties GlassArmorForHull()
        {
            return GlassArmor;
        }

        private static void MakeMissileGlass(Missile missile)
        {
            if (missile == null || ArmorField == null)
                return;
            try { ArmorField.SetValue(missile, GlassArmor); }
            catch { }
        }

        private static void SetChildPartHitPoints(GameObject root, float hp)
        {
            if (root == null)
                return;
            UnitPart[] parts = null;
            try { parts = root.GetComponentsInChildren<UnitPart>(true); }
            catch { return; }
            if (parts == null)
                return;
            for (int i = 0; i < parts.Length; i++)
            {
                UnitPart p = parts[i];
                if (p == null)
                    continue;
                try { p.hitPoints = hp; }
                catch { }
            }
        }

        private static void AttachHangHull(Weapon weapon)
        {
            if (weapon == null || weapon.gameObject == null)
                return;
            try
            {
                if (!weapon.gameObject.scene.IsValid())
                    return;
            }
            catch { return; }
            AmmoDump50THull hull = weapon.GetComponent<AmmoDump50THull>();
            if (hull == null)
            {
                try { hull = weapon.gameObject.AddComponent<AmmoDump50THull>(); }
                catch { return; }
            }
            Unit unit = weapon.attachedUnit;
            if (unit == null)
            {
                try { unit = weapon.GetComponentInParent<Unit>(); }
                catch { }
            }
            hull.Init(unit);
            if (unit != null)
            {
                try { unit.RegisterDamageable(hull); }
                catch { }
                GFactorRutherfordField.NoteCarrier(unit);
            }
        }

        internal static void PrepareDetonate(Missile missile)
        {
            if (!IsMissile(missile))
                return;
            if (BlastYieldField != null)
            {
                try { BlastYieldField.SetValue(missile, NukeYield5000kt); }
                catch { }
            }
            _pendingFxFrames = 12;
        }

        internal static void ScaleSpawnedNukeFx(Vector3 pos)
        {
            if (_pendingFxFrames <= 0)
                return;
            _pendingFxFrames--;
            try
            {
                MushroomCloud[] clouds = UnityEngine.Object.FindObjectsOfType<MushroomCloud>();
                if (clouds != null)
                {
                    for (int i = 0; i < clouds.Length; i++)
                    {
                        MushroomCloud mc = clouds[i];
                        if (mc == null || mc.transform == null)
                            continue;
                        if ((mc.transform.position - pos).sqrMagnitude > 40000f)
                            continue;
                        mc.yield = VisualYieldKt;
                    }
                }
            }
            catch { }
            if (ShockYieldField == null)
                return;
            try
            {
                Shockwave[] waves = UnityEngine.Object.FindObjectsOfType<Shockwave>();
                if (waves == null)
                    return;
                for (int i = 0; i < waves.Length; i++)
                {
                    Shockwave sw = waves[i];
                    if (sw == null || sw.transform == null)
                        continue;
                    if ((sw.transform.position - pos).sqrMagnitude > 40000f)
                        continue;
                    ShockYieldField.SetValue(sw, VisualYieldKt);
                    MarkPurpleShock(sw);
                }
            }
            catch { }
        }

        internal static void TryTintShockwave(Shockwave sw)
        {
            if (sw == null)
                return;
            int id = 0;
            try { id = sw.GetInstanceID(); }
            catch { return; }
            if (id == 0)
                return;
            if (!PurpleShockIds.Contains(id))
            {
                float y = 0f;
                try
                {
                    if (ShockYieldField != null)
                        y = (float)ShockYieldField.GetValue(sw);
                }
                catch { y = 0f; }
                if (y < 2500f)
                    return;
                PurpleShockIds.Add(id);
            }
            ApplyShockTint(sw);
        }

        private static void MarkPurpleShock(Shockwave sw)
        {
            if (sw == null)
                return;
            try { PurpleShockIds.Add(sw.GetInstanceID()); }
            catch { }
            ApplyShockTint(sw);
            if (PurpleShockIds.Count > 48)
                PurpleShockIds.Clear();
            if (TintedRendererIds.Count > 96)
                TintedRendererIds.Clear();
        }

        private static void ApplyShockTint(Shockwave sw)
        {
            if (sw == null)
                return;
            GameObject vapor = null;
            try
            {
                if (ShockVaporGoField != null)
                    vapor = ShockVaporGoField.GetValue(sw) as GameObject;
            }
            catch { vapor = null; }
            if (vapor != null)
            {
                Renderer vr = null;
                try { vr = vapor.GetComponent<Renderer>(); }
                catch { vr = null; }
                PaintShockRenderer(vr);
            }
            Material vaporMat = null;
            try
            {
                if (ShockVaporMatField != null)
                    vaporMat = ShockVaporMatField.GetValue(sw) as Material;
            }
            catch { vaporMat = null; }
            if (vaporMat != null)
            {
                if (vaporMat.name == null || vaporMat.name.IndexOf("GFactor", StringComparison.Ordinal) < 0)
                {
                    Material inst = null;
                    try { inst = UnityEngine.Object.Instantiate(vaporMat); }
                    catch { inst = null; }
                    if (inst != null)
                    {
                        inst.name = "GFactorShock";
                        vaporMat = inst;
                        try { ShockVaporMatField.SetValue(sw, inst); }
                        catch { }
                        if (vapor != null)
                        {
                            Renderer vr2 = null;
                            try { vr2 = vapor.GetComponent<Renderer>(); }
                            catch { vr2 = null; }
                            if (vr2 != null)
                            {
                                try { vr2.sharedMaterial = inst; }
                                catch { }
                            }
                        }
                    }
                }
                PaintShockMat(vaporMat);
            }
            Renderer[] rs = null;
            try { rs = sw.GetComponentsInChildren<Renderer>(true); }
            catch { rs = null; }
            if (rs != null)
            {
                for (int i = 0; i < rs.Length; i++)
                {
                    Renderer r = rs[i];
                    if (r == null || r.gameObject == null)
                        continue;
                    string nm = r.gameObject.name;
                    if (string.IsNullOrEmpty(nm))
                        continue;
                    if (nm.IndexOf("shockwave", StringComparison.OrdinalIgnoreCase) < 0
                        && nm.IndexOf("Shockwave", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    PaintShockRenderer(r);
                }
            }
        }

        private static void PaintShockRenderer(Renderer r)
        {
            if (r == null)
                return;
            int id = 0;
            try { id = r.GetInstanceID(); }
            catch { return; }
            if (!TintedRendererIds.Contains(id))
            {
                Material[] mats = null;
                try { mats = r.materials; }
                catch { mats = null; }
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                        PaintShockMat(mats[i]);
                    try { r.materials = mats; }
                    catch { }
                }
                TintedRendererIds.Add(id);
            }
            try
            {
                r.GetPropertyBlock(ShockMpb);
                ShockMpb.SetColor("_EmissionColor", ShockEmit);
                ShockMpb.SetColor("_Color", ShockAlbedo);
                ShockMpb.SetColor("_BaseColor", ShockAlbedo);
                r.SetPropertyBlock(ShockMpb);
            }
            catch { }
        }

        private static void PaintShockMat(Material mat)
        {
            if (mat == null)
                return;
            try { mat.SetColor("_EmissionColor", ShockEmit); }
            catch { }
            try { mat.SetColor("_Color", ShockAlbedo); }
            catch { }
            try { mat.SetColor("_BaseColor", ShockAlbedo); }
            catch { }
        }

        private static bool CreateClone(WeaponMount src)
        {
            WeaponMount clone = UnityEngine.Object.Instantiate(src);
            clone.name = "WeaponMount_AmmoDump50T";
            clone.jsonKey = JsonKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            clone.ammo = 1;
            clone.Cargo = false;
            clone.Troops = false;
            clone.emptyMass = 0f;
            clone.emptyCost = 0f;
            clone.mass = MassKg;
            clone.drag = 0.55f;
            clone.RCS = 0.4f;

            WeaponInfo info = UnityEngine.Object.Instantiate(src.info);
            info.name = "WeaponInfo_AmmoDump50T";
            info.hideFlags = HideFlags.DontUnloadUnusedAsset;
            info.weaponName = DisplayName;
            info.shortName = ShortName;
            info.description = CurrentDescription();
            info.massPerRound = MassKg;
            info.costPerRound = CostPerRound;
            try { info.SetCostPerRound(CostPerRound); }
            catch { }
            try { info.SetMassPerRound(MassKg); }
            catch { }
            info.nuclear = false;
            info.strategic = false;
            info.bomb = true;
            info.missile = false;
            info.glideBomb = false;
            info.cargo = false;
            info.rearmGround = false;
            info.rearmShip = false;
            info.blastDamage = NukeYield5000kt;
            info.airburstHeight = 150f;
            TryCopyAmmoDumpIcon(info);

            clone.info = info;
            clone.mountName = DisplayName;
            clone.prefab = src.prefab;
            if (src.info != null && src.info.weaponPrefab != null)
                info.weaponPrefab = src.info.weaponPrefab;
            GameObject ufo = FindUfoBombPrefab();
            if (ufo != null)
            {
                info.weaponPrefab = ufo;
                _visualDonor = ufo;
                _visualIsUfo = true;
            }

            _clone = clone;
            _info = info;
            RegisterWithEncyclopedia(Plugin.GetEncyclopedia());
            EnsureMountsInCache();
            return true;
        }

        private static void RestoreIdentity()
        {
            if (_info == null)
                return;
            _info.weaponName = DisplayName;
            _info.shortName = ShortName;
            _info.description = CurrentDescription();
            _info.nuclear = false;
            _info.strategic = false;
            _info.bomb = true;
            _info.cargo = false;
            _info.massPerRound = MassKg;
            _info.costPerRound = CostPerRound;
            _info.blastDamage = NukeYield5000kt;
            try { _info.SetCostPerRound(CostPerRound); }
            catch { }
            try { _info.SetMassPerRound(MassKg); }
            catch { }
            if (_clone != null)
            {
                _clone.info = _info;
                _clone.jsonKey = JsonKey;
                _clone.mountName = DisplayName;
                _clone.mass = MassKg;
                _clone.emptyMass = 0f;
                _clone.emptyCost = 0f;
            }
            TryCopyAmmoDumpIcon(_info);
            if (_info != null && _visualIsUfo && _visualDonor != null)
                _info.weaponPrefab = _visualDonor;
        }

        private static string CurrentDescription()
        {
#if ORITASY_COMBINED
            try
            {
                if (Oritasy.UiLang.IsChinese)
                    return DescriptionZh;
            }
            catch { }
#endif
            return DescriptionEn;
        }

        private static void RegisterWithEncyclopedia(Encyclopedia enc)
        {
            if (enc == null || _clone == null || !Plugin.IsEncyclopediaPopulated(enc))
                return;
            if (enc.weaponMounts != null && !enc.weaponMounts.Contains(_clone))
                enc.weaponMounts.Add(_clone);
            if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(JsonKey))
                Encyclopedia.WeaponLookup[JsonKey] = _clone;
            try
            {
                if (enc.IndexLookup != null && !enc.IndexLookup.Contains(_clone))
                {
                    enc.IndexLookup.Add(_clone);
                    INetworkDefinition nd = _clone;
                    nd.LookupIndex = enc.IndexLookup.Count - 1;
                }
            }
            catch { }
        }

        private static WeaponMount LookupEncyclopediaMount(string key)
        {
            if (string.IsNullOrEmpty(key) || Encyclopedia.WeaponLookup == null)
                return null;
            WeaponMount m = null;
            try
            {
                if (!Encyclopedia.WeaponLookup.TryGetValue(key, out m))
                    return null;
            }
            catch { return null; }
            if (m == null || m.prefab == null || IsKey(m.jsonKey))
                return null;
            return m;
        }

        private static WeaponMount FindBombDonor()
        {
            WeaponMount enc = LookupEncyclopediaMount("nuclearBomb1_external");
            if (enc != null)
                return enc;

            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all == null)
                return null;
            WeaponMount gpo = null;
            WeaponMount nuclearBomb = null;
            WeaponMount anyBomb = null;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (m == null || m.info == null || m.prefab == null)
                    continue;
                if (IsKey(m.jsonKey))
                    continue;
                string key = m.jsonKey != null ? m.jsonKey : string.Empty;
                if (string.Equals(key, "nuclearBomb1_external", StringComparison.OrdinalIgnoreCase))
                    return m;
                if (gpo == null && key.IndexOf("nuclearBomb1", StringComparison.OrdinalIgnoreCase) >= 0)
                    gpo = m;
                if (nuclearBomb == null && m.info.bomb && m.info.nuclear && m.ammo <= 1)
                    nuclearBomb = m;
                if (anyBomb == null && m.ammo <= 1 && (m.info.bomb || m.info.glideBomb))
                    anyBomb = m;
            }
            if (gpo != null)
                return gpo;
            if (nuclearBomb != null)
                return nuclearBomb;
            return anyBomb;
        }

        private static void BindUfoPrefabIfReady()
        {
            if (_visualIsUfo && _visualDonor != null)
                return;
            if (Time.unscaledTime < _nextVisualRetry)
                return;
            _nextVisualRetry = Time.unscaledTime + 4f;
            GameObject ufo = FindUfoBombPrefab();
            if (ufo == null)
                return;
            _visualDonor = ufo;
            _visualIsUfo = true;
            if (_info != null)
                _info.weaponPrefab = ufo;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("G Factor Container: bound UFO bomb model");
        }

        private static GameObject FindVisualDonor()
        {
            if (_visualDonor != null)
                return _visualDonor;
            GameObject ufo = FindUfoBombPrefab();
            if (ufo != null)
            {
                _visualDonor = ufo;
                _visualIsUfo = true;
                return _visualDonor;
            }
            if (_visualTried)
                return null;
            _visualTried = true;
            try
            {
                UnitDefinition[] defs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                if (defs != null)
                {
                    for (int i = 0; i < defs.Length; i++)
                    {
                        UnitDefinition d = defs[i];
                        if (d == null || d.unitPrefab == null || string.IsNullOrEmpty(d.jsonKey))
                            continue;
                        if (string.Equals(d.jsonKey, "ammoDump", StringComparison.OrdinalIgnoreCase))
                        {
                            _visualDonor = d.unitPrefab;
                            return _visualDonor;
                        }
                    }
                    for (int i = 0; i < defs.Length; i++)
                    {
                        UnitDefinition d = defs[i];
                        if (d == null || d.unitPrefab == null || string.IsNullOrEmpty(d.jsonKey))
                            continue;
                        if (string.Equals(d.jsonKey, "MunitionsPallet1", StringComparison.OrdinalIgnoreCase))
                        {
                            _visualDonor = d.unitPrefab;
                            return _visualDonor;
                        }
                    }
                }
            }
            catch { }
            try
            {
                GameObject[] gos = Resources.FindObjectsOfTypeAll<GameObject>();
                if (gos != null)
                {
                    for (int i = 0; i < gos.Length; i++)
                    {
                        GameObject go = gos[i];
                        if (go == null || go.name == null)
                            continue;
                        if (string.Equals(go.name, "ammoDump", StringComparison.OrdinalIgnoreCase))
                        {
                            _visualDonor = go;
                            return _visualDonor;
                        }
                    }
                }
            }
            catch { }
            TryVisualFromPalletMount();
            return _visualDonor;
        }

        private static GameObject FindUfoBombPrefab()
        {
            try
            {
                UnitDefinition[] defs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                if (defs != null)
                {
                    for (int i = 0; i < defs.Length; i++)
                    {
                        UnitDefinition d = defs[i];
                        if (d == null || d.unitPrefab == null || string.IsNullOrEmpty(d.jsonKey))
                            continue;
                        if (!string.Equals(d.jsonKey, "bomb_ufo", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (LooksLikeUfoBombPrefab(d.unitPrefab))
                            return d.unitPrefab;
                    }
                }
            }
            catch { }
            try
            {
                WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (infos != null)
                {
                    for (int i = 0; i < infos.Length; i++)
                    {
                        WeaponInfo w = infos[i];
                        if (w == null || w.weaponPrefab == null)
                            continue;
                        string n = ((w.name != null ? w.name : string.Empty) + " "
                            + (w.weaponName != null ? w.weaponName : string.Empty));
                        if (n.IndexOf("bomb_ufo", StringComparison.OrdinalIgnoreCase) < 0
                            && n.IndexOf("info_bomb_ufo", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (LooksLikeUfoBombPrefab(w.weaponPrefab))
                            return w.weaponPrefab;
                    }
                }
            }
            catch { }
            try
            {
                GameObject[] gos = Resources.FindObjectsOfTypeAll<GameObject>();
                if (gos != null)
                {
                    for (int i = 0; i < gos.Length; i++)
                    {
                        GameObject go = gos[i];
                        if (!LooksLikeUfoBombPrefab(go))
                            continue;
                        return go;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool LooksLikeUfoBombPrefab(GameObject go)
        {
            if (go == null || go.name == null)
                return false;
            string n = go.name;
            if (n.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (n.IndexOf("x3", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            int cut = n.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (cut > 0)
                n = n.Substring(0, cut).Trim();
            return string.Equals(n, "bomb_ufo", StringComparison.OrdinalIgnoreCase);
        }

        private static void TryVisualFromPalletMount()
        {
            try
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null)
                    return;
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount m = all[i];
                    if (m == null || m.prefab == null)
                        continue;
                    string key = m.jsonKey != null ? m.jsonKey : string.Empty;
                    if (key.IndexOf("MunitionsPallet", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    MountedCargo cargoW = m.prefab.GetComponentInChildren<MountedCargo>(true);
                    if (cargoW != null && cargoW.cargo != null && cargoW.cargo.unitPrefab != null)
                    {
                        _visualDonor = cargoW.cargo.unitPrefab;
                        return;
                    }
                    _visualDonor = m.prefab;
                    return;
                }
            }
            catch { }
        }

        private static void TryCopyAmmoDumpIcon(WeaponInfo dest)
        {
            if (dest == null)
                return;
            Sprite icon = FindVanillaAmmoDumpIcon();
            if (icon != null)
                dest.weaponIcon = icon;
        }

        private static Sprite FindVanillaAmmoDumpIcon()
        {
            try
            {
                UnitDefinition[] defs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                if (defs != null)
                {
                    for (int i = 0; i < defs.Length; i++)
                    {
                        UnitDefinition d = defs[i];
                        if (d == null || string.IsNullOrEmpty(d.jsonKey))
                            continue;
                        if (!string.Equals(d.jsonKey, "ammoDump", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (d.friendlyIcon != null)
                            return d.friendlyIcon;
                    }
                }
            }
            catch { }
            try
            {
                WeaponMount[] mounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (mounts != null)
                {
                    for (int i = 0; i < mounts.Length; i++)
                    {
                        WeaponMount m = mounts[i];
                        if (m == null || IsKey(m.jsonKey) || m.info == null || m.info.weaponIcon == null)
                            continue;
                        string key = m.jsonKey != null ? m.jsonKey : string.Empty;
                        if (string.Equals(key, "ammoDump", StringComparison.OrdinalIgnoreCase))
                            return m.info.weaponIcon;
                    }
                }
            }
            catch { }
            try
            {
                WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (infos != null)
                {
                    for (int i = 0; i < infos.Length; i++)
                    {
                        WeaponInfo w = infos[i];
                        if (w == null || w.weaponIcon == null)
                            continue;
                        if (_info != null && object.ReferenceEquals(w, _info))
                            continue;
                        if (w.name != null
                            && w.name.IndexOf("AmmoDump50T", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        string n = ((w.weaponName != null ? w.weaponName : string.Empty) + " "
                            + (w.shortName != null ? w.shortName : string.Empty) + " "
                            + (w.name != null ? w.name : string.Empty));
                        if (n.IndexOf("[50T]", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        if (n.IndexOf("G Factor", StringComparison.OrdinalIgnoreCase) >= 0)
                            continue;
                        if (string.Equals(w.weaponName, "Ammo Dump", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(w.shortName, "Ammo Dump", StringComparison.OrdinalIgnoreCase))
                            return w.weaponIcon;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void TryCopyPalletIcon(WeaponInfo dest)
        {
            if (dest == null)
                return;
            try
            {
                WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (infos == null)
                    return;
                for (int i = 0; i < infos.Length; i++)
                {
                    WeaponInfo w = infos[i];
                    if (w == null || w.weaponIcon == null)
                        continue;
                    string n = ((w.weaponName != null ? w.weaponName : string.Empty) + " "
                        + (w.name != null ? w.name : string.Empty));
                    if (n.IndexOf("Munitions Pallet", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("MunitionsPallet", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Ammo Dump", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        dest.weaponIcon = w.weaponIcon;
                        return;
                    }
                }
            }
            catch { }
        }

        private static void ApplyVisual(GameObject root, bool hangar)
        {
            if (root == null)
                return;
            try
            {
                if (!root.scene.IsValid())
                    return;
            }
            catch { return; }
            if (_visualIsUfo && LooksLikeUfoBombPrefab(root))
                return;
            try
            {
                if (root.transform.Find(MarkerName) != null)
                    return;
            }
            catch { return; }
            GameObject donor = FindVisualDonor();
            if (donor == null)
                return;
            if (_visualIsUfo && LooksLikeUfoBombPrefab(root))
                return;
            MeshFilter[] src = null;
            try { src = donor.GetComponentsInChildren<MeshFilter>(true); }
            catch { return; }
            if (src == null || src.Length == 0)
                return;

            float scale = _visualIsUfo ? VisualScaleUfo : VisualScaleDump;
            GameObject marker = new GameObject(MarkerName);
            marker.transform.SetParent(root.transform, false);

            GameObject vis = new GameObject(VisualChildName);
            vis.transform.SetParent(root.transform, false);
            vis.transform.localRotation = Quaternion.identity;
            vis.transform.localScale = new Vector3(scale, scale, scale);

            Transform donorRoot = donor.transform;
            Vector3 acc = Vector3.zero;
            int copied = 0;
            for (int i = 0; i < src.Length; i++)
            {
                MeshFilter mf = src[i];
                if (mf == null || mf.sharedMesh == null || mf.transform == null)
                    continue;
                string mn = mf.sharedMesh.name != null ? mf.sharedMesh.name : string.Empty;
                if (mn.IndexOf("col", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                GameObject part = new GameObject(mf.gameObject.name);
                part.transform.SetParent(vis.transform, false);
                part.transform.localPosition = donorRoot.InverseTransformPoint(mf.transform.position);
                part.transform.localRotation = Quaternion.Inverse(donorRoot.rotation) * mf.transform.rotation;
                part.transform.localScale = mf.transform.localScale;
                MeshFilter nf = part.AddComponent<MeshFilter>();
                nf.sharedMesh = mf.sharedMesh;
                MeshRenderer nr = part.AddComponent<MeshRenderer>();
                MeshRenderer sr = mf.GetComponent<MeshRenderer>();
                if (sr != null && sr.sharedMaterials != null && sr.sharedMaterials.Length > 0)
                    nr.sharedMaterials = sr.sharedMaterials;
                nr.shadowCastingMode = ShadowCastingMode.On;
                nr.receiveShadows = true;
                acc += part.transform.localPosition;
                copied++;
            }
            if (copied <= 0)
                return;
            Vector3 mid = acc / copied;
            vis.transform.localPosition = hangar
                ? new Vector3(-mid.x * scale, 0.05f - mid.y * scale, -mid.z * scale)
                : new Vector3(-mid.x * scale, -mid.y * scale, -mid.z * scale);

            MeshRenderer[] hide = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < hide.Length; i++)
            {
                MeshRenderer mr = hide[i];
                if (mr == null || mr.transform == null)
                    continue;
                if (mr.transform == vis.transform || mr.transform.IsChildOf(vis.transform))
                    continue;
                string nn = mr.transform.name != null ? mr.transform.name : string.Empty;
                if (hangar && nn.IndexOf("pylon", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                mr.enabled = false;
            }
        }

        private static bool IsJ35Draken(WeaponManager wm)
        {
            if (wm == null)
                return false;
            Aircraft ac = null;
            try { ac = wm.GetComponentInParent<Aircraft>(); }
            catch { ac = null; }
            if (ac == null)
            {
                try { ac = wm.GetComponent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null || ac.definition == null)
                return false;
            string key = ac.definition.jsonKey;
            return string.Equals(key, "J35_Draken", StringComparison.Ordinal)
                || string.Equals(key, "J35XE_Wyvern", StringComparison.Ordinal);
        }

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
                if (wm.GetComponentInParent<Aircraft>() != null
                    || wm.GetComponent<Aircraft>() != null)
                {
                    AirWmIds.Add(id);
                    return false;
                }
            }
            catch { }
            ShipWmIds.Add(id);
            return true;
        }

        internal static bool HardpointCanTakeStore(HardpointSet hs)
        {
            if (hs == null || Plugin.IsNavalHardpoint(hs))
                return false;
            if (hs.weaponOptions == null || hs.weaponOptions.Count == 0)
                return true;
            bool sawUtility = false;
            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null)
                    continue;
                if (m.GunAmmo || m.countermeasure || m.radar || m.tailHook || m.slingloadHook)
                {
                    sawUtility = true;
                    continue;
                }
                return true;
            }
            return !sawUtility;
        }
    }

    public class AmmoDump50TMark : MonoBehaviour
    {
        public float HitPoints = 1f;
    }

    public class AmmoDump50THull : MonoBehaviour, IDamageable
    {
        public float HitPoints = 1f;
        public bool Hung = true;
        private Unit _unit;
        private bool _dead;

        public void Init(Unit unit)
        {
            _unit = unit;
            HitPoints = AmmoDump50TWeapon.HullHitPoints;
            _dead = false;
            Hung = true;
        }

        public void TakeDamage(float pierceDamage, float blastDamage, float amountAffected,
            float fireDamage, float ImpactDamage, PersistentID dealerID)
        {
            if (GFactorRutherfordField.IsShielded(_unit))
                return;
            ApplyDamage(pierceDamage, blastDamage, fireDamage, ImpactDamage);
        }

        public void ApplyDamage(float pierceDamage, float blastDamage, float fireDamage, float impactDamage)
        {
            if (_dead)
                return;
            if (GFactorRutherfordField.IsShielded(_unit))
                return;
            float dmg = pierceDamage + blastDamage + fireDamage + impactDamage;
            if (dmg <= 0f)
                return;
            HitPoints -= dmg;
            if (HitPoints > 0f)
                return;
            Die();
        }

        public void TakeShockwave(Vector3 origin, float overpressure, float blastPower)
        {
            if (_dead)
                return;
            if (GFactorRutherfordField.IsShielded(_unit))
                return;
            if (overpressure <= 0f && blastPower <= 0f)
                return;
            Die();
        }

        public void Detach(Vector3 velocity, Vector3 relativePos)
        {
            Die();
        }

        public ArmorProperties GetArmorProperties()
        {
            return AmmoDump50TWeapon.GlassArmorForHull();
        }

        public Unit GetUnit()
        {
            return _unit;
        }

        public float GetMass()
        {
            return AmmoDump50TWeapon.MassKg;
        }

        public Transform GetTransform()
        {
            return transform;
        }

        private void OnDestroy()
        {
            GFactorRutherfordField.NoteHullGone();
        }

        private void Die()
        {
            if (_dead)
                return;
            _dead = true;
            HitPoints = 0f;
            Vector3 pos = transform.position;
            AmmoDump50TWeapon.DetonateAt(pos);
            try { gameObject.SetActive(false); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Missile), "TakeDamage")]
    internal static class Patch_Missile_TakeDamage_AmmoDump50T
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, float pierceDamage, float blastDamage,
            float fireDamage, float impactDamage)
        {
            if (!AmmoDump50TWeapon.AbsorbHit(__instance, pierceDamage, blastDamage, fireDamage, impactDamage))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "ApplyDamage")]
    internal static class Patch_Missile_ApplyDamage_AmmoDump50T
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, float pierceDamage, float blastDamage,
            float fireDamage, float impactDamage)
        {
            if (!AmmoDump50TWeapon.AbsorbHit(__instance, pierceDamage, blastDamage, fireDamage, impactDamage))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "TakeShockwave")]
    internal static class Patch_Missile_TakeShockwave_AmmoDump50T
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, float overpressure, float blastPower)
        {
            if (!AmmoDump50TWeapon.IsMissile(__instance))
                return true;
            if (GFactorRutherfordField.IsInsideField(__instance))
                return false;
            if (overpressure <= 0f && blastPower <= 0f)
                return true;
            AmmoDump50TWeapon.KillNow(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "OnEnable")]
    internal static class Patch_Missile_OnEnable_AmmoDump50T
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            AmmoDump50TWeapon.ApplyToMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class Patch_Warhead_Detonate_AmmoDump50T
    {
        [HarmonyPostfix]
        private static void Postfix(Rigidbody rb, PersistentID ownerID, Vector3 position, Vector3 normal,
            bool armed, float blastYield, bool hitArmor, bool hitTerrain)
        {
            if (blastYield < AmmoDump50TWeapon.NukeYield5000kt * 0.5f)
                return;
            AmmoDump50TWeapon.ScaleSpawnedNukeFx(position);
            AmmoDump50TWeapon.NoteShatter(position, 0f);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Start")]
    internal static class Patch_Shockwave_GFactorTintStart
    {
        [HarmonyPostfix]
        private static void Postfix(Shockwave __instance)
        {
            AmmoDump50TWeapon.TryTintShockwave(__instance);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Update")]
    internal static class Patch_Shockwave_GFactorTintUpdate
    {
        [HarmonyPostfix]
        private static void Postfix(Shockwave __instance)
        {
            AmmoDump50TWeapon.TryTintShockwave(__instance);
        }
    }
}
