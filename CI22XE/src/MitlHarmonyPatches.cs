using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Core MITL (man-in-the-loop) Harmony hooks. Lives in Oritasy combined —
    /// Oritasy Hud is independent and must not own these patches.
    /// </summary>
    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Mitl_SpawnerMissile
    {
        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new System.Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result)
        {
            MissileCameraHud.NotifySpawn(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new System.Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result)
        {
            MissileCameraHud.NotifySpawn(__result);
        }
    }

    [HarmonyPatch(typeof(Missile), "Steering")]
    [HarmonyPriority(Priority.Last)]
    internal static class Patch_Mitl_MissileManualSteering
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            if (!MissileCameraHud.ManualActive)
                return;
            MissileCameraHud.ApplyGuidanceForSteering(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "MissedTarget")]
    internal static class Patch_Mitl_MissileManualMissed
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, ref bool __result)
        {
            if (!MissileCameraHud.IsManualPiloting(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "LosingGround")]
    internal static class Patch_Mitl_MissileManualLosingGround
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, ref bool __result)
        {
            if (!MissileCameraHud.IsManualPiloting(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
    internal static class Patch_Mitl_AxisWhileMitl
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");

        [HarmonyPrefix]
        private static bool Prefix(PilotPlayerState __instance)
        {
            if (__instance == null || !MissileCameraHud.ManualActive)
                return true;
            try
            {
                __instance.pitchInput = 0f;
                __instance.rollInput = 0f;
                __instance.yawInput = 0f;
                Aircraft ac = AircraftField != null
                    ? AircraftField.GetValue(__instance) as Aircraft : null;
                if (ac != null)
                {
                    ControlInputs ci = ac.GetInputs();
                    if (ci != null)
                    {
                        ci.pitch = 0f;
                        ci.roll = 0f;
                        ci.yaw = 0f;
                        ci.brake = 0f;
                        ci.customAxis1 = 0f;
                    }
                }
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerControls")]
    internal static class Patch_Mitl_ControlsWhileMitl
    {
        private static readonly FieldInfo PilotField =
            AccessTools.Field(typeof(PilotBaseState), "pilot");
        private static FieldInfo _playerInputField;
        private static MethodInfo _rewiredGetButtonDown;
        private static readonly object[] RewiredButtonArgs = new object[1];

        [HarmonyPrefix]
        private static bool Prefix(PilotPlayerState __instance)
        {
            if (!MissileCameraHud.ManualActive)
                return true;
            TryEject(__instance);
            return false;
        }

        private static void TryEject(PilotPlayerState state)
        {
            if (state == null || !GameManager.flightControlsEnabled)
                return;
            if (!RewiredButtonDown("Eject"))
                return;
            try
            {
                Pilot pilot = PilotField != null ? PilotField.GetValue(state) as Pilot : null;
                if (pilot == null || pilot.aircraft == null)
                    return;
                pilot.aircraft.StartEjectionSequence();
            }
            catch { }
        }

        private static bool RewiredButtonDown(string action)
        {
            try
            {
                if (_playerInputField == null)
                    _playerInputField = typeof(GameManager).GetField("playerInput",
                        BindingFlags.Public | BindingFlags.Static);
                object pi = _playerInputField != null ? _playerInputField.GetValue(null) : null;
                if (pi == null)
                    return false;
                if (_rewiredGetButtonDown == null)
                    _rewiredGetButtonDown = pi.GetType().GetMethod("GetButtonDown",
                        new Type[] { typeof(string) });
                if (_rewiredGetButtonDown == null)
                    return false;
                RewiredButtonArgs[0] = action;
                object v = _rewiredGetButtonDown.Invoke(pi, RewiredButtonArgs);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }
    }
}
