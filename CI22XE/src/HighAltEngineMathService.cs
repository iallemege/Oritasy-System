using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield high-altitude engine assist math (0.0.9.65).
    /// HighAltEngineAssist owns Harmony patches + reflection fields.
    /// </summary>
    internal static class HighAltEngineMathService
    {
        internal const float AssistRadarAltM = 14000f;
        internal const float MinThrottle = 0.2f;
        internal const float NormalThrustFactor = 0.45f;
        internal const float GuardianThrustFactor = 0.7f;
        internal const float HighAltEngineThrustFactor = 1f;

        internal static float DensityFloor(float minDensity, bool forceGuardian)
        {
            float floor = Mathf.Max(0.18f, minDensity * 1.25f);
            if (forceGuardian)
                floor = Mathf.Max(floor, 0.28f);
            return floor;
        }

        internal static float ThrustWant(float maxThrust, float throttle, bool forceGuardian, bool highAltEngines)
        {
            if (maxThrust < 1f || throttle < MinThrottle)
                return 0f;
            float factor = NormalThrustFactor;
            if (highAltEngines)
                factor = HighAltEngineThrustFactor;
            else if (forceGuardian)
                factor = GuardianThrustFactor;
            return maxThrust * Mathf.Clamp01(throttle) * factor;
        }

        internal static bool AltitudeWarrantsAssist(float radarAltM)
        {
            return radarAltM >= AssistRadarAltM;
        }
    }
}
