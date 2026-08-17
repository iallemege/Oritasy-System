using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield landing energy / speed-brake law (0.0.9.57+; 120C high-thrust bleed).
    /// </summary>
    internal static class LandingEnergyLaws
    {
        internal static void ApplySoftLandEnergy(ControlInputs inputs, Aircraft ac, float tgtSpd,
            float cruise, float landSpd, bool flare, bool rollout, float ralt, bool carrier)
        {
            if (inputs == null || ac == null)
                return;
            float spd = ac.speed;
            bool cv = carrier;

            float floorMul = flare ? 1.14f : 1.2f;
            if (cv)
                floorMul = flare ? 0.92f : (ralt < 120f ? 1.02f : 1.1f);
            else if (flare)
                floorMul = 0.96f;
            else if (ralt > 0f && ralt < 120f)
                floorMul = 1.08f;
            float floor = landSpd * floorMul;
            if (!rollout && ralt > 6f)
                tgtSpd = Mathf.Max(tgtSpd, floor);
            float err = spd - tgtSpd;

            if (rollout && ralt < 14f)
            {
                inputs.throttle = 0f;
                inputs.brake = 1f;
                return;
            }
            if (ralt > 0f && ralt < 2f && spd < landSpd * 1.05f)
            {
                inputs.throttle = 0f;
                inputs.brake = 1f;
                return;
            }
            if (cv && ralt < 3.5f && spd < 45f)
            {
                inputs.throttle = 0f;
                inputs.brake = 1f;
                return;
            }

            float sink = 0f;
            try
            {
                if (ac.rb != null)
                    sink = -ac.rb.velocity.y;
            }
            catch { }

            // True stall only — old 1.18× floor fought approach tgt and kept thr high forever
            // on buffed (×3–×4) airframes.
            float stallSpd = landSpd * (cv
                ? ((flare || ralt < 55f) ? 0.82f : 0.95f)
                : ((flare || ralt < 120f) ? 0.9f : 1.05f));
            if (spd < stallSpd && ralt > 3f)
            {
                inputs.throttle = Mathf.Clamp(Mathf.Max(cruise, 0.75f), 0.55f, 1f);
                inputs.brake = 0f;
                return;
            }

            // Hard bleed when hot — high-thrust packs need idle + full board earlier.
            if (err > 40f && ralt > 8f)
            {
                inputs.throttle = 0f;
                inputs.brake = 1f;
                return;
            }

            float thr = (tgtSpd + 4f - spd) * 0.12f;
            if (flare)
                thr = Mathf.Min(thr, cv ? 0.28f : 0.4f);
            if (err > 8f)
                thr = Mathf.Min(thr, 0.15f);
            if (err > 18f)
                thr = Mathf.Min(thr, 0.05f);
            if (err > 28f)
                thr = 0f;
            if (ralt > 8f && ralt < 280f && sink > 12f && spd > landSpd * 1.35f)
                thr = Mathf.Min(thr, 0.12f);
            if (ralt > 8f && ralt < 140f && sink > 18f && spd > landSpd * 1.45f)
                thr = Mathf.Min(thr, 0.06f);
            if (cv && ralt < 70f && err > 8f)
                thr = Mathf.Min(thr, 0.08f);
            if (cv && ralt < 45f && sink > 8f && spd > 55f)
                thr = Mathf.Min(thr, 0.15f);
            float thrCap = cruise * (flare ? 0.55f : (ralt > 0f && ralt < 200f ? 0.72f : 0.88f));
            if (cv && flare)
                thrCap = Mathf.Min(thrCap, 0.45f);
            if (err > 12f)
                thrCap = Mathf.Min(thrCap, 0.35f);
            if (err < -6f)
                thrCap = Mathf.Max(thrCap, 1f);
            inputs.throttle = Mathf.Clamp(thr, 0f, thrCap);

            float brake = 0f;
            if (err > 6f)
                brake = Mathf.Clamp01((err - 6f) * 0.045f);
            if (err > 16f)
                brake = Mathf.Max(brake, Mathf.Clamp01((err - 16f) * 0.06f));
            if (flare && err > 6f)
                brake = Mathf.Max(brake, Mathf.Clamp01((err - 6f) * 0.07f));
            if (cv && ralt < 90f && err > 4f)
                brake = Mathf.Max(brake, Mathf.Clamp01((err - 4f) * 0.07f));
            if (cv && ralt < 50f && spd > 58f)
                brake = Mathf.Max(brake, 0.65f);
            if (cv && ralt < 35f && spd > 48f)
                brake = Mathf.Max(brake, 0.9f);
            if (ralt > 0.5f && ralt < 40f && spd > landSpd * 1.35f)
                brake = Mathf.Max(brake, 0.45f);
            if (ralt > 0.5f && ralt < 18f && spd > landSpd * 1.22f)
                brake = Mathf.Max(brake, 0.75f);
            if (ralt > 0.5f && ralt < 8f && spd > landSpd * 1.12f)
                brake = Mathf.Max(brake, 1f);
            if (ralt > 12f && ralt < 250f && sink > 14f && spd > landSpd * 1.4f)
                brake = Mathf.Max(brake, Mathf.Clamp01((sink - 14f) * 0.04f));
            // Pattern join: force boards out when still hot above approach band.
            if (!flare && !rollout && ralt > 80f && err > 22f)
                brake = Mathf.Max(brake, 0.85f);
            inputs.brake = Mathf.Clamp01(brake);
        }
    }
}
