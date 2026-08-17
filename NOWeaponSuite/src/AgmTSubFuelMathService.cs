using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield AGM-T GS25 submunition fuel/thrust gates (0.0.9.86).
    /// AgmTSubBrain owns AddForce and warhead arming.
    /// </summary>
    internal static class AgmTSubFuelMathService
    {
        internal const float DefaultBurnSec = 42f;
        internal const float DefaultMaxRangeM = 35000f;
        internal const float DefaultThrust = 22000f;
        internal const float TopSpeedMps = 1400f;
        internal const float MinBurnSec = 1f;

        internal static float ClampBurnTime(float configured)
        {
            if (configured < MinBurnSec)
                return MinBurnSec;
            return configured;
        }

        /// <summary>
        /// Updates fuelLeft [0,1]. Returns false when thrust must stop (fuel exhausted).
        /// </summary>
        internal static bool TryUpdateFuel(
            float ageSec,
            float traveledM,
            float burnSec,
            float maxRangeM,
            out float fuelLeft)
        {
            burnSec = ClampBurnTime(burnSec);
            if (ageSec >= burnSec || traveledM >= maxRangeM)
            {
                fuelLeft = 0f;
                return false;
            }
            fuelLeft = 1f - (ageSec / burnSec);
            return fuelLeft > 0f;
        }

        internal static bool ShouldApplyThrust(float fuelLeft, float speedMps)
        {
            return fuelLeft > 0f && speedMps < TopSpeedMps;
        }
    }
}
