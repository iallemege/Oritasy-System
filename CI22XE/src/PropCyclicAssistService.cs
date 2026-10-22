using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// CI-22XE tractor prop: periodic (cyclic) blade pitch that helps pitch
    /// the nose. Vanilla differentialControlFactors are zero and also gated by
    /// Dot(up, prop.forward) ≈ 0 on a fuselage-aligned hub, so they never fire.
    /// Gain is raised outside the FBW speed band (below takeoff / above Vmax)
    /// where elevator is weak. Visual hub RPM is scaled down so the cyclic
    /// is visible; physics RPM is unchanged.
    /// </summary>
    internal static class PropCyclicAssistService
    {
        private const float VisualRpmScale = 0.12f;
        private const float CyclicDeg = 9f;
        private const float AutoRateGain = 0.55f;
        private const float MaxPitchRate = 1.15f;
        private const float OutsideMul = 2.8f;
        private const float InsideMul = 1f;
        private const float GroundTorqueMul = 0.22f;
        private const float TorqueAccel = 3.4f;
        private const float MinRpm = 80f;

        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(ConstantSpeedProp), "aircraft");
        private static readonly FieldInfo BladesField =
            AccessTools.Field(typeof(ConstantSpeedProp), "blades");
        private static readonly FieldInfo HubField =
            AccessTools.Field(typeof(ConstantSpeedProp), "hubVisible");
        private static readonly FieldInfo DiscField =
            AccessTools.Field(typeof(ConstantSpeedProp), "propDisc");
        private static readonly FieldInfo DiscMeshesField =
            AccessTools.Field(typeof(ConstantSpeedProp), "propDiscMeshes");
        private static readonly FieldInfo BlurIndexField =
            AccessTools.Field(typeof(ConstantSpeedProp), "propBlurIndex");
        private static readonly FieldInfo TurnField =
            AccessTools.Field(typeof(ConstantSpeedProp), "turnDirection");
        private static readonly FieldInfo PitchDirField =
            AccessTools.Field(typeof(ConstantSpeedProp), "pitchDirection");
        private static readonly FieldInfo BladeXfField =
            AccessTools.Field(typeof(ConstantSpeedProp.PropBlade), "transform");
        private static readonly FieldInfo BladeRendererField =
            AccessTools.Field(typeof(ConstantSpeedProp.PropBlade), "renderer");
        private static readonly FieldInfo PartField =
            AccessTools.Field(typeof(ConstantSpeedProp), "unitPart");
        private static readonly FieldInfo RpmLimitField =
            AccessTools.Field(typeof(ConstantSpeedProp), "rpmLimit");

        private static readonly Dictionary<int, float> LastCyclic =
            new Dictionary<int, float>(16);

        internal static bool AppliesTo(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                return AircraftIdentity.IsCi22(AircraftIdentity.GetKey(ac))
                    || AircraftIdentity.IsCoinAircraft(ac);
            }
            catch
            {
                return false;
            }
        }

        internal static Aircraft ReadAircraft(ConstantSpeedProp prop)
        {
            if (prop == null || AircraftField == null)
                return null;
            try { return AircraftField.GetValue(prop) as Aircraft; }
            catch { return null; }
        }

        internal static void ApplyCyclic(ConstantSpeedProp prop)
        {
            if (prop == null || BladesField == null || BladeXfField == null)
                return;
            Aircraft ac = ReadAircraft(prop);
            if (!AppliesTo(ac))
                return;
            if (PropFeatherService.ShouldFeather(ac))
                return;
            if (ReverseThrustService.IsActive(ac))
                return;
            if (prop.RPM < MinRpm)
            {
                ClearCyclic(prop);
                return;
            }

            // 264C: ground roll below ~75% takeoff — OutsideBand hits 1 and cyclic
            // amp 2.8x; stick pull fights gear → bounce + speed digit jitter.
            if (!ReverseThrustService.IsAirborne(ac))
            {
                float gSpd = 0f;
                try { gSpd = ac.speed; }
                catch
                {
                    if (ac.rb != null)
                        gSpd = ac.rb.velocity.magnitude;
                }
                float takeoff = 55f;
                try
                {
                    AircraftDefinition def = ac.definition as AircraftDefinition;
                    if (def != null && def.aircraftParameters != null
                        && def.aircraftParameters.takeoffSpeed > 1f)
                        takeoff = def.aircraftParameters.takeoffSpeed;
                }
                catch { }
                if (gSpd < takeoff * 0.75f)
                {
                    ClearCyclic(prop);
                    return;
                }
            }

            ControlInputs inputs = ReverseThrustService.InputsOf(ac);
            float stick = 0f;
            if (inputs != null)
                stick = Mathf.Clamp(inputs.pitch, -1f, 1f);

            float pitchRate = 0f;
            if (ac.rb != null)
                pitchRate = Vector3.Dot(ac.rb.angularVelocity, ac.transform.right);
            float demand = stick * MaxPitchRate;
            float auto = Mathf.Clamp((demand - pitchRate) * AutoRateGain, -1.2f, 1.2f);
            float cmd = Mathf.Clamp(stick + auto, -1.5f, 1.5f);

            float band = OutsideBand(ac);
            if (!ReverseThrustService.IsAirborne(ac))
                band = Mathf.Min(band, 0.15f);
            float amp = CyclicDeg * (InsideMul + (OutsideMul - InsideMul) * band);
            float sign = PitchSign(prop);

            List<ConstantSpeedProp.PropBlade> blades =
                BladesField.GetValue(prop) as List<ConstantSpeedProp.PropBlade>;
            if (blades == null || blades.Count == 0)
                return;

            Transform hub = prop.transform;
            for (int i = 0; i < blades.Count; i++)
            {
                ConstantSpeedProp.PropBlade blade = blades[i];
                if (blade == null)
                    continue;
                Transform xf = BladeXfField.GetValue(blade) as Transform;
                if (xf == null)
                    continue;
                Vector3 local = hub.InverseTransformPoint(xf.position);
                float az = Mathf.Atan2(local.y, local.x);
                // Nose-up (cmd+) → more pitch on the bottom of the disc.
                float cyclic = -cmd * amp * Mathf.Sin(az);
                int id = xf.GetInstanceID();
                float last = 0f;
                LastCyclic.TryGetValue(id, out last);
                float delta = cyclic - last;
                if (delta * delta > 0.0001f)
                    blade.SetPitch(delta * sign);
                LastCyclic[id] = cyclic;
            }

            ApplyPitchTorque(prop, ac, cmd, band);
        }

        internal static void ScaleVisualSpin(ConstantSpeedProp prop)
        {
            if (prop == null)
                return;
            Aircraft ac = ReadAircraft(prop);
            if (!AppliesTo(ac))
                return;
            if (ReverseThrustService.IsActive(ac))
                return;
            if (HubField == null || TurnField == null)
                return;
            GameObject hubGo = HubField.GetValue(prop) as GameObject;
            if (hubGo == null)
                return;
            float turn = 1f;
            try { turn = (float)(int)TurnField.GetValue(prop); }
            catch { }
            float extra = prop.RPM * -6f * turn * Time.deltaTime * (VisualRpmScale - 1f);
            hubGo.transform.Rotate(0f, 0f, extra, Space.Self);
            ShowBlades(prop);
        }

        private static void ShowBlades(ConstantSpeedProp prop)
        {
            if (BlurIndexField != null)
            {
                try { BlurIndexField.SetValue(prop, 0); }
                catch { }
            }
            if (DiscField != null && DiscMeshesField != null)
            {
                try
                {
                    MeshFilter disc = DiscField.GetValue(prop) as MeshFilter;
                    List<Mesh> meshes = DiscMeshesField.GetValue(prop) as List<Mesh>;
                    if (disc != null && meshes != null && meshes.Count > 0)
                        disc.mesh = meshes[0];
                }
                catch { }
            }
            if (BladesField == null || BladeRendererField == null)
                return;
            List<ConstantSpeedProp.PropBlade> blades =
                BladesField.GetValue(prop) as List<ConstantSpeedProp.PropBlade>;
            if (blades == null)
                return;
            for (int i = 0; i < blades.Count; i++)
            {
                if (blades[i] == null)
                    continue;
                Renderer r = BladeRendererField.GetValue(blades[i]) as Renderer;
                if (r != null)
                    r.enabled = true;
            }
        }

        private static void ClearCyclic(ConstantSpeedProp prop)
        {
            if (prop == null || BladesField == null || BladeXfField == null)
                return;
            List<ConstantSpeedProp.PropBlade> blades =
                BladesField.GetValue(prop) as List<ConstantSpeedProp.PropBlade>;
            if (blades == null)
                return;
            float sign = PitchSign(prop);
            for (int i = 0; i < blades.Count; i++)
            {
                if (blades[i] == null)
                    continue;
                Transform xf = BladeXfField.GetValue(blades[i]) as Transform;
                if (xf == null)
                    continue;
                int id = xf.GetInstanceID();
                float last;
                if (!LastCyclic.TryGetValue(id, out last))
                    continue;
                if (last * last > 0.0001f)
                    blades[i].SetPitch(-last * sign);
                LastCyclic.Remove(id);
            }
        }

        private static float PitchSign(ConstantSpeedProp prop)
        {
            float turn = 1f;
            float dir = -1f;
            try
            {
                if (TurnField != null)
                    turn = (float)(int)TurnField.GetValue(prop);
                if (PitchDirField != null)
                    dir = (float)(int)PitchDirField.GetValue(prop);
            }
            catch { }
            return (-turn) * (-dir);
        }

        private static float OutsideBand(Aircraft ac)
        {
            float speed = 0f;
            try { speed = ac.speed; }
            catch
            {
                if (ac.rb != null)
                    speed = ac.rb.velocity.magnitude;
            }
            float vlo = 55f;
            float vhi = 175f;
            try
            {
                AircraftDefinition def = ac.definition as AircraftDefinition;
                if (def != null && def.aircraftParameters != null)
                {
                    AircraftParameters ap = def.aircraftParameters;
                    if (ap.takeoffSpeed > 1f)
                        vlo = ap.takeoffSpeed;
                    else if (ap.approachSpeed > 1f)
                        vlo = ap.approachSpeed;
                    if (ap.maxSpeed > 1f)
                        vhi = ap.maxSpeed;
                }
            }
            catch { }
            float lo = 0f;
            if (speed < vlo)
                lo = Mathf.InverseLerp(vlo, vlo * 0.45f, speed);
            float hi = 0f;
            if (speed > vhi)
                hi = Mathf.InverseLerp(vhi, vhi * 1.25f, speed);
            return Mathf.Clamp01(Mathf.Max(lo, hi));
        }

        private static void ApplyPitchTorque(
            ConstantSpeedProp prop, Aircraft ac, float cmd, float outside)
        {
            if (ac == null || ac.rb == null || cmd * cmd < 0.0004f)
                return;
            float rpmN = 0f;
            try
            {
                float lim = 2400f;
                if (RpmLimitField != null)
                    lim = (float)RpmLimitField.GetValue(prop);
                if (lim < 1f)
                    lim = 2400f;
                rpmN = Mathf.Clamp01(prop.RPM / lim);
            }
            catch { rpmN = Mathf.Clamp01(prop.RPM / 2400f); }
            float air = 1f;
            try { air = Mathf.Clamp(ac.airDensity / 1.225f, 0.3f, 1.2f); }
            catch { }
            bool airborne = ReverseThrustService.IsAirborne(ac);
            float ground = airborne ? 1f : (GroundTorqueMul * 0.35f);
            float band = airborne ? outside : Mathf.Min(outside, 0.2f);
            float mag = cmd * TorqueAccel * (0.35f + 0.65f * band)
                * rpmN * rpmN * air * ground;
            // +right torque is nose-down in Z-forward Y-up; invert for stick-up.
            ac.rb.AddTorque(-ac.transform.right * mag, ForceMode.Acceleration);
            UnitPart part = null;
            if (PartField != null)
            {
                try { part = PartField.GetValue(prop) as UnitPart; }
                catch { part = null; }
            }
            if (part != null && part.rb != null && !object.ReferenceEquals(part.rb, ac.rb))
                part.rb.AddTorque(-ac.transform.right * mag * 0.35f, ForceMode.Acceleration);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "FixedUpdate")]
    internal static class Patch_Prop_CyclicAssist
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            PropCyclicAssistService.ApplyCyclic(__instance);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "PropAnimate")]
    internal static class Patch_Prop_CyclicVisual
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            PropCyclicAssistService.ScaleVisualSpin(__instance);
        }
    }
}
