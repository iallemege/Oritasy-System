using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield AI combat math (0.0.9.64): targeting validity, map clamp, scores.
    /// Engagement gates live in AiCombatEngagementService (0.0.9.79).
    /// AiCombatBrain owns FSM / firing / ACM execution.
    /// </summary>
    internal static class AiCombatMathService
    {
        internal const float MaxTargetRangeM = 45000f;
        internal const float MinTargetRangeM = 15f;
        internal const float SoftMapFrac = 0.78f;
        internal const float HardMapFrac = 0.90f;

        internal static int ClampLevel(int level, int min, int max)
        {
            if (level < min) return min;
            if (level > max) return max;
            return level;
        }

        internal static bool ValidTarget(Unit tgt, Aircraft self)
        {
            if (tgt == null || self == null)
                return false;
            try
            {
                if (tgt.disabled)
                    return false;
                if (object.ReferenceEquals(tgt, self))
                    return false;
                if (tgt.NetworkHQ != null && self.NetworkHQ != null
                    && object.ReferenceEquals(tgt.NetworkHQ, self.NetworkHQ))
                    return false;
                float d = Vector3.Distance(self.transform.position, tgt.transform.position);
                if (d > MaxTargetRangeM || d < MinTargetRangeM)
                    return false;
                return true;
            }
            catch { return false; }
        }

        internal static float ScoreAirTarget(Aircraft other, Aircraft self, Aircraft localPrefer, bool preferPlayer)
        {
            if (other == null || self == null)
                return -1f;
            float dist = Vector3.Distance(self.transform.position, other.transform.position);
            float score = 40000f - dist;
            if (preferPlayer && localPrefer != null && object.ReferenceEquals(other, localPrefer))
                score += 12000f;
            try
            {
                if (other.Player != null)
                    score += 4000f;
            }
            catch { }
            return score;
        }

        internal static float ScoreGroundTarget(Unit u, Aircraft self)
        {
            if (u == null || self == null)
                return -1f;
            float dist = Vector3.Distance(self.transform.position, u.transform.position);
            if (dist > 22000f)
                return -1f;
            float score = 22000f - dist;
            try
            {
                if (u is Ship)
                    score += 2500f;
            }
            catch { }
            return score;
        }

        internal enum WeaponRole
        {
            None = 0,
            Gun = 1,
            Air = 2,
            Ground = 3,
            Dual = 4
        }

        internal static string WeaponNameBlob(WeaponInfo w)
        {
            if (w == null)
                return string.Empty;
            string a = w.weaponName != null ? w.weaponName : string.Empty;
            string b = w.shortName != null ? w.shortName : string.Empty;
            return a + " " + b;
        }

        internal static bool NameContains(string blob, string token)
        {
            if (string.IsNullOrEmpty(blob) || string.IsNullOrEmpty(token))
                return false;
            return blob.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// AAM vs AGM by name / seeker envelope, not just effectiveness.
        /// Vanilla AGM-48 has antiAir=0.5 but maxSpeed=50 (ground / hovering helo only).
        /// Dual-role IAL cluster (ACM-119) is Dual. Guns are Gun.
        /// </summary>
        internal static WeaponRole ClassifyWeapon(WeaponInfo w)
        {
            if (w == null)
                return WeaponRole.None;
            if (w.gun || w.energy)
                return WeaponRole.Gun;
            if (w.bomb || w.glideBomb)
                return WeaponRole.Ground;

            string n = WeaponNameBlob(w);
            if (NameContains(n, "AAM-2CV"))
                return WeaponRole.Air;
            if (NameContains(n, "ACM-119") || NameContains(n, "ACNM") || NameContains(n, "GS25"))
                return WeaponRole.Dual;
            if (NameContains(n, "AAM") || NameContains(n, "IRM") || NameContains(n, "MMR")
                || NameContains(n, "Scythe") || NameContains(n, "Scimitar")
                || NameContains(n, "ARH"))
                return WeaponRole.Air;
            if (NameContains(n, "AGM") || NameContains(n, "TGM") || NameContains(n, "ATP")
                || NameContains(n, "AShM") || NameContains(n, "ARM")
                || NameContains(n, "Kh-85") || NameContains(n, "Kh85")
                || NameContains(n, "Piledriver") || NameContains(n, "rocket"))
                return WeaponRole.Ground;

            float maxSpd = 0f;
            float aa = 0f;
            float asurf = 0f;
            try
            {
                maxSpd = w.targetRequirements.maxSpeed;
                aa = w.effectiveness.antiAir;
                asurf = w.effectiveness.antiSurface;
            }
            catch { }

            // AGM-family envelope: cannot chase jets even if antiAir is padded.
            if (maxSpd > 1f && maxSpd <= 80f)
                return WeaponRole.Ground;
            if (asurf < 0.12f && aa >= 0.5f)
                return WeaponRole.Air;
            if (aa < 0.12f && asurf >= 0.35f)
                return WeaponRole.Ground;
            if (aa >= 0.35f && asurf >= 0.35f)
                return WeaponRole.Dual;
            if (asurf > aa + 0.08f)
                return WeaponRole.Ground;
            if (w.missile || w.laserGuided || w.boresight)
                return aa >= asurf ? WeaponRole.Air : WeaponRole.Ground;
            return WeaponRole.None;
        }

        internal static bool WeaponIsA2G(WeaponInfo w)
        {
            WeaponRole r = ClassifyWeapon(w);
            return r == WeaponRole.Ground || r == WeaponRole.Dual;
        }

        internal static bool WeaponIsA2A(WeaponInfo w)
        {
            WeaponRole r = ClassifyWeapon(w);
            if (r == WeaponRole.Gun)
                return true;
            return r == WeaponRole.Air || r == WeaponRole.Dual;
        }

        internal static bool WeaponFitsTarget(WeaponInfo w, Unit tgt)
        {
            if (w == null || tgt == null)
                return false;
            WeaponRole role = ClassifyWeapon(w);
            if (role == WeaponRole.Gun)
                return true;
            if (role == WeaponRole.None)
                return false;

            bool airTgt = tgt is Aircraft;
            if (airTgt)
            {
                if (role == WeaponRole.Air || role == WeaponRole.Dual)
                    return true;
                if (role != WeaponRole.Ground)
                    return false;
                float maxSpd = 0f;
                try { maxSpd = w.targetRequirements.maxSpeed; }
                catch { }
                if (maxSpd <= 1f || maxSpd >= 120f)
                    return false;
                float ts = 0f;
                try
                {
                    if (tgt.rb != null)
                        ts = tgt.rb.velocity.magnitude;
                }
                catch { }
                return ts <= maxSpd * 1.15f;
            }

            return role == WeaponRole.Ground || role == WeaponRole.Dual;
        }

        internal static void ScanLoadout(Aircraft ac, out bool a2a, out bool a2g, out bool gun)
        {
            a2a = false;
            a2g = false;
            gun = false;
            if (ac == null || ac.weaponStations == null)
                return;
            try
            {
                for (int i = 0; i < ac.weaponStations.Count; i++)
                {
                    WeaponStation st = ac.weaponStations[i];
                    if (st == null || st.Ammo <= 0 || st.WeaponInfo == null)
                        continue;
                    WeaponRole role = ClassifyWeapon(st.WeaponInfo);
                    if (role == WeaponRole.Gun)
                        gun = true;
                    else if (role == WeaponRole.Air)
                        a2a = true;
                    else if (role == WeaponRole.Ground)
                        a2g = true;
                    else if (role == WeaponRole.Dual)
                    {
                        a2a = true;
                        a2g = true;
                    }
                }
            }
            catch { }
        }

        internal static float SafeRalt(Aircraft ac)
        {
            try { return ac.radarAlt; }
            catch { return 300f; }
        }

        internal static Vector3 TargetVel(Unit tgt)
        {
            try
            {
                if (tgt != null && tgt.rb != null)
                    return tgt.rb.velocity;
            }
            catch { }
            return Vector3.zero;
        }

        internal static Vector3 OwnVel(Aircraft ac)
        {
            try
            {
                if (ac != null && ac.rb != null)
                    return ac.rb.velocity;
            }
            catch { }
            return Vector3.zero;
        }

        /// <summary>
        /// Collision-course intercept. Caps lead so the point tracks every physics tick
        /// instead of sitting 10–20 s ahead (vanilla / old LeadTime). Aft intercepts
        /// collapse to pure/short pursuit so the AI does not chase a point behind itself.
        /// </summary>
        internal static Vector3 ComputeAirIntercept(
            Vector3 myPos,
            Vector3 myVel,
            float mySpeed,
            Vector3 myFwd,
            Vector3 tgtPos,
            Vector3 tgtVel,
            bool dogfight,
            bool guns,
            float skill,
            float leadGain)
        {
            Vector3 rel = tgtPos - myPos;
            float dist = rel.magnitude;
            if (dist < 8f)
                return tgtPos;

            float spd = Mathf.Max(50f, mySpeed);
            skill = Mathf.Clamp01(skill);
            float gain = Mathf.Clamp(0.45f + leadGain * 0.5f, 0.65f, 1f);

            float maxT;
            if (guns)
                maxT = Mathf.Clamp(dist / 900f, 0.12f, 1.1f);
            else if (dogfight)
                maxT = Mathf.Lerp(1.7f, 0.95f, skill);
            else
                maxT = Mathf.Lerp(4.2f, 2.8f, skill);

            float t = SolveInterceptTime(rel, tgtVel, spd);
            if (t < 0.04f)
            {
                Vector3 los = rel / dist;
                float closing = Vector3.Dot(los, myVel - tgtVel);
                t = dist / Mathf.Max(closing, spd * 0.22f);
            }
            t = Mathf.Clamp(t * gain, 0.05f, maxT);

            Vector3 intercept = tgtPos + tgtVel * t;
            Vector3 toInt = intercept - myPos;
            if (toInt.sqrMagnitude < 1f)
                return tgtPos;

            float fwdDot = Vector3.Dot(myFwd.sqrMagnitude > 0.01f ? myFwd.normalized : Vector3.forward,
                toInt.normalized);

            // Behind the aircraft: do not fly to a stale collision point.
            if (fwdDot < 0.06f)
            {
                if (dogfight || dist < 7000f)
                {
                    Vector3 shortLead = tgtPos + tgtVel * Mathf.Min(0.28f, t);
                    Vector3 toShort = shortLead - myPos;
                    if (toShort.sqrMagnitude > 1f
                        && Vector3.Dot(myFwd, toShort) > 0f)
                        return shortLead;
                    return tgtPos;
                }
            }
            return intercept;
        }

        /// <summary>Smallest t&gt;0 for |rel + vt t| = mySpeed t. Negative = none.</summary>
        internal static float SolveInterceptTime(Vector3 rel, Vector3 tgtVel, float mySpeed)
        {
            float a = Vector3.Dot(tgtVel, tgtVel) - mySpeed * mySpeed;
            float b = 2f * Vector3.Dot(rel, tgtVel);
            float c = Vector3.Dot(rel, rel);
            if (Mathf.Abs(a) < 0.05f)
            {
                if (Mathf.Abs(b) < 0.05f)
                    return -1f;
                float tLin = -c / b;
                return tLin > 0.05f ? tLin : -1f;
            }
            float disc = b * b - 4f * a * c;
            if (disc < 0f)
                return -1f;
            float s = Mathf.Sqrt(disc);
            float inv = 1f / (2f * a);
            float t1 = (-b - s) * inv;
            float t2 = (-b + s) * inv;
            float t = 1e9f;
            if (t1 > 0.05f && t1 < t)
                t = t1;
            if (t2 > 0.05f && t2 < t)
                t = t2;
            if (t > 90f)
                return -1f;
            return t;
        }

        /// <summary>Fast follow (ace ~45 ms). Snap on first sample or 2 km jumps.</summary>
        internal static Vector3 FollowIntercept(
            Vector3 prev,
            bool hasPrev,
            Vector3 raw,
            float dt,
            float skill)
        {
            if (!hasPrev)
                return raw;
            if ((raw - prev).sqrMagnitude > 4e6f)
                return raw;
            dt = Mathf.Clamp(dt, 0.008f, 0.05f);
            // Slow enough that a ~20 m lateral intercept twitch cannot flip AutoAim bank.
            float tau = Mathf.Lerp(0.32f, 0.16f, Mathf.Clamp01(skill));
            float k = 1f - Mathf.Exp(-dt / Mathf.Max(0.05f, tau));
            return Vector3.Lerp(prev, raw, k);
        }

        internal static Vector3 SoftMapClampAim(Aircraft ac, Vector3 aim, float mapHalfExtent)
        {
            if (ac == null || mapHalfExtent <= 500f)
                return aim;

            float soft = mapHalfExtent * SoftMapFrac;
            float hard = mapHalfExtent * HardMapFrac;
            Vector3 pos = ac.transform.position;
            float r = Mathf.Sqrt(pos.x * pos.x + pos.z * pos.z);

            if (r >= soft)
            {
                Vector3 inward = new Vector3(-pos.x, 0f, -pos.z);
                if (inward.sqrMagnitude < 0.01f)
                    inward = Vector3.forward;
                inward.Normalize();
                float pull = r >= hard ? 7000f : 4500f;
                aim = pos + inward * pull;
                aim.y = pos.y + (r >= hard ? 150f : 80f);
                return aim;
            }

            float ar = Mathf.Sqrt(aim.x * aim.x + aim.z * aim.z);
            if (ar > soft && ar > 1f)
            {
                float s = soft / ar;
                aim.x *= s;
                aim.z *= s;
            }
            return aim;
        }

        internal static void ApplySkillFloor(Aircraft ac, float skill, float bravery)
        {
            if (ac == null)
                return;
            try
            {
                ac.skill = Mathf.Clamp01(Mathf.Max(ac.skill, skill));
                ac.bravery = Mathf.Clamp01(Mathf.Max(ac.bravery, bravery));
            }
            catch { }
        }

        internal static void ApplySkillBonus(Aircraft ac, float skillAdd, float bravAdd, float minSkill)
        {
            if (ac == null)
                return;
            skillAdd = Mathf.Clamp(skillAdd, 0f, 0.45f);
            bravAdd = Mathf.Clamp(bravAdd, 0f, 0.45f);
            minSkill = Mathf.Clamp01(minSkill);
            try
            {
                ac.skill = Mathf.Clamp01(Mathf.Max(ac.skill + skillAdd, minSkill));
                ac.bravery = Mathf.Clamp01(ac.bravery + bravAdd);
            }
            catch { }
        }

        internal static float ScaleMissileReactTime(float t, float scale)
        {
            scale = Mathf.Clamp(scale, 0.2f, 1f);
            if (scale >= 0.999f)
                return t;
            if (t > 0.01f)
                return Mathf.Max(0.02f, t * scale);
            if (t < -0.05f)
                return t * scale;
            return t;
        }
    }
}
