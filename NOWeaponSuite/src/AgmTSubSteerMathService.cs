using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// GS25 (ACM-119 / ACNM-118 sub) anti-loop: vanilla optical Seek + unlimited
    /// gLimit + synthetic thrust commanded 90° off velocity → in-place horizontal
    /// or vertical circles. Clamp aim to a forward cone, cap G, coast after eject.
    /// </summary>
    internal static class AgmTSubSteerMathService
    {
        internal const float CoastSec = 1.55f;
        internal const float MinSteerSpeedMps = 50f;
        internal const float MaxG = 24f;
        internal const float MaxTurnRate = 28f;
        internal const float TerminalM = 900f;
        internal const float HuntWideM = 1400f;
        internal const float HuntMaxAspectDeg = 85f;
        internal const float MidcourseOffDeg = 55f;
        internal const float SlowOffDeg = 40f;
        internal const float TerminalOffDeg = 70f;
        internal const float MidcourseDiveDeg = 40f;
        internal const float MidcourseClimbDeg = 35f;
        internal const float TerminalDiveDeg = 62f;
        internal const float TerminalClimbDeg = 42f;

        private static readonly FieldInfo GLimitField = AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo TorqueField = AccessTools.Field(typeof(Missile), "torque");
        private static readonly FieldInfo TimeFuseField = AccessTools.Field(typeof(OpticalSeeker), "timeFuse");
        private static readonly FieldInfo SelfDestructSpeedField =
            AccessTools.Field(typeof(OpticalSeeker), "selfDestructAtSpeed");

        internal const float LifetimeFuseSec = 180f;

        internal static void ApplyLimits(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (GLimitField != null)
                    GLimitField.SetValue(missile, MaxG);
            }
            catch { }

            try
            {
                float torque = 12f;
                if (TorqueField != null)
                {
                    try { torque = (float)TorqueField.GetValue(missile); }
                    catch { }
                }
                if (torque < 8f)
                    torque = 12f;
                missile.SetTorque(torque, MaxTurnRate);
            }
            catch { }

            RelaxSeekerLifetime(missile);
        }

        /// <summary>
        /// Vanilla GS25 is a short-lived cluster bomblet (timeFuse + SlowChecks after
        /// EngineOn=false). ACM GS25 is a powered cruise sub — stretch fuse, kill SD speed.
        /// </summary>
        internal static void RelaxSeekerLifetime(Missile missile)
        {
            if (missile == null)
                return;
            OpticalSeeker opt = null;
            try { opt = missile.GetComponent<OpticalSeeker>(); }
            catch { }
            if (opt == null)
            {
                try { opt = missile.GetComponentInChildren<OpticalSeeker>(true); }
                catch { }
            }
            if (opt == null)
                return;
            try
            {
                if (TimeFuseField != null)
                    TimeFuseField.SetValue(opt, LifetimeFuseSec);
            }
            catch { }
            try
            {
                if (SelfDestructSpeedField != null)
                    SelfDestructSpeedField.SetValue(opt, 0f);
            }
            catch { }
        }

        internal static bool IsCoasting(Missile missile)
        {
            if (missile == null || !AgmTWeapon.IsGs25Submunition(missile))
                return false;
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }
            try
            {
                AgmTSubBrain b = missile.GetComponent<AgmTSubBrain>();
                if (b != null)
                    age = b.AgeSeconds;
            }
            catch { }
            return age < CoastSec;
        }

        internal static Vector3 ThrustDir(Vector3 forward, Vector3 velocity)
        {
            if (velocity.sqrMagnitude < 400f)
                return forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;
            Vector3 velDir = velocity.normalized;
            if (forward.sqrMagnitude < 0.01f)
                return velDir;
            return Vector3.Slerp(forward.normalized, velDir, 0.32f).normalized;
        }

        internal static bool AcceptHuntTarget(
            Vector3 forward,
            Vector3 velocity,
            Vector3 toTarget,
            float distM,
            float ageSec)
        {
            if (ageSec < CoastSec)
                return false;
            if (toTarget.sqrMagnitude < 1f)
                return false;
            if (distM < HuntWideM)
                return true;
            Vector3 dir = velocity.sqrMagnitude > 25f ? velocity.normalized : forward;
            if (dir.sqrMagnitude < 0.01f)
                return true;
            return Vector3.Angle(dir, toTarget) <= HuntMaxAspectDeg;
        }

        internal static Vector3 LeadAimPoint(
            Vector3 missilePos,
            Vector3 missileVel,
            Vector3 targetPos,
            Vector3 targetVel)
        {
            Vector3 fwd = missileVel.sqrMagnitude > 1f ? missileVel.normalized : Vector3.forward;
            float speed = missileVel.magnitude;
            if (speed < 1f)
                speed = 1f;
            return MultiModeGuideMathService.LeadPosition(
                missilePos, fwd, speed, targetPos, targetVel);
        }

        internal static Vector3 ClampAim(
            Vector3 missilePos,
            Vector3 velocity,
            Vector3 forward,
            float speedMps,
            Vector3 aim,
            float distToTarget)
        {
            Vector3 refDir = velocity.sqrMagnitude > 25f ? velocity.normalized : forward;
            if (refDir.sqrMagnitude < 0.01f)
                refDir = Vector3.forward;
            refDir.Normalize();

            if (speedMps < MinSteerSpeedMps)
            {
                float lookSlow = Mathf.Max(450f, speedMps * 3.2f);
                return missilePos + refDir * lookSlow;
            }

            Vector3 toAim = aim - missilePos;
            if (toAim.sqrMagnitude < 1f)
                return missilePos + refDir * 500f;

            Vector3 want = toAim.normalized;
            bool terminal = distToTarget > 1f && distToTarget < TerminalM;
            float maxOff = terminal
                ? TerminalOffDeg
                : (speedMps < 160f ? SlowOffDeg : MidcourseOffDeg);
            float aspect = Vector3.Angle(refDir, want);
            if (aspect > maxOff)
                want = Vector3.RotateTowards(refDir, want, maxOff * Mathf.Deg2Rad, 0f);

            float maxDive = terminal ? TerminalDiveDeg : MidcourseDiveDeg;
            float maxClimb = terminal ? TerminalClimbDeg : MidcourseClimbDeg;
            float pitch = Mathf.Asin(Mathf.Clamp(want.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (pitch < -maxDive || pitch > maxClimb)
            {
                float clampPitch = Mathf.Clamp(pitch, -maxDive, maxClimb) * Mathf.Deg2Rad;
                Vector3 horiz = new Vector3(want.x, 0f, want.z);
                if (horiz.sqrMagnitude < 0.0001f)
                    horiz = new Vector3(refDir.x, 0f, refDir.z);
                if (horiz.sqrMagnitude < 0.0001f)
                    horiz = Vector3.forward;
                horiz.Normalize();
                want = (horiz * Mathf.Cos(clampPitch) + Vector3.up * Mathf.Sin(clampPitch)).normalized;
            }

            float look = Mathf.Clamp(speedMps * 2.2f, 500f, 3500f);
            return missilePos + want * look;
        }

        internal static float DistToMissileTarget(Missile missile)
        {
            if (missile == null)
                return -1f;
            try
            {
                PersistentID tid = missile.targetID;
                Unit u;
                if (tid.IsValid && UnitRegistry.TryGetUnit(tid, out u) && u != null)
                    return Vector3.Distance(missile.transform.position, u.transform.position);
            }
            catch { }
            return -1f;
        }
    }

    [HarmonyPatch(typeof(Missile), "SetAimpoint")]
    internal static class Patch_Missile_SetAimpoint_Gs25
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, ref GlobalPosition aimPoint)
        {
            if (__instance == null || !AgmTWeapon.IsGs25Submunition(__instance))
                return;
            if (AgmTSubSteerMathService.IsCoasting(__instance))
            {
                Vector3 pos = __instance.transform.position;
                Vector3 vel = Vector3.zero;
                try
                {
                    if (__instance.rb != null)
                        vel = __instance.rb.velocity;
                }
                catch { }
                Vector3 dir = AgmTSubSteerMathService.ThrustDir(__instance.transform.forward, vel);
                aimPoint = (pos + dir * 800f).ToGlobalPosition();
                return;
            }

            Vector3 world;
            try { world = aimPoint.ToLocalPosition(); }
            catch { return; }

            Vector3 mpos = __instance.transform.position;
            Vector3 vel2 = Vector3.zero;
            try
            {
                if (__instance.rb != null)
                    vel2 = __instance.rb.velocity;
            }
            catch { }
            float speed = vel2.magnitude;
            try
            {
                if (__instance.speed > speed)
                    speed = __instance.speed;
            }
            catch { }
            Vector3 clamped = AgmTSubSteerMathService.ClampAim(
                mpos,
                vel2,
                __instance.transform.forward,
                speed,
                world,
                AgmTSubSteerMathService.DistToMissileTarget(__instance));
            aimPoint = clamped.ToGlobalPosition();
        }
    }

    [HarmonyPatch(typeof(MissileSeeker), "Seek")]
    internal static class Patch_Seeker_Seek_Gs25Coast
    {
        [HarmonyPrefix]
        private static bool Prefix(MissileSeeker __instance)
        {
            if (__instance == null)
                return true;
            Missile m = null;
            try { m = __instance.GetComponent<Missile>(); }
            catch { }
            if (m == null)
            {
                try { m = __instance.GetComponentInParent<Missile>(); }
                catch { }
            }
            if (AgmTSubSteerMathService.IsCoasting(m))
                return false;
            if (!AgmTWeapon.IsPoweredGs25Sub(m))
                return true;
            // After coast, optical Seek may run only with a lock (no world-origin dive).
            try
            {
                PersistentID tid = m.targetID;
                if (tid.IsValid)
                    return true;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), "TimeFuse")]
    internal static class Patch_OpticalSeeker_TimeFuse_Gs25
    {
        [HarmonyPrefix]
        private static bool Prefix(OpticalSeeker __instance)
        {
            if (AgmTWeapon.AcmGs25SpawnDepth > 0)
                return false;
            if (__instance == null)
                return true;
            Missile m = null;
            try { m = __instance.GetComponent<Missile>(); }
            catch { }
            if (m == null)
            {
                try { m = __instance.GetComponentInParent<Missile>(); }
                catch { }
            }
            if (AgmTWeapon.IsPoweredGs25Sub(m))
                return false;
            if (m != null && Plugin.ShouldSuppressSeekerSelfDestruct(m))
                return false;
            return true;
        }
    }
}
