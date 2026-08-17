using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield radar MFD sweep / clutter / polar math (0.0.9.70).
    /// RadarMfdOverlay owns detect, lists, and GUI paint.
    /// </summary>
    internal static class RadarMfdMathService
    {
        internal static float ClampSweepRpm(float rpm, bool lowEnd)
        {
            float r = rpm;
            if (lowEnd && r > 18f)
                r = 18f;
            if (r < 6f)
                r = 6f;
            if (r > 60f)
                r = 60f;
            return r;
        }

        internal static float DegPerSecFromRpm(float rpm)
        {
            return ClampSweepRpm(rpm, false) * 6f;
        }

        internal static float AdvanceSweep(float angleDeg, float degPerSec, float dt)
        {
            return Mathf.Repeat(angleDeg + dt * degPerSec, 360f);
        }

        internal static float RangeMeters(float rangeKm)
        {
            return Mathf.Max(5f, rangeKm) * 1000f;
        }

        internal static float ClampBeamWidthDeg(float beamW)
        {
            return beamW < 1.5f ? 1.5f : beamW;
        }

        internal static float ClampPersistSec(float persist)
        {
            return persist < 0.4f ? 0.4f : persist;
        }

        internal struct Layout
        {
            public Vector2 Center;
            public float Radius;
        }

        internal static Layout ComputeLayout(
            float screenW,
            float screenH,
            float sizeFrac,
            float normX,
            float normY,
            bool hasScreenRect,
            Rect screenRect)
        {
            Layout L = new Layout();
            float size = Mathf.Clamp(sizeFrac, 0.14f, 0.40f) * screenH;
            float cx = Mathf.Clamp01(normX) * screenW;
            float cy = Mathf.Clamp01(normY) * screenH;
            float radius = size * 0.5f;
            if (hasScreenRect && screenRect.width > 8f && screenRect.height > 8f)
            {
                cx = screenRect.x + screenRect.width * 0.5f;
                cy = screenRect.y + screenRect.height * 0.5f;
                radius = Mathf.Min(screenRect.width, screenRect.height) * 0.42f;
                if (radius < 40f)
                    radius = 40f;
            }
            L.Center = new Vector2(cx, cy);
            L.Radius = radius;
            return L;
        }

        internal static float ClutterAglFactor(float aglM)
        {
            float aglFactor = Mathf.Clamp01(1.15f - aglM / 2500f);
            return Mathf.Lerp(0.18f, 1f, aglFactor);
        }

        internal static float ClutterSurfaceBump(float aglM)
        {
            if (aglM < 120f)
                return 1.35f;
            if (aglM < 400f)
                return 1.1f;
            return 1f;
        }

        internal static int ClutterCount(int cap, float intensity, float aglFactor, float surfaceBump, bool lowEnd)
        {
            float i = Mathf.Clamp(intensity, 0f, 1.5f);
            int count = Mathf.RoundToInt(cap * 0.55f * i * aglFactor * surfaceBump);
            if (lowEnd)
            {
                if (count < 4)
                    count = 4;
            }
            else if (count < 8)
                count = 8;
            if (count > cap)
                count = cap;
            return count;
        }

        internal static float ClutterStrength(
            float rand01,
            float intensity,
            float aglFactor,
            float rangeNorm,
            float aglM)
        {
            float rangeFade = 1f - rangeNorm * 0.85f;
            float str = (0.15f + 0.85f * rand01) * intensity * aglFactor * rangeFade * 0.55f;
            if (aglM < 80f && rangeNorm < 0.35f)
                str *= 1.4f;
            return Mathf.Clamp01(str);
        }

        internal static float BeamGain(float deltaBearingAbs, float halfBeamPlus)
        {
            float den = halfBeamPlus;
            if (den < 0.01f)
                den = 0.01f;
            return 1f - deltaBearingAbs / den;
        }

        internal static float RangeFalloff(float rangeNorm)
        {
            return 1f / (0.35f + rangeNorm * 1.4f);
        }

        internal static float PhosphorDecay(float age01)
        {
            float age = Mathf.Clamp01(age01);
            return Mathf.Pow(1f - age, 1.6f);
        }

        internal static Vector2 PolarToScreen(Vector2 center, float radius, float bearingDeg, float rangeNorm)
        {
            float rad = bearingDeg * Mathf.Deg2Rad;
            float r = Mathf.Clamp01(rangeNorm) * radius;
            return new Vector2(
                center.x + Mathf.Sin(rad) * r,
                center.y - Mathf.Cos(rad) * r);
        }
    }
}
