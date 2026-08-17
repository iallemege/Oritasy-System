using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield reflection cache for engine / FBW / fuel fields.
    /// Written from scratch for the 0.0.9.56 rewrite.
    /// </summary>
    internal static class EngineReflection
    {
        internal static readonly FieldInfo TurbineMaxFuel =
            AccessTools.Field(typeof(TurbineEngine), "maxFuelConsumption");
        internal static readonly FieldInfo PropNominalPower =
            AccessTools.Field(typeof(ConstantSpeedProp), "nominalPower");
        internal static readonly FieldInfo PropFanNominalPower =
            AccessTools.Field(typeof(PropFan), "nominalPower");
        internal static readonly FieldInfo RotorNominalPower =
            AccessTools.Field(typeof(RotorShaft), "nominalPower");
        internal static readonly FieldInfo RotorTorqueLimit =
            AccessTools.Field(typeof(RotorShaft), "torqueLimit");
        internal static readonly FieldInfo DuctedMaxThrust =
            AccessTools.Field(typeof(DuctedFan), "maxThrust");
        internal static readonly FieldInfo DuctedMaxPower =
            AccessTools.Field(typeof(DuctedFan), "maxPower");
        internal static readonly FieldInfo DuctedNominalPower =
            AccessTools.Field(typeof(DuctedFan), "nominalPower");
        internal static readonly FieldInfo TurbofanThrust =
            AccessTools.Field(typeof(Turbofan), "staticThrust");
        internal static readonly FieldInfo TurbojetThrust =
            AccessTools.Field(typeof(Turbojet), "maxThrust");
        internal static readonly FieldInfo TurbofanFuel =
            AccessTools.Field(typeof(Turbofan), "fuelConsumptionMax");
        internal static readonly FieldInfo TurbojetFuel =
            AccessTools.Field(typeof(Turbojet), "fuelConsumptionMax");
        internal static readonly FieldInfo AircraftFuelCapacity =
            AccessTools.Field(typeof(Aircraft), "fuelCapacity");
        internal static readonly FieldInfo ControlsFilter =
            AccessTools.Field(typeof(Aircraft), "controlsFilter");
        internal static readonly FieldInfo GearSpring =
            AccessTools.Field(typeof(LandingGear), "springRate");
        internal static readonly FieldInfo GearDamping =
            AccessTools.Field(typeof(LandingGear), "dampingRate");
        internal static readonly FieldInfo GearAlign =
            AccessTools.Field(typeof(LandingGear), "aligningStrength");
        internal static readonly FieldInfo TankCapacity =
            AccessTools.Field(typeof(FuelTank), "fuelCapacity");
        internal static readonly FieldInfo MountDisabled =
            AccessTools.Field(typeof(WeaponMount), "disabled");

        internal static readonly FieldInfo FlyByWire =
            AccessTools.Field(typeof(ControlsFilter), "flyByWire");
        internal static readonly Type FlyByWireType = ResolveFbwType();
        internal static readonly FieldInfo FbwGLimit =
            AccessTools.Field(FlyByWireType, "gLimitPositive");
        internal static readonly FieldInfo FbwCorner =
            AccessTools.Field(FlyByWireType, "cornerSpeed");
        internal static readonly FieldInfo FbwMaxRoll =
            AccessTools.Field(FlyByWireType, "maxRollSpeed");
        internal static readonly FieldInfo FbwPostStall =
            AccessTools.Field(FlyByWireType, "postStallManeuverSpeed");
        internal static readonly FieldInfo FbwMaxPitch =
            AccessTools.Field(FlyByWireType, "maxPitchAngularVel");
        internal static readonly FieldInfo FbwMaxRollVel =
            AccessTools.Field(FlyByWireType, "maxRollAngularVel");
        internal static readonly FieldInfo FbwAlpha =
            AccessTools.Field(FlyByWireType, "alphaLimiter");
        internal static readonly FieldInfo FbwTakeoff =
            AccessTools.Field(FlyByWireType, "takeoffSpeed");
        internal static readonly FieldInfo CfParams =
            AccessTools.Field(typeof(ControlsFilter), "aircraftParameters");

        internal static readonly FieldInfo PilotAircraft =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");
        internal static readonly FieldInfo PilotMaxG =
            AccessTools.Field(typeof(PilotPlayerState), "maxG");
        internal static readonly FieldInfo PilotStrength =
            AccessTools.Field(typeof(PilotPlayerState), "pilotStrength");
        internal static readonly FieldInfo ShipThrust =
            AccessTools.Field(typeof(ShipPropulsion), "thrust");
        internal static readonly FieldInfo ShipSteerThrust =
            AccessTools.Field(typeof(ShipPropulsion), "steeringThrust");
        internal static readonly FieldInfo GvTopOnroad =
            AccessTools.Field(typeof(GroundVehicle), "topSpeedOnroad");
        internal static readonly FieldInfo GvTopOffroad =
            AccessTools.Field(typeof(GroundVehicle), "topSpeedOffroad");
        internal static readonly FieldInfo GvAccel =
            AccessTools.Field(typeof(GroundVehicle), "acceleration");

        private static Type ResolveFbwType()
        {
            try
            {
                Type t = AccessTools.Inner(typeof(ControlsFilter), "FlyByWire");
                if (t != null)
                    return t;
            }
            catch { }
            try
            {
                Type t = typeof(ControlsFilter).GetNestedType("FlyByWire",
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (t != null)
                    return t;
            }
            catch { }
            try
            {
                if (FlyByWire != null)
                    return FlyByWire.FieldType;
            }
            catch { }
            return null;
        }

        internal static void MulField(FieldInfo field, object target, float mul)
        {
            if (field == null || target == null || Mathf.Abs(mul - 1f) < 0.0001f)
                return;
            try
            {
                object raw = field.GetValue(target);
                if (raw is float)
                    field.SetValue(target, ((float)raw) * mul);
            }
            catch { }
        }

        internal static void SetAbsoluteFromBaseline(
            FieldInfo field, object target, int baselineKey, float mul,
            System.Collections.Generic.Dictionary<int, float> baselines)
        {
            if (field == null || target == null || baselines == null)
                return;
            mul = Mathf.Max(0.05f, mul);
            try
            {
                float current = (float)field.GetValue(target);
                float baseline;
                if (!baselines.TryGetValue(baselineKey, out baseline) || baseline <= 0f)
                {
                    // Infer vanilla as current / 1 when first seen under mul already applied elsewhere.
                    baseline = current;
                    baselines[baselineKey] = baseline;
                }
                field.SetValue(target, baseline * mul);
            }
            catch { }
        }
    }
}
