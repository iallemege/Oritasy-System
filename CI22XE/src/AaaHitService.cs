using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Buff AI AAA / SPAAG / CIWS hit quality: tighter spread, lead correction, less rake, snappier turrets.
    /// Skips aircraft guns and pure surface-fire artillery.
    /// </summary>
    internal static class AaaHitService
    {
        private static readonly FieldInfo TurretAttached =
            AccessTools.Field(typeof(Turret), "attachedUnit");
        private static readonly FieldInfo TurretAimSolver =
            AccessTools.Field(typeof(Turret), "aimSolver");
        private static readonly FieldInfo TurretStations =
            AccessTools.Field(typeof(Turret), "weaponStations");
        private static readonly FieldInfo TurretLockTime =
            AccessTools.Field(typeof(Turret), "lockTime");
        private static readonly FieldInfo TurretTraverseRate =
            AccessTools.Field(typeof(Turret), "traverseRate");
        private static readonly FieldInfo TurretElevationRate =
            AccessTools.Field(typeof(Turret), "elevationRate");
        private static readonly FieldInfo TurretAssessInterval =
            AccessTools.Field(typeof(Turret), "targetAssessmentInterval");

        private static readonly FieldInfo AimCorrectShots =
            AccessTools.Field(typeof(AimSolver), "correctShots");
        private static readonly FieldInfo AimArtillery =
            AccessTools.Field(typeof(AimSolver), "artillery");
        private static readonly FieldInfo AimRakeAmount =
            AccessTools.Field(typeof(AimSolver), "rakeAmount");
        private static readonly FieldInfo AimRakeFrequency =
            AccessTools.Field(typeof(AimSolver), "rakeFrequency");

        private static readonly FieldInfo GunSpread =
            AccessTools.Field(typeof(Gun), "bulletSpread");
        private static readonly FieldInfo GunProximity =
            AccessTools.Field(typeof(Gun), "proximityTimer");
        private static readonly FieldInfo GunInfo =
            AccessTools.Field(typeof(Gun), "info");

        private static readonly HashSet<int> BuffedTurrets = new HashSet<int>();

        internal static float SpreadMul
        {
            get
            {
                return Plugin.AaaSpreadMul != null
                    ? Mathf.Clamp(Plugin.AaaSpreadMul.Value, 0.05f, 1f)
                    : 0.35f;
            }
        }

        internal static float RakeMul
        {
            get
            {
                return Plugin.AaaRakeMul != null
                    ? Mathf.Clamp(Plugin.AaaRakeMul.Value, 0f, 1f)
                    : 0.2f;
            }
        }

        internal static float TrackMul
        {
            get
            {
                return Plugin.AaaTrackMul != null
                    ? Mathf.Max(1f, Plugin.AaaTrackMul.Value)
                    : 1.6f;
            }
        }

        internal static float LockMul
        {
            get
            {
                return Plugin.AaaLockMul != null
                    ? Mathf.Clamp(Plugin.AaaLockMul.Value, 0.1f, 1f)
                    : 0.4f;
            }
        }

        internal static void TryBuffTurret(Turret turret)
        {
            if (turret == null || !Plugin.IsRuntimeInstance(turret))
                return;
            if (Plugin.AaaHitEnabled != null && !Plugin.AaaHitEnabled.Value)
                return;
            if (!BuffedTurrets.Add(turret.GetInstanceID()))
                return;

            if (!IsAaaCandidate(turret))
                return;

            float track = TrackMul;
            float lockM = LockMul;
            float spreadM = SpreadMul;
            float rakeM = RakeMul;

            try
            {
                if (TurretTraverseRate != null)
                {
                    float v = (float)TurretTraverseRate.GetValue(turret);
                    TurretTraverseRate.SetValue(turret, v * track);
                }
                if (TurretElevationRate != null)
                {
                    float v = (float)TurretElevationRate.GetValue(turret);
                    TurretElevationRate.SetValue(turret, v * track);
                }
                if (TurretLockTime != null)
                {
                    float v = (float)TurretLockTime.GetValue(turret);
                    TurretLockTime.SetValue(turret, Mathf.Max(0.05f, v * lockM));
                }
                if (TurretAssessInterval != null)
                {
                    float v = (float)TurretAssessInterval.GetValue(turret);
                    TurretAssessInterval.SetValue(turret, Mathf.Max(0.05f, v * 0.55f));
                }

                AimSolver solver = TurretAimSolver != null
                    ? TurretAimSolver.GetValue(turret) as AimSolver
                    : null;
                if (solver != null)
                {
                    if (AimCorrectShots != null)
                        AimCorrectShots.SetValue(solver, true);
                    if (AimRakeAmount != null)
                    {
                        float rake = (float)AimRakeAmount.GetValue(solver);
                        AimRakeAmount.SetValue(solver, rake * rakeM);
                    }
                    if (AimRakeFrequency != null)
                    {
                        float freq = (float)AimRakeFrequency.GetValue(solver);
                        AimRakeFrequency.SetValue(solver, freq * Mathf.Lerp(1f, 0.5f, 1f - rakeM));
                    }
                }

                Gun[] guns = turret.GetComponentsInChildren<Gun>(true);
                for (int i = 0; i < guns.Length; i++)
                {
                    Gun g = guns[i];
                    if (g == null || GunSpread == null)
                        continue;
                    float spread = (float)GunSpread.GetValue(g);
                    GunSpread.SetValue(g, spread * spreadM);
                }

                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogInfo("AAA hit buff: " + turret.name
                        + " spreadx" + spreadM.ToString("0.##")
                        + " rakex" + rakeM.ToString("0.##")
                        + " trackx" + track.ToString("0.##"));
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("AAA hit buff failed: " + ex.Message);
            }
        }

        internal static void TryBuffHost(Component host)
        {
            if (host == null || !Plugin.IsRuntimeInstance(host))
                return;
            Turret[] turrets = host.GetComponentsInChildren<Turret>(true);
            for (int i = 0; i < turrets.Length; i++)
                TryBuffTurret(turrets[i]);
        }

        private static bool IsAaaCandidate(Turret turret)
        {
            Unit host = TurretAttached != null
                ? TurretAttached.GetValue(turret) as Unit
                : null;
            if (host == null)
                host = turret.GetComponentInParent<Unit>();
            if (host is Aircraft)
                return false;

            AimSolver solver = TurretAimSolver != null
                ? TurretAimSolver.GetValue(turret) as AimSolver
                : null;
            if (solver != null && AimArtillery != null && (bool)AimArtillery.GetValue(solver))
                return false;

            if (NameLooksAaa(host) || NameLooksAaa(turret))
                return true;

            float bestAa = 0f;
            float bestSurface = 0f;
            bool anyProx = false;
            bool anyGun = false;

            WeaponStation[] stations = TurretStations != null
                ? TurretStations.GetValue(turret) as WeaponStation[]
                : null;
            if (stations != null)
            {
                for (int i = 0; i < stations.Length; i++)
                {
                    WeaponStation st = stations[i];
                    if (st == null || st.WeaponInfo == null)
                        continue;
                    RoleIdentity eff = st.WeaponInfo.effectiveness;
                    if (eff.antiAir > bestAa)
                        bestAa = eff.antiAir;
                    if (eff.antiSurface > bestSurface)
                        bestSurface = eff.antiSurface;
                }
            }

            Gun[] guns = turret.GetComponentsInChildren<Gun>(true);
            for (int i = 0; i < guns.Length; i++)
            {
                Gun g = guns[i];
                if (g == null)
                    continue;
                anyGun = true;
                if (GunProximity != null && (bool)GunProximity.GetValue(g))
                    anyProx = true;
                WeaponInfo info = GunInfo != null ? GunInfo.GetValue(g) as WeaponInfo : null;
                if (info == null)
                    continue;
                RoleIdentity eff = info.effectiveness;
                if (eff.antiAir > bestAa)
                    bestAa = eff.antiAir;
                if (eff.antiSurface > bestSurface)
                    bestSurface = eff.antiSurface;
                if (NameLooksAaa(info.weaponName) || NameLooksAaa(info.shortName))
                    return true;
            }

            if (!anyGun)
                return false;

            // SPAAG / CIWS / AAA: meaningful AA role, not a tank HE main gun.
            if (anyProx && bestAa >= 0.25f)
                return true;
            if (bestAa >= 0.45f && bestAa >= bestSurface * 0.55f)
                return true;

            UnitDefinition def = host != null ? host.definition : null;
            if (def != null)
            {
                if (def.roleIdentity.antiAir >= 0.5f && def.typeIdentity.air >= 0.35f)
                    return true;
                if (NameLooksAaa(def.unitName) || NameLooksAaa(def.code) || NameLooksAaa(def.jsonKey))
                    return true;
            }

            return false;
        }

        private static bool NameLooksAaa(UnityEngine.Object obj)
        {
            return obj != null && NameLooksAaa(obj.name);
        }

        private static bool NameLooksAaa(Unit unit)
        {
            if (unit == null)
                return false;
            if (NameLooksAaa(unit.name))
                return true;
            UnitDefinition def = unit.definition;
            if (def == null)
                return false;
            return NameLooksAaa(def.unitName)
                || NameLooksAaa(def.code)
                || NameLooksAaa(def.jsonKey)
                || NameLooksAaa(def.bogeyName);
        }

        private static bool NameLooksAaa(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            string u = s.ToUpperInvariant();
            return u.IndexOf("AAA", StringComparison.Ordinal) >= 0
                || u.IndexOf("SPAAG", StringComparison.Ordinal) >= 0
                || u.IndexOf("CIWS", StringComparison.Ordinal) >= 0
                || u.IndexOf("CRAM", StringComparison.Ordinal) >= 0
                || u.IndexOf("AEROSENTRY", StringComparison.Ordinal) >= 0
                || u.IndexOf("FLAK", StringComparison.Ordinal) >= 0;
        }
    }

    [HarmonyPatch(typeof(Turret), "Awake")]
    internal static class Patch_Turret_Awake_AaaHit
    {
        [HarmonyPostfix]
        private static void Postfix(Turret __instance)
        {
            AaaHitService.TryBuffTurret(__instance);
        }
    }
}
