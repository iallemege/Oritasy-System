using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield rocket CCIP math (0.0.9.61): ballistics integrate, CEP, rocket-name heuristics.
    /// RocketCcipHud owns config, cache, and drawing.
    /// </summary>
    internal static class RocketCcipMathService
    {
        internal const int MaxSteps = 220;
        internal const float StepDt = 0.05f;
        internal const int TerrainMask = -8193;
        internal const float CepMinMeters = 3f;
        internal const float CepMaxMeters = 400f;
        internal const float CepRayleighToCep50 = 1.1774f;

        internal struct BallisticsState
        {
            public Vector3 Pos;
            public Vector3 Vel;
            public float DragK;
            public float GravMult;
            public float Burn;
            public float Thrust;
            public float Mass;
            public float SeaY;
        }

        internal struct ImpactResult
        {
            public bool Hit;
            public Vector3 Point;
            public float TimeOfFlight;
        }

        internal static ImpactResult SimulateImpact(BallisticsState s)
        {
            ImpactResult r;
            r.Hit = false;
            r.Point = s.Pos;
            r.TimeOfFlight = 0f;

            float t = 0f;
            Vector3 pos = s.Pos;
            Vector3 vel = s.Vel;
            Vector3 prev = pos;
            float mass = Mathf.Max(0.1f, s.Mass);

            for (int i = 0; i < MaxSteps; i++)
            {
                float dt = StepDt;
                Vector3 accel = -Vector3.up * (9.81f * s.GravMult);
                float speedSq = vel.sqrMagnitude;
                if (speedSq > 0.01f)
                {
                    Vector3 vDir = vel / Mathf.Sqrt(speedSq);
                    accel -= vDir * (s.DragK * speedSq);
                    if (t < s.Burn && s.Thrust > 1f)
                        accel += vDir * (s.Thrust / mass);
                }

                vel += accel * dt;
                Vector3 next = pos + vel * dt;
                t += dt;

                Vector3 delta = next - prev;
                float segLen = delta.magnitude;
                if (segLen > 0.05f)
                {
                    RaycastHit rh;
                    if (Physics.Raycast(prev, delta / segLen, out rh, segLen + 0.05f, TerrainMask))
                    {
                        r.Point = rh.point;
                        r.Hit = true;
                        r.TimeOfFlight = t - dt * (1f - Mathf.Clamp01(rh.distance / (segLen + 0.05f)));
                        return r;
                    }
                }

                if (next.y < s.SeaY && prev.y >= s.SeaY)
                {
                    float frac = (prev.y - s.SeaY) / Mathf.Max(1e-4f, prev.y - next.y);
                    Vector3 hitPoint = Vector3.Lerp(prev, next, Mathf.Clamp01(frac));
                    hitPoint.y = s.SeaY;
                    r.Point = hitPoint;
                    r.Hit = true;
                    r.TimeOfFlight = t;
                    return r;
                }

                prev = pos;
                pos = next;

                if (pos.y < s.SeaY - 50f && vel.y < 0f)
                {
                    r.Point = new Vector3(pos.x, s.SeaY, pos.z);
                    r.Hit = true;
                    r.TimeOfFlight = t;
                    return r;
                }
            }

            return r;
        }

        internal static float EstimateCepMeters(
            float circularError,
            float timeOfFlight,
            Vector3 muzzleWorld,
            Vector3 impactWorld,
            float muzzleVel,
            float burn,
            float thrust,
            float mass,
            float scale,
            float baseline)
        {
            scale = Mathf.Clamp(scale, 0.05f, 10f);
            baseline = Mathf.Clamp(baseline, 0f, 200f);

            if (circularError > 0.05f)
            {
                float weaponCep = circularError * scale;
                weaponCep *= 1f + 0.08f * Mathf.Max(0f, timeOfFlight - 1f);
                return Mathf.Clamp(weaponCep, CepMinMeters, CepMaxMeters);
            }

            float tof = Mathf.Max(0.05f, timeOfFlight);
            float slant = Vector3.Distance(muzzleWorld, impactWorld);
            if (slant < 1f)
                slant = Mathf.Max(1f, tof * 80f);

            float vEff = Mathf.Max(25f, muzzleVel);
            if (burn > 0.05f && thrust > 1f && mass > 0.1f)
            {
                float burnUse = Mathf.Min(burn, tof);
                vEff += 0.5f * (thrust / mass) * burnUse;
            }

            float angSigma = 0.008f * Mathf.Sqrt(250f / Mathf.Max(50f, vEff));
            float sigmaAng = angSigma * slant;
            float sigmaTof = 1.8f * tof * Mathf.Sqrt(Mathf.Max(0.25f, tof));
            float sigma = sigmaAng + sigmaTof;
            float cep = (baseline + CepRayleighToCep50 * sigma) * scale;
            return Mathf.Clamp(cep, CepMinMeters, CepMaxMeters);
        }

        internal static string FormatCepMeters(float m)
        {
            if (m >= 100f)
                return Mathf.RoundToInt(m).ToString();
            if (m >= 10f)
                return m.ToString("0");
            return m.ToString("0.0");
        }

        internal static bool NameLooksLikeRocket(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            string n = s.ToLowerInvariant();
            if (n.IndexOf("rocket", System.StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("rocketpod", System.StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("agr", System.StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("hydra", System.StringComparison.Ordinal) >= 0)
                return true;
            if (n.IndexOf("ffar", System.StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }
    }
}
