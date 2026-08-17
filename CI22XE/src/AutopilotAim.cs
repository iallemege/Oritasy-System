using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield AutoAim bridge (plane vs helo). Written for 0.0.9.57.
    /// bankAllowed is a max; 180° on a 1° heading error PIO-rolls the jet.
    /// </summary>
    internal static class AutopilotAim
    {
        internal const float DefaultBank = 180f;
        internal const float DefaultEffort = 0.95f;

        /// <summary>Straight / resupply / AI cruise — enough to correct, not enough to rock.</summary>
        internal const float CruiseBank = 38f;
        internal const float OrbitBank = 52f;
        internal const float HuntBank = 62f;
        internal const float AttackBank = 95f;
        internal const float BreakBank = 78f;
        internal const float EvadeBank = 72f;
        internal const float AcmBank = 180f;

        /// <summary>
        /// AutoAim bearing is atan(lateral/range). A 1 s intercept ~250 m ahead turns
        /// a 20 m sideslip into ~5° that flips sign every tick. Project to this range.
        /// </summary>
        internal const float LookAheadM = 8000f;

        internal static void AutoAim(Autopilot ap, GlobalPosition dest, bool aimVelocity,
            bool ignoreCollisions, bool runwayAlign, float effort, float bankAllowed,
            bool followTerrain, float altitudeHold, Vector3 targetVelocity)
        {
            if (ap == null)
                return;
            AutopilotPlane plane = ap as AutopilotPlane;
            if (plane != null)
            {
                plane.AutoAim(dest, aimVelocity, ignoreCollisions, runwayAlign, effort, bankAllowed,
                    followTerrain, altitudeHold, targetVelocity);
                return;
            }
            Vector3 aimDir = Vector3.forward;
            try
            {
                if (ap.aircraft != null)
                    aimDir = ap.aircraft.transform.forward;
            }
            catch { }
            ap.AutoAim(dest, altitudeHold, aimDir, targetVelocity, followTerrain);
        }

        /// <summary>Level terrain-follow hold toward a far heading point.</summary>
        internal static void HoldHeadingTerrain(Autopilot ap, Aircraft ac, float headingDeg,
            float aglMeters, float effort, float bank)
        {
            if (ap == null || ac == null)
                return;
            Vector3 fwd = Quaternion.Euler(0f, headingDeg, 0f) * Vector3.forward;
            Vector3 aimLocal = ac.transform.position + fwd * 15000f;
            aimLocal.y = ac.transform.position.y;
            AutoAim(ap, aimLocal.ToGlobalPosition(), true, false, false, effort, bank,
                true, aglMeters, Vector3.zero);
        }

        /// <summary>Keep AutoAim's line-of-sight angle stable (direction preserved).</summary>
        internal static Vector3 LookAhead(Vector3 from, Vector3 aim, float lookM)
        {
            if (lookM < 500f)
                lookM = LookAheadM;
            Vector3 d = aim - from;
            float mag = d.magnitude;
            if (mag < 0.5f)
                return from + Vector3.forward * lookM;
            return from + d * (lookM / mag);
        }

        /// <summary>Horizontal ground track, not banked nose (nose-chase rocks left/right).</summary>
        internal static Vector3 GroundTrackAim(Aircraft ac, float distM)
        {
            Vector3 track = Vector3.forward;
            try
            {
                if (ac != null && ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f)
                    track = ac.rb.velocity;
                else if (ac != null)
                    track = ac.transform.forward;
            }
            catch
            {
                if (ac != null)
                    track = ac.transform.forward;
            }
            track.y = 0f;
            if (track.sqrMagnitude < 0.01f)
                track = Vector3.forward;
            track.Normalize();
            Vector3 origin = ac != null ? ac.transform.position : Vector3.zero;
            Vector3 aim = origin + track * Mathf.Max(2000f, distM);
            aim.y = origin.y;
            return aim;
        }
    }
}
