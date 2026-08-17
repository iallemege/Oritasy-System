using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// ILS localizer / glideslope math. Angle is runtime-configurable (F4, default 5°).
    /// </summary>
    internal static class IlsApproachMathService
    {
        internal const float MinGlideSlopeDeg = 3f;
        internal const float MaxGlideSlopeDeg = 8f;
        internal const float DefaultGlideSlopeDeg = 5f;

        internal const float LocFullScaleM = 150f;     // ±2 dots
        internal const float GsDotDeg = 0.35f;         // 1 dot ≈ 0.35°
        internal const float MinAlongForGsM = 80f;
        internal const float MaxNeedleDots = 2f;

        private static float _glideSlopeDeg = DefaultGlideSlopeDeg;

        /// <summary>Active glideslope degrees (clamped 3–8).</summary>
        internal static float GlideSlopeDeg
        {
            get { return _glideSlopeDeg; }
            set { _glideSlopeDeg = ClampDeg(value); }
        }

        /// <summary>tan(active GS). Prefer this over any fixed constant.</summary>
        internal static float GlideTan
        {
            get { return Mathf.Tan(_glideSlopeDeg * Mathf.Deg2Rad); }
        }

        internal static float ClampDeg(float deg)
        {
            return Mathf.Clamp(deg, MinGlideSlopeDeg, MaxGlideSlopeDeg);
        }

        internal struct Result
        {
            public float AlongM;
            public float LateralM;
            public float DistM;
            public float IdealAglM;
            public float LocDots;
            public float GsDots;
            public bool GsValid;
            public bool OnFinalCorridor;
        }

        internal static Result Evaluate(Vector3 acPos, float radarAltM, Vector3 touchPos, Vector3 rwyDirFlat)
        {
            Result r = new Result();
            Vector3 dir = rwyDirFlat;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.forward;
            dir.Normalize();

            LandingMath.RunwayAlongLateral(acPos, touchPos, dir, out r.AlongM, out r.LateralM);
            r.DistM = Vector3.Distance(acPos, touchPos);

            float alongForGs = Mathf.Max(r.AlongM, 0f);
            float tan = GlideTan;
            r.IdealAglM = alongForGs * tan;
            r.LocDots = Mathf.Clamp(r.LateralM / (LocFullScaleM * 0.5f), -MaxNeedleDots, MaxNeedleDots);

            r.GsValid = r.AlongM >= MinAlongForGsM;
            if (r.GsValid)
            {
                float errDeg = Mathf.Atan2(radarAltM - r.IdealAglM, r.AlongM) * Mathf.Rad2Deg;
                r.GsDots = Mathf.Clamp(errDeg / GsDotDeg, -MaxNeedleDots, MaxNeedleDots);
            }

            r.OnFinalCorridor = r.AlongM > 0f
                && r.AlongM < 10000f
                && Mathf.Abs(r.LateralM) < 800f;
            return r;
        }

        /// <summary>Aimpoint on active GS path at look-ahead distance along runway.</summary>
        internal static Vector3 GlideAimPoint(Vector3 touchPos, Vector3 rwyDirFlat, float lookAheadM)
        {
            Vector3 dir = rwyDirFlat;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.forward;
            dir.Normalize();
            float d = Mathf.Max(50f, lookAheadM);
            Vector3 p = touchPos - dir * d;
            p.y = touchPos.y + d * GlideTan;
            return p;
        }

        /// <summary>Prefer final (along&gt;0), small lateral, closer threshold.</summary>
        internal static float ScoreApproach(float along, float lat, float distToTouch)
        {
            float pen = Mathf.Abs(lat) * 1.2f + distToTouch * 0.15f;
            if (along < 0f)
                pen += 2500f - along;
            else
                pen += along * 0.05f;
            return pen;
        }

        internal static bool ShouldDrawGlideAim(bool onFinalCorridor, float alongM)
        {
            return onFinalCorridor && alongM >= 100f;
        }

        internal static float GlideAimLookAheadM(float alongM)
        {
            return Mathf.Clamp(alongM * 0.45f, 200f, 900f);
        }

        /// <summary>True when AP should fly the ILS corridor (not rollout / not beam).</summary>
        internal static bool ShouldTrackIls(float alongM, float lateralM, bool rollout)
        {
            return !rollout
                && alongM > 80f
                && alongM < 12000f
                && Mathf.Abs(lateralM) < 900f;
        }

        /// <summary>
        /// Active-GS glide aim + strong localizer pull toward centerline.
        /// </summary>
        internal static Vector3 CorrectedGlideAim(
            Vector3 touchPos, Vector3 rwyDirFlat, float alongM, float lateralM)
        {
            float look = GlideAimLookAheadM(alongM);
            Vector3 aim = GlideAimPoint(touchPos, rwyDirFlat, look);
            Vector3 dir = rwyDirFlat;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.forward;
            dir.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, dir);
            if (right.sqrMagnitude > 0.01f)
            {
                right.Normalize();
                // Stronger localizer — align runway earlier.
                float pull = Mathf.Abs(lateralM) > 120f ? 1.05f : 0.85f;
                aim -= right * Mathf.Clamp(lateralM * pull, -320f, 320f);
            }
            return aim;
        }

        /// <summary>Ideal ILS AGL at current along-track (no intercept shaping).</summary>
        internal static float IdealPathAgl(float alongM, bool carrier)
        {
            float tan = carrier ? Mathf.Min(GlideTan, 0.07f) : GlideTan;
            return Mathf.Max(alongM, 60f) * tan;
        }

        /// <summary>
        /// Target AGL: ride the path. From above, step down toward it (not dump);
        /// from below, climb to intercept — avoids early dive / late dump.
        /// </summary>
        internal static float ResolveIlsAltHold(
            float alongM, float ralt, bool carrier, bool flare, bool shortFinal)
        {
            float path = IdealPathAgl(alongM, carrier);
            if (flare)
                path = Mathf.Clamp(path * 0.4f, 4f, 22f);
            else if (shortFinal || alongM < 1200f)
                path = Mathf.Clamp(path, 14f, carrier ? 110f : 160f);
            else
                path = Mathf.Clamp(path, 35f, carrier ? 320f : 520f);

            if (ralt <= 0.1f)
                return path;

            // Above path: command path (or gentle step if very high).
            if (ralt > path + 25f)
            {
                float step = Mathf.Max(path, ralt - Mathf.Lerp(60f, 140f, Mathf.Clamp01((ralt - path) / 600f)));
                return step;
            }
            // Below path: climb to intercept — do not dive further.
            if (ralt < path - 20f)
                return Mathf.Min(path, ralt + 50f);
            return path;
        }

        /// <summary>Legacy overload without ralt (assumes already on path).</summary>
        internal static float ResolveIlsAltHold(
            float alongM, bool carrier, bool flare, bool shortFinal)
        {
            return ResolveIlsAltHold(alongM, IdealPathAgl(alongM, carrier), carrier, flare, shortFinal);
        }
    }
}
