using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// CallingSupport-style exoatmospheric hold: above 20 km freeze fins / attitude.
    /// Steering, aero, RCS, seeker aim, and fin servos must not turn the missile
    /// until it falls back into air.
    /// </summary>
    internal static class HighAltMissileFreeze
    {
        internal const float FreezeAboveM = 20000f;

        internal static bool IsFrozen(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                // TGM-85 (especially S) owns high-alt loft/cruise — do not freeze fins.
                if (Plugin.IsKh85Missile(missile))
                    return false;
            }
            catch { }
            try
            {
                // After F9 20 km handoff, never slam-down again (causes vertical loops).
                if (F9DropMark.IsReleased(missile))
                    return false;
            }
            catch { }
            try
            {
                // Ground-launched Piledriver must loft through 20 km.
                if (Plugin.IsBallisticMissile(missile) && !F9DropMark.Has(missile))
                    return false;
            }
            catch { }
            try
            {
                Transform t = missile.transform;
                if (t == null)
                    return false;
                return t.position.y >= FreezeAboveM;
            }
            catch
            {
                return false;
            }
        }

        internal static bool TryFreeze(Missile missile)
        {
            if (!IsFrozen(missile))
            {
                ReleaseRotation(missile);
                return false;
            }
            HoldAttitude(missile);
            return true;
        }

        private static void HoldAttitude(Missile missile)
        {
            Rigidbody rb = null;
            try { rb = missile.rb; }
            catch { }
            if (rb == null)
                return;
            try
            {
                rb.angularVelocity = Vector3.zero;
                rb.constraints = rb.constraints | RigidbodyConstraints.FreezeRotation;
                Quaternion down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                missile.transform.rotation = down;
                rb.rotation = down;
                float spd = rb.velocity.magnitude;
                if (spd < 50f)
                    spd = 50f;
                rb.velocity = Vector3.down * spd;
            }
            catch { }
        }

        private static void ReleaseRotation(Missile missile)
        {
            Rigidbody rb = null;
            try { rb = missile.rb; }
            catch { }
            if (rb == null)
                return;
            try
            {
                RigidbodyConstraints c = rb.constraints;
                if ((c & RigidbodyConstraints.FreezeRotation) == 0)
                    return;
                rb.constraints = c & ~RigidbodyConstraints.FreezeRotation;
                rb.velocity = StrategicArsenalMathService.CapSpeed(
                    rb.velocity, StrategicArsenalMathService.TerminalTurnSpeedMs);
            }
            catch { }
        }

        internal static Missile MissileFromBehaviour(MonoBehaviour mb)
        {
            if (mb == null)
                return null;
            try
            {
                return mb.GetComponentInParent<Missile>();
            }
            catch
            {
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(Missile), "Steering")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Missile_HighAltFreezeSteering
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            return !HighAltMissileFreeze.TryFreeze(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "ApplyAero")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Missile_HighAltFreezeAero
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            return !HighAltMissileFreeze.IsFrozen(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "SetAimpoint")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Missile_HighAltFreezeAim
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            return !HighAltMissileFreeze.TryFreeze(__instance);
        }
    }

    [HarmonyPatch(typeof(ControlSurfacePhysics), "FixedUpdate")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Csp_HighAltFreezeFins
    {
        [HarmonyPrefix]
        private static bool Prefix(ControlSurfacePhysics __instance)
        {
            Missile m = HighAltMissileFreeze.MissileFromBehaviour(__instance);
            return !HighAltMissileFreeze.TryFreeze(m);
        }
    }

    [HarmonyPatch(typeof(MissileSeeker), "Seek")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Seeker_HighAltFreezeSeek
    {
        [HarmonyPrefix]
        private static bool Prefix(MissileSeeker __instance)
        {
            if (__instance == null)
                return true;
            Missile m = HighAltMissileFreeze.MissileFromBehaviour(__instance);
            if (m == null)
            {
                try { m = __instance.GetComponent<Missile>(); }
                catch { }
            }
            if (m != null)
            {
                try
                {
                    if (m.GetComponent<BallisticMissileGuidance>() != null)
                        return true;
                }
                catch { }
            }
            return !HighAltMissileFreeze.IsFrozen(m);
        }
    }

    [HarmonyPatch(typeof(BallisticMissileGuidance), "Seek")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Bmg_HighAltFreezeSeek
    {
        [HarmonyPrefix]
        private static bool Prefix(BallisticMissileGuidance __instance)
        {
            return true;
        }
    }

    [HarmonyPatch]
    internal static class Patch_BmgRcs_HighAltCorrect
    {
        private static MethodBase TargetMethod()
        {
            try
            {
                Type[] nested = typeof(BallisticMissileGuidance).GetNestedTypes(
                    BindingFlags.Public | BindingFlags.NonPublic);
                if (nested == null)
                    return null;
                for (int i = 0; i < nested.Length; i++)
                {
                    Type t = nested[i];
                    if (t == null || t.Name.IndexOf("RCS") < 0)
                        continue;
                    MethodInfo m = AccessTools.Method(t, "CorrectTrajectory");
                    if (m != null)
                        return m;
                }
            }
            catch { }
            return null;
        }

        [HarmonyPrefix]
        private static bool Prefix(Rigidbody rb)
        {
            if (rb == null)
                return true;
            try
            {
                Missile m = rb.GetComponent<Missile>();
                if (m == null)
                    m = rb.GetComponentInParent<Missile>();
                if (F9DropMark.IsReleased(m))
                    return true;
                if (Plugin.IsBallisticMissile(m) && !F9DropMark.Has(m))
                    return true;
                if (rb.position.y >= HighAltMissileFreeze.FreezeAboveM)
                {
                    rb.angularVelocity = Vector3.zero;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
