using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield CV 五边 pattern geometry + leg FSM (0.0.9.96).
    /// LandingGuidance owns AutoAim, energy, gear, and PlayerAutopilot state writes.
    /// </summary>
    internal static class LandingCvPatternGateService
    {
        internal struct Geometry
        {
            public float LatOff;
            public float SternDist;
            public float UpwindLen;
            public float PatAgl;
        }

        internal struct LegAim
        {
            public Vector3 AimFlat;
            public float ApAlt;
            public float ApBank;
            public float TgtSpd;
            public bool FollowTerrain;
            public string PhaseTag;
            public bool CaptureWing;
        }

        internal static Geometry ComputeGeometry(float turnR, float dmgApBoost)
        {
            Geometry g;
            g.LatOff = Mathf.Clamp(Mathf.Max(turnR * 0.45f, 1400f), 1200f, 2200f);
            g.SternDist = Mathf.Clamp(Mathf.Max(turnR * 1.1f, 2600f), 2200f, 3800f);
            g.UpwindLen = Mathf.Clamp(Mathf.Max(turnR * 0.55f, 1400f), 1200f, 2400f);
            g.PatAgl = Mathf.Clamp(480f + dmgApBoost * 0.2f, 320f, 700f);
            return g;
        }

        internal static float ResolvePatternSide(float lat, float prevSide)
        {
            float absLat = Mathf.Abs(lat);
            if (absLat > 80f)
                return Mathf.Sign(lat);
            if (prevSide == 0f)
                return 1f;
            return prevSide >= 0f ? 1f : -1f;
        }

        internal static PlayerAutopilot.CvPatternLeg PickEntryLeg(
            float along, float lat, float hdgDot, float sternDist, float latOff)
        {
            float absLat = Mathf.Abs(lat);
            // Already on stern final corridor → FINAL.
            if (along > 900f && along < sternDist && absLat < 280f && hdgDot > 0.7f)
                return PlayerAutopilot.CvPatternLeg.Final;
            // Wide abeam / downwind corridor.
            if (absLat > latOff * 0.55f && along > -900f && along < sternDist + 1200f)
                return along > sternDist * 0.72f
                    ? PlayerAutopilot.CvPatternLeg.Base
                    : PlayerAutopilot.CvPatternLeg.Downwind;
            // Past bow / overshoot.
            if (along < -80f)
                return PlayerAutopilot.CvPatternLeg.Upwind;
            // Default join: break to pattern width then downwind.
            return PlayerAutopilot.CvPatternLeg.Crosswind;
        }

        internal static bool ShouldHardGoAround(float along, float absLat, float dist)
        {
            // Softened: prior absLat/dist gates bounced Final→Upwind mid-correction.
            return along < -200f
                || (along < 40f && absLat > 360f && dist < 900f);
        }

        internal static PlayerAutopilot.CvPatternLeg AdvanceLeg(
            PlayerAutopilot.CvPatternLeg leg,
            float along,
            float absLat,
            float dist,
            float hdgDot,
            float hdgDown,
            float crossDot,
            Geometry g)
        {
            // Hard go-around first, then still allow same-frame geometric advance (matches prior).
            if (ShouldHardGoAround(along, absLat, dist))
                leg = PlayerAutopilot.CvPatternLeg.Upwind;

            if (leg == PlayerAutopilot.CvPatternLeg.Upwind)
            {
                if (along < -g.UpwindLen * 0.4f
                    || (along < -400f && crossDot > 0.2f)
                    || absLat > g.LatOff * 0.55f)
                    return PlayerAutopilot.CvPatternLeg.Crosswind;
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Crosswind)
            {
                if (absLat > g.LatOff * 0.55f && (hdgDown > 0.15f || along > 100f || hdgDot < 0.2f))
                    return PlayerAutopilot.CvPatternLeg.Downwind;
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Downwind)
            {
                if ((along > g.SternDist * 0.55f && absLat > g.LatOff * 0.4f)
                    || (along > g.SternDist * 0.7f && absLat > 600f)
                    || (along > g.SternDist * 0.85f))
                    return PlayerAutopilot.CvPatternLeg.Base;
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Base)
            {
                if ((absLat < 600f && along > 900f && along < g.SternDist + 1200f && hdgDot > 0.25f)
                    || (absLat < 350f && along > 700f && hdgDot > 0.55f))
                    return PlayerAutopilot.CvPatternLeg.Final;
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Final)
            {
                // Stay on Final through mild crab/lat — only leave when clearly lost.
                if (absLat > 800f || (along < 250f && absLat > 380f) || hdgDot < -0.15f)
                    return absLat > g.LatOff * 0.45f
                        ? PlayerAutopilot.CvPatternLeg.Downwind
                        : PlayerAutopilot.CvPatternLeg.Base;
            }
            return leg;
        }

        internal static bool ShouldWatchdogForce(
            PlayerAutopilot.CvPatternLeg leg,
            PlayerAutopilot.CvPatternLeg watchLeg,
            float watchSince,
            float now,
            float sinkAbs)
        {
            if (leg != watchLeg)
                return false;
            if (leg == PlayerAutopilot.CvPatternLeg.Final || leg == PlayerAutopilot.CvPatternLeg.None)
                return false;
            return (now - watchSince) > 14f && sinkAbs < 6f;
        }

        internal static PlayerAutopilot.CvPatternLeg WatchdogNext(PlayerAutopilot.CvPatternLeg leg)
        {
            if (leg == PlayerAutopilot.CvPatternLeg.Upwind)
                return PlayerAutopilot.CvPatternLeg.Crosswind;
            if (leg == PlayerAutopilot.CvPatternLeg.Crosswind)
                return PlayerAutopilot.CvPatternLeg.Downwind;
            if (leg == PlayerAutopilot.CvPatternLeg.Downwind)
                return PlayerAutopilot.CvPatternLeg.Base;
            if (leg == PlayerAutopilot.CvPatternLeg.Base)
                return PlayerAutopilot.CvPatternLeg.Final;
            return leg;
        }

        internal static LegAim ResolveLegAim(
            PlayerAutopilot.CvPatternLeg leg,
            Vector3 touchPos,
            Vector3 dir,
            Vector3 right,
            float side,
            float along,
            float lat,
            float absLat,
            float ralt,
            float hdgDot,
            float landSpd,
            float corner,
            float bankScale,
            float speed,
            Geometry g)
        {
            LegAim a = new LegAim();
            a.FollowTerrain = true;
            a.CaptureWing = false;

            if (leg == PlayerAutopilot.CvPatternLeg.Upwind)
            {
                a.AimFlat = touchPos + dir * g.UpwindLen + right * (side * Mathf.Min(absLat + 200f, 600f));
                a.ApAlt = g.PatAgl;
                a.ApBank = 160f * bankScale;
                a.TgtSpd = Mathf.Min(corner * 0.78f, landSpd * 1.7f);
                a.PhaseTag = "CV UPWIND";
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Crosswind)
            {
                a.AimFlat = touchPos + dir * 150f + right * (side * g.LatOff);
                a.AimFlat -= dir * Mathf.Clamp(along + 400f, 0f, 1200f) * 0.15f;
                a.ApAlt = g.PatAgl * 0.92f;
                a.ApBank = 175f * bankScale;
                a.TgtSpd = Mathf.Min(corner * 0.72f, landSpd * 1.55f);
                a.PhaseTag = "CV XWIND";
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Downwind)
            {
                float leadAlong = Mathf.Clamp(along + 1200f, 800f, g.SternDist + 100f);
                a.AimFlat = touchPos - dir * leadAlong + right * (side * g.LatOff);
                a.ApAlt = Mathf.Min(g.PatAgl * 0.78f, 420f);
                a.ApBank = 145f * bankScale;
                a.TgtSpd = Mathf.Clamp(Mathf.Min(corner * 0.65f, landSpd * 1.42f), landSpd * 1.18f, 125f);
                a.PhaseTag = "CV DOWNWIND";
            }
            else if (leg == PlayerAutopilot.CvPatternLeg.Base)
            {
                float baseLat = Mathf.Clamp(absLat * 0.35f, 200f, g.LatOff * 0.4f);
                a.AimFlat = touchPos - dir * g.SternDist + right * (side * baseLat);
                a.ApAlt = Mathf.Lerp(g.PatAgl * 0.65f, Mathf.Max(180f, g.SternDist * 0.045f), 0.55f);
                a.ApBank = 170f * bankScale;
                a.TgtSpd = Mathf.Clamp(landSpd * 1.28f, landSpd * 1.12f, 105f);
                a.PhaseTag = "CV BASE";
            }
            else
            {
                float finalAlong = Mathf.Clamp(along > 200f ? along * 0.55f : g.SternDist * 0.55f,
                    700f, g.SternDist);
                a.AimFlat = touchPos - dir * finalAlong;
                if (absLat > 40f)
                    a.AimFlat += right * (Mathf.Sign(lat) * Mathf.Min(absLat * 0.25f, 180f));

                float glideAgl = Mathf.Clamp(Mathf.Max(along, 200f) * 0.048f, 20f, g.PatAgl * 0.7f);
                if (along < 1600f)
                    glideAgl = Mathf.Min(glideAgl, Mathf.Max(16f, along * 0.055f));
                if (ralt > 40f)
                    glideAgl = Mathf.Min(glideAgl, Mathf.Max(20f, ralt - 50f));
                a.ApAlt = glideAgl;
                a.AimFlat.y = touchPos.y + glideAgl;
                a.ApBank = (absLat > 120f || hdgDot < 0.85f) ? 155f * bankScale : 95f * bankScale;
                a.TgtSpd = Mathf.Clamp(landSpd * 1.18f, 55f, 90f);
                a.FollowTerrain = false;
                a.PhaseTag = "CV FINAL";

                // Capture wing / ILS earlier so deck centerline is established far out.
                bool linedUp = along > 700f && along < 4200f && absLat < 380f && hdgDot > 0.45f;
                bool closeIn = along > 400f && along < 2600f && absLat < 280f && hdgDot > 0.6f;
                if ((linedUp && speed < landSpd * 2.2f)
                    || (closeIn && speed < landSpd * 2.1f)
                    || (along > 500f && along < 2200f && absLat < 220f && hdgDot > 0.75f))
                {
                    a.CaptureWing = true;
                    a.PhaseTag = "CV WING";
                    if (ralt > 30f)
                        a.ApAlt = Mathf.Min(a.ApAlt, Mathf.Max(18f, ralt - 35f));
                    a.TgtSpd = Mathf.Min(a.TgtSpd, landSpd * 1.12f);
                }
            }
            return a;
        }

        internal static float ResolveAimY(
            PlayerAutopilot.CvPatternLeg leg,
            Vector3 acPos,
            Vector3 touchPos,
            float apAlt,
            float aimY)
        {
            if (leg == PlayerAutopilot.CvPatternLeg.Final)
                return aimY;
            float floorY = touchPos.y + apAlt;
            float softCeil = acPos.y + 20f;
            return Mathf.Min(softCeil, Mathf.Max(floorY, touchPos.y + apAlt * 0.9f));
        }

        internal static bool ShouldDropGear(
            PlayerAutopilot.CvPatternLeg leg, float dist, float ralt)
        {
            // Earlier gear: downwind abeam / base, not only short final.
            if (leg == PlayerAutopilot.CvPatternLeg.Downwind
                || leg == PlayerAutopilot.CvPatternLeg.Base
                || leg == PlayerAutopilot.CvPatternLeg.Final)
                return dist < 9000f || ralt < 700f;
            return dist < 5500f || ralt < 500f;
        }
    }
}
