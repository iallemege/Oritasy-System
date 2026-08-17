using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield airbase CaptureRange / near-airbase safe circle math (0.0.9.68).
    /// Matches vanilla FactionHQ.AnyNearAirbase → Airbase.GetRadius() (SavedAirbase.CaptureRange).
    /// Landed + inside radius → destroy/disable is not counted as a crash life.
    /// </summary>
    internal static class AirportSafeRangeMathService
    {
        internal const float DefaultCaptureRangeM = 1000f;
        internal const int RingSegments = 48;

        internal static float ResolveRadiusM(float getRadiusOrZero)
        {
            if (getRadiusOrZero > 1f)
                return getRadiusOrZero;
            return DefaultCaptureRangeM;
        }

        internal static float HorizontalDistanceM(Vector3 from, Vector3 center)
        {
            float dx = from.x - center.x;
            float dz = from.z - center.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        internal static bool InsideRadius(float horizontalDistM, float radiusM)
        {
            return horizontalDistM <= radiusM;
        }

        internal static float RemainingToEdgeM(float horizontalDistM, float radiusM)
        {
            return radiusM - horizontalDistM;
        }

        /// <summary>Ground-plane ring sample at airbase altitude.</summary>
        internal static Vector3 RingPoint(Vector3 center, float radiusM, int index, int segments)
        {
            int n = segments < 8 ? 8 : segments;
            float ang = (Mathf.PI * 2f) * (index % n) / n;
            return new Vector3(
                center.x + Mathf.Cos(ang) * radiusM,
                center.y,
                center.z + Mathf.Sin(ang) * radiusM);
        }
    }
}
