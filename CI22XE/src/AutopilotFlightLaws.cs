using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield STRAIGHT / ORBIT flight laws (0.0.9.57).
    /// Energy + aim rewritten from first principles; still drives game Autopilot.AutoAim.
    /// </summary>
    internal static class AutopilotFlightLaws
    {
        /// <summary>
        /// Heading + AGL terrain follow with proportional speed governor.
        /// </summary>
        internal static string ApplyStraight(Aircraft ac, Autopilot ap, ControlInputs inputs,
            float holdHeadingDeg, float holdAglM, float holdSpeedMs, float cornerMs)
        {
            if (ac == null || ap == null || inputs == null)
                return "ERR";

            float effort = AutopilotAim.DefaultEffort;
            float bank = AutopilotAim.CruiseBank;
            AutopilotAim.HoldHeadingTerrain(ap, ac, holdHeadingDeg, holdAglM, effort, bank);

            float tgt = holdSpeedMs > 10f ? holdSpeedMs : Mathf.Max(80f, cornerMs);
            ApplySpeedGovernor(inputs, ac.speed, tgt, 0.55f, 0.15f, 1f, 40f);

            return "HDG " + Mathf.RoundToInt(holdHeadingDeg).ToString("000")
                + "  AGL " + holdAglM.ToString("0")
                + "  " + GameUnitDisplayService.Speed(tgt);
        }

        /// <summary>
        /// Stable circular orbit: radial error → aim lead on the circle (CCW).
        /// </summary>
        internal static string ApplyOrbit(Aircraft ac, Autopilot ap, ControlInputs inputs,
            ref Vector3 orbitCenterLocal, ref bool hasOrbitCenter, float holdHeadingDeg,
            float holdAglM, float radiusM, float cornerMs, float cruiseThrottle)
        {
            if (ac == null || ap == null || inputs == null)
                return "ERR";

            if (!hasOrbitCenter)
            {
                orbitCenterLocal = ac.transform.position;
                hasOrbitCenter = true;
            }

            radiusM = Mathf.Max(800f, radiusM);
            Vector3 offset = ac.transform.position - orbitCenterLocal;
            offset.y = 0f;

            Vector3 radial;
            if (offset.sqrMagnitude > 100f)
                radial = offset.normalized;
            else
                radial = Quaternion.Euler(0f, holdHeadingDeg, 0f) * Vector3.forward;

            // Radius error: pull aim inward/outward so the jet settles on the ring.
            float dist = offset.magnitude;
            float radialScale = 1f;
            if (dist > 1f)
            {
                float errR = dist - radiusM;
                // Outside ring → aim more inward; inside → push out.
                radialScale = Mathf.Clamp(1f - errR / Mathf.Max(500f, radiusM), 0.55f, 1.35f);
            }

            Vector3 tangent = Vector3.Cross(Vector3.up, radial).normalized; // CCW
            float lead = radiusM * 0.85f;
            Vector3 aimLocal = orbitCenterLocal + radial * (radiusM * radialScale) + tangent * lead;
            aimLocal.y = ac.transform.position.y;

            AutopilotAim.AutoAim(ap, aimLocal.ToGlobalPosition(),
                true, false, false, AutopilotAim.DefaultEffort, AutopilotAim.OrbitBank,
                true, holdAglM, Vector3.zero);

            float tgt = Mathf.Max(60f, cornerMs * 0.9f);
            float thrCap = cruiseThrottle > 0.05f ? cruiseThrottle : 0.7f;
            ApplySpeedGovernor(inputs, ac.speed, tgt, 0.5f, 0.2f, thrCap, 35f);

            return "ORBIT  r=" + radiusM.ToString("0") + "  d=" + dist.ToString("0");
        }

        /// <summary>
        /// Proportional throttle + light speed-brake when well above target.
        /// </summary>
        internal static void ApplySpeedGovernor(ControlInputs inputs, float speedMs, float targetMs,
            float baseThr, float thrMin, float thrMax, float brakeStartExcessMs)
        {
            if (inputs == null)
                return;
            float err = speedMs - targetMs;
            // ~0.02 thr per m/s was legacy; keep similar gain, clamp hard.
            inputs.throttle = Mathf.Clamp(baseThr - err * 0.02f, thrMin, thrMax);
            if (err > brakeStartExcessMs)
                inputs.brake = Mathf.Clamp01((err - brakeStartExcessMs) * 0.01f);
            else
                inputs.brake = 0f;
        }
    }
}
