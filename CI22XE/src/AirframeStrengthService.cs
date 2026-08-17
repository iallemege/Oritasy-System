using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Scale airframe joint / impact durability without touching mass or G limits.
    /// High thrust otherwise tears trainers (e.g. T/A-30) apart in maneuvers.
    /// </summary>
    internal static class AirframeStrengthService
    {
        private static readonly FieldInfo AeroJoints =
            AccessTools.Field(typeof(AeroPart), "joints");
        private static readonly FieldInfo UnitImpact =
            AccessTools.Field(typeof(UnitPart), "impactDamage");
        private static readonly FieldInfo UnitStructural =
            AccessTools.Field(typeof(UnitPart), "structuralThreshold");
        private static readonly FieldInfo ImpactThreshold =
            AccessTools.Field(typeof(ImpactDamage), "threshold");
        private static readonly FieldInfo ImpactMultiplier =
            AccessTools.Field(typeof(ImpactDamage), "multiplier");

        private static readonly HashSet<int> BuffedAircraft = new HashSet<int>();

        internal static float StrengthMul
        {
            get
            {
                return Plugin.AirframeStrengthMul != null
                    ? Mathf.Max(1f, Plugin.AirframeStrengthMul.Value)
                    : 3f;
            }
        }

        internal static void TryBuff(Aircraft aircraft)
        {
            if (aircraft == null || !AircraftIdentity.IsXeAircraft(aircraft))
                return;
            if (!Plugin.IsRuntimeInstance(aircraft))
                return;

            int aid = aircraft.GetInstanceID();
            if (!BuffedAircraft.Add(aid))
                return;

            float mul = StrengthMul;
            if (mul <= 1.001f)
                return;

            AeroPart[] aeros = aircraft.GetComponentsInChildren<AeroPart>(true);
            for (int i = 0; i < aeros.Length; i++)
                BuffAeroPart(aeros[i], mul);

            // Non-aero UnitParts (fuselage chunks, pylons): impact threshold + structural detach.
            UnitPart[] parts = aircraft.GetComponentsInChildren<UnitPart>(true);
            for (int i = 0; i < parts.Length; i++)
            {
                UnitPart part = parts[i];
                if (part == null || part is AeroPart)
                    continue;
                BuffUnitPartDurability(part, mul);
            }

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("AirframeStrength x" + mul.ToString("0.##")
                    + " on " + AircraftIdentity.GetKey(aircraft)
                    + " aeros=" + aeros.Length);
        }

        internal static void Unregister(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            BuffedAircraft.Remove(aircraft.GetInstanceID());
        }

        private static void BuffAeroPart(AeroPart part, float mul)
        {
            if (part == null)
                return;

            BuffUnitPartDurability(part, mul);

            if (AeroJoints == null)
                return;
            PartJoint[] joints = null;
            try { joints = AeroJoints.GetValue(part) as PartJoint[]; }
            catch { return; }
            if (joints == null)
                return;

            for (int i = 0; i < joints.Length; i++)
            {
                PartJoint pj = joints[i];
                if (pj == null)
                    continue;
                if (pj.breakForce > 0f && !float.IsInfinity(pj.breakForce))
                    pj.breakForce *= mul;
                if (pj.breakTorque > 0f && !float.IsInfinity(pj.breakTorque))
                    pj.breakTorque *= mul;

                Joint j = pj.joint;
                if (j == null)
                    continue;
                if (j.breakForce > 0f && !float.IsInfinity(j.breakForce))
                    j.breakForce *= mul;
                if (j.breakTorque > 0f && !float.IsInfinity(j.breakTorque))
                    j.breakTorque *= mul;
            }
        }

        private static void BuffUnitPartDurability(UnitPart part, float mul)
        {
            if (part == null)
                return;

            // Lower structuralThreshold → needs more damage before detach.
            if (UnitStructural != null)
            {
                try
                {
                    float th = (float)UnitStructural.GetValue(part);
                    if (th > 0.01f)
                        UnitStructural.SetValue(part, th / mul);
                }
                catch { }
            }

            // Higher impact threshold / lower multiplier → harder to break from collision stress.
            if (UnitImpact == null || ImpactThreshold == null)
                return;
            try
            {
                object impact = UnitImpact.GetValue(part);
                if (impact == null)
                    return;
                float th = (float)ImpactThreshold.GetValue(impact);
                if (th > 0.01f)
                    ImpactThreshold.SetValue(impact, th * mul);
                if (ImpactMultiplier != null)
                {
                    float m = (float)ImpactMultiplier.GetValue(impact);
                    if (m > 0.01f)
                        ImpactMultiplier.SetValue(impact, m / mul);
                }
            }
            catch { }
        }
    }
}
