using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Ground-vehicle cook-off: mushroom-cloud visuals without Shockwave damage/push,
    /// plus easier FuelTank ignition.
    /// </summary>
    internal static class VehicleCookoffService
    {
        internal const float DeathCookoffChance = 0.85f;
        internal const float IgnitionThresholdMul = 0.22f;
        internal const float VisualYieldKt = 0.45f;

        private static readonly FieldInfo IgnitionPierceMin =
            AccessTools.Field(typeof(FuelTank), "ignitionPierceMin");
        private static readonly FieldInfo IgnitionPierceMax =
            AccessTools.Field(typeof(FuelTank), "ignitionPierceMax");
        private static readonly FieldInfo IgnitionBlastMin =
            AccessTools.Field(typeof(FuelTank), "ignitionBlastMin");
        private static readonly FieldInfo IgnitionBlastMax =
            AccessTools.Field(typeof(FuelTank), "ignitionBlastMax");
        private static readonly FieldInfo IgnitionGMin =
            AccessTools.Field(typeof(FuelTank), "ignitionGMin");
        private static readonly FieldInfo IgnitionGMax =
            AccessTools.Field(typeof(FuelTank), "ignitionGMax");
        private static readonly FieldInfo FireIntensity =
            AccessTools.Field(typeof(FuelTank), "fireIntensity");

        private static readonly HashSet<int> PlayedUnits = new HashSet<int>();
        private static GameObject CachedNukeFx;
        private static bool FxSearchTried;

        internal static void BoostIgnition(GroundVehicle gv)
        {
            if (gv == null || !Plugin.IsRuntimeInstance(gv))
                return;
            FuelTank[] tanks = gv.GetComponentsInChildren<FuelTank>(true);
            if (tanks == null)
                return;
            for (int i = 0; i < tanks.Length; i++)
                ScaleIgnition(tanks[i]);
        }

        private static void ScaleIgnition(FuelTank tank)
        {
            if (tank == null)
                return;
            ScaleField(IgnitionPierceMin, tank, IgnitionThresholdMul, 0.01f);
            ScaleField(IgnitionPierceMax, tank, IgnitionThresholdMul, 0.02f);
            ScaleField(IgnitionBlastMin, tank, IgnitionThresholdMul, 0.01f);
            ScaleField(IgnitionBlastMax, tank, IgnitionThresholdMul, 0.02f);
            ScaleField(IgnitionGMin, tank, IgnitionThresholdMul, 0.05f);
            ScaleField(IgnitionGMax, tank, IgnitionThresholdMul, 0.1f);
            if (FireIntensity != null)
            {
                try
                {
                    float fi = (float)FireIntensity.GetValue(tank);
                    if (fi > 0f)
                        FireIntensity.SetValue(tank, fi * 1.8f);
                }
                catch { }
            }
        }

        private static void ScaleField(FieldInfo fi, object target, float mul, float floor)
        {
            if (fi == null || target == null)
                return;
            try
            {
                float v = (float)fi.GetValue(target);
                if (v <= 0f)
                    return;
                fi.SetValue(target, Mathf.Max(floor, v * mul));
            }
            catch { }
        }

        internal static bool IsVehicleTank(FuelTank tank)
        {
            if (tank == null)
                return false;
            try
            {
                return tank.GetComponentInParent<GroundVehicle>() != null;
            }
            catch
            {
                return false;
            }
        }

        internal static void TryCookoffOnDeath(GroundVehicle gv)
        {
            if (gv == null || !Plugin.IsRuntimeInstance(gv))
                return;
            if (AlreadyPlayed(gv))
                return;
            if (UnityEngine.Random.value > DeathCookoffChance)
                return;
            Vector3 pos;
            try { pos = gv.transform.position; }
            catch { return; }
            SpawnVisualOnlyNuke(pos, gv);
        }

        internal static void TryCookoffFromFireball(FuelTank tank)
        {
            if (!IsVehicleTank(tank))
                return;
            GroundVehicle gv = null;
            try { gv = tank.GetComponentInParent<GroundVehicle>(); }
            catch { }
            if (AlreadyPlayed(gv != null ? (Unit)gv : null))
                return;
            Vector3 pos;
            try { pos = tank.transform.position; }
            catch { return; }
            SpawnVisualOnlyNuke(pos, gv);
        }

        private static bool AlreadyPlayed(Unit u)
        {
            if (u == null)
                return false;
            try { return PlayedUnits.Contains(u.GetInstanceID()); }
            catch { return false; }
        }

        private static void MarkPlayed(Unit u)
        {
            if (u == null)
                return;
            try { PlayedUnits.Add(u.GetInstanceID()); }
            catch { }
        }

        internal static void SpawnVisualOnlyNuke(Vector3 pos, Unit source)
        {
            GameObject prefab = ResolveNukeFx();
            if (prefab == null)
                return;
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity);
            }
            catch
            {
                return;
            }
            if (go == null)
                return;
            try { go.AddComponent<CookoffNukeTag>(); }
            catch { }

            try
            {
                MushroomCloud mc = go.GetComponentInChildren<MushroomCloud>(true);
                if (mc != null)
                    mc.yield = VisualYieldKt;
            }
            catch { }

            try
            {
                Shockwave[] waves = go.GetComponentsInChildren<Shockwave>(true);
                if (waves != null)
                {
                    for (int i = 0; i < waves.Length; i++)
                    {
                        if (waves[i] == null)
                            continue;
                        waves[i].enabled = false;
                    }
                }
            }
            catch { }

            MarkPlayed(source);
        }

        internal static bool IsVisualOnly(Shockwave sw)
        {
            if (sw == null)
                return false;
            try
            {
                return sw.GetComponentInParent<CookoffNukeTag>() != null;
            }
            catch
            {
                return false;
            }
        }

        private static GameObject ResolveNukeFx()
        {
            if (CachedNukeFx != null)
                return CachedNukeFx;
            if (FxSearchTried)
                return CachedNukeFx;
            FxSearchTried = true;

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            if (all != null)
            {
                GameObject sceneHit = null;
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i];
                    if (go == null)
                        continue;
                    string n = go.name;
                    if (n != "explosion_20kt" && n != "explosion_20kt(Clone)"
                        && n != "explosion_10kt" && n != "explosion_5kt"
                        && n != "explosion_1kt")
                        continue;
                    if (go.GetComponentInChildren<MushroomCloud>(true) == null)
                        continue;
                    if (!go.scene.IsValid())
                    {
                        CachedNukeFx = go;
                        break;
                    }
                    if (sceneHit == null)
                        sceneHit = go;
                }
                if (CachedNukeFx == null)
                    CachedNukeFx = sceneHit;
            }

            if (CachedNukeFx == null)
            {
                MushroomCloud[] clouds = Resources.FindObjectsOfTypeAll<MushroomCloud>();
                if (clouds != null)
                {
                    for (int i = 0; i < clouds.Length; i++)
                    {
                        MushroomCloud mc = clouds[i];
                        if (mc == null)
                            continue;
                        GameObject go = mc.gameObject;
                        if (go == null)
                            continue;
                        if (!go.scene.IsValid())
                        {
                            CachedNukeFx = go;
                            break;
                        }
                        if (CachedNukeFx == null)
                            CachedNukeFx = go;
                    }
                }
            }

            if (CachedNukeFx != null && Plugin.Log != null)
                Plugin.Log.LogInfo("Vehicle cook-off FX: " + CachedNukeFx.name);
            else if (Plugin.Log != null)
                Plugin.Log.LogWarning("Vehicle cook-off FX not found.");
            return CachedNukeFx;
        }
    }

    internal sealed class CookoffNukeTag : MonoBehaviour
    {
    }

    [HarmonyPatch(typeof(GroundVehicle), "UnitDisabled")]
    internal static class Patch_GroundVehicle_CookoffDeath
    {
        [HarmonyPostfix]
        private static void Postfix(GroundVehicle __instance, bool oldState, bool newState)
        {
            if (__instance == null || !newState || oldState)
                return;
            VehicleCookoffService.TryCookoffOnDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(FuelTank), "Fireball")]
    internal static class Patch_FuelTank_VehicleCookoffFireball
    {
        [HarmonyPrefix]
        private static bool Prefix(FuelTank __instance)
        {
            if (!VehicleCookoffService.IsVehicleTank(__instance))
                return true;
            VehicleCookoffService.TryCookoffFromFireball(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Start")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Shockwave_CookoffSkipStart
    {
        [HarmonyPrefix]
        private static bool Prefix(Shockwave __instance)
        {
            if (!VehicleCookoffService.IsVisualOnly(__instance))
                return true;
            __instance.enabled = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Update")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Shockwave_CookoffSkipUpdate
    {
        [HarmonyPrefix]
        private static bool Prefix(Shockwave __instance)
        {
            return !VehicleCookoffService.IsVisualOnly(__instance);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "SetOwner")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Shockwave_CookoffSkipOwner
    {
        [HarmonyPrefix]
        private static bool Prefix(Shockwave __instance)
        {
            return !VehicleCookoffService.IsVisualOnly(__instance);
        }
    }
}
