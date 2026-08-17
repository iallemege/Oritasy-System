using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield winged-final phase / energy / sink / bank gates (0.0.9.95).
    /// LandingGuidance owns AutoAim, inputs, CV pattern, and Unity state writes.
    /// </summary>
    internal static class LandingFinalMathService
    {
        internal struct FinalPhases
        {
            public bool Align;
            public bool ShortFinal;
            public bool Flare;
            public bool Rollout;
        }

        /// <summary>Approach speed energy multiplier (STOVL pad vs CV vs winged).</summary>
        internal static float LandSpeedEnergyMul(bool stovl, bool carrier)
        {
            float energyMul = 1f;
            if (stovl && !carrier)
                energyMul *= 1.16f;
            else if (stovl)
                energyMul *= 1.03f;
            if (carrier)
                energyMul *= 1.03f;
            return energyMul;
        }

        internal static bool IsWheelsDownRollout(bool landedWing, float ralt, float speed)
        {
            return landedWing || (ralt < 2.5f && speed < 18f);
        }

        /// <summary>Overshot / abeam too close / heading crossed → go around.</summary>
        internal static bool ShouldBreakOff(
            float alongTrack,
            float latTrack,
            float dist,
            float hdgDot,
            bool carrier)
        {
            // Softened vs 0.0.9.95+: old CV gate (along<700 && hdg<0.75) aborted every
            // normal short-final correction → endless 五边 / unusable LAND CV.
            float absLat = Mathf.Abs(latTrack);
            if (alongTrack < -180f)
                return true;
            if (alongTrack < 80f && absLat > 280f && dist < 1100f)
                return true;
            if (carrier && alongTrack < 350f && alongTrack > 0f
                && (absLat > 340f || hdgDot < 0.25f))
                return true;
            return false;
        }

        internal static FinalPhases ResolveFinalPhases(
            float dist,
            float ralt,
            float alongTrack,
            float speed,
            float landSpd,
            bool carrier)
        {
            FinalPhases p = new FinalPhases();
            p.Align = dist < 2500f;
            // Delay short-final / flare until closer — early “short final” while high dumps energy.
            // NEVER arm from ralt alone when still far — that dumped energy in the pattern join.
            // NEVER arm flare/rollout from alongTrack alone — past-threshold geometry at
            // pattern altitude used to force thr=0/brake=1 and stall the approach into a crash.
            bool nearCorridor = dist < 2200f || (alongTrack > 0f && alongTrack < 2400f);
            p.ShortFinal = nearCorridor && ((dist < 1100f && ralt < 280f) || ralt < 120f);
            p.Flare = nearCorridor && ralt < 70f && (alongTrack < 280f || dist < 560f);
            p.Rollout = ralt < 14f && (alongTrack < 90f || dist < 220f || speed < landSpd * 0.85f);
            // Carrier: arm short-final / flare when actually on stern corridor.
            if (carrier && alongTrack > 0f && alongTrack < 900f && ralt < 100f && Mathf.Abs(alongTrack) < 1200f)
            {
                p.ShortFinal = true;
                if (ralt < 50f)
                    p.Flare = true;
                if (ralt < 12f || (ralt < 18f && speed < landSpd * 1.05f))
                    p.Rollout = true;
            }
            // LAND BASE: arm flare when low on the corridor even if dist still large.
            if (!carrier && alongTrack > 0f && alongTrack < 500f && ralt < 55f)
            {
                p.ShortFinal = true;
                p.Flare = true;
                if (ralt < 12f || speed < landSpd * 1.05f)
                    p.Rollout = true;
            }
            return p;
        }

        internal static float ResolveBankCap(
            FinalPhases p,
            float latTrack,
            float hdgDot,
            float bankScale,
            bool carrier)
        {
            float bank = 120f;
            if (p.Rollout)
                bank = 10f;
            else if (p.Flare)
                bank = 20f;
            else if (p.ShortFinal)
                bank = carrier ? 60f : 45f;
            else if (p.Align)
                bank = carrier ? 110f : 95f;
            // Earlier centerline — allow more bank while still offset / crabbed.
            if (!p.Flare && !p.Rollout
                && (Mathf.Abs(latTrack) > 70f || hdgDot < 0.92f))
                bank = Mathf.Max(bank, carrier ? 165f : 145f);
            return bank * bankScale;
        }

        /// <summary>~3° ILS path AGL (tan≈0.052); CV slightly flatter.</summary>
        internal static float ResolveShallowAgl(
            float alongTrack,
            float ralt,
            float severity,
            bool carrier,
            bool stovl,
            bool flare)
        {
            float track = Mathf.Max(alongTrack, 80f);
            // Prefer true ILS 3°; severity only softens slightly when damaged.
            float glideTan = Mathf.Lerp(IlsApproachMathService.GlideTan, 0.042f, Mathf.Clamp01(severity));
            float shallowAgl = Mathf.Clamp(track * glideTan, 40f, 450f);
            if (carrier)
            {
                shallowAgl = Mathf.Clamp(track * 0.042f, 10f, 280f);
                if (alongTrack < 500f)
                    shallowAgl = Mathf.Min(shallowAgl, Mathf.Max(10f, alongTrack * 0.055f));
                if (flare)
                    shallowAgl = Mathf.Lerp(shallowAgl, 6f, Mathf.Clamp01(1f - ralt / 45f));
            }
            else
            {
                shallowAgl = Mathf.Clamp(track * glideTan, 18f, 420f);
                if (alongTrack < 800f)
                    shallowAgl = Mathf.Min(shallowAgl, Mathf.Max(14f, alongTrack * IlsApproachMathService.GlideTan));
                if (stovl && !flare && ralt > 80f)
                    shallowAgl = Mathf.Max(shallowAgl, 40f);
                if (flare)
                    shallowAgl = Mathf.Lerp(shallowAgl, 8f, Mathf.Clamp01(1f - ralt / 55f));
            }
            return shallowAgl;
        }

        /// <summary>Drop gear early on join / final — gear needs time before short final.</summary>
        internal static bool ShouldDropGearEarly(
            float dist, float along, float ralt, float speed, float landSpd, bool shortFinal, bool flare)
        {
            if (shortFinal || flare)
                return true;
            if (dist < 8500f || along < 7000f)
                return true;
            if (ralt < 750f && (along > 0f || dist < 12000f))
                return true;
            if (speed < landSpd * 1.85f && dist < 12000f)
                return true;
            return false;
        }

        internal static float ResolveMaxSink(
            float speed,
            float ralt,
            float sinkScale,
            float severity,
            bool flare,
            bool carrier)
        {
            float maxSink = Mathf.Clamp(speed * 0.05f, 2.5f, 8f) * sinkScale;
            if (ralt < 220f) maxSink = Mathf.Min(maxSink, 5.5f * sinkScale);
            if (ralt < 120f) maxSink = Mathf.Min(maxSink, 3.8f * sinkScale);
            if (ralt < 70f) maxSink = Mathf.Min(maxSink, 2.8f * sinkScale);
            if (ralt < 40f || flare) maxSink = Mathf.Min(maxSink, 2.0f * sinkScale);
            if (ralt < 20f) maxSink = Mathf.Min(maxSink, 1.4f * Mathf.Lerp(1f, 0.75f, severity));
            if (carrier && ralt < 55f)
                maxSink = Mathf.Min(maxSink, 2.2f * sinkScale);
            return maxSink;
        }

        internal static float ResolveSinkLift(
            float sink,
            float maxSink,
            float ralt,
            bool carrier,
            bool shortFinal)
        {
            float lift = (sink - maxSink) * Mathf.Clamp(ralt * 0.8f, 80f, 420f);
            if (ralt < 80f)
                lift = Mathf.Min(lift, Mathf.Lerp(8f, 35f, Mathf.Clamp01(ralt / 80f)));
            else if (!carrier && shortFinal)
                lift = Mathf.Min(lift, 60f);
            return lift;
        }

        internal static float ResolveSafeLo(float landSpd, bool carrier, bool flare, bool shortFinal)
        {
            // Must sit BELOW flare/short-final targets. Old CV fixed 1.12 fought flare tgt
            // 1.02 (and 52 m/s cap) → never energySafe → climb-hold forever.
            if (carrier)
                return landSpd * (flare ? 0.86f : (shortFinal ? 0.94f : 1.02f));
            return landSpd * (flare ? 0.92f : (shortFinal ? 1.02f : 1.08f));
        }

        internal static float ResolveStallMul(bool carrier, bool flare, bool shortFinal)
        {
            // Stall floor must be below commanded approach speed or AutoAim climbs out.
            if (carrier)
                return flare ? 0.84f : (shortFinal ? 0.92f : 1.0f);
            return flare ? 0.95f : (shortFinal ? 1.02f : 1.1f);
        }

        internal static float ResolveTargetSpeed(
            float landSpd,
            float dist,
            float speed,
            float ralt,
            FinalPhases p,
            bool carrier,
            bool stallThreat,
            bool sinkHot)
        {
            float cvMul = carrier ? 1.02f : 1f;
            float tgtSpd = Mathf.Min(
                landSpd * (carrier ? 1.2f : 1.36f) * cvMul
                    + Mathf.Max(dist - 1600f, 0f) * 0.005f,
                landSpd * (carrier ? 1.4f : 1.65f) * cvMul);
            if (p.ShortFinal)
                tgtSpd = landSpd * (carrier ? 1.1f : 1.22f) * cvMul;
            if (p.Flare)
                tgtSpd = landSpd * (carrier ? 1.02f : 1.08f) * cvMul;
            if (p.Rollout)
                tgtSpd = Mathf.Min(tgtSpd, landSpd * 0.7f);
            if (carrier)
            {
                if (p.ShortFinal)
                    tgtSpd = Mathf.Min(tgtSpd, 62f);
                if (p.Flare)
                    tgtSpd = Mathf.Min(tgtSpd, 52f);
                if (p.Rollout)
                    tgtSpd = Mathf.Min(tgtSpd, 28f);
            }
            // Only raise target on a true stall — never yank flare tgt back up to 1.12×.
            if (stallThreat && speed < landSpd * (carrier ? 0.82f : 0.9f))
                tgtSpd = Mathf.Max(tgtSpd, landSpd * (carrier ? 0.98f : 1.05f) * cvMul);
            else if (sinkHot && ralt < 250f && !carrier && !p.Flare)
                tgtSpd = Mathf.Max(tgtSpd, landSpd * 1.15f * cvMul);
            else if (sinkHot && carrier && ralt < 60f && speed < 55f)
                tgtSpd = Mathf.Max(tgtSpd, landSpd * 1.05f);
            return tgtSpd;
        }

        /// <summary>Pattern-join AGL — pull onto ILS path earlier when nearly lined up.</summary>
        internal static float ResolveJoinApAlt(
            float turnR,
            float dmgApBoost,
            float speed,
            float landSpd,
            float alongAp,
            float latAp,
            float raltAp,
            float severity)
        {
            float tan = IlsApproachMathService.GlideTan;
            float ilsPath = Mathf.Max(alongAp, 80f) * tan;
            float apAltBase = Mathf.Max(0.12f * turnR * 3f, 480f + dmgApBoost * 0.35f);
            if (speed > landSpd * 1.8f)
                apAltBase = Mathf.Max(apAltBase, 700f + dmgApBoost * 0.25f);
            if (speed > landSpd * 2.4f)
                apAltBase = Mathf.Max(apAltBase, 900f);

            float absLat = Mathf.Abs(latAp);
            // Early ILS intercept once roughly stern / not beam-on.
            if (alongAp > 500f && alongAp < 8000f && absLat < 600f)
                apAltBase = Mathf.Min(apAltBase, Mathf.Max(ilsPath + 30f, 120f));
            if (alongAp > 400f && alongAp < 4500f && absLat < 350f)
                apAltBase = Mathf.Min(apAltBase, Mathf.Max(ilsPath + 10f, alongAp * tan));
            if (alongAp > 600f && alongAp < 3500f && absLat < 220f
                && speed <= landSpd * 1.75f)
                apAltBase = Mathf.Min(apAltBase, Mathf.Max(ilsPath, 80f));

            // From high: step toward path (not hold forever, not dump 140 m only).
            if (raltAp > apAltBase + 40f)
                apAltBase = Mathf.Max(apAltBase, raltAp - Mathf.Lerp(70f, 160f,
                    Mathf.Clamp01((raltAp - apAltBase) / 800f)));
            return apAltBase;
        }

        internal static bool ShouldCaptureApproach(
            float alongAp,
            float latAp,
            float raltAp,
            float apAltBase,
            float speed,
            float landSpd,
            float sinkAp,
            float severity,
            bool alreadyReached)
        {
            // Capture earlier / wider so final can straighten onto runway sooner.
            bool sternReady = alongAp > 350f && alongAp < 4200f && Mathf.Abs(latAp) < 420f;
            float gateSinkBase = Mathf.Lerp(14f, 8f, Mathf.Clamp01(severity));
            float gateSpdBase = landSpd * 1.85f;
            float gateFloorBase = landSpd * 0.9f;
            bool altReady = raltAp <= 0f || raltAp < apAltBase + 280f || raltAp < 750f;
            if (sternReady && altReady && speed <= gateSpdBase && speed >= gateFloorBase
                && sinkAp < gateSinkBase)
                return true;
            if (!alreadyReached && sternReady && alongAp < 2800f
                && Mathf.Abs(latAp) < 320f && speed <= landSpd * 2.0f && raltAp < 900f)
                return true;
            // Nearly lined up far out — start final / ILS tracking.
            if (!alreadyReached && alongAp > 800f && alongAp < 5500f
                && Mathf.Abs(latAp) < 180f && speed <= landSpd * 2.1f)
                return true;
            return false;
        }
    }
}
