using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// AI missile-evade: BVR 3-9 beam vs radar (kinematic notch — aircraft have no chaff),
    /// IR dump flares. AiCombatBrain owns warning probes and Unity CM calls.
    /// </summary>
    internal static class AiCombatEvadeService
    {
        internal const float BvrMissileDistM = 3800f;
        internal const float BvrImpactSec = 4.8f;
        internal const float KnifeFightM = 3000f;
        internal const float BeamAimM = 3400f;
        internal const float OnBeamDeg = 28f;
        internal const float HeloNoeAgl = 95f;
        internal const float HeloNoeAglCompound = 140f;
        internal const float HeloNoeAimM = 8000f;
        internal const float HeloCruiseAgl = 160f;
        internal const float HeloCruiseAglCompound = 220f;
        internal const float HeloAttackAgl = 140f;
        internal const float HeloAttackAglCompound = 200f;
        internal const float HeloMinAgl = 70f;
        internal const float HeloMinAglCompound = 120f;
        internal const float ClimboutAglM = 420f;
        internal const float ClimboutSlowAglM = 650f;
        internal const float ClimboutSlowSpeed = 85f;
        internal const float ClimboutAimUpM = 700f;
        internal const float ClimboutAimFwdM = 5000f;
        internal const float ClimboutMissileTti = 5.5f;
        internal const float ClimboutMissileDistM = 2200f;

        internal static bool IsClimbout(float ralt, float speed)
        {
            if (ralt < ClimboutAglM)
                return true;
            return ralt < ClimboutSlowAglM && speed < ClimboutSlowSpeed;
        }

        /// <summary>
        /// RWR paint alone is not a missile. Climb-out only breaks for a close inbound.
        /// High-skill jets were beaming away at 75 m AGL after takeoff.
        /// </summary>
        internal static bool AllowEvadeCommit(
            bool climbout,
            bool hasNearMissile,
            float impactSec,
            float missileDist)
        {
            if (!hasNearMissile)
                return false;
            if (!climbout)
                return true;
            return impactSec < ClimboutMissileTti || missileDist < ClimboutMissileDistM;
        }

        internal static bool ShouldIdle(bool warn, bool hasNearMissile, float now, float evadeUntil)
        {
            return !warn && !hasNearMissile && now > evadeUntil;
        }

        internal static bool ThreatActive(bool warn, bool hasNearMissile)
        {
            return hasNearMissile || warn;
        }

        internal static bool NeedsNewEvadeWindow(float now, float evadeUntil)
        {
            return now >= evadeUntil;
        }

        internal static float ScheduleHeloNoeUntil(float now)
        {
            return now + 1.1f;
        }

        internal static bool IsCompoundHelo(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                string key = AircraftIdentity.GetKey(ac);
                if (AircraftIdentity.IsUh90(key))
                    return true;
            }
            catch { }
            return false;
        }

        internal static float ResolveHeloNoeAgl(Aircraft ac)
        {
            return IsCompoundHelo(ac) ? HeloNoeAglCompound : HeloNoeAgl;
        }

        internal static float ResolveHeloCruiseAgl(Aircraft ac)
        {
            return IsCompoundHelo(ac) ? HeloCruiseAglCompound : HeloCruiseAgl;
        }

        internal static float ResolveHeloAttackAgl(Aircraft ac)
        {
            return IsCompoundHelo(ac) ? HeloAttackAglCompound : HeloAttackAgl;
        }

        internal static float ResolveHeloMinAgl(Aircraft ac)
        {
            return IsCompoundHelo(ac) ? HeloMinAglCompound : HeloMinAgl;
        }

        internal static float ResolveHeloEvadeThrottle(Aircraft ac)
        {
            float ralt = 200f;
            try { ralt = ac != null ? ac.radarAlt : 200f; }
            catch { }
            if (ralt < 90f)
                return 0.62f;
            if (IsCompoundHelo(ac) && ralt < 160f)
                return 0.78f;
            return 1f;
        }

        /// <summary>
        /// Helo vs radar: keep speed, drop to the weeds, only cut if flying into the missile.
        /// </summary>
        internal static Vector3 MakeHeloNoeAim(Aircraft ac, Missile threat)
        {
            Vector3 pos = ac != null ? ac.transform.position : Vector3.zero;
            Vector3 track = Vector3.forward;
            try
            {
                if (ac != null && ac.rb != null && ac.rb.velocity.sqrMagnitude > 16f)
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

            if (threat != null)
            {
                Vector3 los = threat.transform.position - pos;
                los.y = 0f;
                if (los.sqrMagnitude > 1f)
                {
                    los.Normalize();
                    if (Vector3.Dot(track, los) > 0.28f)
                    {
                        Vector3 cut = track - los * 0.9f;
                        cut.y = 0f;
                        if (cut.sqrMagnitude > 0.01f)
                            track = cut.normalized;
                        else
                            track = Vector3.Cross(Vector3.up, los).normalized;
                    }
                }
            }

            Vector3 aim = pos + track * HeloNoeAimM;
            aim.y = pos.y;
            return aim;
        }

        /// <summary>IR / SAM / close radar: long enough for dive then zoom.</summary>
        internal static float ScheduleDiveZoomUntil(float now, float skill, float ralt)
        {
            if (ralt < 220f)
                return now + Mathf.Lerp(2.5f, 3.4f, Mathf.Clamp01(skill));
            return now + Mathf.Lerp(4.7f, 6.5f, Mathf.Clamp01(skill));
        }

        internal static bool DiveZoomDivePhase(float started, float until, float now, float ralt)
        {
            if (ralt < 220f)
                return false;
            float span = Mathf.Max(0.45f, until - started);
            return (now - started) < span * 0.48f;
        }

        internal static Vector3 MakeDiveZoomOffset(bool dive, Vector3 fwd, float sideSign, float skill)
        {
            return MakeDiveZoomOffset(dive, fwd, sideSign, skill, false);
        }

        internal static Vector3 MakeDiveZoomOffset(
            bool dive, Vector3 fwd, float sideSign, float skill, bool climbout)
        {
            Vector3 f = fwd;
            f.y = 0f;
            if (f.sqrMagnitude < 0.01f)
                f = Vector3.forward;
            f.Normalize();
            float sign = sideSign >= 0f ? 1f : -1f;
            Vector3 side = Vector3.Cross(Vector3.up, f) * sign;
            if (climbout)
                return f * 2500f + Vector3.up * Mathf.Lerp(420f, 820f, Mathf.Clamp01(skill)) + side * 220f;
            if (dive)
                return f * 3000f + side * 650f - Vector3.up * Mathf.Lerp(280f, 90f, Mathf.Clamp01(skill));
            return f * 1100f + Vector3.up * Mathf.Lerp(1000f, 1700f, Mathf.Clamp01(skill)) + side * 180f;
        }

        internal static float Schedule39Until(float now, float skill)
        {
            return now + Mathf.Lerp(4.2f, 7.4f, Mathf.Clamp01(skill));
        }

        internal static Vector3 MakeEvadeOffset(float errorSeed, float skill, float angleJitter)
        {
            float ang = errorSeed + angleJitter;
            float y = 400f + Mathf.Clamp01(skill) * 600f;
            return new Vector3(Mathf.Sin(ang) * 1800f, y, Mathf.Cos(ang) * 1800f);
        }

        /// <summary>
        /// Aircraft carry flares, not chaff. Never call ChooseCountermeasure — it switches
        /// to an empty chaff station and then Deploy/Pop does nothing.
        /// </summary>
        internal static bool IsIrThreat(Missile m)
        {
            if (m == null)
                return false;
            try
            {
                if (m.GetComponent<IRSeeker>() != null)
                    return true;
                if (m.GetComponentInChildren<IRSeeker>(true) != null)
                    return true;
            }
            catch { }
            return false;
        }

        internal static void DumpFlares(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                if (ac.countermeasureManager != null)
                    ac.countermeasureManager.PopFlares();
                else
                    ac.Countermeasures(true, 0);
            }
            catch { }
        }

        internal static bool IsLongRange39(float missileDist, float impactSec, float targetDist)
        {
            if (targetDist > 0f && targetDist < KnifeFightM && missileDist < 2500f)
                return false;
            return missileDist >= BvrMissileDistM || impactSec >= BvrImpactSec;
        }

        internal static float MissileImpactSec(Aircraft ac, Missile m)
        {
            if (ac == null || m == null)
                return 99f;
            Vector3 myPos = ac.transform.position;
            Vector3 mPos = m.transform.position;
            Vector3 toM = mPos - myPos;
            float dist = toM.magnitude;
            if (dist < 1f)
                return 0.1f;
            Vector3 mVel = Vector3.zero;
            Vector3 myVel = Vector3.zero;
            try
            {
                if (m.rb != null)
                    mVel = m.rb.velocity;
            }
            catch { }
            try
            {
                if (ac.rb != null)
                    myVel = ac.rb.velocity;
            }
            catch { }
            float closing = Vector3.Dot(toM.normalized, mVel - myVel);
            return dist / Mathf.Max(closing, 50f);
        }

        internal static float MissileDistance(Aircraft ac, Missile m)
        {
            if (ac == null || m == null)
                return 99999f;
            try { return Vector3.Distance(ac.transform.position, m.transform.position); }
            catch { return 99999f; }
        }

        internal static Vector3 ThreatNotchPoint(Aircraft ac, Missile m)
        {
            Vector3 fallback = m != null ? m.transform.position : ac.transform.position + ac.transform.forward * 2000f;
            if (m == null)
                return fallback;
            try
            {
                GlobalPosition gp = m.GetEvasionPoint();
                Vector3 p = gp.ToLocalPosition();
                if ((p - ac.transform.position).sqrMagnitude > 100f)
                    return p;
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Horizontal 3-9: fly perpendicular to threat LOS (radar notch / crank).
        /// sideSign +1 = one abeam, -1 = the other; lock it for the evade window.
        /// </summary>
        internal static float Pick39Side(Vector3 myFwd, Vector3 myPos, Vector3 threatPos)
        {
            Vector3 los = threatPos - myPos;
            los.y = 0f;
            if (los.sqrMagnitude < 0.01f)
                return 1f;
            los.Normalize();
            Vector3 beam = Vector3.Cross(Vector3.up, los);
            if (beam.sqrMagnitude < 0.01f)
                beam = Vector3.Cross(Vector3.up, myFwd);
            beam.Normalize();
            Vector3 fwdH = myFwd;
            fwdH.y = 0f;
            if (fwdH.sqrMagnitude < 0.01f)
                fwdH = Vector3.forward;
            return Vector3.Dot(beam, fwdH) >= 0f ? 1f : -1f;
        }

        internal static Vector3 Make39Offset(
            Vector3 myPos,
            Vector3 myFwd,
            Vector3 threatPos,
            float sideSign,
            float skill)
        {
            return Make39Offset(myPos, myFwd, threatPos, sideSign, skill, false);
        }

        internal static Vector3 Make39Offset(
            Vector3 myPos,
            Vector3 myFwd,
            Vector3 threatPos,
            float sideSign,
            float skill,
            bool climbout)
        {
            Vector3 los = threatPos - myPos;
            los.y = 0f;
            if (los.sqrMagnitude < 0.01f)
                los = myFwd;
            los.y = 0f;
            if (los.sqrMagnitude < 0.01f)
                los = Vector3.forward;
            los.Normalize();

            Vector3 beam = Vector3.Cross(Vector3.up, los);
            if (beam.sqrMagnitude < 0.01f)
                beam = Vector3.right;
            beam.Normalize();
            if (sideSign < 0f)
                beam = -beam;

            // Climb-out / high skill: notch while gaining height. Low skill may dip.
            float vert;
            if (climbout)
                vert = Mathf.Lerp(180f, 420f, Mathf.Clamp01(skill));
            else
                vert = -Mathf.Lerp(90f, 25f, Mathf.Clamp01(skill));
            return beam * BeamAimM + Vector3.up * vert;
        }

        internal static bool On39Beam(Aircraft ac, Vector3 offset)
        {
            if (ac == null)
                return false;
            Vector3 beam = offset;
            beam.y = 0f;
            if (beam.sqrMagnitude < 0.01f)
                return false;
            Vector3 vel = Vector3.zero;
            try
            {
                if (ac.rb != null)
                    vel = ac.rb.velocity;
            }
            catch { }
            vel.y = 0f;
            if (vel.sqrMagnitude < 1f)
                vel = ac.transform.forward;
            return Vector3.Angle(beam, vel) < OnBeamDeg;
        }

        internal static bool ShouldDumpCmOn39(float impactSec, bool onBeam, float skill)
        {
            if (impactSec > 8.5f)
                return false;
            if (onBeam)
                return impactSec < Mathf.Lerp(6.5f, 8.5f, Mathf.Clamp01(skill));
            return impactSec < 3.2f;
        }

        internal static bool ShouldDeployCm(float now, float nextCmAt, float cmChance, float roll01)
        {
            return now >= nextCmAt && roll01 <= Mathf.Clamp01(cmChance);
        }

        internal static float ScheduleNextCmAt(float now, float skill)
        {
            return now + Mathf.Lerp(1.4f, 0.35f, Mathf.Clamp01(skill));
        }

        internal static bool IsEvading(float now, float evadeUntil)
        {
            return now <= evadeUntil;
        }

        internal static float EvadeHoldAltitude(float radarAlt)
        {
            return Mathf.Max(200f, radarAlt + 250f);
        }

        internal static float Hold39Altitude(float radarAlt, float skill)
        {
            float climb = Mathf.Lerp(80f, 220f, Mathf.Clamp01(skill));
            return Mathf.Max(350f, radarAlt + climb);
        }
    }
}
