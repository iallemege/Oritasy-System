using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield radar PPI contact / screen-name math (0.0.9.61).
    /// RadarMfdOverlay owns sweep paint, clutter, and MFD detection.
    /// </summary>
    internal static class RadarContactMathService
    {
        internal static float FlatBearingDeg(Vector3 origin, Quaternion heading, Vector3 worldPos)
        {
            Vector3 delta = worldPos - origin;
            Vector3 flat = delta;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
                return 0f;
            Vector3 fwd = heading * Vector3.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            return Vector3.SignedAngle(fwd, flat.normalized, Vector3.up);
        }

        internal static float FlatRangeM(Vector3 origin, Vector3 worldPos)
        {
            Vector3 flat = worldPos - origin;
            flat.y = 0f;
            return flat.magnitude;
        }

        internal static float ContactStrength(bool fromRadar, float rangeM, float rangeMaxM)
        {
            float strength = fromRadar ? 0.95f : 0.7f;
            float denom = Mathf.Max(1f, rangeMaxM);
            strength *= Mathf.Clamp(0.55f + 0.45f * (1f - Mathf.Clamp01(rangeM / denom)), 0.4f, 1f);
            return strength;
        }

        internal static bool NameLooksRadar(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.IndexOf("雷达", StringComparison.Ordinal) >= 0
                || n.IndexOf("战术", StringComparison.Ordinal) >= 0
                || n.IndexOf("搜索", StringComparison.Ordinal) >= 0)
                return true;
            string u = n.ToUpperInvariant();
            return u.IndexOf("RAD", StringComparison.Ordinal) >= 0
                || u.IndexOf("TAC", StringComparison.Ordinal) >= 0
                || u.IndexOf("RDR", StringComparison.Ordinal) >= 0
                || u.IndexOf("MAP", StringComparison.Ordinal) >= 0
                || u.IndexOf("PPI", StringComparison.Ordinal) >= 0
                || u.IndexOf("SCOPE", StringComparison.Ordinal) >= 0
                || u.IndexOf("SENSOR", StringComparison.Ordinal) >= 0;
        }
    }
}
