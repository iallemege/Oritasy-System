namespace WeXon
{
    /// <summary>
    /// Free-hunt radius must exceed the missile's own kinematic range.
    /// Plugin estimates range; this service only does the max(floor, range * factor) math.
    /// </summary>
    internal static class HuntRangeGateService
    {
        internal const float BeyondRangeFactor = 1.25f;
        internal const float FloorM = 35000f;
        internal const float CruiseClassMinRangeM = 100000f;
        internal const float BallisticClassMinRangeM = 120000f;
        internal const float DefaultSpeedMps = 280f;
        internal const float DefaultCruiseSpeedMps = 320f;

        internal static float PickSpeedMps(float infoMaxSpeed, float topSpeed, float fallback)
        {
            float spd = fallback > 10f ? fallback : DefaultSpeedMps;
            if (infoMaxSpeed > spd)
                spd = infoMaxSpeed;
            if (topSpeed > spd)
                spd = topSpeed;
            return spd;
        }

        internal static float KinematicRangeM(float burnSec, float speedMps)
        {
            if (burnSec < 0.5f || speedMps < 10f)
                return 0f;
            return burnSec * speedMps;
        }

        /// <summary>Hunt radius = max(config floor, estimated range * 1.25). Never shorter than range.</summary>
        internal static float ResolveHuntRadiusM(float configuredM, float missileRangeM)
        {
            float floor = configuredM > 0f ? configuredM : FloorM;
            if (floor < FloorM)
                floor = FloorM;
            float need = missileRangeM * BeyondRangeFactor;
            if (need > floor)
                return need;
            return floor;
        }

        internal static float ResolveTbmHuntRadiusM(float configuredM, float huntBeyondRangeM)
        {
            float r = configuredM;
            if (r < 1000f)
                r = 60000f;
            if (huntBeyondRangeM > r)
                return huntBeyondRangeM;
            return r;
        }
    }
}
