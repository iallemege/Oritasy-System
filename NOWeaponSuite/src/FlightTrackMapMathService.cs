using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield flight-track map projection (0.0.9.69).
    /// FlightTrackMap owns textures, LevelInfo cache, and GUI draw.
    /// </summary>
    internal static class FlightTrackMapMathService
    {
        internal const float DefaultMapHalfM = 40000f;

        internal static float ResolveMapHalf(float levelMapSize, float settingsMaxAxis)
        {
            float half = DefaultMapHalfM;
            if (levelMapSize > 500f)
                half = levelMapSize * 0.5f;
            if (settingsMaxAxis > 500f)
                half = Mathf.Max(half, settingsMaxAxis * 0.5f);
            return half;
        }

        /// <summary>Largest centered square inside r (isotropic XZ projection).</summary>
        internal static Rect SquareContentRect(Rect r)
        {
            float side = Mathf.Min(r.width, r.height);
            if (side < 1f)
                side = 1f;
            return new Rect(
                r.x + (r.width - side) * 0.5f,
                r.y + (r.height - side) * 0.5f,
                side,
                side);
        }

        /// <summary>Global XZ → GUI rect (north/+Z at top). xz.x=X, xz.y=Z.</summary>
        internal static Vector2 WorldToGui(Rect r, float half, Vector2 xz)
        {
            if (half < 1f)
                half = 1f;
            float u = (xz.x / half + 1f) * 0.5f;
            float v = (xz.y / half + 1f) * 0.5f;
            return new Vector2(r.x + Mathf.Clamp01(u) * r.width,
                r.y + (1f - Mathf.Clamp01(v)) * r.height);
        }
    }
}
