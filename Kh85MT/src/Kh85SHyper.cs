using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85S Seaker — hypersonic strike profile:
    /// high thrust/fuel, boost loft, high-speed cruise, steep terminal dive.
    /// </summary>
    internal static class Kh85SHyper
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ThrustMul;
        internal static ConfigEntry<float> FuelMul;
        internal static ConfigEntry<float> CruiseAltitude;
        internal static ConfigEntry<float> TerminalRange;
        internal static ConfigEntry<float> DiveRange;

        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("HyperS", "Enabled", true,
                "TGM-85S Seaker: hypersonic motors + loft/cruise/dive profile.");
            // Keep modest defaults — extreme muls + Mirage glitches made S look like it vanished.
            // Values above ~2.5 are clamped at runtime (see ExtraThrustMul).
            ThrustMul = config.Bind("HyperS", "ThrustMul", 2.2f,
                "Motor thrust multiplier vs AGM-68 donor on S spawn (instance only). Clamped to 2.5.");
            FuelMul = config.Bind("HyperS", "FuelMul", 1.8f,
                "Motor fuelMass multiplier vs AGM-68 donor on S spawn (instance only). Clamped to 2.2.");
            CruiseAltitude = config.Bind("HyperS", "CruiseAltitude", 22000f,
                "Hypersonic cruise altitude (m ASL) after boost.");
            TerminalRange = config.Bind("HyperS", "TerminalRange", 12000f,
                "Begin descent blend inside this range (m).");
            DiveRange = config.Bind("HyperS", "DiveRange", 5000f,
                "Commit to steep dive inside this range (m).");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsSVariant(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag != null)
                return tag.Letter == "S";
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "S";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsSVariant(missile))
                return;
            if (missile.GetComponent<Kh85SHyperBrain>() != null)
                return;
            try { missile.gameObject.AddComponent<Kh85SHyperBrain>(); }
            catch { }
        }

        internal static float ExtraThrustMul(Missile missile)
        {
            if (!IsEnabled() || !IsSVariant(missile))
                return 1f;
            float m = ThrustMul != null ? ThrustMul.Value : 2.2f;
            if (m < 0.1f)
                m = 1f;
            // Hard cap — 4x+ thrust historically made S despawn / Mirage-glitch on fire.
            if (m > 2.5f)
                m = 2.5f;
            return m;
        }

        internal static float ExtraFuelMul(Missile missile)
        {
            if (!IsEnabled() || !IsSVariant(missile))
                return 1f;
            float m = FuelMul != null ? FuelMul.Value : 1.8f;
            if (m < 0.1f)
                m = 1f;
            if (m > 2.2f)
                m = 2.2f;
            return m;
        }

        internal static void ApplyProfile(Missile missile)
        {
            if (missile == null || !IsEnabled())
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(missile))
                return;
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag == null || tag.Letter != "S")
                return;
            if (Kh85MclosGate.ManualActive)
                return;
            if (missile.disabled)
                return;
            if (Kh85Weapon.ShouldDeferAim(missile))
                return;

            Unit target = null;
            try
            {
                if (TargetField != null)
                    target = TargetField.GetValue(missile) as Unit;
            }
            catch { }
            if (target == null)
                target = Kh85Weapon.ResolveMissileDesignatedTarget(missile);
            if (target == null)
                return;

            Vector3 mpos = missile.transform.position;
            Vector3 tpos = target.transform.position;
            Vector3 tvel = Vector3.zero;
            try
            {
                if (target.rb != null)
                    tvel = target.rb.velocity;
            }
            catch { }

            float speed = 400f;
            Vector3 vel = Vector3.zero;
            try
            {
                if (missile.rb != null)
                {
                    vel = missile.rb.velocity;
                    speed = Mathf.Max(vel.magnitude, 120f);
                }
            }
            catch { }

            float dist = Vector3.Distance(mpos, tpos);
            float cruise = CruiseAltitude != null ? CruiseAltitude.Value : 22000f;
            float terminal = TerminalRange != null ? TerminalRange.Value : 12000f;
            float dive = DiveRange != null ? DiveRange.Value : 5000f;
            if (dive > terminal)
                dive = terminal * 0.45f;

            Vector3 aim = Kh85Weapon.EnergyLead(mpos, vel, speed, tpos, tvel);

            if (dist > terminal)
            {
                // Boost / hypersonic cruise corridor — climb hard then hold.
                float wantY = Mathf.Max(cruise, tpos.y + 8000f);
                if (mpos.y < wantY - 200f)
                {
                    float climb = Mathf.Clamp((wantY - mpos.y) * 0.55f, 400f, 1600f);
                    aim.y = mpos.y + climb;
                }
                else
                    aim.y = wantY;

                Vector3 horiz = tpos - mpos;
                horiz.y = 0f;
                if (horiz.sqrMagnitude > 1f)
                {
                    horiz.Normalize();
                    float look = Mathf.Clamp(speed * 3.2f, 1200f, 6000f);
                    Vector3 xy = mpos + horiz * look;
                    aim.x = Mathf.Lerp(xy.x, aim.x, 0.25f);
                    aim.z = Mathf.Lerp(xy.z, aim.z, 0.25f);
                }
            }
            else if (dist > dive)
            {
                float u = 1f - Mathf.Clamp01((dist - dive) / Mathf.Max(terminal - dive, 1f));
                float high = Mathf.Max(cruise * 0.75f, mpos.y);
                aim.y = Mathf.Lerp(high, tpos.y + 400f, u * u);
            }
            else
            {
                // Terminal dive — keep aim at/above target so DetectCollisions does not
                // treat an underground point as impact and snap-teleport the body.
                aim = tpos + tvel * 0.35f * Mathf.Clamp(dist / Mathf.Max(speed, 80f), 0.12f, 4f);
                aim.y = tpos.y + 12f;
            }

            // Soft angle clamp so we do not stall the airframe at extreme pitch / re-lock.
            if (vel.sqrMagnitude > 1f)
            {
                Vector3 toAim = aim - mpos;
                if (toAim.sqrMagnitude > 0.01f)
                {
                    Vector3 want = toAim.normalized;
                    float ang = Vector3.Angle(vel.normalized, want);
                    float maxDeg = Kh85Weapon.MaxSteerOffBoresightDeg(speed, ang);
                    if (ang > maxDeg)
                        want = Vector3.RotateTowards(vel.normalized, want, maxDeg * Mathf.Deg2Rad, 0f);
                    float look = Mathf.Clamp(speed * 2.8f, 1200f, 7000f);
                    if (look < speed * 0.55f)
                        look = speed * 0.55f;
                    Vector3 clamped = mpos + want * look;
                    if (dist > terminal)
                        clamped.y = aim.y;
                    aim = clamped;
                }
            }

            float dropCap = dist > terminal ? 500f : 900f;
            float minAgl = dist > dive ? 80f : 12f;
            Kh85Weapon.SafeSetAimpoint(missile, aim, tvel, minAgl, dropCap);
        }
    }

    public class Kh85SHyperBrain : MonoBehaviour
    {
        private Missile _missile;
        private float _nextMotorTry;
        private bool _motorFailLogged;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
            Kh85Weapon.EnsureMotors(_missile);
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null)
                return;
            if (Kh85Weapon.MotorsScaled(_missile))
                return;
            if (Time.time < _nextMotorTry)
                return;
            _nextMotorTry = Time.time + 0.35f;
            Kh85Weapon.EnsureMotors(_missile);

            // P2: one-time log if motors never appear after a few seconds.
            if (_motorFailLogged || Plugin.Log == null)
                return;
            try
            {
                if (_missile.timeSinceSpawn < 3f)
                    return;
            }
            catch { return; }
            if (Kh85Weapon.MotorsMissing(_missile) || !Kh85Weapon.MotorsScaled(_missile))
            {
                _motorFailLogged = true;
                Plugin.Log.LogWarning("TGM-85S: motors never found/scaled after 3s — thrust mul skipped.");
            }
        }
    }

    /// <summary>
    /// Prefix before Steering — Postfix left Seek forward-coast in control.
    /// Priority.VeryLow so CI22XE MCLOS (Priority.Last) wins when ManualActive.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Kh85S_Steering
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Prefix(Missile __instance)
        {
            Kh85SHyper.ApplyProfile(__instance);
        }
    }
}
