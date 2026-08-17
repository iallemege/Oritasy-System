using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// 155mm guided artillery can engage large aircraft (bombers / VL-49) at low
    /// priority so ground fire still wins. Fighters, CAS, and helos are refused.
    /// </summary>
    internal static class ArtilleryAaService
    {
        private const float WeaponAntiAir = 0.12f;
        private const float UnitAntiAir = 0.10f;
        private const float ShellAntiAir = 0.20f;
        internal const float AirOpportunityMul = 0.18f;
        private const float TargetMaxSpeed = 450f;
        private const float SeekerMaxSpeed = 450f;
        private const float MinMaxElevation = 75f;
        private const float LargeFootprint = 500f;
        private const float LargeEmptyWeight = 30000f;
        private const float LargeMaxWeight = 50000f;
        private const float LargeMass = 25000f;

        private static readonly FieldInfo TurretAttached =
            AccessTools.Field(typeof(Turret), "attachedUnit");
        private static readonly FieldInfo TurretStations =
            AccessTools.Field(typeof(Turret), "weaponStations");
        private static readonly FieldInfo TurretAimSolver =
            AccessTools.Field(typeof(Turret), "aimSolver");
        private static readonly FieldInfo TurretMaxElev =
            AccessTools.Field(typeof(Turret), "maxElevation");
        private static readonly FieldInfo TurretTraverseRate =
            AccessTools.Field(typeof(Turret), "traverseRate");
        private static readonly FieldInfo TurretElevationRate =
            AccessTools.Field(typeof(Turret), "elevationRate");
        private static readonly FieldInfo TurretAssessInterval =
            AccessTools.Field(typeof(Turret), "targetAssessmentInterval");
        private static readonly FieldInfo AimCorrectShots =
            AccessTools.Field(typeof(AimSolver), "correctShots");
        private static readonly FieldInfo GunInfo =
            AccessTools.Field(typeof(Gun), "info");
        private static readonly FieldInfo GunGuided =
            AccessTools.Field(typeof(Gun), "guidedProjectile");
        private static readonly FieldInfo GunProximity =
            AccessTools.Field(typeof(Gun), "proximityTimer");
        private static readonly FieldInfo InertialMaxSpd =
            AccessTools.Field(typeof(InertialSeekerShell), "maxTargetSpeed");
        private static readonly FieldInfo OpticalMaxSpd =
            AccessTools.Field(typeof(OpticalSeekerShell), "maxTargetSpeed");
        private static readonly FieldInfo SeekerMissile =
            AccessTools.Field(typeof(MissileSeeker), "missile");

        private static readonly HashSet<int> BuffedTurrets = new HashSet<int>();
        private static bool _applied;

        internal static bool IsOn()
        {
            return Plugin.Artillery155AaEnabled == null || Plugin.Artillery155AaEnabled.Value;
        }

        internal static void Apply()
        {
            if (!IsOn())
                return;
            PatchWeaponInfos();
            PatchUnitDefs();
            PatchSeekerPrefabs();
            _applied = true;
        }

        internal static void TryBuffHost(Component host)
        {
            if (host == null || !Plugin.IsRuntimeInstance(host))
                return;
            if (!_applied)
                Apply();
            Turret[] turrets = host.GetComponentsInChildren<Turret>(true);
            for (int i = 0; i < turrets.Length; i++)
                TryBuffTurret(turrets[i]);
        }

        internal static void TryBuffTurret(Turret turret)
        {
            if (turret == null || !IsOn())
                return;
            if (!BuffedTurrets.Add(turret.GetInstanceID()))
                return;
            if (!TurretHas155(turret))
                return;

            try
            {
                if (TurretMaxElev != null)
                {
                    float e = (float)TurretMaxElev.GetValue(turret);
                    if (e < MinMaxElevation)
                        TurretMaxElev.SetValue(turret, MinMaxElevation);
                }
                if (TurretTraverseRate != null)
                {
                    float v = (float)TurretTraverseRate.GetValue(turret);
                    TurretTraverseRate.SetValue(turret, v * 1.35f);
                }
                if (TurretElevationRate != null)
                {
                    float v = (float)TurretElevationRate.GetValue(turret);
                    TurretElevationRate.SetValue(turret, v * 1.35f);
                }
                if (TurretAssessInterval != null)
                {
                    float v = (float)TurretAssessInterval.GetValue(turret);
                    if (v > 2f)
                        TurretAssessInterval.SetValue(turret, 2f);
                }
                AimSolver solver = TurretAimSolver != null
                    ? TurretAimSolver.GetValue(turret) as AimSolver
                    : null;
                if (solver != null && AimCorrectShots != null)
                    AimCorrectShots.SetValue(solver, true);

                WeaponStation[] stations = TurretStations != null
                    ? TurretStations.GetValue(turret) as WeaponStation[]
                    : null;
                if (stations != null)
                {
                    for (int i = 0; i < stations.Length; i++)
                    {
                        WeaponStation st = stations[i];
                        if (st == null)
                            continue;
                        if (st.TypeLookup != null)
                            st.TypeLookup.Clear();
                        if (st.Weapons == null)
                            continue;
                        for (int w = 0; w < st.Weapons.Count; w++)
                        {
                            Gun g = st.Weapons[w] as Gun;
                            if (g != null)
                                PatchGun(g);
                        }
                    }
                }

                Gun[] guns = turret.GetComponentsInChildren<Gun>(true);
                for (int i = 0; i < guns.Length; i++)
                    PatchGun(guns[i]);
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("155 AA turret: " + ex.Message);
            }
        }

        internal static bool Is155GuidedWeapon(WeaponInfo info)
        {
            if (info == null)
                return false;
            string blob = InfoBlob(info);
            if (blob.IndexOf("RAILGUN", StringComparison.Ordinal) >= 0)
                return false;
            if (blob.IndexOf("GUN155MM_GUIDED", StringComparison.Ordinal) >= 0)
                return true;
            if (blob.IndexOf("155MM CANNON", StringComparison.Ordinal) >= 0)
                return true;
            if (blob.IndexOf("155MM", StringComparison.Ordinal) >= 0
                && blob.IndexOf("GUIDED", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        internal static bool Is155Shell(Unit unit)
        {
            if (unit == null)
                return false;
            UnitDefinition def = null;
            try { def = unit.definition; }
            catch { }
            return Is155ShellDef(def);
        }

        internal static bool Is155ShellDef(UnitDefinition def)
        {
            if (def == null)
                return false;
            string blob = DefBlob(def);
            if (blob.IndexOf("SHELL_155MM_GUIDED", StringComparison.Ordinal) >= 0)
                return true;
            if (blob.IndexOf("155MM GUIDED SHELL", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static void PatchWeaponInfos()
        {
            WeaponInfo[] all = null;
            try { all = Resources.FindObjectsOfTypeAll<WeaponInfo>(); }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponInfo w = all[i];
                if (!Is155GuidedWeapon(w))
                    continue;
                RoleIdentity eff = w.effectiveness;
                eff.antiAir = WeaponAntiAir;
                w.effectiveness = eff;
                TargetRequirements tr = w.targetRequirements;
                if (tr.maxSpeed < TargetMaxSpeed)
                    tr.maxSpeed = TargetMaxSpeed;
                w.targetRequirements = tr;
            }
        }

        private static void PatchUnitDefs()
        {
            UnitDefinition[] all = null;
            try { all = Resources.FindObjectsOfTypeAll<UnitDefinition>(); }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                UnitDefinition def = all[i];
                if (def == null)
                    continue;
                string blob = DefBlob(def);
                if (Is155ShellDef(def))
                {
                    RoleIdentity role = def.roleIdentity;
                    role.antiAir = ShellAntiAir;
                    def.roleIdentity = role;
                    continue;
                }
                if (blob.IndexOf("HLT-MART", StringComparison.Ordinal) >= 0
                    || blob.IndexOf("HLT MOBILE ARTILLERY", StringComparison.Ordinal) >= 0)
                {
                    RoleIdentity role = def.roleIdentity;
                    role.antiAir = UnitAntiAir;
                    def.roleIdentity = role;
                }
            }
        }

        private static void PatchSeekerPrefabs()
        {
            InertialSeekerShell[] inertial = null;
            try { inertial = Resources.FindObjectsOfTypeAll<InertialSeekerShell>(); }
            catch { }
            if (inertial != null)
            {
                for (int i = 0; i < inertial.Length; i++)
                    PatchInertial(inertial[i], false);
            }
            OpticalSeekerShell[] optical = null;
            try { optical = Resources.FindObjectsOfTypeAll<OpticalSeekerShell>(); }
            catch { }
            if (optical != null)
            {
                for (int i = 0; i < optical.Length; i++)
                    PatchOptical(optical[i], false);
            }
        }

        internal static void PatchInertial(InertialSeekerShell seeker, bool require155)
        {
            if (seeker == null)
                return;
            if (require155 && !SeekerIs155(seeker))
                return;
            if (!require155 && !SeekerIs155(seeker) && !PrefabLooks155(seeker))
                return;
            if (InertialMaxSpd != null)
            {
                float v = (float)InertialMaxSpd.GetValue(seeker);
                if (v < SeekerMaxSpeed)
                    InertialMaxSpd.SetValue(seeker, SeekerMaxSpeed);
            }
            seeker.proximityFuse = true;
        }

        internal static void PatchOptical(OpticalSeekerShell seeker, bool require155)
        {
            if (seeker == null)
                return;
            if (require155 && !SeekerIs155(seeker))
                return;
            if (!require155 && !SeekerIs155(seeker) && !PrefabLooks155(seeker))
                return;
            if (OpticalMaxSpd != null)
            {
                float v = (float)OpticalMaxSpd.GetValue(seeker);
                if (v < SeekerMaxSpeed)
                    OpticalMaxSpd.SetValue(seeker, SeekerMaxSpeed);
            }
            seeker.proximityFuse = true;
        }

        private static void PatchGun(Gun gun)
        {
            if (gun == null || !Is155Gun(gun))
                return;
            WeaponInfo info = GunInfo != null ? GunInfo.GetValue(gun) as WeaponInfo : null;
            if (info != null)
            {
                RoleIdentity eff = info.effectiveness;
                eff.antiAir = WeaponAntiAir;
                info.effectiveness = eff;
                TargetRequirements tr = info.targetRequirements;
                if (tr.maxSpeed < TargetMaxSpeed)
                    tr.maxSpeed = TargetMaxSpeed;
                info.targetRequirements = tr;
            }
            if (GunProximity != null)
                GunProximity.SetValue(gun, true);
        }

        internal static void EnableGunProximity(Gun gun)
        {
            if (gun == null || GunProximity == null)
                return;
            GunProximity.SetValue(gun, true);
        }

        internal static bool ShouldSkipAirTarget(Unit target)
        {
            return IsAirborneUnit(target) && !IsLargeAirTarget(target);
        }

        internal static bool IsAirborneUnit(Unit unit)
        {
            if (unit == null)
                return false;
            if (unit is Missile)
                return false;
            if (unit is Aircraft)
                return true;
            try
            {
                UnitDefinition def = unit.definition;
                if (def != null
                    && def.typeIdentity.air >= 0.5f
                    && def.typeIdentity.missile < 0.5f)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsLargeAirTarget(Unit unit)
        {
            if (!IsAirborneUnit(unit))
                return false;
            UnitDefinition def = null;
            try { def = unit.definition; }
            catch { }
            if (def != null)
            {
                if (def.length * def.width >= LargeFootprint)
                    return true;
                if (def.length >= 24f && def.width >= 20f)
                    return true;
                if (def.mass >= LargeMass)
                    return true;
                AircraftDefinition ad = def as AircraftDefinition;
                if (ad != null && ad.aircraftInfo != null)
                {
                    if (ad.aircraftInfo.emptyWeight >= LargeEmptyWeight)
                        return true;
                    if (ad.aircraftInfo.maxWeight >= LargeMaxWeight)
                        return true;
                }
                string blob = DefBlob(def);
                if (blob.IndexOf("DARKREACH", StringComparison.Ordinal) >= 0)
                    return true;
                if (blob.IndexOf("SFB", StringComparison.Ordinal) >= 0)
                    return true;
                if (blob.IndexOf("FASTBOMBER", StringComparison.Ordinal) >= 0)
                    return true;
                if (blob.IndexOf("TARANTULA", StringComparison.Ordinal) >= 0)
                    return true;
                if (blob.IndexOf("QUADVTO", StringComparison.Ordinal) >= 0)
                    return true;
                if (blob.IndexOf("VL-49", StringComparison.Ordinal) >= 0
                    || blob.IndexOf("VL49", StringComparison.Ordinal) >= 0)
                    return true;
            }
            Aircraft ac = unit as Aircraft;
            if (ac != null)
            {
                string key = AircraftIdentity.GetKey(ac);
                if (AircraftIdentity.IsSfb(key))
                    return true;
                if (AircraftIdentity.ContainsAny(key, "Tarantula", "VL-49", "VL49", "QuadVTOL", "FastBomber"))
                    return true;
            }
            return false;
        }

        internal static bool StationIs155(WeaponStation station)
        {
            if (station == null)
                return false;
            if (Is155GuidedWeapon(station.WeaponInfo))
                return true;
            if (station.Weapons == null)
                return false;
            for (int i = 0; i < station.Weapons.Count; i++)
            {
                Gun g = station.Weapons[i] as Gun;
                if (g != null && Is155Gun(g))
                    return true;
            }
            return false;
        }

        internal static bool TurretHas155(Turret turret)
        {
            if (turret == null)
                return false;
            Gun[] guns = turret.GetComponentsInChildren<Gun>(true);
            for (int i = 0; i < guns.Length; i++)
            {
                if (Is155Gun(guns[i]))
                    return true;
            }
            WeaponStation[] stations = TurretStations != null
                ? TurretStations.GetValue(turret) as WeaponStation[]
                : null;
            if (stations == null)
                return false;
            for (int i = 0; i < stations.Length; i++)
            {
                WeaponStation st = stations[i];
                if (st != null && Is155GuidedWeapon(st.WeaponInfo))
                    return true;
            }
            return false;
        }

        internal static bool Is155Gun(Gun gun)
        {
            if (gun == null)
                return false;
            WeaponInfo info = GunInfo != null ? GunInfo.GetValue(gun) as WeaponInfo : null;
            if (Is155GuidedWeapon(info))
                return true;
            MissileDefinition md = GunGuided != null
                ? GunGuided.GetValue(gun) as MissileDefinition
                : null;
            return Is155ShellDef(md);
        }

        private static bool SeekerIs155(MissileSeeker seeker)
        {
            if (seeker == null)
                return false;
            Missile m = null;
            if (SeekerMissile != null)
                m = SeekerMissile.GetValue(seeker) as Missile;
            if (m == null)
                m = seeker.GetComponent<Missile>();
            if (m == null)
                m = seeker.GetComponentInParent<Missile>();
            return Is155Shell(m);
        }

        private static bool PrefabLooks155(Component c)
        {
            if (c == null)
                return false;
            string n = c.name;
            if (!string.IsNullOrEmpty(n)
                && n.IndexOf("155", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            Transform t = c.transform;
            while (t != null)
            {
                string tn = t.name;
                if (!string.IsNullOrEmpty(tn)
                    && tn.IndexOf("155", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                t = t.parent;
            }
            return false;
        }

        private static string InfoBlob(WeaponInfo info)
        {
            string s = "";
            try { s = info.name + " " + info.weaponName + " " + info.shortName; }
            catch { }
            return s.ToUpperInvariant();
        }

        private static string DefBlob(UnitDefinition def)
        {
            string s = "";
            try
            {
                s = def.name + " " + def.jsonKey + " " + def.unitName + " " + def.code;
            }
            catch { }
            return s.ToUpperInvariant();
        }

        internal static Unit UnitFromTracking(TrackingInfo info)
        {
            if (info == null)
                return null;
            Unit u = null;
            try
            {
                if (info.TryGetUnit(out u) && u != null)
                    return u;
            }
            catch { }
            try { u = info.GetUnit(); }
            catch { }
            return u;
        }
    }

    [HarmonyPatch(typeof(Turret), "Awake")]
    internal static class Patch_Turret_Awake_ArtilleryAa
    {
        [HarmonyPostfix]
        private static void Postfix(Turret __instance)
        {
            ArtilleryAaService.TryBuffTurret(__instance);
        }
    }

    [HarmonyPatch(typeof(InertialSeekerShell), "Initialize")]
    internal static class Patch_InertialSeekerShell_Init_ArtilleryAa
    {
        [HarmonyPostfix]
        private static void Postfix(InertialSeekerShell __instance)
        {
            if (!ArtilleryAaService.IsOn())
                return;
            ArtilleryAaService.PatchInertial(__instance, true);
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerShell), "Initialize")]
    internal static class Patch_OpticalSeekerShell_Init_ArtilleryAa
    {
        [HarmonyPostfix]
        private static void Postfix(OpticalSeekerShell __instance)
        {
            if (!ArtilleryAaService.IsOn())
                return;
            ArtilleryAaService.PatchOptical(__instance, true);
        }
    }

    [HarmonyPatch(typeof(CombatAI), "AnalyzeTarget")]
    internal static class Patch_AnalyzeTarget_ArtilleryAa
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponStation weaponStation, TrackingInfo trackingInfo, ref OpportunityThreat __result)
        {
            if (!ArtilleryAaService.IsOn() || !ArtilleryAaService.StationIs155(weaponStation))
                return true;
            Unit u = ArtilleryAaService.UnitFromTracking(trackingInfo);
            if (!ArtilleryAaService.ShouldSkipAirTarget(u))
                return true;
            __result = new OpportunityThreat(0f, 0f);
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(WeaponStation weaponStation, TrackingInfo trackingInfo, ref OpportunityThreat __result)
        {
            if (!ArtilleryAaService.IsOn() || !ArtilleryAaService.StationIs155(weaponStation))
                return;
            Unit u = ArtilleryAaService.UnitFromTracking(trackingInfo);
            if (!ArtilleryAaService.IsLargeAirTarget(u))
                return;
            try
            {
                __result = new OpportunityThreat(
                    __result.opportunity * ArtilleryAaService.AirOpportunityMul,
                    __result.threat);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(CombatAI), "InterceptViability")]
    internal static class Patch_InterceptViability_ArtilleryAa
    {
        [HarmonyPostfix]
        private static void Postfix(Unit target, WeaponStation weaponStation, ref float __result)
        {
            if (!ArtilleryAaService.IsOn() || !ArtilleryAaService.StationIs155(weaponStation))
                return;
            if (ArtilleryAaService.ShouldSkipAirTarget(target))
                __result = 0f;
        }
    }

    [HarmonyPatch(typeof(Turret), "AssessTargetPriority")]
    internal static class Patch_Turret_Assess_ArtilleryAa
    {
        [HarmonyPrefix]
        private static bool Prefix(Turret __instance, Unit targetCandidate)
        {
            if (!ArtilleryAaService.IsOn() || __instance == null)
                return true;
            if (!ArtilleryAaService.TurretHas155(__instance))
                return true;
            if (ArtilleryAaService.ShouldSkipAirTarget(targetCandidate))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Turret), "ChooseTarget")]
    internal static class Patch_Turret_ChooseTarget_ArtilleryAa
    {
        [HarmonyPostfix]
        private static void Postfix(Turret __instance)
        {
            if (!ArtilleryAaService.IsOn() || __instance == null)
                return;
            if (!ArtilleryAaService.TurretHas155(__instance))
                return;
            Unit t = null;
            try { t = __instance.GetTarget(); }
            catch { }
            if (!ArtilleryAaService.ShouldSkipAirTarget(t))
                return;
            try { __instance.SetTargetFromController(null); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Turret), "SetTargetFromController")]
    internal static class Patch_Turret_SetTarget_ArtilleryAa
    {
        [HarmonyPrefix]
        private static bool Prefix(Turret __instance, Unit target)
        {
            if (target == null || !ArtilleryAaService.IsOn() || __instance == null)
                return true;
            if (!ArtilleryAaService.TurretHas155(__instance))
                return true;
            if (ArtilleryAaService.ShouldSkipAirTarget(target))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Gun), "SetTarget")]
    internal static class Patch_Gun_SetTarget_ArtilleryAa
    {
        [HarmonyPrefix]
        private static bool Prefix(Gun __instance, Unit target)
        {
            if (target == null || !ArtilleryAaService.IsOn() || __instance == null)
                return true;
            if (!ArtilleryAaService.Is155Gun(__instance))
                return true;
            if (ArtilleryAaService.ShouldSkipAirTarget(target))
                return false;
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(Gun __instance, Unit target)
        {
            if (!ArtilleryAaService.IsOn() || __instance == null)
                return;
            if (!ArtilleryAaService.Is155Gun(__instance))
                return;
            if (!ArtilleryAaService.IsLargeAirTarget(target))
                return;
            ArtilleryAaService.EnableGunProximity(__instance);
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class Patch_Gun_Fire_ArtilleryAa
    {
        [HarmonyPrefix]
        private static bool Prefix(Gun __instance, Unit target)
        {
            if (target == null || !ArtilleryAaService.IsOn() || __instance == null)
                return true;
            if (!ArtilleryAaService.Is155Gun(__instance))
                return true;
            if (ArtilleryAaService.ShouldSkipAirTarget(target))
                return false;
            return true;
        }
    }
}
