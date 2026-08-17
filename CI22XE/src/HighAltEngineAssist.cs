using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Thin-air fix: vanilla Turbojet/Turbofan kill below minDensity and altitudeThrust
    /// curves collapse thrust near the service ceiling. Floor density + reinject thrust
    /// for the local player at extreme AGL, and harder while crash-guardian is recovering.
    /// </summary>
    internal static class HighAltEngineAssist
    {
        private static readonly FieldInfo TurbojetAircraft =
            AccessTools.Field(typeof(Turbojet), "aircraft");
        private static readonly FieldInfo TurbofanAircraft =
            AccessTools.Field(typeof(Turbofan), "aircraft");
        private static readonly FieldInfo TurbojetOperable =
            AccessTools.Field(typeof(Turbojet), "operable");
        private static readonly FieldInfo TurbofanOperable =
            AccessTools.Field(typeof(Turbofan), "operable");
        private static readonly FieldInfo TurbojetMinDensity =
            AccessTools.Field(typeof(Turbojet), "minDensity");
        private static readonly FieldInfo TurbofanMinDensity =
            AccessTools.Field(typeof(Turbofan), "minDensity");
        private static readonly FieldInfo TurbojetThrust =
            AccessTools.Field(typeof(Turbojet), "thrust");
        private static readonly FieldInfo TurbojetMaxThrust =
            AccessTools.Field(typeof(Turbojet), "maxThrust");
        private static readonly FieldInfo TurbofanThrust =
            AccessTools.Field(typeof(Turbofan), "currentThrust")
            ?? AccessTools.Field(typeof(Turbofan), "thrust");
        private static readonly FieldInfo TurbofanStaticThrust =
            AccessTools.Field(typeof(Turbofan), "staticThrust");

        internal static bool ShouldAssist(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (BeginnerAssist.ForceHighAltThrust(ac))
                return true;
            if (!IsLocalAircraft(ac))
                return false;
            if (AirframeWearService.HighAltActive())
                return true;
            return HighAltEngineMathService.AltitudeWarrantsAssist(ac.radarAlt);
        }

        private static Aircraft _localAircraft;
        private static int _localAcFrame = -1;

        private static bool IsLocalAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            int frame = Time.frameCount;
            if (frame != _localAcFrame)
            {
                _localAcFrame = frame;
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    _localAircraft = null;
                else
                    _localAircraft = local;
            }
            return _localAircraft != null && object.ReferenceEquals(_localAircraft, ac);
        }

        private static Aircraft AircraftOf(object engine, FieldInfo aircraftField)
        {
            if (engine == null || aircraftField == null)
                return null;
            try { return aircraftField.GetValue(engine) as Aircraft; }
            catch { return null; }
        }

        private static void FloorDensity(Aircraft ac, float minDensity)
        {
            if (ac == null)
                return;
            bool force = BeginnerAssist.ForceHighAltThrust(ac);
            float floor = HighAltEngineMathService.DensityFloor(minDensity, force);
            try
            {
                if (ac.airDensity < floor)
                    ac.airDensity = floor;
            }
            catch { }
        }

        private static float ReadThrottle(Aircraft ac)
        {
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                    return Mathf.Clamp01(inputs.throttle);
            }
            catch { }
            return 1f;
        }

        private static void ForceOperable(FieldInfo operableField, object engine)
        {
            if (operableField == null || engine == null)
                return;
            try
            {
                if (!(bool)operableField.GetValue(engine))
                    operableField.SetValue(engine, true);
            }
            catch { }
        }

        private static void WriteThrust(FieldInfo thrustField, object engine, float value)
        {
            if (thrustField == null || engine == null)
                return;
            try { thrustField.SetValue(engine, value); }
            catch { }
        }

        private static void BoostThrust(FieldInfo thrustField, object engine, float want)
        {
            if (thrustField == null || engine == null || want <= 0f)
                return;
            try
            {
                float cur = (float)thrustField.GetValue(engine);
                if (cur < want)
                    thrustField.SetValue(engine, want);
            }
            catch { }
        }

        [HarmonyPatch(typeof(Turbojet), "FixedUpdate")]
        private static class Patch_Turbojet_HighAlt
        {
            [HarmonyPrefix]
            private static void Prefix(Turbojet __instance)
            {
                Aircraft ac = AircraftOf(__instance, TurbojetAircraft);
                if (!ShouldAssist(ac))
                    return;
                if (!AirframeWearService.EngineShouldProduceThrust(__instance))
                    return;

                float minD = 0.05f;
                try
                {
                    if (TurbojetMinDensity != null)
                        minD = (float)TurbojetMinDensity.GetValue(__instance);
                }
                catch { }
                FloorDensity(ac, minD);
                ForceOperable(TurbojetOperable, __instance);
            }

            [HarmonyPostfix]
            private static void Postfix(Turbojet __instance)
            {
                Aircraft ac = AircraftOf(__instance, TurbojetAircraft);
                if (!IsLocalAircraft(ac))
                    return;
                if (!AirframeWearService.EngineShouldProduceThrust(__instance))
                    return;
                if (!ShouldAssist(ac))
                    return;
                if (ReverseThrustService.SignedThrottle() < 0f)
                    return;

                float throttle = ReadThrottle(ac);
                float maxT = 0f;
                try
                {
                    if (TurbojetMaxThrust != null)
                        maxT = (float)TurbojetMaxThrust.GetValue(__instance);
                }
                catch { }
                float want = HighAltEngineMathService.ThrustWant(
                    maxT, throttle, BeginnerAssist.ForceHighAltThrust(ac),
                    AirframeWearService.HighAltActive());
                if (want <= 0f)
                    return;
                BoostThrust(TurbojetThrust, __instance, want);
            }
        }

        [HarmonyPatch(typeof(Turbofan), "FixedUpdate")]
        private static class Patch_Turbofan_HighAlt
        {
            [HarmonyPrefix]
            private static void Prefix(Turbofan __instance)
            {
                Aircraft ac = AircraftOf(__instance, TurbofanAircraft);
                if (!ShouldAssist(ac))
                    return;
                if (!AirframeWearService.EngineShouldProduceThrust(__instance))
                    return;

                float minD = 0.05f;
                try
                {
                    if (TurbofanMinDensity != null)
                        minD = (float)TurbofanMinDensity.GetValue(__instance);
                }
                catch { }
                FloorDensity(ac, minD);
                ForceOperable(TurbofanOperable, __instance);
            }

            [HarmonyPostfix]
            private static void Postfix(Turbofan __instance)
            {
                Aircraft ac = AircraftOf(__instance, TurbofanAircraft);
                if (!IsLocalAircraft(ac))
                    return;
                if (!AirframeWearService.EngineShouldProduceThrust(__instance))
                    return;
                if (!ShouldAssist(ac))
                    return;
                if (ReverseThrustService.SignedThrottle() < 0f)
                    return;

                float throttle = ReadThrottle(ac);
                float maxT = 0f;
                try
                {
                    if (TurbofanStaticThrust != null)
                        maxT = (float)TurbofanStaticThrust.GetValue(__instance);
                }
                catch { }
                float want = HighAltEngineMathService.ThrustWant(
                    maxT, throttle, BeginnerAssist.ForceHighAltThrust(ac),
                    AirframeWearService.HighAltActive());
                if (want <= 0f)
                    return;
                BoostThrust(TurbofanThrust, __instance, want);
            }
        }
    }
}
