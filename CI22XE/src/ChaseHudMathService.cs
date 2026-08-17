using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield chase/orbit HUD geometry helpers (0.0.9.69).
    /// AircraftChaseNoseHud owns drawing + camera resolve.
    /// </summary>
    internal static class ChaseHudMathService
    {
        internal static float NormalizeHeadingDeg(float eulerY)
        {
            float hdg = eulerY;
            if (hdg < 0f)
                hdg += 360f;
            while (hdg >= 360f)
                hdg -= 360f;
            return hdg;
        }

        internal static int HeadingInt(float eulerY)
        {
            int hdgI = Mathf.RoundToInt(NormalizeHeadingDeg(eulerY)) % 360;
            if (hdgI < 0)
                hdgI += 360;
            return hdgI;
        }

        internal static float PitchFromEulerX(float eulerX)
        {
            return -Mathf.DeltaAngle(0f, eulerX);
        }

        internal static float TapeWidthPx(float screenWidth)
        {
            return Mathf.Clamp(screenWidth * 0.42f, 360f, 620f);
        }

        internal static float StatusStripWidthPx(float screenWidth)
        {
            return Mathf.Clamp(screenWidth * 0.5f, 420f, 720f);
        }

        internal static float Wrap360(float bearing)
        {
            while (bearing < 0f)
                bearing += 360f;
            while (bearing >= 360f)
                bearing -= 360f;
            return bearing;
        }

        internal static float TapeTickX(float midX, float tapeWidth, float deltaDeg)
        {
            return midX + (deltaDeg / 40f) * (tapeWidth * 0.45f);
        }
    }
}
