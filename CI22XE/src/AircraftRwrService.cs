using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield RWR math + blip store (0.0.9.60→71).
    /// Shared helpers for WarThunderRwrHud / OritasyHud circular scopes.
    /// </summary>
    internal static class AircraftRwrService
    {
        internal const string DisplayName = "Oritasy RWR";

        internal struct Blip
        {
            public int Id;
            public float Bearing;
            public float RangeNorm;
            public float Expires;
            public bool Missile;
            public bool Locked;
        }

        internal static float RelativeBearing(Aircraft ac, Vector3 worldPos)
        {
            if (ac == null)
                return 0f;
            Vector3 flat = worldPos - ac.transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
                return 0f;
            Vector3 fwd = ac.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            return Vector3.SignedAngle(fwd, flat.normalized, Vector3.up);
        }

        /// <summary>Radar warning power → ring radius (stronger = closer to center).</summary>
        internal static float RangeNormFromPower(float power01)
        {
            return Mathf.Clamp01(1.05f - Mathf.Clamp01(power01));
        }

        /// <summary>Missile distance → ring radius (farther = outer ring).</summary>
        internal static float RangeNormFromDistance(float distM, float maxRangeM)
        {
            if (maxRangeM < 1f)
                maxRangeM = 9000f;
            return Mathf.Clamp01(distM / maxRangeM);
        }

        internal static void Upsert(List<Blip> blips, int id, float bearing, float rangeNorm,
            bool missile, bool locked, float lifeSec)
        {
            if (blips == null)
                return;
            float exp = Time.unscaledTime + Mathf.Max(0.05f, lifeSec);
            for (int i = 0; i < blips.Count; i++)
            {
                if (blips[i].Id != id)
                    continue;
                Blip b = blips[i];
                b.Bearing = bearing;
                b.RangeNorm = rangeNorm;
                b.Expires = exp;
                b.Missile = missile;
                b.Locked = locked;
                blips[i] = b;
                return;
            }
            Blip n;
            n.Id = id;
            n.Bearing = bearing;
            n.RangeNorm = rangeNorm;
            n.Expires = exp;
            n.Missile = missile;
            n.Locked = locked;
            blips.Add(n);
        }

        internal static void Prune(List<Blip> blips)
        {
            if (blips == null)
                return;
            float now = Time.unscaledTime;
            for (int i = blips.Count - 1; i >= 0; i--)
            {
                if (blips[i].Expires < now)
                    blips.RemoveAt(i);
            }
        }

        internal struct DiscLayout
        {
            public Vector2 Center;
            public float Radius;
        }

        internal static DiscLayout ResolveDisc(
            float screenW,
            float screenH,
            float sizeFrac,
            float normX,
            float normY)
        {
            float sf = Mathf.Clamp(sizeFrac, 0.10f, 0.35f);
            float nx = Mathf.Clamp01(normX);
            float ny = Mathf.Clamp01(normY);
            float dia = Mathf.Clamp(Mathf.Min(screenW, screenH) * sf, 90f, 340f);
            float radius = dia * 0.5f;
            float cx = Mathf.Clamp(nx * screenW, radius + 8f, screenW - radius - 8f);
            float cy = Mathf.Clamp(ny * screenH, radius + 8f, screenH - radius - 8f);
            DiscLayout L = new DiscLayout();
            L.Center = new Vector2(cx, cy);
            L.Radius = radius;
            return L;
        }

        /// <summary>WT-style: closer threats nearer the rim.</summary>
        internal static float BlipRingRadius(float discRadius, float rangeNorm, bool missile)
        {
            float t = missile
                ? Mathf.Clamp01(0.35f + rangeNorm * 0.65f)
                : Mathf.Clamp01(rangeNorm);
            return Mathf.Lerp(discRadius * 0.28f, discRadius * 0.9f, t);
        }

        internal static Vector2 BlipScreenPos(Vector2 center, float ringRadius, float bearingDeg)
        {
            float rad = bearingDeg * Mathf.Deg2Rad;
            return new Vector2(
                center.x + Mathf.Sin(rad) * ringRadius,
                center.y - Mathf.Cos(rad) * ringRadius);
        }
    }
}
