using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Keep BIA / J-35 spawn alive while Blueprinter 1.8.x is loaded.
    /// Blueprinter's JetNozzle postfix calls GetBlendShapeWeight(0) with no
    /// blend-shape check (native crash). Its cockpit prefix enumerates
    /// Aircraft.engines without a null guard. PrefabHashAssigner may also
    /// reassign a unique hash — Oritasy must not stamp FNV over that.
    /// </summary>
    internal static class BlueprinterCompat
    {
        private static bool _done;

        internal static void TryPatch(Harmony harmony)
        {
            if (_done || harmony == null)
                return;
            _done = true;
            Type cockpit = Type.GetType("Blueprinter.AircraftSetCockpitRenderersPatch, Blueprinter");
            Type nozzle = Type.GetType("Blueprinter.JetNozzleBlendShapes, Blueprinter");
            Type station = Type.GetType("Blueprinter.WeaponStationPrioritizeByPositionPatch, Blueprinter");
            Type hashes = Type.GetType("Blueprinter.PrefabHashAssigner, Blueprinter");
            if (cockpit == null && nozzle == null && station == null && hashes == null)
                return;
            _done = true;
            try
            {
                PatchSkip(harmony, cockpit, "Prefix", "SkipCockpit");
                PatchSkip(harmony, nozzle, "Postfix", "SkipNozzle");
                PatchSkip(harmony, station, "Prefix", "SkipStation");
                PatchSkip(harmony, hashes, "AssignFromBundles", "StampBaasBeforeHashes");
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Blueprinter BIA guards installed");
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Blueprinter compat: " + ex.Message);
            }
        }

        private static void PatchSkip(Harmony harmony, Type type, string method, string ours)
        {
            if (type == null)
                return;
            MethodInfo target = AccessTools.Method(type, method);
            MethodInfo skip = AccessTools.Method(typeof(BlueprinterCompat), ours);
            if (target == null || skip == null)
                return;
            harmony.Patch(target, new HarmonyMethod(skip));
        }

        private static bool IsBiaAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (AircraftIdentity.IsBiaAircraft(ac))
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Skip Blueprinter's SetCockpitRenderers replacement for BIA and when
        /// engines is null (their enumerator NREs). __result true = run vanilla.
        /// </summary>
        private static bool SkipCockpit(Aircraft __instance, ref bool __result)
        {
            bool skip = false;
            try
            {
                if (IsBiaAircraft(__instance))
                    skip = true;
                else if (__instance != null && __instance.engines == null)
                    skip = true;
            }
            catch { skip = true; }
            if (!skip)
                return true;
            __result = true;
            return false;
        }

        /// <summary>
        /// Skip Blueprinter nozzle blend-shapes on BIA, and on any JetNozzle
        /// whose mesh has no blend shape 0 (Unity native abort).
        /// </summary>
        private static bool SkipNozzle(JetNozzle __instance)
        {
            if (__instance == null)
                return false;
            try
            {
                Aircraft ac = __instance.GetComponentInParent<Aircraft>();
                if (ac != null)
                {
                    try
                    {
                        if (!ac.networked)
                            return false;
                    }
                    catch { return false; }
                }
                if (IsBiaAircraft(ac))
                    return false;
                SkinnedMeshRenderer smr = __instance.GetComponent<SkinnedMeshRenderer>();
                if (smr == null)
                    return true;
                Mesh mesh = smr.sharedMesh;
                if (mesh == null || mesh.blendShapeCount < 1)
                    return false;
            }
            catch { return false; }
            return true;
        }

        private static bool SkipStation(Aircraft aircraft, ref bool __result)
        {
            if (!IsBiaAircraft(aircraft))
                return true;
            __result = true;
            return false;
        }

        private static void StampBaasBeforeHashes()
        {
            try
            {
                AircraftDefinition[] defs = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
                if (defs == null)
                    return;
                for (int i = 0; i < defs.Length; i++)
                    BaasNetworkFixer.EnsureDefinition(defs[i]);
            }
            catch { }
        }
    }
}
