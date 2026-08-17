using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield aerial resupply cost / rate math (0.0.9.66).
    /// AerialResupply owns menu, payment, and tank writes.
    /// </summary>
    internal static class AerialResupplyMathService
    {
        internal static readonly float[] Rates = { 0.012f, 0.035f, 0.10f };
        internal static readonly float[] PriceMul = { 0.28f, 0.58f, 1.00f };

        internal static int ClampSpeedTier(int tier)
        {
            if (tier < 0) return 0;
            if (tier >= Rates.Length) return Rates.Length - 1;
            return tier;
        }

        internal static float SnapTarget01(float value)
        {
            return Mathf.Round(Mathf.Clamp01(value) * 20f) / 20f;
        }

        internal static float EstimateCost(float curFuel, float curBatt, float targetFuel, float targetBatt, int speedTier)
        {
            float fuelNeed = Mathf.Max(0f, targetFuel - curFuel);
            float battNeed = Mathf.Max(0f, targetBatt - curBatt);
            float work = Mathf.Clamp01((fuelNeed + battNeed) * 0.5f);
            float mul = PriceMul[ClampSpeedTier(speedTier)];
            return Mathf.Min(1f, work * mul);
        }

        internal static float FillRate(int speedTier)
        {
            return Rates[ClampSpeedTier(speedTier)];
        }

        internal static float ProgressFraction(float current, float start, float need0)
        {
            if (need0 <= 0.0001f)
                return 1f;
            return Mathf.Clamp01((current - start) / need0);
        }

        internal static bool TargetReached(float current, float target, float need0)
        {
            return current >= target - 0.01f || need0 < 0.005f;
        }
    }
}
